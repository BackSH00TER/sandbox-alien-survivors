using Sandbox;

/// <summary>A catalog-defined loot item. Eligible consumables use the XP magnet and contact collection; weapons require an owner-only interaction. Unclaimed pickups disappear after <see cref="LifetimeSeconds"/>.</summary>
public sealed class GameLootPickup : Component
{
	public const float WeaponPickupRange = 72f;
	private const float ContactRange = 32f;
	/// <summary>How many of the final seconds of <see cref="LifetimeSeconds"/> the pickup blinks as a warning that it is about to disappear.</summary>
	private const float BlinkSeconds = 8f;
	[Property, Range(5f, 300f), Description("Total seconds an unclaimed pickup stays before it disappears.")] public float LifetimeSeconds { get; set; } = 45f;
	[Sync(SyncFlags.FromHost)] public LootKind Kind { get; private set; }
	[Sync(SyncFlags.FromHost)] public GameWeaponDefinition Weapon { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool Claimed { get; private set; }
	/// <summary>True during the last <see cref="BlinkSeconds"/> before the pickup disappears. Set by the host so every client blinks it.</summary>
	[Sync(SyncFlags.FromHost)] public bool IsBlinking { get; private set; }
	public LootDefinition Definition => GameLootCatalog.Get(Kind);
	public bool CanBeMagnetAttracted => Definition.CanBeMagnetAttracted;
	public string DisplayName => Definition.Effect == LootEffect.Weapon ? Weapon?.DisplayName : Definition.Name;
	private GameObject _visual;
	private float _spawnedAt;

	public static GameLootPickup Spawn(
		Vector3 groundPosition,
		LootDefinition definition,
		GameWeaponDefinition weapon = null
	)
	{
		if (!Networking.IsHost || definition == null || !GameLootCatalog.All.Contains(definition))
		{
			Log.Error("Loot spawning requires the host and a catalog definition.");
			return null;
		}

		if (definition.Effect == LootEffect.Weapon)
		{
			weapon ??= ResourceLibrary.Get<GameWeaponDefinition>(definition.WeaponResourcePath);
			if (weapon == null || !weapon.Validate())
			{
				Log.Error($"Loot '{definition.Name}' requires a valid weapon resource.");
				return null;
			}
		}
		else if (weapon != null)
		{
			Log.Error("Only weapon loot definitions can receive a weapon override.");
			return null;
		}

		var obj = new GameObject(true, "Loot Pickup");
		obj.WorldPosition = groundPosition + Vector3.Up * 16f;
		var pickup = obj.AddComponent<GameLootPickup>();
		pickup.Kind = definition.Id;
		pickup.Weapon = weapon;
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn loot pickup.");
			return null;
		}

		return pickup;
	}

	protected override void OnStart()
	{
		_spawnedAt = Time.Now;
		_visual = new GameObject(GameObject, true, "Loot Visual");
		_visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		foreach (var part in Definition.VisualParts)
		{
			AddPart(part);
		}
	}

	private void AddPart(LootVisualPart part)
	{
		var obj = new GameObject(
			parent: _visual,
			enabled: true,
			name: part.Name
		);
		obj.LocalPosition = part.Position;
		var renderer = obj.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load(part.ModelPath);
		renderer.Tint = part.Tint;
		var modelWidth = part.ScaleToModelWidth ? renderer.Model.Bounds.Size.x : 50f;
		obj.LocalScale = part.Size / modelWidth;
	}

	protected override void OnUpdate()
	{
		if (_visual.IsValid())
		{
			_visual.LocalPosition = Vector3.Up * (System.MathF.Sin(Time.Now * 3f) * 3f);
			_visual.LocalRotation = Rotation.FromYaw(Time.Now * 35f);
			_visual.Enabled = !IsBlinking || (int)(Time.Now * 6f) % 2 == 0; // blink about 3 times a second
		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || Claimed || !GameInProgress())
		{
			return;
		}

		if (UpdateLifetime())
		{
			return;
		}

		if (CanBeMagnetAttracted)
		{
			GamePickupMagnet.Attract(
				pickup: this,
				pickupRange: ContactRange,
				targetHeight: 16f,
				canAttract: CanAttract
			);
		}

		TryCollectNearby();
	}

	// Returns true when the pickup ran out of time and was removed.
	private bool UpdateLifetime()
	{
		var remaining = LifetimeSeconds - (Time.Now - _spawnedAt);
		IsBlinking = remaining <= BlinkSeconds;
		if (remaining > 0f)
		{
			return false;
		}

		Claimed = true;
		GameObject.Destroy();
		return true;
	}

	private bool CanAttract(GamePlayer player)
	{
		if (!CanCollect(player))
		{
			return false;
		}

		var collectionRange = GamePickupMagnet.DefaultRange * player.ExperienceCollectionRangeMultiplier;
		return CanReach(player, collectionRange);
	}

	private bool CanCollect(GamePlayer player)
	{
		if (!player.IsValid() || !player.Active || player.Health <= 0f)
		{
			return false;
		}

		var definition = Definition;
		if (definition.Effect == LootEffect.Weapon)
		{
			return false;
		}

		return definition.CanCollect(player, definition.Amount);
	}

	private void TryCollectNearby()
	{
		foreach (var player in Scene.GetAllComponents<GamePlayer>())
		{
			if (!CanCollect(player) || !CanReach(player, ContactRange))
			{
				continue;
			}

			var definition = Definition;
			if (!definition.Collect(player, definition.Amount))
			{
				continue;
			}

			Claimed = true;
			GameObject.Destroy();
			return;
		}
	}

	public static GameLootPickup FindNearestWeapon(GamePlayer player)
	{
		if (!player.IsValid())
		{
			return null;
		}

		var manager = player.Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (manager != null && manager.State != GameState.InProgress)
		{
			return null;
		}

		return player.Scene.GetAllComponents<GameLootPickup>()
			.Where(x => x.Definition.Effect == LootEffect.Weapon && x.Weapon != null && x.CanReach(player, WeaponPickupRange))
			.OrderBy(x => (x.WorldPosition - player.WorldPosition).LengthSquared)
			.FirstOrDefault();
	}

	internal bool TryEquip(GamePlayer player)
	{
		if (!Networking.IsHost || !GameInProgress() || Definition.Effect != LootEffect.Weapon
			|| !CanReach(player, WeaponPickupRange) || Weapon == null || !Weapon.Validate())
		{
			Log.Warning("Rejected an unavailable or out-of-range weapon pickup.");
			return false;
		}

		Claimed = true;
		player.EquipWeapon(Weapon);
		GameObject.Destroy();
		return true;
	}

	private bool CanReach(GamePlayer player, float range)
	{
		if (!Active || Claimed || !player.IsValid() || !player.Active || player.Health <= 0f
			|| (WorldPosition - (player.WorldPosition + Vector3.Up * 16f)).LengthSquared > range * range)
		{
			return false;
		}

		var trace = Scene.Trace.FromTo(player.WorldPosition + Vector3.Up * 36f, WorldPosition)
			.IgnoreGameObjectHierarchy(player.GameObject).IgnoreGameObjectHierarchy(GameObject).Run();
		return !trace.Hit && !trace.StartedSolid;
	}

	private bool GameInProgress()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		return manager == null || manager.State == GameState.InProgress;
	}
}
