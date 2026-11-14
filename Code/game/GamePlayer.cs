using Sandbox;

public sealed class GamePlayer : GameEntity, ILootCollector, IModifierRecipient
{
	[Property, RequireComponent]
	public PlayerController Controller
	{
		get; set;
	}
	[Property, RequireComponent]
	public GamePlayerStats Stats
	{
		get; set;
	}
	[Property, Range(0.25f, 4f)] public float Zoom { get; set; } = 1.5f;
	[Property, Group("Spectate"), Range(0f, 2000f), Description("Height above the death position that the UAV flies at.")] public float UavAltitude { get; set; } = 600f;
	[Property, Group("Spectate"), Range(100f, 3000f)] public float UavSpeed { get; set; } = 900f;
	[Property, Group("Spectate"), Description("Camera offset from the UAV. Higher and steeper than the alive camera.")] public Vector3 UavCameraOffset { get; set; } = new Vector3(-350f, 0f, 800f);
	[Property, Group("Revive"), Range(0.05f, 1f), Description("Fraction of max health a revived player comes back with.")] public float ReviveHealthFraction { get; set; } = 0.5f;
	[Property, Group("Revive"), Range(0f, 10f), Description("Seconds a revived player cannot take damage.")] public float ReviveInvulnerableSeconds { get; set; } = 3f;
	[Property, Group("Weapon"), Description("Weapon equipped on spawn. If unassigned, uses weapons/pistol.weapon.")]
	public GameWeaponDefinition StartingWeapon
	{
		get; set;
	}
	[Property, Group("Weapon"), Range(0.01f, 5f), Description("Width of the red aiming laser in world units.")] public float LaserWidth { get; set; } = 0.25f;
	[Property, Group("Weapon"), Range(0f, 500f), Description("Minimum distance from the player used for aiming. Close cursor targets are extended outward to prevent sharp muzzle corrections.")] public float MinimumAimDistance { get; set; } = 100f;
	[Sync(SyncFlags.FromHost)]
	public GameWeaponDefinition EquippedWeapon
	{
		get; private set;
	}
	[Property, Group("Debug")] public bool ShowAttackDebug { get; set; } = true;

	[Group("Experience")][Property, Range(1, 1000)] public int BaseExperienceToLevel { get; set; } = 5;
	[Group("Experience")][Property, Range(0, 1000)] public int ExperienceGrowthPerLevel { get; set; } = 5;

	[Sync(SyncFlags.FromHost)] public int Level { get; private set; } = 1;
	[Sync(SyncFlags.FromHost)]
	public int Experience
	{
		get; private set;
	}
	public int ExperienceToNextLevel => System.Math.Max(1, BaseExperienceToLevel + ExperienceGrowthPerLevel * (Level - 1));

	[Sync(SyncFlags.FromHost)]
	public int PendingModifierChoices
	{
		get; private set;
	}
	[Sync(SyncFlags.FromHost)]
	public int RerollDice
	{
		get; private set;
	}
	public bool CanRerollModifiers => Active && !IsDead && !IsRoundOver && PendingModifierChoices > 0 && RerollDice > 0
		&& ModifierCatalog.HasAlternativeOffer(ModifierStacks, CurrentModifierOffer());

	private GameModifier[] CurrentModifierOffer()
	{
		return ModifierOptions.ToArray();
	}

	public bool AddRerollDice(int amount)
	{
		if (!Networking.IsHost || amount <= 0 || amount > int.MaxValue - RerollDice)
		{
			Log.Error("Reroll dice require the host, a positive amount and space in the counter.");
			return false;
		}
		if (!Active || IsDead || IsRoundOver)
		{
			return false;
		}

		RerollDice += amount;
		return true;
	}
	[Sync(SyncFlags.FromHost)]
	public int ModifierOfferId
	{
		get; private set;
	}
	[Sync(SyncFlags.FromHost)]
	public NetList<GameModifier> ModifierOptions { get; private set; } = new();
	[Sync(SyncFlags.FromHost)]
	public int AttackSpeedStacks
	{
		get; private set;
	}
	[Sync(SyncFlags.FromHost)]
	public int VitalityStacks
	{
		get; private set;
	}
	[Sync(SyncFlags.FromHost)] private NetDictionary<GameModifier, int> EffectModifierStacks { get; set; } = new();

