using Sandbox;

/// <summary>Ranged enemy settings, firing and post-shot wandering, driven by Enemy's lifecycle.</summary>
public sealed class GameEnemyRanged : Component
{
	[Property, Range(0, 100), Description("XP dropped by this ranged variant instead of the base enemy reward.")] public int ExperienceDrop { get; set; } = 4;
	[Property, Range(1f, 2000f)] public float FiringRange { get; set; } = 400f;
	[Property, Range(0.1f, 10f)] public float ShotInterval { get; set; } = 2f;
	[Property, Range(0.1f, 5f), Description("Visible charge time before each shot, in seconds.")] public float ShotWindup { get; set; } = 0.5f;
	[Property, Range(1f, 1000f)] public float Damage { get; set; } = 10f;
	[Property, Range(1f, 2000f)] public float ProjectileSpeed { get; set; } = 300f;
	[Property, Range(0.1f, 16f)] public float ProjectileRadius { get; set; } = 6f;

	[Sync(SyncFlags.FromHost)] private bool IsCharging { get; set; }
	private float _chargeEndTime;
	private float _nextShotTime;
	private Vector3 _wanderDirection;
	private SkinnedModelRenderer _bodyRenderer;
	private Vector3 _bodyPosition;
	private float _hoverPhase;

	protected override void OnStart()
	{
		_bodyRenderer = GetComponentsInChildren<SkinnedModelRenderer>().FirstOrDefault();
		if (!_bodyRenderer.IsValid())
		{
			Log.Error("Ranged enemy hover requires a SkinnedModelRenderer.");
		}
		else if (_bodyRenderer.GameObject == GameObject)
		{
			Log.Error("Ranged enemy hover requires its model on a separate visual child.");
			_bodyRenderer = null;
		}
		else
		{
			_bodyRenderer.GameObject.Flags |= GameObjectFlags.NotNetworked;
			_bodyPosition = _bodyRenderer.LocalPosition;
		}
		_hoverPhase = WorldPosition.x * 0.017f + WorldPosition.y * 0.031f;

		if (Networking.IsHost && !IsProxy)
		{
			var enemy = GetComponent<GameEnemy>();
			if (!enemy.IsValid())
			{
				Log.Error("Ranged enemy settings require a GameEnemy component.");
				return;
			}

			enemy.ExperienceDrop = ExperienceDrop;
		}
	}

	private void UpdateHover()
	{
		if (!_bodyRenderer.IsValid())
		{
			return;
		}

		_hoverPhase += Time.Delta * System.MathF.PI * 2f / 1.4f;
		var height = 20f + System.MathF.Sin(_hoverPhase) * 5f;
		_bodyRenderer.LocalPosition = _bodyPosition + Vector3.Up * height;
	}

	protected override void OnUpdate()
	{
		UpdateHover();
	}

	/// <summary>Handles in-range combat movement; false lets Enemy pursue an out-of-range target.</summary>
	public bool TryAttack(GameEnemy enemy, GamePlayer target, out Vector3 movementVelocity)
	{
		movementVelocity = Vector3.Zero;
		if (!Networking.IsHost || IsProxy)
		{
			Log.Error("Ranged enemy attacks must run on the host.");
			return false;
		}

		var origin = WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
		var offset = target.WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight - origin;
		if (offset.LengthSquared > FiringRange * FiringRange)
		{
			CancelAttack();
			return false;
		}

		if (Time.Now < _nextShotTime)
		{
			movementVelocity = GetWanderVelocity(enemy, offset);
			return true;
		}

		if (!IsCharging)
		{
			IsCharging = true;
			_chargeEndTime = Time.Now + ShotWindup;
			return true;
		}

		if (Time.Now < _chargeEndTime)
		{
			return true;
		}

		IsCharging = false;
		_nextShotTime = Time.Now + ShotInterval;
		var shotDirection = offset.LengthSquared > 0.001f ? offset.Normal : enemy.FacingDirection;
		GameWeaponProjectile.SpawnEnemy(
			shooter: enemy,
			position: origin,
			direction: shotDirection,
			damage: Damage,
			speed: ProjectileSpeed,
			radius: ProjectileRadius,
			range: FiringRange
		);
		var horizontal = new Vector3(offset.x, offset.y, 0f);
		var forward = horizontal.LengthSquared > 0.001f ? horizontal.Normal : Vector3.Forward;
		var angle = Game.Random.Float(65f, 115f) * (Game.Random.Float(0f, 1f) < 0.5f ? -1f : 1f);
		_wanderDirection = Rotation.FromYaw(angle) * forward;
		movementVelocity = GetWanderVelocity(enemy, offset);
		return true;
	}

	private Vector3 GetWanderVelocity(GameEnemy enemy, Vector3 targetOffset)
	{
		if (_wanderDirection.LengthSquared <= 0.001f)
		{
			return Vector3.Zero;
		}

		var horizontal = new Vector3(targetOffset.x, targetOffset.y, 0f);
		var direction = _wanderDirection;
		// Steer back into firing range, or away from a player who is too close.
		if (horizontal.Length > FiringRange * 0.8f)
		{
			direction = (direction + horizontal.Normal * 2f).Normal;
		}
		else if (horizontal.Length < FiringRange * 0.35f && horizontal.LengthSquared > 0.001f)
		{
			direction = (direction - horizontal.Normal * 2f).Normal;
		}

		var origin = WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
		var trace = Scene.Trace.Sphere(enemy.HitboxRadius, origin, origin + direction * 48f)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		if (trace.StartedSolid)
		{
			return Vector3.Zero;
		}

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
}
