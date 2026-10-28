using Sandbox;

public enum LootKind
{
	Health,
	Rifle,
	Shotgun
}

/// <summary>Health restores 50 on contact when injured; weapons replace the equipped weapon through an owner-only interaction.</summary>
public sealed class LootPickup : Component
{
	public const float WeaponPickupRange = 72f;
	[Sync(SyncFlags.FromHost)] public LootKind Kind { get; private set; }
	[Sync(SyncFlags.FromHost)] public WeaponDefinition Weapon { get; private set; }
	[Sync(SyncFlags.FromHost)] public bool Claimed { get; private set; }
	public string DisplayName => Kind == LootKind.Health ? "Health +50" : Weapon?.DisplayName;
	private GameObject _visual;

	public static LootPickup Spawn(Vector3 groundPosition, LootKind kind, WeaponDefinition weapon = null)
	{
		if (!Networking.IsHost || !System.Enum.IsDefined(kind)
			|| (kind != LootKind.Health && (weapon == null || !weapon.Validate())))
		{
			Log.Error("Loot spawning requires the host, a valid loot kind, and a valid weapon for weapon drops.");
			return null;
		}

		var obj = new GameObject(true, "Loot Pickup");
		obj.WorldPosition = groundPosition + Vector3.Up * 16f;
		var pickup = obj.AddComponent<LootPickup>();
		pickup.Kind = kind;
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
		_visual = new GameObject(GameObject, true, "Loot Visual");
		_visual.Flags |= GameObjectFlags.NotNetworked | GameObjectFlags.NotSaved;
		if (Kind == LootKind.Health)
		{
			AddPart("Medkit", Vector3.Zero, new Vector3(18f, 24f, 12f), new Color(0.9f, 0.15f, 0.2f));
			AddPart("Cross Vertical", Vector3.Up * 7f, new Vector3(5f, 16f, 2f), Color.White);
			AddPart("Cross Horizontal", Vector3.Up * 7f, new Vector3(13f, 5f, 2f), Color.White);
		}
		else
		{
			var color = Kind == LootKind.Rifle ? new Color(0.25f, 0.65f, 1f) : new Color(1f, 0.65f, 0.15f);
			AddPart("Receiver", Vector3.Zero, new Vector3(26f, 5f, 6f), color);
			AddPart("Barrel", Vector3.Forward * 20f, new Vector3(16f, 3f, 3f), new Color(0.2f, 0.23f, 0.26f));
			AddPart("Stock", new Vector3(-18f, 0f, -2f), new Vector3(12f, 6f, 9f), color);
			AddPart("Grip", new Vector3(-4f, 0f, -7f), new Vector3(5f, 4f, 10f), color);
		}
	}

	private void AddPart(string name, Vector3 position, Vector3 size, Color color)
	{
		var obj = new GameObject(_visual, true, name);
		obj.LocalPosition = position;
		obj.LocalScale = size / 50f;
		var renderer = obj.AddComponent<ModelRenderer>();
		renderer.Model = Model.Load("models/dev/box.vmdl");
		renderer.Tint = color;
	}

	protected override void OnUpdate()
	{
		if (_visual.IsValid())
		{
			_visual.LocalPosition = Vector3.Up * (System.MathF.Sin(Time.Now * 3f) * 3f);
			_visual.LocalRotation = Rotation.FromYaw(Time.Now * 35f);
		}
	}

	protected override void OnFixedUpdate()
	{
		if (!Networking.IsHost || IsProxy || Claimed || Kind != LootKind.Health || !GameInProgress())
			return;

		foreach (var survivor in Scene.GetAllComponents<SurvivorController>())
		{
			if (!CanReach(survivor, 32f) || !survivor.RestoreHealth(50f))
				continue;

			Claimed = true;
			GameObject.Destroy();
			return;
		}
	}

	public static LootPickup FindNearestWeapon(SurvivorController survivor)
	{
		if (!survivor.IsValid())
			return null;

		var manager = survivor.Scene.GetAllComponents<GameManager>().FirstOrDefault();
		if (manager != null && manager.State != GameState.InProgress)
			return null;

		return survivor.Scene.GetAllComponents<LootPickup>()
			.Where(x => x.Kind != LootKind.Health && x.Weapon != null && x.CanReach(survivor, WeaponPickupRange))
			.OrderBy(x => (x.WorldPosition - survivor.WorldPosition).LengthSquared)
			.FirstOrDefault();
	}

	internal bool TryEquip(SurvivorController survivor)
	{
		if (!Networking.IsHost || !GameInProgress() || Kind == LootKind.Health
			|| !CanReach(survivor, WeaponPickupRange) || Weapon == null || !Weapon.Validate())
		{
			Log.Warning("Rejected an unavailable or out-of-range weapon pickup.");
			return false;
		}

		Claimed = true;
		survivor.EquipWeapon(Weapon);
		GameObject.Destroy();
		return true;
	}

	private bool CanReach(SurvivorController survivor, float range)
	{
		if (!Active || Claimed || !survivor.IsValid() || !survivor.Active || survivor.Health <= 0f
			|| (WorldPosition - (survivor.WorldPosition + Vector3.Up * 16f)).LengthSquared > range * range)
			return false;

		var trace = Scene.Trace.FromTo(survivor.WorldPosition + Vector3.Up * 36f, WorldPosition)
			.IgnoreGameObjectHierarchy(survivor.GameObject).IgnoreGameObjectHierarchy(GameObject).Run();
		return !trace.Hit && !trace.StartedSolid;
	}

	private bool GameInProgress()
	{
		var manager = Scene.GetAllComponents<GameManager>().FirstOrDefault();
		return manager == null || manager.State == GameState.InProgress;
	}
}