	public int ModifierStacks(GameModifier modifier) => modifier switch
	{
		GameModifier.AttackSpeed => AttackSpeedStacks,
		GameModifier.Vitality => VitalityStacks,
		_ => EffectModifierStacks.TryGetValue(modifier, out var count) ? count : 0
	};

	/// <summary>Lets the inventory UI detect changed upgrade counts without depending on dictionary iteration order.</summary>
	public int ModifierContentHash()
	{
		int hash = System.HashCode.Combine(AttackSpeedStacks, VitalityStacks);
		foreach (var pair in EffectModifierStacks)
		{
			hash ^= System.HashCode.Combine(pair.Key, pair.Value);
		}

		return hash;
	}

	public float EffectiveAttackInterval => EquippedWeapon == null ? 0f
		: System.MathF.Max(0.1f, 1f / (EquippedWeapon.FireRate * ModifierCatalog.AttackRateMultiplier(ModifierStacks)));
	public float EffectiveAttackRange => EquippedWeapon?.Range ?? 0f;
	public float CritChance => GameShotModifiers.CritChance(ModifierStacks);
	public float CritDamage => GameShotModifiers.CritDamage(ModifierStacks);
	public float ExperienceCollectionRangeMultiplier => ModifierCatalog.ExperienceCollectionRangeMultiplier(ModifierStacks);

	float ILootCollector.HealthPickupMultiplier => 1f + ModifierCatalog.Bonus(GameModifier.HealthPickupBonus, ModifierStacks);
	float ILootCollector.ArmorPickupMultiplier => 1f + ModifierCatalog.Bonus(GameModifier.ArmorPickupBonus, ModifierStacks);

	public bool IsDead => Health <= 0f;

	bool ILootCollector.HasDeadTeammate => Scene.GetAllComponents<GamePlayer>().Any(player => player.IsDead);

	bool ILootCollector.FireReviveFlare()
	{
		var flare = GameReviveFlare.Spawn(WorldPosition);
		return flare.IsValid();
	}

	public bool IsRoundOver
	{
		get
		{
			return Stats.IsValid() && Stats.IsRoundOver;
		}
	}
	// Dead, god mode and the post-revive grace period all block damage.
	public override bool CanTakeDamage => !IsDead && !GodMode && Time.Now >= _invulnerableUntil;

	/// <summary>Debug only: while true this player takes no damage.</summary>
	[Sync(SyncFlags.FromHost)]
	public bool GodMode
	{
		get; internal set;
	}

	/// <summary>
	/// Missing health that is not shown as blood. Set on revive so a revived player comes back clean, then
	/// shrinks as they heal so blood only appears from damage taken after that.
	/// </summary>
	[Sync(SyncFlags.FromHost)]
	public float BloodOffset
	{
		get; private set;
	}

	// A dead player shows no blood, so the UAV drone and the body on revive are clean.
	public override float BloodAmount => IsDead ? 0f : System.MathF.Max(0f, base.BloodAmount - BloodOffset);

	/// <summary>Host only: when this player died, used to raise the flare drop chance the longer they stay dead.</summary>
	public float DeathTime
	{
		get; private set;
	}

	[Sync(SyncFlags.FromHost)]
	private float NextAttackTime
	{
		get; set;
	}
	[Sync]
	private Vector3 CursorRayOrigin
	{
		get; set;
	}
	[Sync]
	private Vector3 CursorRayDirection
	{
		get; set;
	}
	[Sync(SyncFlags.FromHost)]
	private float StunEndTime
	{
		get; set;
	}
	public bool IsStunned => Time.Now < StunEndTime;

	private static readonly Vector3 BaseCameraOffset = new Vector3(-500f, 0f, 800f);
	private bool _missingCameraLogged;
	private bool _wasDead;
	private float _invulnerableUntil; // host clock, only the host checks it
	private float _baseWalkSpeed;
	private int _shotsSincePowerShot;

