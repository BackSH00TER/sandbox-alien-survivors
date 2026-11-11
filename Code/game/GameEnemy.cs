using Sandbox;
using Sandbox.Animation;

public sealed class GameEnemy : GameEntity
{
	public override bool CanTakeDamage => Health > 0f && !IsDescending;
	[Sync(SyncFlags.FromHost)] private float SlowEndTime { get; set; }
	public float MovementMultiplier => Time.Now < SlowEndTime ? GameShotModifiers.SlowMultiplier : 1f;

	public void ApplySlow(float duration)
	{
		if (!Networking.IsHost || !float.IsFinite(duration) || duration <= 0f)
		{
			Log.Error("Applying a slow requires the host and a finite positive duration.");
			return;
		}
		if (!Active || !CanTakeDamage)
		{
			return;
		}

		SlowEndTime = System.MathF.Max(SlowEndTime, Time.Now + duration);
	}

	public void ApplyDamageEffect(DamageEffectSettings settings, GameEntity attacker)
	{
		ApplyEnemyDamageEffect(
			enemy: this,
			settings: settings,
			attacker: attacker
		);
	}

	public GameEnemyKind Kind => GetComponent<GameEnemyBoss>().IsValid() ? GameEnemyKind.Boss
		: GetComponent<GameEnemyRanged>().IsValid() ? GameEnemyKind.Ranged
		: GetComponent<GameEnemyExploding>().IsValid() ? GameEnemyKind.Exploding
		: GameEnemyKind.Melee;

	[Property, Group("Movement"), Range(0f, 1000f), Description("Base pursuit speed for melee and exploding enemies, before the pursuit multiplier and slows.")] public float MoveSpeed { get; set; } = 100f;
	[Property, Group("Movement"), Range(0f, 1000f), Description("Base pursuit and wandering speed for ranged enemies, before the pursuit multiplier and slows.")] public float RangedMoveSpeed { get; set; } = 77f;
	[Property, Group("Movement"), Range(0f, 1f), Description("Steady pursuit fraction of base speed. The default preserves the previous step cadence's average pace without periodic stops. Does not affect attack lunges.")] public float PursuitSpeedMultiplier { get; set; } = 0.35f;
	[Property, Group("Movement"), Range(1f, 100f)] public float HitboxRadius { get; set; } = 18f;
	[Property, Group("Movement"), Range(1f, 2000f), Description("Maximum pursuit velocity change per second. Attack lunges use their own speed.")] public float PursuitAcceleration { get; set; } = 400f;
	[Property, Group("Movement"), Range(0.1f, 20f), Description("How quickly enemies turn toward their target. Higher values turn faster.")] public float PursuitTurnSpeed { get; set; } = 4f;
	[Property, Group("Movement"), Range(1f, 200f), Description("Minimum crowd-spacing radius, in world units. Expands to fit larger enemy bodies.")] public float SeparationRadius { get; set; } = 48f;
	[Property, Group("Movement"), Range(0f, 500f), Description("Maximum combined spacing velocity before the pursuit multiplier. Crowd size cannot raise the pursuit speed limit.")] public float SeparationSpeed { get; set; } = 100f;
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
	[Property, Description("Disabled for enemies deployed directly from a landed dropship.")] public bool DescendOnSpawn { get; set; } = true;
	[Property, Range(0, 100)] public int ExperienceDrop { get; set; } = 1;
	[Property, Group("Hit Reaction"), Range(0f, 2f), Description("Movement and attack interruption after damage, in seconds. Zero disables hitstun.")] public float HitStaggerDuration { get; set; } = 0f;

	[Sync(SyncFlags.FromHost)] private bool IsDescending { get; set; } = true;
	[Sync(SyncFlags.FromHost)] private Vector3 LandingPosition { get; set; }
	private float DescentStartTime { get; set; }
	private LineRenderer _spawnBeam;
	private bool _descentVisualsActive;

