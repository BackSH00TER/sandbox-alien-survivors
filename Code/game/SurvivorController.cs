using Sandbox;

public sealed class SurvivorController : Entity
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Range( 0.25f, 4f )] public float Zoom { get; set; } = 1.5f;
	[Property, Range( 0.1f, 10f )] public float PunchInterval { get; set; } = 1f;
	[Property, Range( 1f, 200f )] public float PunchRange { get; set; } = 64f;
	[Property, Range( 1f, 100f )] public float PunchDamage { get; set; } = 10f;
	[Property, Group( "Debug" )] public bool ShowAttackDebug { get; set; } = true;

	[Group( "Experience" )]
	[Property, Range( 1, 1000 )] public int BaseExperienceToLevel { get; set; } = 5;
	[Group( "Experience" )]
	[Property, Range( 0, 1000 )] public int ExperienceGrowthPerLevel { get; set; } = 5;

	[Sync( SyncFlags.FromHost )] public int Level { get; private set; } = 1;
	[Sync( SyncFlags.FromHost )] public int Experience { get; private set; }
	public int ExperienceToNextLevel => System.Math.Max(1, BaseExperienceToLevel + ExperienceGrowthPerLevel * (Level - 1));

	[Sync( SyncFlags.FromHost )] public int PendingModifierChoices { get; private set; }
	[Sync( SyncFlags.FromHost )] public int ModifierOfferId { get; private set; }
	[Sync( SyncFlags.FromHost )] public Modifier ModifierOptionOne { get; private set; }
	[Sync( SyncFlags.FromHost )] public Modifier ModifierOptionTwo { get; private set; }
	[Sync( SyncFlags.FromHost )] public Modifier ModifierOptionThree { get; private set; }
	[Sync( SyncFlags.FromHost )] public int AttackSpeedStacks { get; private set; }
	[Sync( SyncFlags.FromHost )] public int AttackRangeStacks { get; private set; }
	[Sync( SyncFlags.FromHost )] public int VitalityStacks { get; private set; }

	public float EffectivePunchInterval => System.MathF.Max(0.1f, PunchInterval / (1f + AttackSpeedStacks));
	public float EffectivePunchRange => PunchRange * (1f + AttackRangeStacks);

	[Sync( SyncFlags.FromHost )] private float NextPunchTime { get; set; }

	private static readonly Vector3 BaseCameraOffset = new Vector3(-500f, 0f, 800f);
	private bool _missingCameraLogged;

	protected override void OnStart()
	{
		base.OnStart();
		Controller.UseCameraControls = false;
		Controller.UseLookControls = false;
		Controller.UseInputControls = false;
		Controller.RunByDefault = false;
		Controller.AltMoveButton = "";
		Controller.RunSpeed = Controller.WalkSpeed;

		if (Networking.IsHost)
		{
			NextPunchTime = Time.Now + EffectivePunchInterval;
			QueueModifierChoice();
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if (IsProxy)
			return;

		Mouse.Visibility = MouseVisibility.Visible;
		if (ShowAttackDebug)
			DrawAttackRange();

		var camera = Scene.Camera;
		if (!camera.IsValid())
		{
			if (!_missingCameraLogged)
			{
				Log.Error("SurvivorController requires an active scene camera.");
				_missingCameraLogged = true;
			}

			return;
		}

		_missingCameraLogged = false;
		camera.WorldPosition = WorldPosition + BaseCameraOffset / Zoom.Clamp(0.25f, 4f);
		camera.WorldRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);

		var ray = camera.ScreenPixelToRay(Mouse.Position);
		var result = Scene.Trace
			.Ray(ray, 10000f)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();

		Vector3 aimPosition;
		if (result.Hit)
		{
			aimPosition = result.EndPosition;
		}
		else
		{
			if (System.MathF.Abs(ray.Forward.z) < 0.0001f)
				return;

			var distance = (WorldPosition.z - ray.Position.z) / ray.Forward.z;
			if (distance < 0f)
				return;

			aimPosition = ray.Position + ray.Forward * distance;
		}

		var offset = aimPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		if (direction.LengthSquared > 0.001f)
			Controller.EyeAngles = Rotation.LookAt(direction, Vector3.Up).Angles();
	}

	protected override void OnFixedUpdate()
	{
		if (!IsProxy)
		{
			var cameraRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);
			var movementRotation = Rotation.FromYaw(cameraRotation.Angles().yaw);
			Controller.WishVelocity = Controller.Mode.UpdateMove(movementRotation, Input.AnalogMove);
		}

		if (!Networking.IsHost)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
			return;

		if (Time.Now >= NextPunchTime)
		{
			NextPunchTime = Time.Now + EffectivePunchInterval;
			PlayPunch();
			Punch<Enemy>(Controller, EffectivePunchRange, PunchDamage);
		}
	}

	private void DrawAttackRange()
	{
		const int segments = 64;
		var center = WorldPosition + Vector3.Up * 2f;
		var previous = center + Vector3.Forward * EffectivePunchRange;
		for (var i = 1; i <= segments; i++)
		{
			var angle = i * (System.MathF.PI * 2f / segments);
			var point = center + new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * EffectivePunchRange;
			DebugOverlay.Line(previous, point, Color.Yellow, 0f);
			previous = point;
		}
	}

	public void AddExperience(int amount)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Experience must be granted on the host.");
			return;
		}

		if (amount <= 0)
		{
			Log.Error("Experience must be a positive amount.");
			return;
		}

		Experience += amount;
		while (Experience >= ExperienceToNextLevel)
		{
			Experience -= ExperienceToNextLevel;
			Level++;
			OnLevelUp();
		}
	}

	private void OnLevelUp()
	{
		QueueModifierChoice();
	}

	private void QueueModifierChoice()
	{
		PendingModifierChoices++;
		if (PendingModifierChoices == 1)
			RollModifierOffer();
	}

	private void RollModifierOffer()
	{
		var options = System.Enum.GetValues<Modifier>();
		for (var i = options.Length - 1; i > 0; i--)
		{
			var other = Game.Random.Int(0, i);
			(options[i], options[other]) = (options[other], options[i]);
		}

		ModifierOptionOne = options[0];
		ModifierOptionTwo = options[1];
		ModifierOptionThree = options[2];
		ModifierOfferId++;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void SelectModifier(int offerId, Modifier modifier)
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Active || (gameManager != null && gameManager.State != GameState.InProgress)
			|| PendingModifierChoices <= 0 || offerId != ModifierOfferId
			|| (modifier != ModifierOptionOne && modifier != ModifierOptionTwo && modifier != ModifierOptionThree))
		{
			Log.Warning("Rejected an unavailable or outdated modifier choice.");
			return;
		}

		switch (modifier)
		{
			case Modifier.AttackSpeed:
				AttackSpeedStacks++;
				NextPunchTime = System.MathF.Min(NextPunchTime, Time.Now + EffectivePunchInterval);
				break;
			case Modifier.AttackRange:
				AttackRangeStacks++;
				break;
			case Modifier.Vitality:
				VitalityStacks++;
				IncreaseMaxHealth(100f);
				break;
			default:
				Log.Error($"Unknown modifier: {modifier}");
				return;
		}

		PendingModifierChoices--;
		// Invalidate the consumed offer so duplicate clicks cannot spend the next queued choice.
		if (PendingModifierChoices > 0)
			RollModifierOffer();
		else
			ModifierOfferId++;
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlayPunch()
	{
		if (!Controller.Renderer.IsValid())
		{
			Log.Error("SurvivorController requires an animated renderer to punch.");
			return;
		}

		Controller.Renderer.Set("holdtype", (int)Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Punch);
		Controller.Renderer.Set("b_attack", true);
	}

	protected override void OnPreRender()
	{
		if (Controller.Renderer.IsValid())
			Controller.Renderer.WorldRotation = Controller.EyeAngles.ToRotation();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (!IsProxy && Controller.IsValid())
			Controller.WishVelocity = Vector3.Zero;
	}
}