	protected override void OnStart()
	{
		base.OnStart();
		var playerRigidbody = Controller.Body;
		if (!AddCollisionTag(playerRigidbody, PlayerCollisionTag))
		{
			Log.Error("Game player requires physics shapes to assign its collision tag.");
		}

		var heldWeapon = GameObject.GetOrAddComponent<GamePlayerHeldWeaponModel>();
		heldWeapon.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		var uavVisual = GameObject.GetOrAddComponent<GameUavVisual>();
		uavVisual.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		Controller.UseCameraControls = false;
		Controller.UseLookControls = false;
		Controller.UseInputControls = false;
		Controller.RunByDefault = false;
		Controller.AltMoveButton = "";
		Controller.RunSpeed = Controller.WalkSpeed;
		_baseWalkSpeed = Controller.WalkSpeed;

		if (Networking.IsHost)
		{
			EquipWeapon(StartingWeapon ?? ResourceLibrary.Get<GameWeaponDefinition>("weapons/pistol.weapon"));
			QueueModifierChoice();
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateDeadState();
		if (IsProxy)
		{
			return;
		}

		Mouse.Visibility = MouseVisibility.Visible;
		Mouse.CursorType = IsRoundOver ? "default" : "none";
		if (IsRoundOver)
		{
			return;
		}

		if (IsDead)
		{
			UpdateUav();
			return;
		}

		if (Input.Pressed("Use"))
		{
			var pickup = GameLootPickup.FindNearestWeapon(this);
			if (pickup.IsValid())
			{
				PickupWeapon(pickup);
			}
		}

		if (ShowAttackDebug)
		{
			GameDebugTools.DrawAttackRange(this);
		}

		var camera = Scene.Camera;
		if (!camera.IsValid())
		{
			if (!_missingCameraLogged)
			{
				Log.Error("GamePlayer requires an active scene camera.");
				_missingCameraLogged = true;
			}

			return;
		}

		_missingCameraLogged = false;
		camera.WorldPosition = WorldPosition + BaseCameraOffset / Zoom.Clamp(0.25f, 4f);
		camera.WorldRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);

		var ray = camera.ScreenPixelToRay(Mouse.Position);
		CursorRayOrigin = ray.Position;
		CursorRayDirection = ray.Forward;

		var aimOrigin = WorldPosition + Vector3.Up * GameWeaponProjectile.CollisionHeight;
		var heldWeapon = GetComponent<GamePlayerHeldWeaponModel>();
		if (heldWeapon.IsValid() && heldWeapon.TryGetMuzzlePosition(out var muzzlePosition))
		{
			aimOrigin = WorldPosition.WithZ(muzzlePosition.z);
		}

		if (TryGetAimDirection(aimOrigin, out var direction))
		{
			var bodyRotation = Rotation.LookAt(direction, Vector3.Up);
			Controller.EyeAngles = bodyRotation.Angles();
			// Aim the torso along the shot while keeping locomotion facing the cursor.
			if (TryGetShotTransform(out _, out var shotDirection))
			{
				Controller.EyeAngles = Rotation.LookAt(shotDirection, Vector3.Up).Angles();
			}

			if (Controller.Renderer.IsValid())
			{
				Controller.Renderer.WorldRotation = bodyRotation;
			}
		}
	}

	private bool TryGetAimDirection(Vector3 origin, out Vector3 direction)
	{
		direction = default;
		if (System.MathF.Abs(CursorRayDirection.z) < 0.0001f)
		{
			return false;
		}

		// Intersect the cursor ray at shot height, without snapping to scene geometry.
		var distance = (origin.z - CursorRayOrigin.z) / CursorRayDirection.z;
		if (distance < 0f)
		{
			return false;
		}

		var aimPosition = CursorRayOrigin + CursorRayDirection * distance;
		var playerAtAimHeight = WorldPosition.WithZ(origin.z);
		var cursorOffset = (aimPosition - playerAtAimHeight).WithZ(0f);
		var cursorDistance = cursorOffset.Length;
		if (cursorDistance > 0.001f && cursorDistance < MinimumAimDistance)
		{
			aimPosition = playerAtAimHeight + cursorOffset / cursorDistance * MinimumAimDistance;
		}

		var offset = aimPosition - origin;
		var horizontal = new Vector3(offset.x, offset.y, 0f);
		if (horizontal.LengthSquared <= 0.001f)
		{
			return false;
		}

		direction = horizontal.Normal;
		return true;
	}

