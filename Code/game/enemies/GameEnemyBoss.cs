using Sandbox;

/// <summary>A large melee boss. Its death, rather than the countdown, completes the match.</summary>
public sealed class GameEnemyBoss : GameEnemy
{
	public override GameEnemyKind Kind => GameEnemyKind.Boss;
	[Property, Range(1f, 10000f), Sync(SyncFlags.FromHost)] public override float MaxHealth { get; set; } = 1000f;
	[Property, Range(0, 100)] public override int ExperienceDrop { get; set; } = 0;
	[Property, Range(1f, 1000f)] public override float PunchDamage { get; set; } = 25f;
	[Property, Range(1f, 500f)] public override float PunchRange { get; set; } = 100f;
	[Property, Range(1f, 500f)] public override float PunchHitRange { get; set; } = 80f;

	[Property, Group("Stun Attack"), Range(0.1f, 10f), Sync(SyncFlags.FromHost)] public float StunChargeTime { get; set; } = 1.5f;
	[Property, Group("Stun Attack"), Range(1f, 1000f), Sync(SyncFlags.FromHost)] public float StunRadius { get; set; } = 120f;
	[Property, Group("Stun Attack"), Range(0.1f, 10f)] public float StunDuration { get; set; } = 1.5f;
	[Property, Group("Stun Attack"), Range(0.1f, 30f)] public float StunCooldown { get; set; } = 6f;
	[Property, Group("Stun Attack"), Range(1f, 2000f)] public float StunCastRange { get; set; } = 600f;

	[Sync(SyncFlags.FromHost)] private bool IsChargingStun { get; set; }
	[Sync(SyncFlags.FromHost)] private Vector3 StunCenter { get; set; }
	[Sync(SyncFlags.FromHost)] private float ChargeEndTime { get; set; }
	[Sync(SyncFlags.FromHost)] private float ImpactTime { get; set; } = -100f;
	private float _nextStunTime;
	private GameObject _stunAreaIndicator;
	private LineRenderer _boundary;
	private LineRenderer _progress;

	protected override void OnStart()
	{
		if (Networking.IsHost && !IsProxy)
		{
			_nextStunTime = Time.Now + StunCooldown;
		}

		base.OnStart();
		_stunAreaIndicator = new GameObject(
			parent: GameObject,
			enabled: true,
			name: "Boss Stun Warning"
		);
		_stunAreaIndicator.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		_boundary = CreateRing();
		_progress = CreateRing();
		_stunAreaIndicator.Enabled = false;
	}

	private LineRenderer CreateRing()
	{
		var ring = _stunAreaIndicator.AddComponent<LineRenderer>();
		ring.UseVectorPoints = true;
		ring.VectorPoints = new List<Vector3>();
		for (var i = 0; i <= 64; i++)
		{
			ring.VectorPoints.Add(Vector3.Zero);
		}

		ring.Width = 4f;
		ring.Lighting = false;
		ring.CastShadows = false;
		ring.Opaque = false;
		ring.Additive = true;
		return ring;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		if (!_stunAreaIndicator.IsValid())
		{
			return;
		}

		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		_stunAreaIndicator.Enabled = Health > 0f
			&& (manager == null || manager.State == GameState.InProgress)
			&& (IsChargingStun || Time.Now < ImpactTime + 0.35f);
		if (!_stunAreaIndicator.Enabled)
		{
			return;
		}

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
	private bool TryStunAttack(PlayerController target)
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
				foreach (var player in Scene.GetAllComponents<GamePlayer>())
				{
					if (!player.Active || player.Health <= 0f)
					{
						continue;
					}

					var offset = player.WorldPosition - StunCenter;
					if (new Vector3(offset.x, offset.y, 0f).LengthSquared <= StunRadius * StunRadius
						&& System.MathF.Abs(offset.z) <= GameWeaponProjectile.CollisionHeight)
					{
						player.ApplyStun(StunDuration);
					}
				}
			}
			return true;
		}

		if (Time.Now < _nextStunTime || !target.IsValid()
			|| (target.WorldPosition - WorldPosition).LengthSquared > StunCastRange * StunCastRange)
		{
			return false;
		}

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

	protected override void UpdateCombat(PlayerController target, float distanceSquared)
	{
		if (TryStunAttack(target: target))
		{
			base.CancelAttack();
			return;
		}

		base.UpdateCombat(target: target, distanceSquared: distanceSquared);
	}

	protected override void CancelAttack()
	{
		IsChargingStun = false;
		ImpactTime = -100f;
		base.CancelAttack();
	}

	protected override void OnHealthDepleted()
	{
		CancelAttack();
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!manager.IsValid())
		{
			Log.Error("Boss death requires a GameManager to end the match.");
			return;
		}

		manager.BossDefeated(enemy: this);
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (_stunAreaIndicator.IsValid())
		{
			_stunAreaIndicator.Destroy();
		}
	}
}
