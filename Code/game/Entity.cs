using Sandbox;

public abstract class Entity : Component
{
	[Property, Range(1f, 1000f), Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
	[Property, Range(0.01f, 1f)] public float HitFlashDuration { get; set; } = 0.12f;
	[Sync(SyncFlags.FromHost)] public float Health { get; private set; } = 100f;
	[Property, Group("Debug")] public bool ShowAttackHitboxes { get; set; } = true;
	public virtual bool CanTakeDamage => true;
	[Sync(SyncFlags.FromHost)] public NetDictionary<DamageType, int> EffectStacks { get; private set; } = new();
	private readonly TimedDamageEffects<Entity> _damageEffects = new();
	private readonly Dictionary<DamageType, float> _nextSpreadTimes = new();
	private GameManager _roundManager;

	private GameManager RoundManager
	{
		get
		{
			// Reuse the scene lookup while the manager is alive; reacquire it if removed or the scene changes.
			if (!_roundManager.IsValid() || _roundManager.Scene != Scene)
				_roundManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
			return _roundManager;
		}
	}

	private bool IsDamageRoundInProgress
	{
		get
		{
			var roundManager = RoundManager;
			return !roundManager.IsValid() || roundManager.State == GameState.InProgress;
		}
	}

	private bool CanReceiveEffectDamage =>
		this.IsValid() && Active && CanTakeDamage && Health > 0f && IsDamageRoundInProgress;

	public int EffectStackCount(DamageType type) => EffectStacks.TryGetValue(type, out var count) ? count : 0;

	/// <summary>0 to 1 amount of blood splatter drawn on the model. By default it follows how much health is missing.</summary>
	public virtual float BloodAmount => 1f - Health / System.MathF.Max(1f, MaxHealth);

	private const float PunchFacingThreshold = 0.5f;
	private const float PunchTraceHeight = 36f;
	private readonly Dictionary<ModelRenderer, Material> _originalMaterials = new();
	private Material _flashMaterial;
	private float _flashEndTime;
	internal bool IsHitFlashing => Time.Now < _flashEndTime;

	protected override void OnStart()
	{
		var blood = GameObject.GetOrAddComponent<BloodiedModel>();
		blood.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		if (Networking.IsHost)
			Health = MaxHealth;
	}

	public void TakeDamage(float amount, Vector3 hitDirection = default, DamageType type = DamageType.Physical, Entity attacker = null)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Damage must be applied on the host.");
			return;
		}

		if (!float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Damage must be a finite positive amount.");
			return;
		}

		if (!CanTakeDamage)
			return;