	protected override void OnFixedUpdate()
	{
		var moveSpeedBonus = ModifierCatalog.Bonus(GameModifier.MoveSpeed, ModifierStacks);
		Controller.WalkSpeed = _baseWalkSpeed * (1f + moveSpeedBonus);
		Controller.RunSpeed = Controller.WalkSpeed;
		if (IsStunned || IsRoundOver)
		{
			StopMovement();
		}
		else if (!IsProxy && !IsDead)
		{
			var cameraRotation = Rotation.LookAt(-BaseCameraOffset, Vector3.Up);
			var movementRotation = Rotation.FromYaw(cameraRotation.Angles().yaw);
			Controller.WishVelocity = Controller.Mode.UpdateMove(movementRotation, Input.AnalogMove);
		}

		if (!Networking.IsHost)
		{
			return;
		}

		if (BloodOffset > base.BloodAmount)
		{
			BloodOffset = base.BloodAmount;
		}

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
		{
			return;
		}

		var regeneration = ModifierCatalog.Bonus(GameModifier.HealthRegeneration, ModifierStacks);
		if (!IsDead && Health < MaxHealth && regeneration > 0f && Time.Delta > 0f)
		{
			RestoreHealth(regeneration * Time.Delta);
		}

		if (!IsDead && EquippedWeapon != null && Time.Now >= NextAttackTime)
		{
			NextAttackTime = Time.Now + EffectiveAttackInterval;
			FireWeapon();
		}
	}

	public void ApplyStun(float duration)
	{
		if (!Networking.IsHost || !float.IsFinite(duration) || duration <= 0f)
		{
			Log.Error("Applying a stun requires the host and a finite positive duration.");
			return;
		}

		if (!Active || Health <= 0f)
		{
			return;
		}

		StunEndTime = System.MathF.Max(StunEndTime, Time.Now + duration);
		StopMovement();
	}

	private void StopMovement()
	{
		Controller.WishVelocity = Vector3.Zero;
		if (Controller.Body.IsValid())
		{
			var velocity = Controller.Body.Velocity;
			Controller.Body.Velocity = new Vector3(0f, 0f, velocity.z);
		}
	}
	// Runs on every client so everyone hides the dead player's body and the owner takes off as a UAV.
	private void UpdateDeadState()
	{
		bool isDead = IsDead;
		if (isDead == _wasDead)
		{
			return;
		}

		_wasDead = isDead;
		Controller.Enabled = !isDead;
		if (Controller.Body.IsValid())
		{
			Controller.Body.MotionEnabled = !isDead;
		}

		if (Controller.Renderer.IsValid())
		{
			Controller.Renderer.GameObject.Enabled = !isDead;
		}

		if (isDead && !IsProxy)
		{
			WorldPosition += Vector3.Up * UavAltitude;
		}
	}

	// Camera-relative WASD flight. The owner moves the object; the host reads its position to detect flare claims.
	private void UpdateUav()
	{
		Rotation cameraRotation = Rotation.LookAt(-UavCameraOffset, Vector3.Up);
		Rotation movementRotation = Rotation.FromYaw(cameraRotation.Angles().yaw);
		Vector3 move = movementRotation * Input.AnalogMove.WithZ(0f);
		WorldPosition += move * UavSpeed * Time.Delta;

		CameraComponent camera = Scene.Camera;
		if (!camera.IsValid())
		{
			return;
		}

		camera.WorldPosition = WorldPosition + UavCameraOffset;
		camera.WorldRotation = cameraRotation;
	}

