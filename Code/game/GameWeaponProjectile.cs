using Sandbox;

public sealed class GameWeaponProjectile : Component
{
	public const float CollisionHeight = 36f;
	[Sync(SyncFlags.FromHost)] public GameEntity Shooter { get; private set; }
	[Sync(SyncFlags.FromHost)] private bool IsEnemyProjectile { get; set; }
	[Sync(SyncFlags.FromHost)] public Vector3 Direction { get; private set; }
	[Sync(SyncFlags.FromHost)] public float Speed { get; private set; }
	[Sync(SyncFlags.FromHost)] public float Radius { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool ShowHitbox { get; private set; }
	[Sync(SyncFlags.FromHost)] public Vector3 CollisionOffset { get; private set; }
	public Vector3 CollisionPosition => WorldPosition + CollisionOffset;

	private float _damage;
	private float _remainingRange;
	private bool _finished;

	public static void Spawn(GamePlayer shooter, GameWeaponDefinition weapon, Vector3 position, Vector3 direction, float range)
	{
		Spawn(shooter, position, direction, weapon.Damage, weapon.ProjectileSpeed, weapon.ProjectileRadius, range);
	}

	public static void SpawnEnemy(GameEnemy shooter, Vector3 position, Vector3 direction,
		float damage, float speed, float radius, float range)
	{
		Spawn(shooter, position, direction, damage, speed, radius, range);
	}

	private static void Spawn(GameEntity shooter, Vector3 position, Vector3 direction,
		float damage, float speed, float radius, float range)
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
		projectile.Direction = direction;
		projectile.Speed = speed;
		projectile.Radius = radius;
		projectile.ShowHitbox = shooter.ShowAttackHitboxes;
		// Keep the visible shot at the muzzle while gameplay collision travels at waist height.
		projectile.CollisionOffset = Vector3.Up * (shooter.WorldPosition.z + CollisionHeight - position.z);
		var collisionOrigin = position + projectile.CollisionOffset;
		var clearance = projectile.CreateCollisionTrace()
			.Sphere(radius, shooter.WorldPosition + Vector3.Up * CollisionHeight, collisionOrigin)
			.Run();
		if (clearance.Hit || clearance.StartedSolid)
			bullet.WorldPosition = clearance.EndPosition - projectile.CollisionOffset;
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
		renderer.Tint = IsEnemyProjectile ? new Color(1f, 0.15f, 0.8f) : new Color(1f, 0.8f, 0.2f);
		if (IsEnemyProjectile)
			visual.LocalScale = Vector3.One * (Radius * 2f / renderer.Model.Bounds.Size.x);
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		if (ShowHitbox && GameHUD.IsDebugEnabled(Scene))
			DebugOverlay.Sphere(new Sphere(CollisionPosition, Radius), Color.Yellow, 0f);
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || _finished)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if ((!Shooter.IsValid() && !IsEnemyProjectile) || (gameManager != null && gameManager.State != GameState.InProgress))
		{
			Finish();
			return;
		}

		var distance = System.MathF.Min(Speed * Time.Delta, _remainingRange);
		var start = CollisionPosition;
		var end = start + Direction * distance;
		var trace = CreateCollisionTrace().Sphere(Radius, start, end).Run();
		if (trace.Hit || trace.StartedSolid)
		{
			var target = trace.GameObject.IsValid() ? trace.GameObject.GetComponentInParent<GameEntity>() : null;
			var canDamage = target.IsValid() && target.CanTakeDamage && target.Health > 0f
				&& (IsEnemyProjectile ? target is GamePlayer : target is GameEnemy or GameSupplyCrate);
			DrawTravel(start, trace.EndPosition, canDamage ? Color.Green : Color.Red);
			Finish();
			if (canDamage)
				target.TakeDamage(_damage, Direction, GameDamageType.Physical, Shooter);
			return;
		}

		WorldPosition = end - CollisionOffset;
		_remainingRange -= distance;
		if (ShowHitbox)
			DrawTravel(start, end, Color.Yellow);
		if (_remainingRange <= 0f)
			Finish();
	}

	private SceneTrace CreateCollisionTrace()
	{
		var trace = Scene.Trace.IgnoreGameObjectHierarchy(GameObject);
		if (IsEnemyProjectile)
		{
			// Apply to both muzzle clearance and travel, including enemies spawned after this shot.
			foreach (var enemy in Scene.GetAllComponents<GameEnemy>())
				trace = trace.IgnoreGameObjectHierarchy(enemy.GameObject);
		}
		else if (Shooter.IsValid())
		{
			trace = trace.IgnoreGameObjectHierarchy(Shooter.GameObject);
		}

		return trace;
	}

	private void Finish()
	{
		_finished = true;
		GameObject.Destroy();
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void DrawTravel(Vector3 start, Vector3 end, Color color)
	{
		if (!ShowHitbox || !GameHUD.IsDebugEnabled(Scene))
			return;

		DebugOverlay.Line(start, end, color, 0.25f);
		DebugOverlay.Sphere(new Sphere(end, Radius), color, 0.25f);
	}
}
