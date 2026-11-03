using Sandbox;

/// <summary>A 30-health shootable crate with an equally weighted, configurable drop count. For each item, after the revive-flare roll, armor has a 10% chance; otherwise health, rifle, shotgun and reroll dice are equally weighted.</summary>
public sealed class GameSupplyCrate : GameEntity
{
	public const int DefaultMinDrops = 1;
	public const int DefaultMaxDrops = 3;

	[Sync(SyncFlags.FromHost)]
	public GameWeaponDefinition Rifle
	{
		get; set;
	}
	[Sync(SyncFlags.FromHost)]
	public GameWeaponDefinition Shotgun
	{
		get; set;
	}
	[Property, Group("Loot"), Description("Minimum items dropped, inclusive. Must be at least one.")]
	public int MinDrops { get; set; } = DefaultMinDrops;
	[Property, Group("Loot"), Description("Maximum items dropped, inclusive. Must be at least MinDrops.")]
	public int MaxDrops { get; set; } = DefaultMaxDrops;
	public override bool CanTakeDamage => Health > 0f && !_opened;
	private bool _opened;

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

		return crate;
	}

	protected override void OnStart()
	{
		base.OnStart();
		AddPart("Wooden Crate", Vector3.Zero, new Vector3(40f, 40f, 44f), new Color(0.55f, 0.32f, 0.12f));
		AddPart("Left Band", new Vector3(0f, -13f, 0f), new Vector3(42f, 4f, 46f), new Color(0.23f, 0.25f, 0.27f));
		AddPart("Right Band", new Vector3(0f, 13f, 0f), new Vector3(42f, 4f, 46f), new Color(0.23f, 0.25f, 0.27f));
	}

	private void AddPart(string name, Vector3 position, Vector3 size, Color color)
	{
		var part = new GameObject(GameObject, true, name);
		part.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		part.LocalPosition = position;
		part.LocalScale = size / 50f;
		var renderer = part.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/box.vmdl");
		renderer.Tint = color;
	}

	// Armor and flare have their own chances; the remaining kinds share the ordinary drop pool.
	private static readonly LootKind[] NormalDrops = System.Enum.GetValues<LootKind>()
		.Where(x => x != LootKind.Flare && x != LootKind.Armor).ToArray();

	/// <summary>
	/// Picks one item the crate drops. A flare is rolled first, and only has a chance while someone is dead. Otherwise
	/// armor has a 10% chance, then one of the normal drops is chosen with equal weight.
	/// </summary>
	private LootKind RollKind()
	{
		var spawner = Scene.GetAllComponents<GameCrateSpawner>().FirstOrDefault();
		if (spawner != null && Game.Random.Float() < spawner.GetFlareDropChance())
		{
			return LootKind.Flare;
		}

		if (Game.Random.Float() < 0.1f)
		{
			return LootKind.Armor;
		}

		return NormalDrops[Game.Random.Int(0, NormalDrops.Length - 1)];
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
			var kind = RollKind();
			GameWeaponDefinition weapon = kind switch
			{
				LootKind.Rifle => Rifle,
				LootKind.Shotgun => Shotgun,
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
				kind: kind,
				weapon: weapon
			);
		}
		GameObject.Destroy();
	}
}
