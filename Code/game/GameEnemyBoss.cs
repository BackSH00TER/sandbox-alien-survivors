using Sandbox;

/// <summary>A large melee boss. Its death, rather than the countdown, completes the match.</summary>
public sealed class GameEnemyBoss : Component
{
	[Property, Range(1f, 10000f)] public float BossHealth { get; set; } = 1000f;
	[Property, Range(1f, 4f)] public float Size { get; set; } = 1.8f;
	[Property, Range(0f, 1000f)] public float MoveSpeed { get; set; } = 100f;
	[Property, Range(1f, 1000f)] public float PunchDamage { get; set; } = 25f;
	[Property, Range(1f, 500f)] public float PunchRange { get; set; } = 100f;

	[Property, Group("Stun Attack"), Range(0.1f, 10f), Sync(SyncFlags.FromHost)]
	public float StunChargeTime { get; set; } = 1.5f;
	[Property, Group("Stun Attack"), Range(1f, 1000f), Sync(SyncFlags.FromHost)]
	public float StunRadius { get; set; } = 120f;
	[Property, Group("Stun Attack"), Range(0.1f, 10f)]
	public float StunDuration { get; set; } = 1.5f;
	[Property, Group("Stun Attack"), Range(0.1f, 30f)]
	public float StunCooldown { get; set; } = 6f;
	[Property, Group("Stun Attack"), Range(1f, 2000f)]
	public float StunCastRange { get; set; } = 600f;

	[Sync(SyncFlags.FromHost)] private bool IsChargingStun { get; set; }
	[Sync(SyncFlags.FromHost)] private Vector3 StunCenter { get; set; }
	[Sync(SyncFlags.FromHost)] private float ChargeEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] private float ImpactTime { get; set; } = -100f;
	private float _nextStunTime;
	private GameObject _warning;
	private LineRenderer _boundary;
	private LineRenderer _progress;

	protected override void OnStart()
	{
		if (Networking.IsHost && !IsProxy)
			_nextStunTime = Time.Now + StunCooldown;

		_warning = new GameObject(GameObject, true, "Boss Stun Warning");
		_warning.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_boundary = CreateRing();
		_progress = CreateRing();
		_warning.Enabled = false;
	}

	private LineRenderer CreateRing()
	{
		var ring = _warning.AddComponent<LineRenderer>();
		ring.UseVectorPoints = true;
		ring.VectorPoints = new List<Vector3>();
		for (var i = 0; i <= 64; i++)
			ring.VectorPoints.Add(Vector3.Zero);
		ring.Width = 4f;
		ring.Lighting = false;
		ring.CastShadows = false;
		ring.Opaque = false;
		ring.Additive = true;
		return ring;
	}

	protected override void OnUpdate()
	{
		if (!_warning.IsValid())
			return;

		var enemy = GetComponent<GameEnemy>();
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		_warning.Enabled = enemy.IsValid() && enemy.Health > 0f
			&& (manager == null || manager.State == GameState.InProgress)
			&& (IsChargingStun || Time.Now < ImpactTime + 0.35f);
		if (!_warning.Enabled)
			return;

		var progress = IsChargingStun
			? (1f - (ChargeEndTime - Time.Now) / StunChargeTime).Clamp(0f, 1f) : 1f;
		_boundary.Color = IsChargingStun ? new Color(1f, 0.15f, 0.1f) : Color.White;
		_progress.Color = new Color(1f, 0.5f, 0.1f);
		for (var i = 0; i < _boundary.VectorPoints.Count; i++)
		{
			var angle = i * (System.MathF.PI * 2f / (_boundary.VectorPoints.Count - 1));
			var offset = new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * StunRadius;
			var center = StunCenter + Vector3.Up * 4f;
			_boundary.VectorPoints[i] = center + offset;
			_progress.VectorPoints[i] = center + offset * progress;
		}
	}

	/// <summary>Predicts the target's position at impact, then locks the warning so changing direction avoids it.</summary>
	public bool TryStunAttack(PlayerController target)
	{
		if (!Networking.IsHost || IsProxy)
		{
			Log.Error("Boss stun attacks must run on the host.");
			return false;
		}

		if (IsChargingStun)
		{
			if (Time.Now >= ChargeEndTime)
			{
				IsChargingStun = false;
				ImpactTime = Time.Now;
				_nextStunTime = Time.Now + StunCooldown;
				foreach (var survivor in Scene.GetAllComponents<GamePlayer>())
				{
					if (!survivor.Active || survivor.Health <= 0f)
						continue;

					var offset = survivor.WorldPosition - StunCenter;
					if (new Vector3(offset.x, offset.y, 0f).LengthSquared <= StunRadius * StunRadius
						&& System.MathF.Abs(offset.z) <= GameWeaponProjectile.CollisionHeight)
						survivor.ApplyStun(StunDuration);
				}
			}
			return true;
		}

		if (Time.Now < _nextStunTime || !target.IsValid()
			|| (target.WorldPosition - WorldPosition).LengthSquared > StunCastRange * StunCastRange)
			return false;

		if (!target.Body.IsValid())
		{
			Log.Error("Boss stun prediction requires the target's controller body.");
			return false;
		}

		var velocity = target.Body.Velocity;
		var horizontalVelocity = new Vector3(velocity.x, velocity.y, 0f);
		StunCenter = target.WorldPosition + horizontalVelocity * StunChargeTime;
		ChargeEndTime = Time.Now + StunChargeTime;
		IsChargingStun = true;
		return true;
	}

	public void CancelStunAttack()
	{
		IsChargingStun = false;
		ImpactTime = -100f;
	}

	public void Configure(GameEnemy enemy)
	{
		enemy.MaxHealth = BossHealth;
		enemy.MoveSpeed = MoveSpeed;
		enemy.PunchDamage = PunchDamage;
		enemy.PunchRange = PunchRange;
		enemy.HitStaggerDuration = 0f;
		enemy.HitReactionStrength = 0.15f;
		enemy.ExperienceDrop = 0;
		GameObject.WorldScale *= Size;
	}

	public void Defeated(GameEnemy enemy)
	{
		CancelStunAttack();
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!manager.IsValid())
		{
			Log.Error("Boss death requires a GameManager to end the match.");
			return;
		}

		manager.BossDefeated(enemy);
	}

	protected override void OnDisabled()
	{
		if (_warning.IsValid())
			_warning.Destroy();
	}
}
