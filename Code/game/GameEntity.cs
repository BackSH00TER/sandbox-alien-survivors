using Sandbox;

public abstract class GameEntity : Component
{
	internal const string PlayerCollisionTag = "alien_survivors_player";
	internal const string EnemyCollisionTag = "alien_survivors_enemy";
	internal const string EnemyRagdollCollisionTag = "alien_survivors_enemy_ragdoll";

	[Property, Range(1f, 1000f), Sync(SyncFlags.FromHost)] public float MaxHealth { get; set; } = 100f;
	[Property, Range(0.01f, 1f)] public float HitFlashDuration { get; set; } = 0.12f;
	[Sync(SyncFlags.FromHost)] public float Health { get; private set; } = 100f;
	[Property, Group("Armor"), Range(0, int.MaxValue), Sync(SyncFlags.FromHost), Description("Current armor. Set on creation; the cost of blocking a hit depends on its damage type.")]
	public int Armor
	{
		get => _armor;
		set
		{
			if (value < 0)
			{
				Log.Error("Armor must be non-negative.");
				return;
			}

			_armor = value;
		}
	}
	[Property, Group("Armor"), Range(1, int.MaxValue), Description("Armor needed to fully block a physical hit.")] public int PhysicalArmorCost { get; set; } = 1;
	[Property, Group("Armor"), Range(1, int.MaxValue), Description("Armor needed to fully block an explosive hit.")] public int ExplosiveArmorCost { get; set; } = 5;
	[Property, Group("Armor"), Range(1, int.MaxValue), Description("Armor needed to fully block a fire hit.")] public int FireArmorCost { get; set; } = 1;
	[Property, Group("Armor"), Range(1, int.MaxValue), Description("Armor needed to fully block an ice hit.")] public int IceArmorCost { get; set; } = 1;
	[Property, Group("Armor"), Range(1, int.MaxValue), Description("Armor needed to fully block an electric hit.")] public int ElectricArmorCost { get; set; } = 1;
	[Property, Group("Armor"), Description("When armor cannot cover the full hit cost, the unprotected fraction damages health. If disabled, any remaining armor blocks the full hit.")] public bool ArmorDamageOverflow { get; set; } = true;
	[Property, Group("Debug")] public bool ShowAttackHitboxes { get; set; } = true;
	public virtual bool CanTakeDamage => true;
	[Sync(SyncFlags.FromHost)] public NetDictionary<GameDamageType, int> EffectStacks { get; private set; } = new();
	private readonly TimedDamageEffects<GameEntity> _damageEffects = new();
	private readonly Dictionary<GameDamageType, float> _nextSpreadTimes = new();
	private GameManager _roundManager;

