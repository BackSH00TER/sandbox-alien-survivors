using Sandbox;

public sealed class GameWeaponProjectile : Component
{
	public const float CollisionHeight = 36f;
	[Sync(SyncFlags.FromHost)]
	public GameEntity Shooter { get; private set; }
	[Sync(SyncFlags.FromHost)] private bool IsEnemyProjectile { get; set; }
	[Sync(SyncFlags.FromHost)]
	public Vector3 Direction { get; private set; }
	[Sync(SyncFlags.FromHost)]
	public float Speed { get; private set; }
	[Sync(SyncFlags.FromHost)]
	public float Radius { get; private set; }
	[Sync(SyncFlags.FromHost)]
	public bool ShowHitbox { get; private set; }
	[Sync(SyncFlags.FromHost)]
	public Vector3 CollisionOffset { get; private set; }
	[Sync(SyncFlags.FromHost)] public GameDamageType EffectType { get; private set; } = GameDamageType.Physical;
	public Vector3 CollisionPosition => WorldPosition + CollisionOffset;

	private float _damage;
	private float _remainingRange;
	private bool _finished;
	private DamageEffectSettings _effect;
	private GameBulletModifiers _modifiers = new();
	private int _remainingPierces;
	private bool _piercing;
	private readonly HashSet<GameObject> _hitObjects = new();

	public static void Spawn(
		GamePlayer shooter,
		GameWeaponDefinition weapon,
		Vector3 position,
		Vector3 direction,
		float range,
		float damageMultiplier = 1f
	)
	{
		Spawn(
			shooter: shooter,
			position: position,
			direction: direction,
			damage: weapon.Damage * damageMultiplier,
			speed: weapon.ProjectileSpeed,
			radius: weapon.ProjectileRadius,
			range: range
		);
	}

	public static void SpawnEnemy(
		GameEnemy shooter,
		Vector3 position,
		Vector3 direction,
		float damage,
		float speed,
		float radius,
		float range
	)
	{
		Spawn(
			shooter: shooter,
			position: position,
			direction: direction,
			damage: damage,
			speed: speed,
			radius: radius,
			range: range
		);
	}