		float previousHealth = Health;
		Health = System.MathF.Max(0f, Health - amount);
		bool wasKill = previousHealth > 0f && Health <= 0f;
		PlayerStats.RecordDamage(attacker, this, previousHealth - Health, type, wasKill);
		FlashHit();
		OnDamaged(hitDirection);
		if (wasKill)
		{
			ClearDamageEffects();
			OnHealthDepleted();
		}
	}

	public void ApplyDamageEffect(DamageEffectSettings settings, Entity attacker)
	{
		if (!Networking.IsHost || settings == null || !attacker.IsValid()
			|| !DamageEffectCatalog.All.Any(effectDefinition => effectDefinition.Type == settings.Type))
		{
			Log.Error("Applying a damage effect requires the host, implemented effect settings and a valid attacker.");
			return;
		}

		// Pay any partial tick before replacing an application, so the previous attacker keeps that damage credit.
		UpdateDamageEffects(allowSpread: false);
		if (!CanReceiveEffectDamage)
			return;

		if (_damageEffects.Count(settings.Type, Time.Now) == 0)
			_nextSpreadTimes[settings.Type] = Time.Now + 1f;
		_damageEffects.Apply(settings, attacker, Time.Now);
		SyncEffectStacks();
	}

	private void UpdateDamageEffects(bool allowSpread)
	{
		if (!Networking.IsHost || _damageEffects.Stacks.Count == 0)
			return;

		if (!Active || Health <= 0f || !IsDamageRoundInProgress)
		{
			ClearDamageEffects();
			return;
		}

		// Invulnerability consumes effect time without letting damage accumulate for a later tick.
		_damageEffects.Advance(Time.Now, DealDamageEffectTick, flush: !allowSpread || !CanTakeDamage);
		if (!this.IsValid() || Health <= 0f || !IsDamageRoundInProgress)
			return;
		SyncEffectStacks();

		if (allowSpread && this is Enemy && CanTakeDamage)
			SpreadDamageEffects();
	}

	private void DealDamageEffectTick(TimedDamageStack<Entity> effectStack, float amount)
	{
		// A previous tick can kill the target or end the round, so check again for every application.
		if (!CanReceiveEffectDamage)
			return;

		var attacker = effectStack.Source.IsValid() ? effectStack.Source : null;
		TakeDamage(amount, type: effectStack.Settings.Type, attacker: attacker);
	}

	private void SpreadDamageEffects()
	{
		var spreadEffectStacks = _damageEffects.Stacks
			.Where(effectStack => effectStack.Settings.SpreadChance > 0f)
			.GroupBy(effectStack => effectStack.Settings.Type)
			.Select(stacksByType => stacksByType.Last())
			.ToArray();

		// Each effect type gets one chance to spread per second, regardless of stack count.
		// The newest spread-enabled application supplies the settings and original attacker.
		foreach (var effectStack in spreadEffectStacks)
			TrySpreadDamageEffect(effectStack);
	}

	private void TrySpreadDamageEffect(TimedDamageStack<Entity> effectStack)
	{
		var settings = effectStack.Settings;
		if (Time.Now < _nextSpreadTimes[settings.Type])
			return;
		_nextSpreadTimes[settings.Type] = Time.Now + 1f;
		if (!effectStack.Source.IsValid())
			return;

		var spreadRollSucceeded = settings.SpreadChance >= 1f || Game.Random.Float() < settings.SpreadChance;
		if (!spreadRollSucceeded)
			return;

		var spreadTargets = Scene.GetAllComponents<Enemy>()
			.Where(enemy => IsEligibleSpreadTarget(enemy, settings))
			.ToList();
		if (spreadTargets.Count == 0)
			return;

		var targetIndex = Game.Random.Int(0, spreadTargets.Count - 1);
		spreadTargets[targetIndex].ApplyDamageEffect(settings, effectStack.Source);
	}

	private bool IsEligibleSpreadTarget(Enemy enemy, DamageEffectSettings settings)
	{
		if (enemy == this || !enemy.Active || !enemy.CanTakeDamage || enemy.Health <= 0f)
			return false;

		// Spread can add to burning enemies below the cap, but cannot refresh a full target.
		if (!enemy._damageEffects.CanSpreadTo(settings, Time.Now))
			return false;
		if ((enemy.WorldPosition - WorldPosition).LengthSquared > settings.SpreadRadius * settings.SpreadRadius)
			return false;

		return HasSpreadLineOfSight(enemy);
	}

	private bool HasSpreadLineOfSight(Enemy enemy)
	{
		// Use bullet collision height so walls block spreading even when an enemy's feet are visible.
		var collisionHeight = Vector3.Up * WeaponProjectile.CollisionHeight;
		var trace = Scene.Trace
			.FromTo(WorldPosition + collisionHeight, enemy.WorldPosition + collisionHeight)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		return !trace.Hit || trace.GameObject.GetComponentInParent<Entity>() == enemy;
	}

	private void SyncEffectStacks()
	{
		foreach (var definition in DamageEffectCatalog.All)
		{
			var count = _damageEffects.Count(definition.Type, Time.Now);
			if (EffectStackCount(definition.Type) != count)
				EffectStacks[definition.Type] = count;
		}
	}

	private void ClearDamageEffects()
	{
		_damageEffects.Clear();
		_nextSpreadTimes.Clear();
		if (Networking.IsHost)
			EffectStacks.Clear();
	}

	protected virtual void OnHealthDepleted()
	{
	}

	public bool RestoreHealth(float amount)
	{
		if (!Networking.IsHost || !float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Healing requires a finite positive amount on the host.");
			return false;
		}

		if (Health <= 0f || Health >= MaxHealth)
			return false;

		Health = System.MathF.Min(MaxHealth, Health + amount);
		return true;
	}

	/// <summary>Host only. Brings a dead entity back with the given health. Does nothing if it is already alive.</summary>
	public bool Revive(float health)
	{
		if (!Networking.IsHost || !float.IsFinite(health) || health <= 0f)
		{
			Log.Error("Reviving requires a finite positive amount on the host.");
			return false;
		}

		if (Health > 0f)
			return false;

		Health = System.MathF.Min(MaxHealth, health);
		return true;
	}

	protected void IncreaseMaxHealth(float amount)
	{
		if (!Networking.IsHost || !float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Maximum health increases require a finite positive amount on the host.");
			return;
		}

		MaxHealth += amount;
		if (Health > 0f)
			Health = System.MathF.Min(MaxHealth, Health + amount);
	}

	protected virtual void OnDamaged(Vector3 hitDirection)
	{
	}

	protected void Punch<T>(PlayerController controller, float range, float damage) where T : Entity
	{
		T target = null;
		var closestDistanceSquared = range * range;
		foreach (var candidate in Scene.GetAllComponents<T>())
		{
			if (!candidate.Active || !candidate.CanTakeDamage || candidate == this)
				continue;

			var offset = candidate.WorldPosition - WorldPosition;
			var direction = new Vector3(offset.x, offset.y, 0f);
			if (offset.LengthSquared > closestDistanceSquared
				|| (direction.LengthSquared > 0.001f && direction.Normal.Dot(controller.EyeAngles.ToRotation().Forward) < PunchFacingThreshold))
				continue;

			var trace = Scene.Trace
				.FromTo(WorldPosition + Vector3.Up * PunchTraceHeight, candidate.WorldPosition + Vector3.Up * PunchTraceHeight)
				.IgnoreGameObjectHierarchy(GameObject)
				.Run();
			if (trace.Hit && trace.GameObject.GetComponentInParent<Entity>() != candidate)
				continue;

			target = candidate;
			closestDistanceSquared = offset.LengthSquared;
		}

		if (ShowAttackHitboxes)
			DrawPunchHitbox(WorldPosition, controller.EyeAngles.ToRotation().Forward, range,
				target != null, target?.WorldPosition ?? WorldPosition);

		if (target != null)
			target.TakeDamage(damage, (target.WorldPosition - WorldPosition).Normal, DamageType.Physical, this);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void DrawPunchHitbox(Vector3 origin, Vector3 forward, float range, bool hit, Vector3 targetPosition)
	{
		if (!ShowAttackHitboxes || !GameHUD.IsDebugEnabled(Scene))
			return;

		const float duration = 0.25f;
		const int segments = 24;
		var color = hit ? Color.Green : Color.Red;
		var planarForward = new Vector3(forward.x, forward.y, 0f);
		var planarLength = planarForward.Length;
		if (planarLength >= PunchFacingThreshold)
		{
			// Punch uses 3D distance but a horizontal facing test.
			var halfAngle = System.MathF.Acos((PunchFacingThreshold / planarLength).Clamp(0f, 1f));
			var yaw = System.MathF.Atan2(forward.y, forward.x);
			for (var elevationIndex = -1; elevationIndex <= 1; elevationIndex++)
			{
				var elevation = elevationIndex * System.MathF.PI / 4f;
				var radius = range * System.MathF.Cos(elevation);
				var height = Vector3.Up * (range * System.MathF.Sin(elevation));
				var previous = origin;
				for (var i = 0; i <= segments; i++)
				{
					var angle = yaw - halfAngle + 2f * halfAngle * i / segments;
					var point = origin + height + new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * radius;
					if (i > 0)
						DebugOverlay.Line(previous, point, color, duration);
					if (i == 0 || i == segments)
						DebugOverlay.Line(origin, point, color, duration);
					previous = point;
				}
			}

			for (var side = -1; side <= 1; side++)
			{
				var angle = yaw + side * halfAngle;
				var direction = new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f);
				var previous = origin - Vector3.Up * range;
				for (var i = 1; i <= segments; i++)
				{
					var elevation = -System.MathF.PI / 2f + System.MathF.PI * i / segments;
					var point = origin + range * (direction * System.MathF.Cos(elevation) + Vector3.Up * System.MathF.Sin(elevation));
					DebugOverlay.Line(previous, point, color, duration);
					previous = point;
				}
			}
		}

		if (hit)
			DebugOverlay.Line(origin + Vector3.Up * PunchTraceHeight,
				targetPosition + Vector3.Up * PunchTraceHeight, Color.Yellow, duration);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void FlashHit()
	{
		_flashMaterial ??= Material.Create("entity_hit_flash", "shaders/hit_flash.shader");
		_flashEndTime = Time.Now + HitFlashDuration;
		ApplyHitFlash();
	}

	private void ApplyHitFlash()
	{
		foreach (var renderer in GetComponentsInChildren<ModelRenderer>())
		{
			if (!_originalMaterials.ContainsKey(renderer))
				_originalMaterials.Add(renderer, renderer.MaterialOverride);

			renderer.MaterialOverride = _flashMaterial;
		}
	}

	protected override void OnUpdate()
	{
		UpdateDamageEffects(allowSpread: true);
		if (_originalMaterials.Count == 0)
			return;

		if (Time.Now < _flashEndTime)
			ApplyHitFlash();
		else
			RestoreMaterials();
	}

	protected void RestoreMaterials()
	{
		foreach (var entry in _originalMaterials)
		{
			if (entry.Key.IsValid())
				entry.Key.MaterialOverride = entry.Value;
		}

		_originalMaterials.Clear();
	}

	protected override void OnDisabled()
	{
		ClearDamageEffects();
		RestoreMaterials();
	}
}
