using Sandbox;

/// <summary>A 30-health shootable crate with configurable loot from <see cref="GameLootCatalog"/> and 0-3 nearby XP orbs.</summary>
public sealed class GameSupplyCrate : GameEntity
{
	public const int DefaultMinDrops = 1;
	public const int DefaultMaxDrops = 3;

	[Sync(SyncFlags.FromHost)] public GameWeaponDefinition Rifle { get; set; }
	[Sync(SyncFlags.FromHost)] public GameWeaponDefinition Shotgun { get; set; }
	[Property, Group("Loot"), Description("Minimum items dropped, inclusive. Must be at least one.")] public int MinDrops { get; set; } = DefaultMinDrops;
	[Property, Group("Loot"), Description("Maximum items dropped, inclusive. Must be at least MinDrops.")] public int MaxDrops { get; set; } = DefaultMaxDrops;
	public override bool CanTakeDamage => Health > 0f && !_opened;
	private bool _opened;
	private bool _visualCreated;

	public static GameSupplyCrate Spawn(
		Vector3 groundPosition,
		GameWeaponDefinition rifle,
		GameWeaponDefinition shotgun,
		int minDrops = DefaultMinDrops,
		int maxDrops = DefaultMaxDrops
	)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Supply crates must be spawned on the host.");
			return null;
		}

		if (minDrops < 1 || maxDrops < minDrops)
		{
			Log.Error("Supply crates require 1 <= MinDrops <= MaxDrops.");
			return null;
		}

		var obj = new GameObject(true, "Supply Crate");
		obj.WorldPosition = groundPosition + Vector3.Up * 22f;
		var crate = obj.AddComponent<GameSupplyCrate>();
		crate.MaxHealth = 30f;
		crate.Rifle = rifle;
		crate.Shotgun = shotgun;
		crate.MinDrops = minDrops;
		crate.MaxDrops = maxDrops;
		var collider = obj.AddComponent<BoxCollider>();
		collider.Scale = new Vector3(40f, 40f, 44f);
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn supply crate.");
			return null;
		}

		crate.EnsureVisual();
		return crate;
	}

	protected override void OnStart()
	{
		base.OnStart();
		EnsureVisual();
	}

	private void EnsureVisual()
	{
		if (_visualCreated)
		{
			return;
		}

		// OnStart is deferred while paused, but newly spawned crates should already be visible.
		var crateSize = new Vector3(
			40f,
			40f,
			44f
		);
		var crateColor = new Color(
			0.55f,
			0.32f,
			0.12f
		);
		AddPart(
			name: "Wooden Crate",
			position: Vector3.Zero,
			size: crateSize,
			color: crateColor
		);

		var bandSize = new Vector3(
			42f,
			4f,
			46f
		);
		var bandColor = new Color(
			0.23f,
			0.25f,
			0.27f
		);
		var leftBandPosition = new Vector3(
			0f,
			-13f,
			0f
		);
		AddPart(
			name: "Left Band",
			position: leftBandPosition,
			size: bandSize,
			color: bandColor
		);
		var rightBandPosition = new Vector3(
			0f,
			13f,
			0f
		);
		AddPart(
			name: "Right Band",
			position: rightBandPosition,
			size: bandSize,
			color: bandColor
		);
		_visualCreated = true;
	}

	private void AddPart(
		string name,
		Vector3 position,
		Vector3 size,
		Color color
	)
	{
		var part = new GameObject(
			parent: GameObject,
			enabled: true,
			name: name
		);
		part.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		part.LocalPosition = position;
		part.LocalScale = size / 50f;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/box.vmdl");
		renderer.Tint = color;
	}

	private LootDefinition RollLoot()
	{
		var spawner = Scene.GetAllComponents<GameCrateSpawner>().FirstOrDefault();
		var flareChance = spawner?.GetFlareDropChance() ?? 0f;
		return GameLootCatalog.Roll(flareChance: flareChance, random: () => Game.Random.Float());
	}

	protected override void OnHealthDepleted()
	{
		if (_opened)
		{
			return;
		}

		if (MinDrops < 1 || MaxDrops < MinDrops)
		{
			Log.Error("Supply crates require 1 <= MinDrops <= MaxDrops.");
			return;
		}

		_opened = true;
		var dropCount = Game.Random.Int(MinDrops, MaxDrops);
		var groundPosition = WorldPosition - Vector3.Up * 22f;
		for (var i = 0; i < dropCount; i++)
		{
			var definition = RollLoot();
			var weapon = definition.WeaponSlot switch
			{
				LootWeaponSlot.Rifle => Rifle,
				LootWeaponSlot.Shotgun => Shotgun,
				_ => null
			};
			// Spread multiple pickups evenly around a 32-unit ring; keep a single pickup centered.
			var angle = i * System.MathF.PI * 2f / dropCount;
			var offset = Vector3.Zero;
			if (dropCount > 1)
			{
				var directionX = System.MathF.Cos(angle);
				var directionY = System.MathF.Sin(angle);
				var direction = new Vector3(
					directionX,
					directionY,
					0f
				);
				offset = direction * 32f;
			}
			var pickupPosition = groundPosition + offset;
			GameLootPickup.Spawn(
				groundPosition: pickupPosition,
				definition: definition,
				weapon: weapon
			);
		}

		var orbCount = Game.Random.Int(0, 3);
		for (var i = 0; i < orbCount; i++)
		{
			var angle = Game.Random.Float(0f, System.MathF.PI * 2f);
			var distance = Game.Random.Float(24f, 48f);
			var directionX = System.MathF.Cos(angle);
			var directionY = System.MathF.Sin(angle);
			var direction = new Vector3(
				directionX,
				directionY,
				0f
			);
			var orbPosition = groundPosition + direction * distance;
			GameExperienceOrb.Spawn(
				scene: Scene,
				position: orbPosition,
				amount: 1
			);
		}

		GameObject.Destroy();
	}
}