	private static void Spawn(
		GameEntity shooter,
		Vector3 position,
		Vector3 direction,
		float damage,
		float speed,
		float radius,
		float range
	)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Weapon projectiles must be spawned on the host.");
			return;
		}

		var bullet = new GameObject(true, "Weapon Projectile");
		bullet.WorldPosition = position;
		bullet.WorldRotation = Rotation.LookAt(direction);
		var projectile = bullet.AddComponent<GameWeaponProjectile>();
		projectile.Shooter = shooter;
		projectile.IsEnemyProjectile = shooter is GameEnemy;
		if (shooter is GamePlayer player)
		{
			projectile._effect = DamageEffectCatalog.Roll(player.ModifierStacks, () => Game.Random.Float());
			projectile.EffectType = projectile._effect?.Type ?? GameDamageType.Physical;
			projectile._modifiers = GameShotModifiers.CaptureBullet(player.ModifierStacks, () => Game.Random.Float());
			projectile._remainingPierces = projectile._modifiers.AdditionalTargets;
			projectile._piercing = projectile._remainingPierces > 0;
		}
		projectile.Direction = direction;
		projectile.Speed = speed;
		projectile.Radius = radius;
		projectile.ShowHitbox = shooter.ShowAttackHitboxes;
		// Keep the visible shot at the muzzle while gameplay collision travels at waist height.
		projectile.CollisionOffset = Vector3.Up * (shooter.WorldPosition.z + CollisionHeight - position.z);
		var collisionOrigin = position + projectile.CollisionOffset;
		var clearanceOrigin = shooter.WorldPosition + Vector3.Up * CollisionHeight;
		var clearance = projectile.CreateCollisionTrace()
			.Sphere(
				radius: radius,
				from: clearanceOrigin,
				to: collisionOrigin
			)
			.Run();
		if (clearance.Hit || clearance.StartedSolid)
		{
			bullet.WorldPosition = clearance.EndPosition - projectile.CollisionOffset;
		}

		projectile._damage = damage;
		projectile._remainingRange = range;
		if (!bullet.NetworkSpawn())
		{
			bullet.Destroy();
			Log.Error("Failed to network-spawn weapon projectile.");
		}
	}

	protected override void OnStart()
	{
		var visual = new GameObject(GameObject, true, "Bullet Visual");
		visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		var renderer = visual.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/sphere.vmdl");
		// Exaggerate the visible bullet without enlarging its collision radius.
		visual.LocalScale = new Vector3(20f, 7f, 7f) / renderer.Model.Bounds.Size.x;
		renderer.Tint = IsEnemyProjectile ? new Color(1f, 0.15f, 0.8f)
			: EffectType != GameDamageType.Physical ? DamageEffectCatalog.Get(EffectType).BulletColor
			: new Color(1f, 0.8f, 0.2f);
		if (IsEnemyProjectile)
		{
			visual.LocalScale = Vector3.One * (Radius * 2f / renderer.Model.Bounds.Size.x);
		}

		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		if (ShowHitbox)
		{
			GameDebugTools.DrawProjectileHitbox(
				projectile: this,
				position: CollisionPosition,
				radius: Radius
			);
		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || _finished)
		{
			return;
		}

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ((!Shooter.IsValid() && !IsEnemyProjectile) || (gameManager != null && gameManager.State != GameState.InProgress))
		{
			Finish();
			return;
		}

		var distance = System.MathF.Min(Speed * Time.Delta, _remainingRange);
		var start = CollisionPosition;
		var end = start + Direction * distance;
		while (true)
		{
			if (gameManager.IsValid() && gameManager.State != GameState.InProgress)
			{
				Finish();
				return;
			}

			var trace = CreateCollisionTrace().Sphere(
				radius: Radius,
				from: start,
				to: end
			).Run();
			if (!trace.Hit && !trace.StartedSolid)
			{
				break;
			}

			var target = trace.GameObject.IsValid() ? trace.GameObject.GetComponentInParent<GameEntity>() : null;
			var canDamage = target.IsValid() && target.CanTakeDamage && target.Health > 0f
				&& (IsEnemyProjectile ? target is GamePlayer : target is GameEnemy or GameSupplyCrate or GameDropship);
			DrawTravel(
				start: start,
				end: trace.EndPosition,
				color: canDamage ? Color.Green : Color.Red
			);
			var hitEnemy = canDamage && target is GameEnemy;
			var canPierce = hitEnemy && _remainingPierces > 0;
			if (canDamage)
			{
				// Capture visibility and the hit hierarchy before a lethal hit creates a corpse.
				var splashTargets = hitEnemy && _modifiers.Splash ? FindSplashTargets((GameEnemy)target) : null;
				if (splashTargets != null && Shooter is GamePlayer splashShooter)
				{
					splashShooter.ShowSplashImpact(target.WorldPosition);
				}
				if (canPierce)
				{
					_hitObjects.Add(target.GameObject);
				}
				if (target is GameEnemy aggroEnemy && Shooter is GamePlayer playerShooter)
				{
					aggroEnemy.SetAggroTarget(playerShooter);
				}

				target.TakeDamage(
					amount: _damage,
					hitDirection: Direction,
					type: GameDamageType.Physical,
					attacker: Shooter
				);
				if (_modifiers.SlowDuration > 0f && target.IsValid() && target is GameEnemy slowedEnemy && slowedEnemy.CanTakeDamage)
				{
					slowedEnemy.ApplySlow(_modifiers.SlowDuration);
				}
				if (_effect != null && target.IsValid() && target is GameEnemy enemy && enemy.CanTakeDamage && enemy.Health > 0f)
				{
					enemy.ApplyDamageEffect(_effect, Shooter);
				}
				if (splashTargets != null)
				{
					ApplySplash(splashTargets, gameManager);
				}
			}

			if (!canPierce)
			{
				Finish();
				return;
			}

			_remainingPierces--;
			start = trace.EndPosition;
		}

		WorldPosition = end - CollisionOffset;
		_remainingRange -= distance;
		if (ShowHitbox)
		{
			DrawTravel(
				start: start,
				end: end,
				color: Color.Yellow
			);
		}

		if (_remainingRange <= 0f)
		{
			Finish();
		}
	}

	private SceneTrace CreateCollisionTrace()
	{
		var trace = Scene.Trace.IgnoreGameObjectHierarchy(GameObject);
		if (_piercing)
		{
			trace = trace.WithoutTags(GameEntity.EnemyRagdollCollisionTag);
		}
		foreach (var hitObject in _hitObjects)
		{
			if (hitObject.IsValid())
			{
				trace = trace.IgnoreGameObjectHierarchy(hitObject);
			}
		}
		if (IsEnemyProjectile)
		{
			// Apply to both muzzle clearance and travel, including enemies spawned after this shot.
			foreach (var enemy in Scene.GetAllComponents<GameEnemy>())
			{
				trace = trace.IgnoreGameObjectHierarchy(enemy.GameObject);
			}
		}
		else if (Shooter.IsValid())
		{
			trace = trace.IgnoreGameObjectHierarchy(Shooter.GameObject);
		}

		return trace;
	}

	private List<GameEnemy> FindSplashTargets(GameEnemy initialTarget)
	{
		var targets = new List<GameEnemy>();
		foreach (var enemy in Scene.GetAllComponents<GameEnemy>())
		{
			if (enemy == initialTarget || !enemy.Active || !enemy.CanTakeDamage
				|| (enemy.WorldPosition - initialTarget.WorldPosition).LengthSquared > GameShotModifiers.SplashRadius * GameShotModifiers.SplashRadius)
			{
				continue;
			}

			var visible = GameEntity.HasEffectLineOfSight(
				scene: Scene,
				originPosition: initialTarget.WorldPosition,
				originObject: initialTarget.GameObject,
				enemy: enemy
			);
			if (visible)
			{
				targets.Add(enemy);
			}
		}
		return targets;
	}

	private void ApplySplash(List<GameEnemy> targets, GameManager gameManager)
	{
		foreach (var enemy in targets)
		{
			if (!Shooter.IsValid() || (gameManager.IsValid() && gameManager.State != GameState.InProgress))
			{
				return;
			}
			if (!enemy.IsValid() || !enemy.Active || !enemy.CanTakeDamage)
			{
				continue;
			}

			if (Shooter is GamePlayer player)
			{
				enemy.SetAggroTarget(player);
			}
			enemy.TakeDamage(
				amount: _damage * _modifiers.SplashDamageMultiplier,
				hitDirection: Direction,
				type: GameDamageType.Explosive,
				attacker: Shooter
			);
		}
	}

	private void Finish()
	{
		_finished = true;
		GameObject.Destroy();
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void DrawTravel(
		Vector3 start,
		Vector3 end,
		Color color
	)
	{
		if (!ShowHitbox)
		{
			return;
		}

		GameDebugTools.DrawProjectileTravel(
			projectile: this,
			start: start,
			end: end,
			color: color,
			radius: Radius
		);
	}
}
