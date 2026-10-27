using Sandbox;

public sealed class Enemy : Entity
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Range( 1f, 200f )] public float PunchRange { get; set; } = 64f;
	[Property, Range( 0.1f, 10f )] public float PunchInterval { get; set; } = 1f;
	[Property, Range( 0f, 2f )] public float PunchWindup { get; set; } = 0.35f;
	[Property, Range( 0f, 2f )] public float PunchRecovery { get; set; } = 0.4f;
	[Property, Range( 1f, 100f )] public float PunchDamage { get; set; } = 10f;

	[Sync( SyncFlags.FromHost )] private float NextPunchTime { get; set; }
	[Sync( SyncFlags.FromHost )] private bool IsWindingUp { get; set; }
	[Sync( SyncFlags.FromHost )] private float WindupEndTime { get; set; }
	[Sync( SyncFlags.FromHost )] private float MovementUnlockTime { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Controller.UseCameraControls = false;

		if (!Networking.IsHost || IsProxy)
			return;

		var dresser = GetComponent<Dresser>();
		if (dresser == null)
		{
			Log.Error("Enemy must include a Dresser.");
			GameObject.Destroy();
			return;
		}

		dresser.Source = Dresser.ClothingSource.Manual;
		dresser.Randomize();
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
			return;

		Controller.WishVelocity = Vector3.Zero;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			IsWindingUp = false;
			return;
		}

		if (IsWindingUp)
		{
			StopAttackMovement();
			if (Time.Now >= WindupEndTime)
			{
				IsWindingUp = false;
				NextPunchTime = Time.Now + System.MathF.Max(0.1f, PunchInterval);
				MovementUnlockTime = Time.Now + System.MathF.Max(0f, PunchRecovery);
				PlayPunch();
				Punch<SurvivorController>(Controller, PunchRange, PunchDamage);
			}

			return;
		}

		if (Time.Now < MovementUnlockTime)
		{
			StopAttackMovement();
			return;
		}

		PlayerController target = null;
		var nearestDistanceSquared = float.PositiveInfinity;

		foreach (var player in Scene.GetAllComponents<PlayerController>())
		{
			if (!player.Active || player.GameObject.GetComponent<Enemy>() != null)
				continue;

			var distanceSquared = (player.WorldPosition - WorldPosition).LengthSquared;
			if (distanceSquared < nearestDistanceSquared)
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
			}
		}

		if (target == null)
			return;

		var offset = target.WorldPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		if (direction.LengthSquared > 0.001f)
			Controller.EyeAngles = Rotation.LookAt(direction).Angles();

		if (PunchRange > 0f && offset.LengthSquared <= PunchRange * PunchRange)
		{
			if (Time.Now >= NextPunchTime)
			{
				IsWindingUp = true;
				WindupEndTime = Time.Now + System.MathF.Max(0f, PunchWindup);
				StopAttackMovement();
			}

			return;
		}

		var stoppingDistance = Controller.BodyRadius + target.BodyRadius;
		if (direction.LengthSquared <= stoppingDistance * stoppingDistance)
			return;

		Controller.WishVelocity = direction.Normal * Controller.WalkSpeed;
	}

	private void StopAttackMovement()
	{
		Controller.WishVelocity = Vector3.Zero;
		if (Controller.Body.IsValid())
		{
			var velocity = Controller.Body.Velocity;
			Controller.Body.Velocity = new Vector3(0f, 0f, velocity.z);
		}
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	private void PlayPunch()
	{
		if (!Controller.Renderer.IsValid())
		{
			Log.Error("Enemy requires an animated renderer to punch.");
			return;
		}

		Controller.Renderer.Set("holdtype", (int)Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Punch);
		Controller.Renderer.Set("b_attack", true);
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (Networking.IsHost && !IsProxy && Controller.IsValid())
		{
			IsWindingUp = false;
			MovementUnlockTime = 0f;
			Controller.WishVelocity = Vector3.Zero;
		}
	}
}
