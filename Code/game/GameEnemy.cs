using Sandbox;

public sealed class GameEnemy : GameEntity
{
	public override bool CanTakeDamage => Health > 0f && !IsDescending;

	public GameEnemyKind Kind => GetComponent<GameEnemyBoss>().IsValid() ? GameEnemyKind.Boss
		: GetComponent<GameEnemyRanged>().IsValid() ? GameEnemyKind.Ranged
		: GetComponent<GameEnemyExploding>().IsValid() ? GameEnemyKind.Exploding
		: GameEnemyKind.Melee;

	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Group("Movement"), Range(0f, 1000f), Description("Pursuit speed for melee and exploding enemies, in world units per second.")] public float MoveSpeed { get; set; } = 100f;
	[Property, Group("Movement"), Range(0f, 1000f), Description("Pursuit and wandering speed for ranged enemies, in world units per second.")] public float RangedMoveSpeed { get; set; } = 77f;
	[Property, Group("Movement"), Range(1f, 2000f), Description("Maximum pursuit velocity change per second. Attack lunges use their own speed.")] public float PursuitAcceleration { get; set; } = 400f;
	[Property, Group("Movement"), Range(0.1f, 20f), Description("How quickly enemies turn toward their target. Higher values turn faster.")] public float PursuitTurnSpeed { get; set; } = 4f;
	[Property, Group("Movement"), Range(1f, 200f), Description("Minimum crowd-spacing radius, in world units. Expands to fit larger enemy bodies.")] public float SeparationRadius { get; set; } = 48f;
	[Property, Group("Movement"), Range(0f, 500f), Description("Maximum combined spacing velocity. Crowd size cannot increase it beyond this limit or raise pursuit above its normal speed.")] public float SeparationSpeed { get; set; } = 100f;
	[Property, Range(1f, 200f), Description("Distance at which the enemy starts winding up a punch.")] public float PunchRange { get; set; } = 64f;
	[Property, Range(1f, 200f), Description("Reach of the punch when the lunge ends.")] public float PunchHitRange { get; set; } = 54f;
	[Property, Range(0.1f, 10f)] public float PunchInterval { get; set; } = 1f;
	[Property, Range(0f, 2f)] public float PunchWindup { get; set; } = 0.15f;
	[Property, Group("Melee Attack"), Range(0f, 200f), Description("Distance the enemy lunges in its locked attack direction after the windup.")] public float PunchLungeDistance { get; set; } = 36f;
	[Property, Group("Melee Attack"), Range(0.05f, 1f), Description("Duration of the committed punch lunge.")] public float PunchLungeDuration { get; set; } = 0.24f;
	[Property, Range(0f, 2f)] public float PunchRecovery { get; set; } = 0.4f;
	[Property, Range(1f, 100f)] public float PunchDamage { get; set; } = 10f;
	[Property, Range(50f, 1000f)] public float SpawnHeight { get; set; } = 300f;
	[Property, Range(0.1f, 10f)] public float SpawnDuration { get; set; } = 2f;
	[Property, Range(0, 100)] public int ExperienceDrop { get; set; } = 1;
	[Property, Group("Hit Reaction"), Range(0f, 1f)] public float HitReactionStrength { get; set; } = 0.6f;
	[Property, Group("Hit Reaction"), Range(0f, 2f), Description("Movement and attack interruption after damage, in seconds. Zero disables hitstun.")] public float HitStaggerDuration { get; set; } = 0f;

	[Sync(SyncFlags.FromHost)] private bool IsDescending { get; set; } = true;
	[Sync(SyncFlags.FromHost)] private Vector3 LandingPosition { get; set; }
	[Sync(SyncFlags.FromHost)] private float DescentStartTime { get; set; }
	private LineRenderer _spawnBeam;
	private bool _descentVisualsActive;

	[Sync(SyncFlags.FromHost)] private float NextPunchTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool IsWindingUp { get; set; }
	[Sync(SyncFlags.FromHost)] private float WindupEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool IsLunging { get; set; }
	[Sync(SyncFlags.FromHost)] private float LungeEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] private Vector3 LungeDirection { get; set; }
	[Sync(SyncFlags.FromHost)] private float MovementUnlockTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool IsTargetInPunchRange { get; set; }
	[Sync(SyncFlags.FromHost)] private float StaggerEndTime { get; set; }
	private const float AggroSwitchCooldown = 5f;
	private readonly EnemyAggro<GamePlayer> _aggro = new(AggroSwitchCooldown, IsEligibleAggroTarget);
	private Vector3 _pursuitVelocity;

	protected override void OnStart()
	{
		base.OnStart();
		var enemyRigidbody = Controller.Body;
		if (!AddCollisionTag(enemyRigidbody, EnemyCollisionTag))
		{
			Log.Error("Game enemy requires physics shapes to assign its collision tag.");
		}

		var burningVisual = GameObject.GetOrAddComponent<BurningVisual>();
		burningVisual.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		Controller.UseInputControls = false;
		Controller.UseLookControls = false;
		Controller.UseCameraControls = false;
		Controller.RunByDefault = true;
		Controller.Enabled = false;
		Controller.Body.MotionEnabled = false;
		_descentVisualsActive = true;

		if (!Networking.IsHost || IsProxy)
		{
			return;
		}

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
				&& (IsWindingUp || IsLunging || Time.Now < MovementUnlockTime);
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
			{
				_spawnBeam.Destroy();
			}

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
		{
			return;
		}

		Controller.WishVelocity = Vector3.Zero;
		if (IsDescending)
		{
			var progress = ((Time.Now - DescentStartTime) / System.MathF.Max(0.1f, SpawnDuration)).Clamp(0f, 1f);
			var eased = progress * progress * (3f - 2f * progress);
			WorldPosition = LandingPosition + Vector3.Up * SpawnHeight * (1f - eased);
			if (progress >= 1f)
			{
				IsDescending = false;
			}

			return;
		}

		if (Health <= 0f)
		{
			return;
		}

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			GetComponent<GameEnemyBoss>()?.CancelStunAttack();
			GetComponent<GameEnemyRanged>()?.CancelAttack();
			IsWindingUp = false;
			IsLunging = false;
			IsTargetInPunchRange = false;
			MovementUnlockTime = 0f;
			StopAttackMovement();
			return;
		}

		_aggro.Advance(Time.Now);

		if (Time.Now < StaggerEndTime)
		{
			GetComponent<GameEnemyRanged>()?.CancelAttack();
			StopAttackMovement();
			return;
		}

		PlayerController target = null;
		var nearestDistanceSquared = float.PositiveInfinity;

		foreach (var controller in Scene.GetAllComponents<PlayerController>())
		{
			if (!controller.Active || controller.GameObject.GetComponent<GameEnemy>() != null)
			{
				continue;
			}

			GamePlayer player = controller.GameObject.GetComponent<GamePlayer>();
			if (player != null && player.IsDead)
			{
				continue;
			}

			var distanceSquared = (controller.WorldPosition - WorldPosition).LengthSquared;
			if (_aggro.Target.IsValid() && player == _aggro.Target)
			{
				target = controller;
				nearestDistanceSquared = distanceSquared;
				break;
			}

			if (distanceSquared < nearestDistanceSquared)
			{
				target = controller;
				nearestDistanceSquared = distanceSquared;
			}
		}

		var boss = GetComponent<GameEnemyBoss>();
		if (boss.IsValid() && boss.TryStunAttack(target))
		{
			IsWindingUp = false;
			IsLunging = false;
			IsTargetInPunchRange = false;
			StopAttackMovement();
			return;
		}

		var ranged = GetComponent<GameEnemyRanged>();
		if (ranged.IsValid())
		{
			if (target == null)
			{
				ranged.CancelAttack();
				StopAttackMovement();
				return;
			}

			var aim = target.WorldPosition - WorldPosition;
			var horizontal = new Vector3(aim.x, aim.y, 0f);
			if (horizontal.LengthSquared > 0.001f)
			{
				UpdatePursuitFacing(direction: horizontal);
			}

			var rangedStoppingDistance = Controller.BodyRadius + target.BodyRadius;
			if (ranged.TryAttack(this, target, out var combatVelocity))
			{
				if (combatVelocity.LengthSquared <= 0.001f)
				{
					StopAttackMovement();
				}
				else
				{
					SetPursuitMovement(velocity: combatVelocity, maximumSpeed: RangedMoveSpeed);
				}
			}
			else if (horizontal.LengthSquared > rangedStoppingDistance * rangedStoppingDistance)
			{
				var pursuitVelocity = horizontal.Normal * RangedMoveSpeed;
				SetPursuitMovement(velocity: pursuitVelocity, maximumSpeed: RangedMoveSpeed);
			}
			else
			{
				StopAttackMovement();
			}

			return;
		}

		IsTargetInPunchRange = target != null && PunchRange > 0f
			&& nearestDistanceSquared <= PunchRange * PunchRange;

		if (IsWindingUp)
		{
			StopAttackMovement();
			if (Time.Now < WindupEndTime)
			{
				return;
			}

			IsWindingUp = false;
			IsLunging = true;
			var lungeDuration = System.MathF.Max(0.05f, PunchLungeDuration);
			LungeEndTime = Time.Now + lungeDuration;
			NextPunchTime = Time.Now + System.MathF.Max(0.1f, PunchInterval);
			PlayPunch();
		}

		if (IsLunging)
		{
			if (Time.Now >= LungeEndTime)
			{
				IsLunging = false;
				MovementUnlockTime = Time.Now + System.MathF.Max(0f, PunchRecovery);
				StopAttackMovement();
				Punch<GamePlayer>(
					controller: Controller,
					range: PunchHitRange,
					damage: PunchDamage
				);
				return;
			}

			ApplyLungeMovement();
			return;
		}

		if (Time.Now < MovementUnlockTime)
		{
			StopAttackMovement();
			return;
		}

		if (target == null)
		{
			StopAttackMovement();
			return;
		}

		var offset = target.WorldPosition - WorldPosition;
		var direction = new Vector3(offset.x, offset.y, 0f);
		if (direction.LengthSquared > 0.001f)
		{
			UpdatePursuitFacing(direction: direction);
		}

		if (IsTargetInPunchRange)
		{
			StopAttackMovement();
			if (Time.Now >= NextPunchTime)
			{
				LungeDirection = Controller.EyeAngles.ToRotation().Forward.WithZ(0f).Normal;
				IsWindingUp = true;
				WindupEndTime = Time.Now + System.MathF.Max(0f, PunchWindup);
			}

			return;
		}

		var stoppingDistance = Controller.BodyRadius + target.BodyRadius;
		if (direction.LengthSquared <= stoppingDistance * stoppingDistance)
		{
			StopAttackMovement();
			return;
		}

		var forward = Controller.EyeAngles.ToRotation().Forward.WithZ(0f).Normal;
		SetPursuitMovement(velocity: forward * MoveSpeed, maximumSpeed: MoveSpeed);
	}

	internal void SetAggroTarget(GamePlayer player)
	{
		if (!Networking.IsHost || IsProxy)
		{
			Log.Error("Enemy aggro targets must be set on the host.");
			return;
		}

		if (!IsEligibleAggroTarget(player))
		{
			return;
		}

		_aggro.RecordHit(player, Time.Now);
	}

	private static bool IsEligibleAggroTarget(GamePlayer player)
	{
		return player.IsValid() && player.Active && !player.IsDead
			&& player.Controller.IsValid() && player.Controller.Active;
	}

	private void UpdatePursuitFacing(Vector3 direction)
	{
		var currentRotation = Controller.EyeAngles.ToRotation();
		var targetRotation = Rotation.LookAt(direction);
		var turnSpeed = System.MathF.Max(0.1f, PursuitTurnSpeed);
		var blend = 1f - System.MathF.Exp(-turnSpeed * Time.Delta);
		var rotation = Rotation.Lerp(
			a: currentRotation,
			b: targetRotation,
			frac: blend
		);
		Controller.EyeAngles = rotation.Angles();
	}

	private void SetPursuitMovement(Vector3 velocity, float maximumSpeed)
	{
		var separation = GetSeparationVelocity();
		var desiredVelocity = velocity + separation;
		var speedLimit = System.MathF.Max(0f, maximumSpeed);
		if (desiredVelocity.LengthSquared > speedLimit * speedLimit)
		{
			desiredVelocity = desiredVelocity.Normal * speedLimit;
		}

		var velocityChange = desiredVelocity - _pursuitVelocity;
		var maximumChange = System.MathF.Max(1f, PursuitAcceleration) * Time.Delta;
		if (velocityChange.LengthSquared > maximumChange * maximumChange)
		{
			velocityChange = velocityChange.Normal * maximumChange;
		}

		_pursuitVelocity += velocityChange;
		Controller.WishVelocity = _pursuitVelocity;
	}

	private Vector3 GetSeparationVelocity()
	{
		var separation = Vector3.Zero;
		var speedLimit = System.MathF.Max(0f, SeparationSpeed);
		foreach (var other in Scene.GetAllComponents<GameEnemy>())
		{
			if (other == this || other.Health <= 0f || other.IsDescending || !other.Controller.IsValid())
			{
				continue;
			}

			var offset = (WorldPosition - other.WorldPosition).WithZ(0f);
			var distanceSquared = offset.LengthSquared;
			var bodySpacing = Controller.BodyRadius + other.Controller.BodyRadius;
			var radius = System.MathF.Max(SeparationRadius, bodySpacing);
			if (distanceSquared >= radius * radius)
			{
				continue;
			}

			if (distanceSquared <= 0.001f)
			{
				// Choose opposite directions for coincident pairs instead of normalizing zero.
				var side = GameObject.Id.CompareTo(other.GameObject.Id) < 0 ? -1f : 1f;
				separation += Vector3.Left * side * speedLimit;
				continue;
			}

			var proximity = 1f - distanceSquared / (radius * radius);
			separation += offset.Normal * (proximity * speedLimit);
		}

		if (separation.LengthSquared > speedLimit * speedLimit)
		{
			separation = separation.Normal * speedLimit;
		}

		return separation;
	}

	private void ApplyLungeMovement()
	{
		Controller.EyeAngles = Rotation.LookAt(LungeDirection).Angles();
		var lungeDistance = System.MathF.Max(0f, PunchLungeDistance);
		var lungeDuration = System.MathF.Max(0.05f, PunchLungeDuration);
		Controller.WishVelocity = LungeDirection * (lungeDistance / lungeDuration);
	}

	private void StopAttackMovement()
	{
		_pursuitVelocity = Vector3.Zero;
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
		{
			return;
		}

		if (HitStaggerDuration > 0f)
		{
			IsWindingUp = false;
			IsLunging = false;
			IsTargetInPunchRange = false;
			GetComponent<GameEnemyRanged>()?.CancelAttack();
			StaggerEndTime = Time.Now + HitStaggerDuration;
			MovementUnlockTime = System.MathF.Max(MovementUnlockTime, StaggerEndTime);
			NextPunchTime = System.MathF.Max(NextPunchTime, StaggerEndTime);
			StopAttackMovement();
		}

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
		GetComponent<GameEnemyRanged>()?.CancelAttack();
		IsWindingUp = false;
		IsLunging = false;
		IsTargetInPunchRange = false;
		StopAttackMovement();
		var boss = GetComponent<GameEnemyBoss>();
		if (boss.IsValid())
		{
			boss.Defeated(this);
			return;
		}
		GameExperienceOrb.Spawn(Scene, WorldPosition, ExperienceDrop);

		if (!Controller.Renderer.IsValid())
		{
			Log.Error("Enemy requires an animated renderer to create a ragdoll.");
			return;
		}

		RestoreMaterials();
		var ragdoll = Controller.CreateRagdoll("Enemy Ragdoll");
		ragdoll.AddComponent<GameBloodiedModel>().Amount = 1f;
		var ragdollCollision = ragdoll.AddComponent<GameEnemyRagdollCollision>();
		if (!ragdollCollision.ApplyCollisionTags())
		{
			ragdoll.Destroy();
			Log.Error("Failed to assign collision tags to the enemy ragdoll.");
			return;
		}

		var exploder = GetComponent<GameEnemyExploding>();
		if (exploder.IsValid())
		{
			exploder.ConfigureDeathExplosion(ragdoll);
		}

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
		{
			_spawnBeam.Destroy();
		}

		if (Networking.IsHost && !IsProxy && Controller.IsValid())
		{
			IsWindingUp = false;
			IsLunging = false;
			MovementUnlockTime = 0f;
			StopAttackMovement();
		}
	}
}
