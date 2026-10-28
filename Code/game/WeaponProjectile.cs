using Sandbox;

public sealed class WeaponProjectile : Component
{
	[Sync(SyncFlags.FromHost)] public SurvivorController Shooter { get; private set; }
	[Sync(SyncFlags.FromHost)] public Vector3 Direction { get; private set; }
	[Sync(SyncFlags.FromHost)] public float Speed { get; private set; }
	[Sync(SyncFlags.FromHost)] public float Radius { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool ShowHitbox { get; private set; }

	private float _damage;
	private float _remainingRange;
	private bool _finished;

	public static void Spawn(SurvivorController shooter, WeaponDefinition weapon, Vector3 position, Vector3 direction, float range)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Weapon projectiles must be spawned on the host.");
			return;
		}

		var bullet = new GameObject(true, "Weapon Projectile");
		bullet.WorldPosition = position;
		bullet.WorldRotation = Rotation.LookAt(direction);
		var projectile = bullet.AddComponent<WeaponProjectile>();
		projectile.Shooter = shooter;
		projectile.Direction = direction;
		projectile.Speed = weapon.ProjectileSpeed;
		projectile.Radius = weapon.ProjectileRadius;
		projectile.ShowHitbox = shooter.ShowAttackHitboxes;
		projectile._damage = weapon.Damage;
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
		renderer.Tint = new Color(1f, 0.8f, 0.2f);
		renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		if (ShowHitbox && GameHUD.IsDebugEnabled(Scene))
			DebugOverlay.Sphere(new Sphere(WorldPosition, Radius), Color.Yellow, 0f);
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || _finished)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Shooter.IsValid() || (gameManager != null && gameManager.State != GameState.InProgress))
		{
			Finish();
			return;
		}

		var distance = System.MathF.Min(Speed * Time.Delta, _remainingRange);
		var start = WorldPosition;
		var end = start + Direction * distance;
		var trace = Scene.Trace.Sphere(Radius, start, end)
			.IgnoreGameObjectHierarchy(Shooter.GameObject)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		if (trace.Hit || trace.StartedSolid)
		{
			var enemy = trace.GameObject.IsValid() ? trace.GameObject.GetComponentInParent<Enemy>() : null;
			var hitEnemy = enemy.IsValid() && enemy.CanTakeDamage;
			DrawTravel(start, trace.EndPosition, hitEnemy ? Color.Green : Color.Red);
			Finish();
			if (hitEnemy)
				enemy.TakeDamage(_damage, Direction);
			return;
		}

		WorldPosition = end;
		_remainingRange -= distance;
		if (ShowHitbox)
			DrawTravel(start, end, Color.Yellow);
		if (_remainingRange <= 0f)
			Finish();
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
