using Sandbox;

public enum GameCrateKind
{
	Supply,
	Weapon
}

/// <summary>A 30-health shootable crate with configurable loot from <see cref="GameLootCatalog"/> and 0-3 nearby XP orbs.</summary>
public sealed class GameSupplyCrate : GameEntity
{
	public const int DefaultMinDrops = 1;
	public const int DefaultMaxDrops = 3;

	[Sync(SyncFlags.FromHost)] public GameWeaponDefinition Rifle { get; set; }
	[Sync(SyncFlags.FromHost)] public GameWeaponDefinition Shotgun { get; set; }
	[Sync(SyncFlags.FromHost)] public GameCrateKind Kind { get; set; }
	[Property, Group("Loot"), Description("Minimum items dropped, inclusive. Must be at least one.")] public int MinDrops { get; set; } = DefaultMinDrops;
	[Property, Group("Loot"), Description("Maximum items dropped, inclusive. Must be at least MinDrops.")] public int MaxDrops { get; set; } = DefaultMaxDrops;
	public override bool CanTakeDamage => Health > 0f && !_opened;
	private bool _opened;

	public static GameSupplyCrate Spawn(
		Vector3 groundPosition,
		GameWeaponDefinition rifle,
		GameWeaponDefinition shotgun,
		int minDrops = DefaultMinDrops,
		int maxDrops = DefaultMaxDrops,
		GameCrateKind kind = GameCrateKind.Supply
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

		if (!System.Enum.IsDefined(kind)
			|| (kind == GameCrateKind.Weapon && (rifle == null || shotgun == null || !rifle.Validate() || !shotgun.Validate())))
		{
			Log.Error("Crates require a valid kind; weapon crates also require valid rifle and shotgun resources.");
			return null;
		}

		var name = kind == GameCrateKind.Weapon ? "Weapon Crate" : "Supply Crate";
		var obj = new GameObject(enabled: true, name: name);
		obj.WorldPosition = groundPosition + Vector3.Up * 22f;
		var crate = obj.AddComponent<GameSupplyCrate>();
		crate.MaxHealth = 30f;
		crate.Rifle = rifle;
		crate.Shotgun = shotgun;
		crate.MinDrops = minDrops;
		crate.MaxDrops = maxDrops;
		crate.Kind = kind;
		var collider = obj.AddComponent<BoxCollider>();
		collider.Scale = new Vector3(
			x: 40f,
			y: 40f,
			z: 44f
		);
		if (!obj.NetworkSpawn())
		{
			obj.Destroy();
			Log.Error("Failed to network-spawn supply crate.");
			return null;
		}

		return crate;
	}

	protected override void OnStart()
	{
		base.OnStart();
		BuildPrototypeVisuals();
	}

	private void BuildPrototypeVisuals()
	{
		var bodySize = new Vector3(
			x: 40f,
			y: 40f,
			z: 44f
		);
		var bodyColor = new Color(
			r: 0.55f,
			g: 0.32f,
			b: 0.12f
		);
		var bandColor = new Color(
			r: 0.23f,
			g: 0.25f,
			b: 0.27f
		);
		if (Kind == GameCrateKind.Weapon)
		{
			bodyColor = new Color(
				r: 0.16f,
				g: 0.36f,
				b: 0.51f
			);
			bandColor = new Color(
				r: 1f,
				g: 0.78f,
				b: 0.34f
			);
		}
		AddPart(
			name: "Crate Body",
			position: Vector3.Zero,
			size: bodySize,
			color: bodyColor
		);
		var bandSize = new Vector3(
			x: 42f,
			y: 4f,
			z: 46f
		);
		AddPart(
			name: "Left Band",
			position: Vector3.Left * 13f,
			size: bandSize,
			color: bandColor
		);
		AddPart(
			name: "Right Band",
			position: Vector3.Right * 13f,
			size: bandSize,
			color: bandColor
		);
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
		if (Kind == GameCrateKind.Weapon)
		{
			return GameLootCatalog.RollWeapon(random: () => Game.Random.Float());
		}

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
