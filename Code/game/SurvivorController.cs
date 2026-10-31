using Sandbox;

public sealed class SurvivorController : Entity
{
	[Property, RequireComponent] public PlayerController Controller { get; set; }
	[Property, Range(0.25f, 4f)] public float Zoom { get; set; } = 1.5f;
	[Property, Group("Spectate"), Range(0f, 2000f), Description("Height above the death position that the UAV flies at.")]
	public float UavAltitude { get; set; } = 600f;
	[Property, Group("Spectate"), Range(100f, 3000f)] public float UavSpeed { get; set; } = 900f;
	[Property, Group("Spectate"), Description("Camera offset from the UAV. Higher and steeper than the alive camera.")]
	public Vector3 UavCameraOffset { get; set; } = new Vector3(-350f, 0f, 800f);
	[Property, Group("Revive"), Range(0.05f, 1f), Description("Fraction of max health a revived survivor comes back with.")]
	public float ReviveHealthFraction { get; set; } = 0.5f;
	[Property, Group("Revive"), Range(0f, 10f), Description("Seconds a revived survivor cannot take damage.")]
	public float ReviveInvulnerableSeconds { get; set; } = 3f;
	[Property, Group("Weapon"), Description("Weapon equipped on spawn. If unassigned, uses weapons/pistol.weapon.")]
	public WeaponDefinition StartingWeapon { get; set; }
	[Sync(SyncFlags.FromHost)] public WeaponDefinition EquippedWeapon { get; private set; }
	[Property, Group("Debug")] public bool ShowAttackDebug { get; set; } = true;

	[Group("Experience")]
	[Property, Range(1, 1000)] public int BaseExperienceToLevel { get; set; } = 5;
	[Group("Experience")]
	[Property, Range(0, 1000)] public int ExperienceGrowthPerLevel { get; set; } = 5;

	[Sync(SyncFlags.FromHost)] public int Level { get; private set; } = 1;
	[Sync(SyncFlags.FromHost)] public int Experience { get; private set; }
	public int ExperienceToNextLevel => System.Math.Max(1, BaseExperienceToLevel + ExperienceGrowthPerLevel * (Level - 1));

	[Sync(SyncFlags.FromHost)] public int PendingModifierChoices { get; private set; }
	[Sync(SyncFlags.FromHost)] public int ModifierOfferId { get; private set; }
	[Sync(SyncFlags.FromHost)] public Modifier ModifierOptionOne { get; private set; }
	[Sync(SyncFlags.FromHost)] public Modifier ModifierOptionTwo { get; private set; }
	[Sync(SyncFlags.FromHost)] public Modifier ModifierOptionThree { get; private set; }
	[Sync(SyncFlags.FromHost)] public int AttackSpeedStacks { get; private set; }
	[Sync(SyncFlags.FromHost)] public int AttackRangeStacks { get; private set; }
	[Sync(SyncFlags.FromHost)] public int VitalityStacks { get; private set; }

	public float EffectiveAttackInterval => EquippedWeapon == null ? 0f
		: System.MathF.Max(0.1f, 1f / (EquippedWeapon.FireRate * (1f + AttackSpeedStacks)));
	public float EffectiveAttackRange => EquippedWeapon == null ? 0f
		: EquippedWeapon.Range * (1f + AttackRangeStacks);

	public bool IsDead => Health <= 0f;

	public bool IsRoundOver
	{
		get
		{
			PlayerStats stats = GetComponent<PlayerStats>();
			return stats.IsValid() && stats.IsRoundOver;
		}
	}
	// Dead, god mode and the post-revive grace period all block damage.
	public override bool CanTakeDamage => !IsDead && !GodMode && Time.Now >= _invulnerableUntil;

	/// <summary>Debug only: while true this survivor takes no damage.</summary>
	[Sync(SyncFlags.FromHost)] public bool GodMode { get; private set; }

	/// <summary>
	/// Missing health that is not shown as blood. Set on revive so a revived survivor comes back clean, then
	/// shrinks as they heal so blood only appears from damage taken after that.
	/// </summary>
	[Sync(SyncFlags.FromHost)] public float BloodOffset { get; private set; }

	// A dead survivor shows no blood, so the UAV drone and the body on revive are clean.
	public override float BloodAmount => IsDead ? 0f : System.MathF.Max(0f, base.BloodAmount - BloodOffset);

	/// <summary>Host only: when this survivor died, used to raise the flare drop chance the longer they stay dead.</summary>
	public float DeathTime { get; private set; }

	[Sync(SyncFlags.FromHost)] private float NextAttackTime { get; set; }
	[Sync] private Vector3 CursorRayOrigin { get; set; }
	[Sync] private Vector3 CursorRayDirection { get; set; }
	[Sync(SyncFlags.FromHost)] private float StunEndTime { get; set; }
	public bool IsStunned => Time.Now < StunEndTime;

	private static readonly Vector3 BaseCameraOffset = new Vector3(-500f, 0f, 800f);
	private bool _missingCameraLogged;
	private bool _wasDead;
	private float _invulnerableUntil; // host clock, only the host checks it

