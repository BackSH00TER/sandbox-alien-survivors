using Sandbox;

public sealed class Enemy : Entity
{
	public override bool CanTakeDamage => Health > 0f && !IsDescending;

	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Range(1f, 200f)] public float PunchRange { get; set; } = 64f;
	[Property, Range(0.1f, 10f)] public float PunchInterval { get; set; } = 1f;
	[Property, Range(0f, 2f)] public float PunchWindup { get; set; } = 0.35f;
	[Property, Range(0f, 2f)] public float PunchRecovery { get; set; } = 0.4f;
	[Property, Range(1f, 100f)] public float PunchDamage { get; set; } = 10f;
	[Property, Range(50f, 1000f)] public float SpawnHeight { get; set; } = 300f;
	[Property, Range(0.1f, 10f)] public float SpawnDuration { get; set; } = 2f;
	[Property, Range(0, 100)] public int ExperienceDrop { get; set; } = 1;
	[Property, Group("Hit Reaction"), Range(0f, 1f)] public float HitReactionStrength { get; set; } = 0.6f;
	[Property, Group("Hit Reaction"), Range(0f, 2f)] public float HitStaggerDuration { get; set; } = 0.2f;

	[Sync(SyncFlags.FromHost)] private bool IsDescending { get; set; } = true;
	[Sync(SyncFlags.FromHost)] private Vector3 LandingPosition { get; set; }
	[Sync(SyncFlags.FromHost)] private float DescentStartTime { get; set; }
	private LineRenderer _spawnBeam;
	private bool _descentVisualsActive;

	[Sync(SyncFlags.FromHost)] private float NextPunchTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool IsWindingUp { get; set; }
	[Sync(SyncFlags.FromHost)] private float WindupEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] private float MovementUnlockTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool IsTargetInPunchRange { get; set; }
	[Sync(SyncFlags.FromHost)] private float StaggerEndTime { get; set; }

	protected override void OnStart()
	{
		base.OnStart();
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Controller.UseCameraControls = false;
		Controller.RunByDefault = true;
		Controller.Enabled = false;
		Controller.Body.MotionEnabled = false;
		_descentVisualsActive = true;

		if (!Networking.IsHost || IsProxy)
			return;

		var ground = Scene.Trace
			.FromTo(WorldPosition + Vector3.Up * 64f, WorldPosition - Vector3.Up * 2000f)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		if (!ground.Hit)
		{
			Log.Error("Enemy spawn requires ground beneath its spawn position.");
			GameObject.Destroy();
			return;
		}

		LandingPosition = ground.EndPosition;
		DescentStartTime = Time.Now;
		WorldPosition = LandingPosition + Vector3.Up * SpawnHeight;

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

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if (Controller.Renderer.IsValid())
		{
			var punching = !IsDescending && Health > 0f && Time.Now >= StaggerEndTime
				&& (IsTargetInPunchRange || IsWindingUp || Time.Now < MovementUnlockTime);
			Controller.Renderer.Set("holdtype", (int)(punching
				? Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Punch
				: Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.None));
		}
		if (IsDescending)
		{
			if (!_spawnBeam.IsValid())
			{
				_spawnBeam = GameObject.AddComponent<LineRenderer>();
				_spawnBeam.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
				_spawnBeam.UseVectorPoints = true;
				_spawnBeam.VectorPoints = new List<Vector3> { Vector3.Zero, Vector3.Zero };
				_spawnBeam.Width = 80f;
				_spawnBeam.Face = SceneLineObject.FaceMode.Cylinder;
				_spawnBeam.CylinderSegments = 24;
				_spawnBeam.Color = new Color(0.2f, 0.85f, 1f, 0.2f);
				_spawnBeam.Opaque = false;
				_spawnBeam.Additive = true;
				_spawnBeam.CastShadows = false;
				_spawnBeam.Lighting = false;
			}

			_spawnBeam.VectorPoints[0] = LandingPosition + Vector3.Up * 4f;
			_spawnBeam.VectorPoints[1] = LandingPosition + Vector3.Up * (SpawnHeight + 150f);
			if (Controller.Renderer.IsValid())
			{
				var progress = ((Time.Now - DescentStartTime) / System.MathF.Max(0.1f, SpawnDuration)).Clamp(0f, 1f);
				var fallSpeed = -SpawnHeight * 6f * progress * (1f - progress) / System.MathF.Max(0.1f, SpawnDuration);
				Controller.Renderer.Set("b_grounded", false);
				Controller.Renderer.Set("move_z", fallSpeed);
				Controller.Renderer.Set("move_groundspeed", 0f);
				Controller.Renderer.Set("wish_groundspeed", 0f);
			}
		}
		else if (_descentVisualsActive)
		{
			_descentVisualsActive = false;
			if (_spawnBeam.IsValid())
				_spawnBeam.Destroy();

			Controller.Body.MotionEnabled = true;
			Controller.Enabled = true;
			if (Controller.Renderer.IsValid())
			{
				Controller.Renderer.Set("b_grounded", true);
				Controller.Renderer.Set("move_z", 0f);
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
			return;

		Controller.WishVelocity = Vector3.Zero;
		if (IsDescending)
		{
			var progress = ((Time.Now - DescentStartTime) / System.MathF.Max(0.1f, SpawnDuration)).Clamp(0f, 1f);
			var eased = progress * progress * (3f - 2f * progress);
			WorldPosition = LandingPosition + Vector3.Up * SpawnHeight * (1f - eased);
			if (progress >= 1f)
				IsDescending = false;

			return;
		}

		if (Health <= 0f)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			IsWindingUp = false;
			IsTargetInPunchRange = false;
			MovementUnlockTime = 0f;
			return;
		}

		if (Time.Now < StaggerEndTime)
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

			SurvivorController survivor = player.GameObject.GetComponent<SurvivorController>();
			if (survivor != null && survivor.IsDead)
				continue;

			var distanceSquared = (player.WorldPosition - WorldPosition).LengthSquared;
			if (distanceSquared < nearestDistanceSquared)
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
			}
		}

		IsTargetInPunchRange = target != null && PunchRange > 0f
			&& nearestDistanceSquared <= PunchRange * PunchRange;

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

		if (target == null)
			return;

		var offset = target.WorldPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		if (direction.LengthSquared > 0.001f)
			Controller.EyeAngles = Rotation.LookAt(direction).Angles();

		if (IsTargetInPunchRange)
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

		Controller.WishVelocity = direction.Normal * Controller.RunSpeed;
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

	protected override void OnDamaged(Vector3 hitDirection)
	{
		if (Health <= 0f || IsDescending)
			return;

		IsWindingUp = false;
		IsTargetInPunchRange = false;
		StaggerEndTime = Time.Now + HitStaggerDuration;
		MovementUnlockTime = System.MathF.Max(MovementUnlockTime, StaggerEndTime);
		NextPunchTime = System.MathF.Max(NextPunchTime, StaggerEndTime);
		StopAttackMovement();
		PlayHitReaction(hitDirection);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void PlayHitReaction(Vector3 hitDirection)
	{
		var renderer = Controller.Renderer;
		if (!renderer.IsValid())
		{
			Log.Error("Enemy requires an animated renderer for hit reactions.");
			return;
		}

		var bone = renderer.GetClosestBone(WorldPosition + Vector3.Up * 48f);
		renderer.Set("hit_bone", bone.Index);
		renderer.Set("hit_offset", Vector3.Zero);
		renderer.Set("hit_direction", hitDirection.LengthSquared > 0.001f
			? hitDirection.Normal : -Controller.EyeAngles.ToRotation().Forward);
		renderer.Set("hit_strength", HitReactionStrength);
		renderer.Set("hit", true);
	}

	protected override void OnHealthDepleted()
	{
		IsWindingUp = false;
		IsTargetInPunchRange = false;
		StopAttackMovement();
		ExperienceOrb.Spawn(Scene, WorldPosition, ExperienceDrop);

		if (!Controller.Renderer.IsValid())
		{
			Log.Error("Enemy requires an animated renderer to create a ragdoll.");
			return;
		}

		RestoreMaterials();
		var ragdoll = Controller.CreateRagdoll("Enemy Ragdoll");
		ragdoll.AddComponent<BloodiedModel>().Amount = 1f;
		if (!ragdoll.NetworkSpawn())
		{
			ragdoll.Destroy();
			Log.Error("Failed to network-spawn enemy ragdoll.");
			return;
		}

		GameObject.Destroy();
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
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
		if (_spawnBeam.IsValid())
			_spawnBeam.Destroy();
		if (Networking.IsHost && !IsProxy && Controller.IsValid())
		{
			IsWindingUp = false;
			MovementUnlockTime = 0f;
			Controller.WishVelocity = Vector3.Zero;
		}
	}
}