	/// <summary>Equip a weapon on the host while preserving the player's modifier stacks.</summary>
	public void EquipWeapon(GameWeaponDefinition weapon)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Weapons must be equipped on the host.");
			return;
		}

		if (weapon == null)
		{
			Log.Error("Cannot equip a missing weapon definition.");
			return;
		}

		if (!weapon.Validate())
		{
			return;
		}

		EquippedWeapon = weapon;
		NextAttackTime = Time.Now + EffectiveAttackInterval;
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugKill()
	{
		GameDebugTools.Kill(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugToggleGodMode()
	{
		GameDebugTools.ToggleGodMode(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugToggleAttackHitboxes()
	{
		GameDebugTools.ToggleAttackHitboxes(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugSpawnFlarePickup()
	{
		GameDebugTools.SpawnFlarePickup(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugSpawnArmorPickup()
	{
		GameDebugTools.SpawnArmorPickup(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugSpawnCrate()
	{
		GameDebugTools.SpawnCrate(this);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugGrantModifier(GameModifier modifier)
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Active || IsDead || IsRoundOver
			|| (gameManager != null && gameManager.State != GameState.InProgress))
		{
			Log.Warning("Rejected a debug modifier grant for an inactive player or ended round.");
			return;
		}

		GameDebugTools.GrantModifier(this, modifier);
	}

	protected override void OnHealthDepleted()
	{
		DeathTime = Time.Now;
	}

	/// <summary>Host only. Revives a dead player at the given ground position with partial health and a short grace period.</summary>
	public bool ReviveAt(Vector3 groundPosition)
	{
		if (!Networking.IsHost || !IsDead)
		{
			return false;
		}

		if (!Revive(MaxHealth * ReviveHealthFraction))
		{
			return false;
		}

		_invulnerableUntil = Time.Now + ReviveInvulnerableSeconds;
		GamePlayerStats.Record(this, GameStatKeys.TimesRevived);
		BloodOffset = base.BloodAmount;
		TeleportTo(groundPosition);
		return true;
	}

	// A player's position is owned by their client, so the host asks the owner to move them.
	[Rpc.Owner]
	private void TeleportTo(Vector3 position)
	{
		WorldPosition = position;
		if (Controller.Body.IsValid())
		{
			Controller.Body.Velocity = Vector3.Zero;
		}
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void PickupWeapon(GameLootPickup pickup)
	{
		if (!pickup.IsValid() || pickup.Scene != Scene)
		{
			Log.Warning("Rejected a missing weapon pickup.");
			return;
		}

		pickup.TryEquip(this);
	}

	public bool TryGetShotTransform(out Vector3 origin, out Vector3 forward)
	{
		origin = default;
		forward = default;
		var heldWeapon = GetComponent<GamePlayerHeldWeaponModel>();
		if (!heldWeapon.IsValid())
		{
			Log.Error("GamePlayer requires a held weapon model to fire.");
			return false;
		}

		if (!heldWeapon.TryGetMuzzlePosition(out origin)
			|| !TryGetAimDirection(WorldPosition.WithZ(origin.z), out forward))
		{
			return false;
		}

		if (TryGetAimDirection(origin, out var muzzleDirection))
		{
			const float maxCorrectionDegrees = 15f;
			var cross = Vector3.Cross(forward, muzzleDirection);
			var dot = Vector3.Dot(forward, muzzleDirection);
			var correctionDegrees = System.MathF.Atan2(cross.z, dot) * (180f / System.MathF.PI);
			var limitedCorrection = correctionDegrees.Clamp(-maxCorrectionDegrees, maxCorrectionDegrees);
			forward = Rotation.FromYaw(limitedCorrection) * forward;
		}

		return true;
	}

	private void FireWeapon()
	{
		if (!TryGetShotTransform(out var origin, out var forward))
		{
			return;
		}

		var extraBullets = GameShotModifiers.ExtraBulletCount(ModifierStacks, () => Game.Random.Float());
		var bulletCount = EquippedWeapon.BulletsPerShot + extraBullets;
		var damageMultiplier = GameShotModifiers.DamageMultiplier(
			stacks: ModifierStacks,
			shotsSincePowerShot: ref _shotsSincePowerShot
		);
		for (var i = 0; i < bulletCount; i++)
		{
			var angle = bulletCount == 1 ? 0f
				: -EquippedWeapon.SpreadDegrees * 0.5f + EquippedWeapon.SpreadDegrees * i / (bulletCount - 1);
			var direction = Rotation.FromYaw(angle) * forward;
			GameWeaponProjectile.Spawn(
				shooter: this,
				weapon: EquippedWeapon,
				position: origin,
				direction: direction,
				range: EffectiveAttackRange,
				damageMultiplier: damageMultiplier
			);
		}

		PlayFire((int)EquippedWeapon.HoldType);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	internal void ShowSplashImpact(Vector3 position)
	{
		SplashVisual.Spawn(
			scene: Scene,
			center: position,
			radius: GameShotModifiers.SplashRadius
		);
	}

	public void AddExperience(int amount)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Experience must be granted on the host.");
			return;
		}

		if (amount <= 0)
		{
			Log.Error("Experience must be a positive amount.");
			return;
		}

		Experience += amount;
		while (Experience >= ExperienceToNextLevel)
		{
			Experience -= ExperienceToNextLevel;
			Level++;
			OnLevelUp();
		}
	}

	private void OnLevelUp()
	{
		if (Stats.IsValid())
		{
			Stats.RecordLevel(Level);
		}
		else
		{
			Log.Error("GamePlayer requires a GamePlayerStats component to record level history.");
		}

		QueueModifierChoice();
	}

	private void QueueModifierChoice()
	{
		PendingModifierChoices++;
		if (PendingModifierChoices == 1)
		{
			RollModifierOffer();
		}
	}

	private void RollModifierOffer(IReadOnlyList<GameModifier> previousOffer = null)
	{
		var options = ModifierCatalog.RollOffer(
			getModifierPickCount: ModifierStacks,
			random: () => Game.Random.Float(),
			previousOffer: previousOffer
		);

		ModifierOptions.Clear();
		ModifierOptions.AddRange(options);
		ModifierOfferId++;
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void RerollModifiers(int offerId)
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!CanRerollModifiers || offerId != ModifierOfferId
			|| (gameManager != null && gameManager.State != GameState.InProgress))
		{
			Log.Warning("Rejected an unavailable or outdated modifier reroll.");
			return;
		}

		var previousOffer = CurrentModifierOffer();
		RollModifierOffer(previousOffer: previousOffer);
		RerollDice--;
		GamePlayerStats.Record(this, GameStatKeys.TimesRerolled);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void SelectModifier(int offerId, GameModifier modifier)
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Active || (gameManager != null && gameManager.State != GameState.InProgress)
			|| PendingModifierChoices <= 0 || offerId != ModifierOfferId
			|| !ModifierOptions.Contains(modifier)
			|| !ModifierCatalog.Get(modifier).IsAvailable(ModifierStacks))
		{
			Log.Warning("Rejected an unavailable or outdated modifier choice.");
			return;
		}

		var definition = ModifierCatalog.Get(modifier);
		definition.ApplyTo(this);

		GamePlayerStats.Record(this, GameStatKeys.ModifierPicked(modifier));
		PendingModifierChoices--;
		// Invalidate the consumed offer so duplicate clicks cannot spend the next queued choice.
		if (PendingModifierChoices > 0)
		{
			RollModifierOffer();
		}
		else
		{
			ModifierOfferId++;
		}
	}

	void IModifierRecipient.AddModifierStack(GameModifier modifier)
	{
		// Keep the existing replicated counters; behavior belongs to the catalog entry.
		switch (modifier)
		{
			case GameModifier.AttackSpeed:
				AttackSpeedStacks++;
				break;
			case GameModifier.Vitality:
				VitalityStacks++;
				break;
			default:
				EffectModifierStacks[modifier] = ModifierStacks(modifier) + 1;
				break;
		}
	}

	void IModifierRecipient.RefreshAttackCooldown()
	{
		NextAttackTime = System.MathF.Min(NextAttackTime, Time.Now + EffectiveAttackInterval);
	}

	void IModifierRecipient.IncreaseMaxHealth(float amount)
	{
		IncreaseMaxHealth(amount);
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void PlayFire(int holdType)
	{
		if (!Controller.Renderer.IsValid())
		{
			Log.Error("GamePlayer requires an animated renderer to fire.");
			return;
		}

		Controller.Renderer.Set("holdtype", holdType);
		Controller.Renderer.Set("b_attack", true);
	}

	protected override void OnPreRender()
	{
		if (Controller.Renderer.IsValid())
		{
			if (TryGetShotTransform(out var shotOrigin, out _)
				&& TryGetAimDirection(WorldPosition.WithZ(shotOrigin.z), out var bodyDirection))
			{
				Controller.Renderer.WorldRotation = Rotation.LookAt(bodyDirection, Vector3.Up);
			}

			if (EquippedWeapon != null)
			{
				Controller.Renderer.Set("holdtype", (int)EquippedWeapon.HoldType);
			}
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (!IsProxy)
		{
			Mouse.CursorType = "default";
		}

		if (!IsProxy && Controller.IsValid())
		{
			Controller.WishVelocity = Vector3.Zero;
		}
	}
}
