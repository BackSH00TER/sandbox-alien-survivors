using Sandbox;

/// <summary>Host-authoritative death fuse configured by ExplodingEnemy, with a client-visible warning and blast.</summary>
public sealed class GameEnemyExplosion : Component
{
	[Sync(SyncFlags.FromHost)] public float FuseDuration { get; set; }
	[Sync(SyncFlags.FromHost)] public float BlastRadius { get; set; }
	[Sync(SyncFlags.FromHost)] public float BlastDamage { get; set; }
	[Sync(SyncFlags.FromHost)] private float DetonationTime { get; set; }
	[Sync(SyncFlags.FromHost)] private bool HasExploded { get; set; }
	[Sync(SyncFlags.FromHost)] private float ExplosionTime { get; set; }

	private const float BlastVisualDuration = 0.4f;
	private GameObject _visual;
	private LineRenderer _ring;
	private PointLight _light;

	protected override void OnStart()
	{
		if (Networking.IsHost && !IsProxy)
			DetonationTime = Time.Now + System.MathF.Max(0.1f, FuseDuration);

		_visual = new GameObject(GameObject, true, "Death Explosion Warning");
		_visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_ring = _visual.AddComponent<LineRenderer>();
		_ring.UseVectorPoints = true;
		_ring.VectorPoints = new List<Vector3>();
		for (var i = 0; i <= 48; i++)
			_ring.VectorPoints.Add(Vector3.Zero);
		_ring.Width = 4f;
		_ring.Lighting = false;
		_ring.CastShadows = false;
		_ring.Opaque = false;
		_ring.Additive = true;

		_light = _visual.AddComponent<PointLight>();
		_light.Radius = BlastRadius;
		_light.Shadows = false;
	}

	protected override void OnUpdate()
	{
		if (!_ring.IsValid())
			return;

		var progress = HasExploded
			? ((Time.Now - ExplosionTime) / BlastVisualDuration).Clamp(0f, 1f) : 0f;
		var pulse = 0.5f + 0.5f * System.MathF.Sin(Time.Now * 24f);
		var alpha = HasExploded ? 1f - progress : 0.4f + pulse * 0.6f;
		var color = new Color(1f, HasExploded ? 0.65f : 0.15f, 0.02f, alpha);
		_ring.Color = color;
		_ring.Width = HasExploded ? 12f * (1f - progress) : 3f + pulse * 3f;
		_light.LightColor = new Color(1f, 0.3f, 0.02f) * alpha * (HasExploded ? 4f : 1f);
		var radius = BlastRadius * (HasExploded ? progress : 1f);
		var center = WorldPosition + Vector3.Up * 4f;
		for (var i = 0; i < _ring.VectorPoints.Count; i++)
		{
			var angle = i * (System.MathF.PI * 2f / (_ring.VectorPoints.Count - 1));
			_ring.VectorPoints[i] = center + new Vector3(
				System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * radius;
		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy)
			return;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			GameObject.Destroy();
			return;
		}

		if (HasExploded)
		{
			if (Time.Now >= ExplosionTime + BlastVisualDuration)
				GameObject.Destroy();
			return;
		}

		if (Time.Now < DetonationTime)
			return;

		HasExploded = true;
		ExplosionTime = Time.Now;
		ApplyBlastDamage();
	}

	private void ApplyBlastDamage()
	{
		var center = WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
		// Death can spawn more objects, so snapshot targets before applying damage.
		var targets = Scene.GetAllComponents<GameEntity>().ToArray();
		var blastTrace = Scene.Trace.IgnoreGameObjectHierarchy(GameObject);
		foreach (var target in targets)
		{
			if (target is GameEnemy or GamePlayer)
				blastTrace = blastTrace.IgnoreGameObjectHierarchy(target.GameObject);
		}

		foreach (var target in targets)
		{
			if (!target.IsValid() || !target.Active || !target.CanTakeDamage || target.Health <= 0f
				|| (target is not GameEnemy && target is not GamePlayer))
				continue;

			var targetPosition = target.WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
			var offset = targetPosition - center;
			if (offset.LengthSquared > BlastRadius * BlastRadius)
				continue;

			var trace = blastTrace.FromTo(center, targetPosition).Run();
			if (trace.Hit || trace.StartedSolid)
				continue;

			target.TakeDamage(BlastDamage, offset.LengthSquared > 0.001f ? offset.Normal : Vector3.Up, GameDamageType.Explosive);
		}
	}

	protected override void OnDisabled()
	{
		if (_visual.IsValid())
			_visual.Destroy();
	}
}