	private GameManager RoundManager
	{
		get
		{
			// Reuse the scene lookup while the manager is alive; reacquire it if removed or the scene changes.
			if (!_roundManager.IsValid() || _roundManager.Scene != Scene)
			{
				_roundManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
			}

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

	public int EffectStackCount(GameDamageType type) => EffectStacks.TryGetValue(type, out var count) ? count : 0;

	/// <summary>0 to 1 amount of blood splatter drawn on the model. By default it follows how much health is missing.</summary>
	public virtual float BloodAmount => 1f - Health / System.MathF.Max(1f, MaxHealth);

	internal const float PunchFacingThreshold = 0.7f;
	private const float PunchTraceHeight = 36f;
	private int _armor = 0;
	private readonly Dictionary<ModelRenderer, Material> _originalMaterials = new();
	private Material _flashMaterial;
	private float _flashEndTime;
	internal bool IsHitFlashing => Time.Now < _flashEndTime;

	protected override void OnStart()
	{
		var blood = GameObject.GetOrAddComponent<GameBloodiedModel>();
		blood.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		if (Networking.IsHost)
		{
			Health = MaxHealth;
		}
	}

	internal static bool AddCollisionTag(Rigidbody rigidbody, string tag)
	{
		if (!rigidbody.IsValid())
		{
			return false;
		}

		var body = rigidbody.PhysicsBody;
		if (!body.IsValid)
		{
			return false;
		}

		var shapeCount = 0;
		foreach (var shape in body.Shapes)
		{
			shape.Tags.Add(tag);
			shapeCount++;
		}

		return shapeCount > 0;
	}

	public void TakeDamage(float amount, Vector3 hitDirection = default, GameDamageType type = GameDamageType.Physical, GameEntity attacker = null, bool bypassArmor = false)
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

		if (!System.Enum.IsDefined(type))
		{
			Log.Error("Damage requires a valid damage type.");
			return;
		}

		if (Health <= 0f || !CanTakeDamage)
		{
			return;
		}

		float healthDamage = amount;
		if (Armor > 0 && !bypassArmor)
		{
			int armorCost = type switch
			{
				GameDamageType.Physical => PhysicalArmorCost,
				GameDamageType.Explosive => ExplosiveArmorCost,
				GameDamageType.Fire => FireArmorCost,
				GameDamageType.Ice => IceArmorCost,
				GameDamageType.Electricity => ElectricArmorCost,
				_ => 1
			};
			if (armorCost <= 0)
			{
				Log.Error($"Armor cost for {type} damage must be positive.");
				return;
			}

			int spentArmor = System.Math.Min(Armor, armorCost);
			Armor -= spentArmor;
			healthDamage = ArmorDamageOverflow ? amount * ((armorCost - spentArmor) / (float)armorCost) : 0f;
		}

		bool wasKill = false;
		if (healthDamage > 0f)
		{
			float previousHealth = Health;
			Health = System.MathF.Max(0f, Health - healthDamage);
			wasKill = Health <= 0f;
			var dealtHealthDamage = previousHealth - Health;
			if (dealtHealthDamage > 0f)
			{
				GamePlayerStats.RecordDamage(attacker, this, dealtHealthDamage, type, wasKill);
				ShowDamageNumber(dealtHealthDamage);
			}
		}
		FlashHit();
		OnDamaged(hitDirection);
		if (wasKill)
		{
			ClearDamageEffects();
			OnHealthDepleted();
		}
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void ShowDamageNumber(float damage)
	{
		var damageNumberObject = Scene.CreateObject();
		damageNumberObject.Name = "Damage Number";
		damageNumberObject.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		damageNumberObject.WorldPosition = WorldPosition + Vector3.Up * 72f;

		damageNumberObject.Components.Create<ScreenPanel>();
		damageNumberObject.Components.Create<GameDamageNumber>().Damage = damage;
	}

	protected void ApplyEnemyDamageEffect(
		GameEnemy enemy,
		DamageEffectSettings settings,
		GameEntity attacker
	)
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
		{
			return;
		}

		if (settings.IsInstant)
		{
			ApplyInstantDamageEffect(
				initialTarget: enemy,
				settings: settings,
				attacker: attacker
			);
			return;
		}

		if (_damageEffects.Count(settings.Type, Time.Now) == 0)
		{
			_nextSpreadTimes[settings.Type] = Time.Now + 1f;
		}

		_damageEffects.Apply(settings, attacker, Time.Now);
		SyncEffectStacks();
	}

	private void ApplyInstantDamageEffect(
		GameEnemy initialTarget,
		DamageEffectSettings settings,
		GameEntity attacker
	)
	{
		var scene = Scene;
		var arcStart = WorldPosition + Vector3.Up * 64f;

		bool CanHit(GameEnemy enemy)
		{
			// The original attacker supplies credit; each target must still accept damage in this round.
			return attacker.IsValid() && enemy.IsValid() && enemy.CanReceiveEffectDamage;
		}

		GameEnemy FindNext(GameEnemy previous, IReadOnlySet<GameEnemy> visited)
		{
			// Measure each hop from its previous enemy, excluding every target already hit.
			var originPosition = previous.WorldPosition;
			var originObject = previous.GameObject;
			var radiusSquared = settings.ChainRadius * settings.ChainRadius;
			var candidates = scene.GetAllComponents<GameEnemy>()
				.Where(enemy => !visited.Contains(enemy) && CanHit(enemy))
				.Where(enemy => (enemy.WorldPosition - originPosition).LengthSquared <= radiusSquared)
				.OrderBy(enemy => (enemy.WorldPosition - originPosition).LengthSquared);
			foreach (var enemy in candidates)
			{
				// Choose the nearest visible candidate, not merely the nearest one behind a wall.
				var visible = HasEffectLineOfSight(
					scene: scene,
					originPosition: originPosition,
					originObject: originObject,
					enemy: enemy
				);
				if (visible)
				{
					return enemy;
				}
			}

			return null;
		}

		void Hit(GameEnemy enemy, float damage)
		{
			// Broadcast the arc through the attacker so a lethal hit cannot remove its RPC sender.
			var arcEnd = enemy.WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
			attacker.ShowShockArc(arcStart, arcEnd);
			arcStart = arcEnd;
			enemy.TakeDamage(
				amount: damage,
				type: settings.Type,
				attacker: attacker
			);
		}

		DamageEffectChain.Apply(
			initialTarget: initialTarget,
			settings: settings,
			canHit: CanHit,
			findNext: FindNext,
			hit: Hit,
			random: () => Game.Random.Float()
		);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void ShowShockArc(Vector3 start, Vector3 end)
	{
		ShockVisual.Spawn(
			scene: Scene,
			start: start,
			end: end
		);
	}

	private void UpdateDamageEffects(bool allowSpread)
	{
		if (!Networking.IsHost || _damageEffects.Stacks.Count == 0)
		{
			return;
		}

		if (!Active || Health <= 0f || !IsDamageRoundInProgress)
		{
			ClearDamageEffects();
			return;
		}

		// Invulnerability consumes effect time without letting damage accumulate for a later tick.
		_damageEffects.Advance(Time.Now, DealDamageEffectTick, flush: !allowSpread || !CanTakeDamage);
		if (!this.IsValid() || Health <= 0f || !IsDamageRoundInProgress)
		{
			return;
		}

		SyncEffectStacks();

		if (allowSpread && this is GameEnemy && CanTakeDamage)
		{
			SpreadDamageEffects();
		}
	}

	private void DealDamageEffectTick(TimedDamageStack<GameEntity> effectStack, float amount)
	{
		// A previous tick can kill the target or end the round, so check again for every application.
		if (!CanReceiveEffectDamage)
		{
			return;
		}

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
		{
			TrySpreadDamageEffect(effectStack);
		}
	}

	private void TrySpreadDamageEffect(TimedDamageStack<GameEntity> effectStack)
	{
		var settings = effectStack.Settings;
		if (Time.Now < _nextSpreadTimes[settings.Type])
		{
			return;
		}

		_nextSpreadTimes[settings.Type] = Time.Now + 1f;
		if (!effectStack.Source.IsValid())
		{
			return;
		}

		var spreadRollSucceeded = settings.SpreadChance >= 1f || Game.Random.Float() < settings.SpreadChance;
		if (!spreadRollSucceeded)
		{
			return;
		}

		var spreadTargets = Scene.GetAllComponents<GameEnemy>()
			.Where(enemy => IsEligibleSpreadTarget(enemy, settings))
			.ToList();
		if (spreadTargets.Count == 0)
		{
			return;
		}

		var targetIndex = Game.Random.Int(0, spreadTargets.Count - 1);
		spreadTargets[targetIndex].ApplyDamageEffect(settings, effectStack.Source);
	}

	private bool IsEligibleSpreadTarget(GameEnemy enemy, DamageEffectSettings settings)
	{
		if (enemy == this || !enemy.Active || !enemy.CanTakeDamage || enemy.Health <= 0f)
		{
			return false;
		}

		// Spread can add to burning enemies below the cap, but cannot refresh a full target.
		if (!enemy._damageEffects.CanSpreadTo(settings, Time.Now))
		{
			return false;
		}

		if ((enemy.WorldPosition - WorldPosition).LengthSquared > settings.SpreadRadius * settings.SpreadRadius)
		{
			return false;
		}

		return HasSpreadLineOfSight(enemy);
	}

	private bool HasSpreadLineOfSight(GameEnemy enemy)
	{
		return HasEffectLineOfSight(
			scene: Scene,
			originPosition: WorldPosition,
			originObject: GameObject,
			enemy: enemy
		);
	}

	private static bool HasEffectLineOfSight(
		Scene scene,
		Vector3 originPosition,
		GameObject originObject,
		GameEnemy enemy
	)
	{
		// Use bullet collision height so walls block spreading even when an enemy's feet are visible.
		var collisionHeight = Vector3.Up * GameWeaponProjectile.CollisionHeight;
		var traceBuilder = scene.Trace.FromTo(originPosition + collisionHeight, enemy.WorldPosition + collisionHeight);
		if (originObject.IsValid())
		{
			traceBuilder = traceBuilder.IgnoreGameObjectHierarchy(originObject);
		}

		var trace = traceBuilder.Run();
		return !trace.Hit || (trace.GameObject.IsValid() && trace.GameObject.GetComponentInParent<GameEntity>() == enemy);
	}

	private void SyncEffectStacks()
	{
		foreach (var definition in DamageEffectCatalog.All)
		{
			var count = _damageEffects.Count(definition.Type, Time.Now);
			if (EffectStackCount(definition.Type) != count)
			{
				EffectStacks[definition.Type] = count;
			}
		}
	}

	private void ClearDamageEffects()
	{
		_damageEffects.Clear();
		_nextSpreadTimes.Clear();
		if (Networking.IsHost)
		{
			EffectStacks.Clear();
		}
	}

	protected virtual void OnHealthDepleted()
	{
	}

	/// <summary>Host only. Adds armor without a gameplay cap and records armor gained by survivors this round. Spawn armor is not counted. Protection depends on the configured cost for each damage type.</summary>
	public bool AddArmor(int amount)
	{
		if (!Networking.IsHost || amount <= 0 || Armor > int.MaxValue - amount)
		{
			Log.Error("Adding armor requires a positive amount on the host within integer capacity.");
			return false;
		}

		if (Health <= 0f)
		{
			return false;
		}

		Armor += amount;
		if (this is GamePlayer player)
		{
			GamePlayerStats.Record(player, GameStatKeys.ArmorGained, amount);
		}

		return true;
	}

	public bool RestoreHealth(float amount)
	{
		if (!Networking.IsHost || !float.IsFinite(amount) || amount <= 0f)
		{
			Log.Error("Healing requires a finite positive amount on the host.");
			return false;
		}

		if (Health <= 0f || Health >= MaxHealth)
		{
			return false;
		}

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
		{
			return false;
		}

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
		{
			Health = System.MathF.Min(MaxHealth, Health + amount);
		}
	}

	protected virtual void OnDamaged(Vector3 hitDirection)
	{
	}

	protected void Punch<T>(PlayerController controller, float range, float damage) where T : GameEntity
	{
		T target = null;
		var closestDistanceSquared = range * range;
		foreach (var candidate in Scene.GetAllComponents<T>())
		{
			if (!candidate.Active || !candidate.CanTakeDamage || candidate == this)
			{
				continue;
			}

			var offset = candidate.WorldPosition - WorldPosition;
			var direction = new Vector3(offset.x, offset.y, 0f);
			if (offset.LengthSquared > closestDistanceSquared
				|| (direction.LengthSquared > 0.001f && direction.Normal.Dot(controller.EyeAngles.ToRotation().Forward) < PunchFacingThreshold))
			{
				continue;
			}

			var trace = Scene.Trace
				.FromTo(WorldPosition + Vector3.Up * PunchTraceHeight, candidate.WorldPosition + Vector3.Up * PunchTraceHeight)
				.IgnoreGameObjectHierarchy(GameObject)
				.Run();
			if (trace.Hit && trace.GameObject.GetComponentInParent<GameEntity>() != candidate)
			{
				continue;
			}

			target = candidate;
			closestDistanceSquared = offset.LengthSquared;
		}

		if (ShowAttackHitboxes)
		{
			DrawPunchHitbox(WorldPosition, controller.EyeAngles.ToRotation().Forward, range,
				target != null, target?.WorldPosition ?? WorldPosition);
		}

		if (target != null)
		{
			target.TakeDamage(damage, (target.WorldPosition - WorldPosition).Normal, GameDamageType.Physical, this);
		}
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void DrawPunchHitbox(Vector3 origin, Vector3 forward, float range, bool hit, Vector3 targetPosition)
	{
		if (!ShowAttackHitboxes || !GameHUD.IsDebugEnabled(Scene))
		{
			return;
		}

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
					{
						DebugOverlay.Line(previous, point, color, duration);
					}

					if (i == 0 || i == segments)
					{
						DebugOverlay.Line(origin, point, color, duration);
					}

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
		{
			DebugOverlay.Line(origin + Vector3.Up * PunchTraceHeight,
				targetPosition + Vector3.Up * PunchTraceHeight, Color.Yellow, duration);
		}
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
			{
				_originalMaterials.Add(renderer, renderer.MaterialOverride);
			}

			renderer.MaterialOverride = _flashMaterial;
		}
	}

	protected override void OnUpdate()
	{
		UpdateDamageEffects(allowSpread: true);
		if (_originalMaterials.Count == 0)
		{
			return;
		}

		if (Time.Now < _flashEndTime)
		{
			ApplyHitFlash();
		}
		else
		{
			RestoreMaterials();
		}
	}

	protected void RestoreMaterials()
	{
		foreach (var entry in _originalMaterials)
		{
			if (entry.Key.IsValid())
			{
				entry.Key.MaterialOverride = entry.Value;
			}
		}

		_originalMaterials.Clear();
	}

	protected override void OnDisabled()
	{
		ClearDamageEffects();
		RestoreMaterials();
	}
}