	private float NextPunchTime { get; set; }
	private bool IsWindingUp { get; set; }
	private float WindupEndTime { get; set; }
	private bool IsLunging { get; set; }
	private float LungeEndTime { get; set; }
	private Vector3 LungeDirection { get; set; }
	private float MovementUnlockTime { get; set; }
	private float StaggerEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] public Vector3 FacingDirection { get; private set; } = Vector3.Forward;
	private const float AggroSwitchCooldown = 5f;
	private const float HitboxHeight = 72f;
	private readonly EnemyAggro<GamePlayer> _aggro = new(AggroSwitchCooldown, IsEligibleAggroTarget);
	private Vector3 _pursuitVelocity;
	private SkinnedModelRenderer _animationRenderer;
	private Vector3 _lastAnimationPosition;
	private float _walkDuration = 40f / 30f;
	private float _hitDuration = 10f / 30f;
	private const float WalkAnimationSpeedMultiplier = 1.3f;
	private const float MeleeImpactPhase = 9f / 30f;
	private float _walkPhase;
	private float _meleeStarted = float.NegativeInfinity;
	private float _meleeImpactDelay;
	private float _meleeRecovery;
	private float _hitStarted = float.NegativeInfinity;
	private bool _animationReady;
	private string _skeletonPath = "models/alien_rigify.skel";
	private string _animationFolder = "models/alien_animations";

	protected override void OnStart()
	{
		base.OnStart();
		EnsureCapsuleHitbox(radius: HitboxRadius, height: HitboxHeight);

		_lastAnimationPosition = WorldPosition;
		_animationRenderer = GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault();
		if (!_animationRenderer.IsValid())
		{
			Log.Error("Enemy animation requires a SkinnedModelRenderer.");
		}
		else
		{
			InitializeAnimation();
		}

		var burningVisual = GameObject.GetOrAddComponent<BurningVisual>();
		burningVisual.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		_descentVisualsActive = true;

		if (!Networking.IsHost || IsProxy)
		{
			return;
		}

		var ground = FindSpawnGround(Scene, WorldPosition);
		if (!ground.Hit || ground.StartedSolid)
		{
			Log.Error("Enemy spawn requires ground beneath its spawn position.");
			GameObject.Destroy();
			return;
		}

		LandingPosition = ground.EndPosition;
		DescentStartTime = Time.Now;
		IsDescending = DescendOnSpawn;
		WorldPosition = DescendOnSpawn ? LandingPosition + Vector3.Up * SpawnHeight : LandingPosition;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateAnimation();
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
		}
		else if (_descentVisualsActive)
		{
			_descentVisualsActive = false;
			if (_spawnBeam.IsValid())
			{
				_spawnBeam.Destroy();
			}

		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
		{
			return;
		}

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

		GamePlayer target = null;
		var nearestDistanceSquared = float.PositiveInfinity;

		foreach (var player in Scene.GetAllComponents<GamePlayer>())
		{
			if (!IsEligibleAggroTarget(player))
			{
				continue;
			}

			var distanceSquared = (player.WorldPosition - WorldPosition).LengthSquared;
			if (_aggro.Target.IsValid() && player == _aggro.Target)
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
				break;
			}

			if (distanceSquared < nearestDistanceSquared)
			{
				target = player;
				nearestDistanceSquared = distanceSquared;
			}
		}

		var boss = GetComponent<GameEnemyBoss>();
		if (boss.IsValid() && boss.TryStunAttack(target))
		{
			IsWindingUp = false;
			IsLunging = false;
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

			var rangedStoppingDistance = HitboxRadius + target.HitboxRadius;
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

		var isTargetInPunchRange = target != null && PunchRange > 0f
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
		}

		if (IsLunging)
		{
			if (Time.Now >= LungeEndTime)
			{
				IsLunging = false;
				MovementUnlockTime = Time.Now + System.MathF.Max(0f, PunchRecovery);
				StopAttackMovement();
				Punch<GamePlayer>(
					forward: FacingDirection,
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

		if (isTargetInPunchRange)
		{
			StopAttackMovement();
			if (Time.Now >= NextPunchTime)
			{
				LungeDirection = FacingDirection.WithZ(0f).Normal;
				IsWindingUp = true;
				WindupEndTime = Time.Now + System.MathF.Max(0f, PunchWindup);
				var impactDelay = System.MathF.Max(0f, PunchWindup) + System.MathF.Max(0.05f, PunchLungeDuration);
				var recovery = System.MathF.Max(0f, PunchRecovery);
				PlayMeleeAnimation(impactDelay: impactDelay, recovery: recovery);
			}

			return;
		}

		var stoppingDistance = HitboxRadius + target.HitboxRadius;
		if (direction.LengthSquared <= stoppingDistance * stoppingDistance)
		{
			StopAttackMovement();
			return;
		}

		SetPursuitMovement(velocity: FacingDirection.WithZ(0f).Normal * MoveSpeed, maximumSpeed: MoveSpeed);
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

	private void InitializeAnimation()
	{
		var modelName = Kind switch
		{
			GameEnemyKind.Ranged => "hywirl",
			GameEnemyKind.Boss => "fish",
			GameEnemyKind.Exploding => "mushroom_king",
			_ => null
		};
		if (modelName != null)
		{
			var modelPath = $"models/{modelName}.vmdl";
			var model = Model.Load(modelPath);
			if (model.IsError)
			{
				Log.Error($"Enemy model could not load '{modelPath}'.");
				return;
			}

			_animationRenderer.Model = model;
			_skeletonPath = $"models/{modelName}.skel";
			_animationFolder = $"models/{modelName}_animations";
		}

		var skeleton = Skeleton.Load(_skeletonPath);
		if (skeleton.IsError)
		{
			Log.Error($"Enemy animation could not load '{_skeletonPath}'.");
			return;
		}

		var walk = LoadAnimationClip("walk");
		var hit = LoadAnimationClip("hit");
		_walkDuration = walk.Duration;
		_hitDuration = hit.Duration;
		var graphName = modelName ?? "alien_rigify";
		var graph = Graph.Load($"models/animation_graphs/{graphName}_enemy.ag");
		if (graph.IsError)
		{
			Log.Error($"Enemy animation could not load the '{graphName}' graph.");
			return;
		}

		_animationRenderer.UseAnimGraph = true;
		_animationRenderer.Animation.Graph = graph;
		_animationReady = true;
	}

	private Clip LoadAnimationClip(string name)
	{
		var path = $"{_animationFolder}/{name}.anim";
		var clip = Clip.Load(path);
		if (clip.IsError || clip.SkeletonName != _skeletonPath || clip.Duration <= 0f)
		{
			throw new System.InvalidOperationException($"Enemy animation could not load '{path}' against '{_skeletonPath}'.");
		}

		return clip;
	}

	private void UpdateAnimation()
	{
		var displacement = (WorldPosition - _lastAnimationPosition).WithZ(0f);
		_lastAnimationPosition = WorldPosition;
		if (!_animationReady || !_animationRenderer.IsValid() || Time.Delta <= 0f)
		{
			return;
		}

		if (IsDescending || Health <= 0f)
		{
			return;
		}

		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		var roundOver = manager.IsValid() && manager.State != GameState.InProgress;
		if (roundOver)
		{
			_meleeStarted = float.NegativeInfinity;
			_hitStarted = float.NegativeInfinity;
		}

		var velocity = displacement / Time.Delta;
		if (!roundOver && Kind != GameEnemyKind.Ranged && velocity.LengthSquared > 1f)
		{
			var referenceSpeed = GetComponent<GameEnemyRanged>().IsValid() ? RangedMoveSpeed : MoveSpeed;
			var walkDistance = System.MathF.Max(1f, referenceSpeed) * _walkDuration;
			_walkPhase = (_walkPhase + displacement.Length * WalkAnimationSpeedMultiplier / walkDistance) % 1f;
		}

		var hitElapsed = Time.Now - _hitStarted;
		var meleeElapsed = Time.Now - _meleeStarted;
		var meleeDuration = _meleeImpactDelay + _meleeRecovery;
		var meleePhase = 0f;
		var meleeWeight = 0f;
		if (meleeElapsed >= 0f && meleeElapsed < meleeDuration)
		{
			// Match the punch's extended-hand pose to the existing damage instant.
			if (meleeElapsed < _meleeImpactDelay)
			{
				meleePhase = MeleeImpactPhase * meleeElapsed / _meleeImpactDelay;
			}
			else
			{
				var recoveryProgress = (meleeElapsed - _meleeImpactDelay) / System.MathF.Max(0.001f, _meleeRecovery);
				meleePhase = MeleeImpactPhase + (1f - MeleeImpactPhase) * recoveryProgress;
			}

			var fadeIn = (meleeElapsed / 0.08f).Clamp(0f, 1f);
			var fadeOut = ((meleeDuration - meleeElapsed) / 0.08f).Clamp(0f, 1f);
			meleeWeight = System.MathF.Min(fadeIn, fadeOut);
		}

		var hitWeight = 0f;
		if (hitElapsed >= 0f && hitElapsed < _hitDuration)
		{
			var fadeIn = (hitElapsed / 0.04f).Clamp(0f, 1f);
			var fadeOut = ((_hitDuration - hitElapsed) / 0.08f).Clamp(0f, 1f);
			hitWeight = System.MathF.Min(fadeIn, fadeOut);
		}

		var parameters = _animationRenderer.Animation.Parameters;
		parameters.Set("walk_phase", _walkPhase);
		parameters.Set("melee_phase", meleePhase.Clamp(0f, 1f));
		parameters.Set("melee_weight", meleeWeight);
		parameters.Set("hit_phase", (hitElapsed / _hitDuration).Clamp(0f, 1f));
		parameters.Set("hit_weight", hitWeight);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void PlayMeleeAnimation(float impactDelay, float recovery)
	{
		_meleeStarted = Time.Now;
		_meleeImpactDelay = impactDelay;
		_meleeRecovery = System.MathF.Max(0.08f, recovery);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void PlayHitAnimation(bool cancelMelee)
	{
		if (Time.Now - _hitStarted >= _hitDuration)
		{
			_hitStarted = Time.Now;
		}

		if (cancelMelee)
		{
			_meleeStarted = float.NegativeInfinity;
		}
	}

	private static bool IsEligibleAggroTarget(GamePlayer player)
	{
		return player.IsValid() && player.Active && !player.IsDead;
	}

	private void UpdatePursuitFacing(Vector3 direction)
	{
		var currentRotation = WorldRotation;
		var targetRotation = Rotation.LookAt(direction);
		var turnSpeed = System.MathF.Max(0.1f, PursuitTurnSpeed);
		var blend = 1f - System.MathF.Exp(-turnSpeed * Time.Delta);
		var rotation = Rotation.Lerp(
			a: currentRotation,
			b: targetRotation,
			frac: blend
		);
		WorldRotation = rotation;
		FacingDirection = rotation.Forward.WithZ(0f).Normal;
	}

	private void SetPursuitMovement(Vector3 velocity, float maximumSpeed)
	{
		var separation = GetSeparationVelocity();
		var pursuitMultiplier = PursuitSpeedMultiplier.Clamp(0f, 1f);
		var desiredVelocity = (velocity + separation) * pursuitMultiplier;
		var speedLimit = System.MathF.Max(0f, maximumSpeed) * pursuitMultiplier;
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
		var displacement = _pursuitVelocity * MovementMultiplier * Time.Delta;
		WorldPosition += MoveWithWorldCollision(
			displacement: displacement,
			radius: HitboxRadius,
			collisionHeight: GameWeaponProjectile.CollisionHeight
		);
	}

	private Vector3 GetSeparationVelocity()
	{
		var separation = Vector3.Zero;
		var speedLimit = System.MathF.Max(0f, SeparationSpeed);
		foreach (var other in Scene.GetAllComponents<GameEnemy>())
		{
			if (other == this || other.Health <= 0f || other.IsDescending)
			{
				continue;
			}

			var offset = (WorldPosition - other.WorldPosition).WithZ(0f);
			var distanceSquared = offset.LengthSquared;
			var bodySpacing = HitboxRadius + other.HitboxRadius;
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
		WorldRotation = Rotation.LookAt(LungeDirection);
		FacingDirection = LungeDirection.WithZ(0f).Normal;
		var lungeDistance = System.MathF.Max(0f, PunchLungeDistance);
		var lungeDuration = System.MathF.Max(0.05f, PunchLungeDuration);
		var displacement = LungeDirection * (lungeDistance / lungeDuration) * Time.Delta;
		WorldPosition += MoveWithWorldCollision(
			displacement: displacement,
			radius: HitboxRadius,
			collisionHeight: GameWeaponProjectile.CollisionHeight
		);
	}

	private void StopAttackMovement()
	{
		_pursuitVelocity = Vector3.Zero;
	}

	protected override void OnDamaged(Vector3 hitDirection)
	{
		if (Health <= 0f || IsDescending)
		{
			return;
		}

		PlayHitAnimation(cancelMelee: HitStaggerDuration > 0f);

		if (HitStaggerDuration > 0f)
		{
			IsWindingUp = false;
			IsLunging = false;
			GetComponent<GameEnemyRanged>()?.CancelAttack();
			StaggerEndTime = Time.Now + HitStaggerDuration;
			MovementUnlockTime = System.MathF.Max(MovementUnlockTime, StaggerEndTime);
			NextPunchTime = System.MathF.Max(NextPunchTime, StaggerEndTime);
			StopAttackMovement();
		}

	}

	protected override void OnHealthDepleted()
	{
		GetComponent<GameEnemyRanged>()?.CancelAttack();
		IsWindingUp = false;
		IsLunging = false;
		StopAttackMovement();
		var hitbox = GetComponent<CapsuleCollider>();
		if (hitbox.IsValid())
		{
			hitbox.Enabled = false;
		}

		var boss = GetComponent<GameEnemyBoss>();
		if (boss.IsValid())
		{
			boss.Defeated(this);
			return;
		}
		GameExperienceOrb.Spawn(
			scene: Scene,
			position: WorldPosition,
			amount: ExperienceDrop
		);

		var exploder = GetComponent<GameEnemyExploding>();
		if (exploder.IsValid())
		{
			RestoreMaterials();
			exploder.ConfigureDeathExplosion(GameObject);
			return;
		}

		GameObject.Destroy();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (_spawnBeam.IsValid())
		{
			_spawnBeam.Destroy();
		}

		if (Networking.IsHost && !IsProxy)
		{
			IsWindingUp = false;
			IsLunging = false;
			MovementUnlockTime = 0f;
			StopAttackMovement();
		}
	}
}