	protected override void OnStart()
	{
		base.OnStart();
		var heldWeapon = GameObject.GetOrAddComponent<HeldWeaponModel>();
		heldWeapon.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		var uavVisual = GameObject.GetOrAddComponent<UavVisual>();
		uavVisual.Flags |= ComponentFlags.NotNetworked | ComponentFlags.NotSaved;
		Controller.UseCameraControls = false;
		Controller.UseLookControls = false;
		Controller.UseInputControls = false;
		Controller.RunByDefault = false;
		Controller.AltMoveButton = "";
		Controller.RunSpeed = Controller.WalkSpeed;

		if (Networking.IsHost)
		{
			EquipWeapon(StartingWeapon ?? ResourceLibrary.Get<WeaponDefinition>("weapons/pistol.weapon"));
			QueueModifierChoice();
		}
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();
		UpdateDeadState();
		if (IsProxy)
			return;

		Mouse.Visibility = MouseVisibility.Visible;
		if (IsRoundOver)
			return;

		if (IsDead)
		{
			UpdateUav();
			return;
		}

		HandleDebugKeys();

		if (Input.Pressed("Use"))
		{
			var pickup = LootPickup.FindNearestWeapon(this);
			if (pickup.IsValid())
				PickupWeapon(pickup);
		}

		if (ShowAttackDebug && GameHUD.IsDebugEnabled(Scene))
			DrawAttackRange();

		var camera = Scene.Camera;
		if (!camera.IsValid())
		{
			if (!_missingCameraLogged)
			{
				Log.Error("SurvivorController requires an active scene camera.");
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

		if (TryGetAimDirection(WorldPosition + Vector3.Up * WeaponProjectile.CollisionHeight, out var direction))
			Controller.EyeAngles = Rotation.LookAt(direction, Vector3.Up).Angles();
	}

	private bool TryGetAimDirection(Vector3 origin, out Vector3 direction)
	{
		direction = default;
		if (System.MathF.Abs(CursorRayDirection.z) < 0.0001f)
			return false;

		// Intersect the cursor ray at shot height, without snapping to scene geometry.
		var distance = (origin.z - CursorRayOrigin.z) / CursorRayDirection.z;
		if (distance < 0f)
			return false;

		var aimPosition = CursorRayOrigin + CursorRayDirection * distance;
		var offset = aimPosition - origin;
		var horizontal = new Vector3(offset.x, offset.y, 0f);
		if (horizontal.LengthSquared <= 0.001f)
			return false;

		direction = horizontal.Normal;
		return true;
	}

	protected override void OnFixedUpdate()
	{
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
			return;

		if (BloodOffset > base.BloodAmount)
			BloodOffset = base.BloodAmount;

		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (gameManager != null && gameManager.State != GameState.InProgress)
			return;

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
			return;

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
			return;

		_wasDead = isDead;
		Controller.Enabled = !isDead;
		if (Controller.Body.IsValid())
			Controller.Body.MotionEnabled = !isDead;

		if (Controller.Renderer.IsValid())
			Controller.Renderer.GameObject.Enabled = !isDead;

		if (isDead && !IsProxy)
			WorldPosition += Vector3.Up * UavAltitude;
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
			return;

		camera.WorldPosition = WorldPosition + UavCameraOffset;
		camera.WorldRotation = cameraRotation;
	}

	/// <summary>Equip a weapon on the host while preserving the player's modifier stacks.</summary>
	public void EquipWeapon(WeaponDefinition weapon)
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
			return;

		EquippedWeapon = weapon;
		NextAttackTime = Time.Now + EffectiveAttackInterval;
	}

	// K: kill self, G: drop a flare pickup in front, H: toggle god mode. Only while the debug overlay is on.
	private void HandleDebugKeys()
	{
		if (!GameHUD.IsDebugEnabled(Scene))
			return;

		if (Input.Keyboard.Pressed("K"))
			DebugKill();

		if (Input.Keyboard.Pressed("G"))
			DebugSpawnFlarePickup();

		if (Input.Keyboard.Pressed("H"))
			DebugToggleGodMode();
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugKill()
	{
		if (IsDead)
			return;

		// Killing on purpose wins over god mode, so it is switched off first.
		GodMode = false;
		TakeDamage(Health);
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugToggleGodMode()
	{
		GodMode = !GodMode;
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void DebugSpawnFlarePickup()
	{
		LootPickup.Spawn(WorldPosition + Controller.EyeAngles.ToRotation().Forward * 80f, LootKind.Flare);
	}

	protected override void OnHealthDepleted()
	{
		DeathTime = Time.Now;
	}

	/// <summary>Host only. Revives a dead survivor at the given ground position with partial health and a short grace period.</summary>
	public bool ReviveAt(Vector3 groundPosition)
	{
		if (!Networking.IsHost || !IsDead)
			return false;

		if (!Revive(MaxHealth * ReviveHealthFraction))
			return false;

		_invulnerableUntil = Time.Now + ReviveInvulnerableSeconds;
		PlayerStats.Record(this, StatKeys.TimesRevived);
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
			Controller.Body.Velocity = Vector3.Zero;
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void PickupWeapon(LootPickup pickup)
	{
		if (!pickup.IsValid() || pickup.Scene != Scene)
		{
			Log.Warning("Rejected a missing weapon pickup.");
			return;
		}

		pickup.TryEquip(this);
	}

	private void FireWeapon()
	{
		var heldWeapon = GetComponent<HeldWeaponModel>();
		if (!heldWeapon.IsValid())
		{
			Log.Error("SurvivorController requires a held weapon model to fire.");
			return;
		}

		if (!heldWeapon.TryGetMuzzlePosition(out var muzzlePosition))
			return;

		var clearance = Scene.Trace.Sphere(EquippedWeapon.ProjectileRadius,
				WorldPosition + Vector3.Up * 36f, muzzlePosition)
			.IgnoreGameObjectHierarchy(GameObject)
			.Run();
		var origin = clearance.Hit || clearance.StartedSolid ? clearance.EndPosition : muzzlePosition;
		if (!TryGetAimDirection(origin, out var forward))
			return;

		for (var i = 0; i < EquippedWeapon.BulletsPerShot; i++)
		{
			var angle = EquippedWeapon.BulletsPerShot == 1 ? 0f
				: -EquippedWeapon.SpreadDegrees * 0.5f + EquippedWeapon.SpreadDegrees * i / (EquippedWeapon.BulletsPerShot - 1);
			var direction = Rotation.FromYaw(angle) * forward;
			WeaponProjectile.Spawn(this, EquippedWeapon, origin, direction, EffectiveAttackRange);
		}

		PlayFire((int)EquippedWeapon.HoldType);
	}

	private void DrawAttackRange()
	{
		const int segments = 64;
		var center = WorldPosition + Vector3.Up * 2f;
		var previous = center + Vector3.Forward * EffectiveAttackRange;
		for (var i = 1; i <= segments; i++)
		{
			var angle = i * (System.MathF.PI * 2f / segments);
			var point = center + new Vector3(System.MathF.Cos(angle), System.MathF.Sin(angle), 0f) * EffectiveAttackRange;
			DebugOverlay.Line(previous, point, Color.Yellow, 0f);
			previous = point;
		}
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
		QueueModifierChoice();
	}

	private void QueueModifierChoice()
	{
		PendingModifierChoices++;
		if (PendingModifierChoices == 1)
			RollModifierOffer();
	}

	private void RollModifierOffer()
	{
		var options = System.Enum.GetValues<Modifier>();
		for (var i = options.Length - 1; i > 0; i--)
		{
			var other = Game.Random.Int(0, i);
			(options[i], options[other]) = (options[other], options[i]);
		}

		ModifierOptionOne = options[0];
		ModifierOptionTwo = options[1];
		ModifierOptionThree = options[2];
		ModifierOfferId++;
	}

	[Rpc.Host(NetFlags.OwnerOnly)]
	public void SelectModifier(int offerId, Modifier modifier)
	{
		var gameManager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (!Active || (gameManager != null && gameManager.State != GameState.InProgress)
			|| PendingModifierChoices <= 0 || offerId != ModifierOfferId
			|| (modifier != ModifierOptionOne && modifier != ModifierOptionTwo && modifier != ModifierOptionThree))
		{
			Log.Warning("Rejected an unavailable or outdated modifier choice.");
			return;
		}

		switch (modifier)
		{
			case Modifier.AttackSpeed:
				AttackSpeedStacks++;
				NextAttackTime = System.MathF.Min(NextAttackTime, Time.Now + EffectiveAttackInterval);
				break;
			case Modifier.AttackRange:
				AttackRangeStacks++;
				break;
			case Modifier.Vitality:
				VitalityStacks++;
				IncreaseMaxHealth(100f);
				break;
			default:
				Log.Error($"Unknown modifier: {modifier}");
				return;
		}

		PlayerStats.Record(this, StatKeys.ModifierPicked(modifier));
		PendingModifierChoices--;
		// Invalidate the consumed offer so duplicate clicks cannot spend the next queued choice.
		if (PendingModifierChoices > 0)
			RollModifierOffer();
		else
			ModifierOfferId++;
	}

	[Rpc.Broadcast(NetFlags.HostOnly)]
	private void PlayFire(int holdType)
	{
		if (!Controller.Renderer.IsValid())
		{
			Log.Error("SurvivorController requires an animated renderer to fire.");
			return;
		}

		Controller.Renderer.Set("holdtype", holdType);
		Controller.Renderer.Set("b_attack", true);
	}

	protected override void OnPreRender()
	{
		if (Controller.Renderer.IsValid())
		{
			Controller.Renderer.WorldRotation = Controller.EyeAngles.ToRotation();
			if (EquippedWeapon != null)
				Controller.Renderer.Set("holdtype", (int)EquippedWeapon.HoldType);
		}
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if (!IsProxy && Controller.IsValid())
			Controller.WishVelocity = Vector3.Zero;
	}
}
