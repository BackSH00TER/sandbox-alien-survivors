using Sandbox;

/// <summary>A 30-health shootable crate. After the revive-flare roll, armor has a 10% chance; otherwise health, rifle and shotgun are equally weighted.</summary>
public sealed class SupplyCrate : Entity
{
	[Sync(SyncFlags.FromHost)] public WeaponDefinition Rifle { get; set; }
	[Sync(SyncFlags.FromHost)] public WeaponDefinition Shotgun { get; set; }
	public override bool CanTakeDamage => Health > 0f && !_opened;
	private bool _opened;

	public static SupplyCrate Spawn(Vector3 groundPosition, WeaponDefinition rifle, WeaponDefinition shotgun)
	{
		if (!Networking.IsHost)
		{
			Log.Error("Supply crates must be spawned on the host.");
			return null;
		}

		var obj = new GameObject(true, "Supply Crate");
		obj.WorldPosition = groundPosition + Vector3.Up * 22f;
		var crate = obj.AddComponent<SupplyCrate>();
		crate.MaxHealth = 30f;
		crate.Rifle = rifle;
		crate.Shotgun = shotgun;
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
	/// Picks what the crate drops. A flare is rolled first, and only has a chance while someone is dead. Otherwise
	/// armor has a 10% chance, then one of the normal drops is chosen with equal weight.
	/// </summary>
	private LootKind RollKind()
	{
		var spawner = Scene.GetAllComponents<CrateSpawner>().FirstOrDefault();
		if (spawner != null && Game.Random.Float() < spawner.GetFlareDropChance())
			return LootKind.Flare;

		if (Game.Random.Float() < 0.1f)
			return LootKind.Armor;

		return NormalDrops[Game.Random.Int(0, NormalDrops.Length - 1)];
	}

	protected override void OnHealthDepleted()
	{
		if (_opened)
			return;

		_opened = true;
		var kind = RollKind();
		// Only weapon drops need a weapon definition.
		WeaponDefinition weapon = kind switch
		{
			LootKind.Rifle => Rifle,
			LootKind.Shotgun => Shotgun,
			_ => null
		};
		LootPickup.Spawn(WorldPosition - Vector3.Up * 22f, kind, weapon);
		GameObject.Destroy();
	}
}
