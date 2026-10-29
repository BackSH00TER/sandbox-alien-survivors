using Sandbox;

/// <summary>Ranged enemy settings, firing and post-shot wandering, driven by Enemy's lifecycle.</summary>
public sealed class RangedEnemy : Component
{
	[Property, Range(1f, 2000f)] public float FiringRange { get; set; } = 400f;
	[Property, Range(0.1f, 10f)] public float ShotInterval { get; set; } = 2f;
	[Property, Range(0.1f, 5f), Description("Visible charge time before each shot, in seconds.")]
	public float ShotWindup { get; set; } = 0.5f;
	[Property, Range(1f, 1000f)] public float Damage { get; set; } = 10f;
	[Property, Range(1f, 2000f)] public float ProjectileSpeed { get; set; } = 300f;
	[Property, Range(0.1f, 16f)] public float ProjectileRadius { get; set; } = 6f;

	[Sync(SyncFlags.FromHost)] private bool IsCharging { get; set; }
	private float _chargeEndTime;
	private float _nextShotTime;
	private Vector3 _wanderDirection;
	private GameObject _beacon;
	private ModelRenderer _beaconRenderer;

	protected override void OnStart()
	{
		_beacon = new GameObject(GameObject, true, "Ranged Enemy Beacon");
		_beacon.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_beacon.LocalPosition = Vector3.Up * 82f;
		_beaconRenderer = _beacon.AddComponent<ModelRenderer>();
		_beaconRenderer.Model = Model.Load("models/dev/sphere.vmdl");
		_beaconRenderer.RenderType = ModelRenderer.ShadowRenderType.Off;
	}

	protected override void OnUpdate()
	{
		if (!_beaconRenderer.IsValid())
			return;

		var pulse = IsCharging ? 1f + 0.3f * System.MathF.Sin(Time.Now * 30f) : 1f;
		_beacon.LocalScale = 14f * pulse / _beaconRenderer.Model.Bounds.Size.x;
		_beaconRenderer.Tint = IsCharging ? new Color(1f, 0.35f, 1f) : new Color(0.6f, 0.1f, 1f);
	}

	/// <summary>Handles in-range combat movement; false lets Enemy pursue an out-of-range target.</summary>
	public bool TryAttack(Enemy enemy, PlayerController target, out Vector3 wishVelocity)
	{
		wishVelocity = Vector3.Zero;
		if (!Networking.IsHost || IsProxy)
		{
			Log.Error("Ranged enemy attacks must run on the host.");
			return false;
		}

		var origin = WorldPosition + Vector3.Up * WeaponProjectile.CollisionHeight;
		var offset = target.WorldPosition + Vector3.Up * WeaponProjectile.CollisionHeight - origin;
		if (offset.LengthSquared > FiringRange * FiringRange)
		{
			CancelAttack();
			return false;
		}

		if (Time.Now < _nextShotTime)
		{
			wishVelocity = GetWanderVelocity(enemy, offset);
			return true;
		}

		if (!IsCharging)
		{
			IsCharging = true;
			_chargeEndTime = Time.Now + ShotWindup;
			return true;
		}

		if (Time.Now < _chargeEndTime)
			return true;

		IsCharging = false;
		_nextShotTime = Time.Now + ShotInterval;
		WeaponProjectile.SpawnEnemy(enemy, origin, offset.LengthSquared > 0.001f
			? offset.Normal : enemy.Controller.EyeAngles.ToRotation().Forward,
			Damage, ProjectileSpeed, ProjectileRadius, FiringRange);
		var horizontal = new Vector3(offset.x, offset.y, 0f);
		var forward = horizontal.LengthSquared > 0.001f ? horizontal.Normal : Vector3.Forward;
		var angle = Game.Random.Float(65f, 115f) * (Game.Random.Float(0f, 1f) < 0.5f ? -1f : 1f);
		_wanderDirection = Rotation.FromYaw(angle) * forward;
		wishVelocity = GetWanderVelocity(enemy, offset);
		return true;
	}

	private Vector3 GetWanderVelocity(Enemy enemy, Vector3 targetOffset)
	{
		if (_wanderDirection.LengthSquared <= 0.001f)
			return Vector3.Zero;

		var horizontal = new Vector3(targetOffset.x, targetOffset.y, 0f);
		var direction = _wanderDirection;
		// Steer back into firing range, or away from a player who is too close.
		if (horizontal.Length > FiringRange * 0.8f)
			direction = (direction + horizontal.Normal * 2f).Normal;
		else if (horizontal.Length < FiringRange * 0.35f && horizontal.LengthSquared > 0.001f)
			direction = (direction - horizontal.Normal * 2f).Normal;

		var origin = WorldPosition + Vector3.Up * WeaponProjectile.CollisionHeight;
		var trace = Scene.Trace.Sphere(enemy.Controller.BodyRadius, origin, origin + direction * 48f)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		if (trace.StartedSolid)
			return Vector3.Zero;
		if (trace.Hit)
		{
			direction -= trace.Normal * System.MathF.Min(0f, direction.Dot(trace.Normal));
			direction = new Vector3(direction.x, direction.y, 0f);
		}

		return direction * enemy.RangedMoveSpeed;
	}

	public void CancelAttack()
	{
		IsCharging = false;
		_wanderDirection = Vector3.Zero;
	}

	protected override void OnDisabled()
	{
		if (_beacon.IsValid())
			_beacon.Destroy();
	}
}
