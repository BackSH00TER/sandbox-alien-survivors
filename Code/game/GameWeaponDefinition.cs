using Sandbox;

public enum GameWeaponHoldType
{
	None = 0,
	Pistol = 1,
	Rifle = 2,
	Shotgun = 3,
	HoldItem = 4,
	Punch = 5,
	Swing = 6,
	RPG = 7,
	Physgun = 8
}

/// <summary>Base weapon stats; player modifiers are applied without changing the shared resource.</summary>
[AssetType(Name = "Weapon", Extension = "weapon", Category = "weapons")]
public sealed partial class GameWeaponDefinition : GameResource
{
	[Property] public string DisplayName { get; set; } = "Pistol";
	[Property, Range(0.1f, 20f)] public float FireRate { get; set; } = 1f;
	[Property, Range(1, 32)] public int BulletsPerShot { get; set; } = 1;
	[Property, Range(0f, 180f), Description("Total angle of an evenly spaced multi-bullet fan. A single bullet fires straight.")] public float SpreadDegrees { get; set; } = 0f;
	[Property, Range(1f, 1000f)] public float Damage { get; set; } = 10f;
	[Property, Range(1f, 10000f)] public float Range { get; set; } = 600f;
	[Property, Range(1f, 10000f)] public float ProjectileSpeed { get; set; } = 450f;
	[Property, Range(0.1f, 16f)] public float ProjectileRadius { get; set; } = 2f;
	[Property, Description("Character weapon hold style; independent of the weapon visual.")] public GameWeaponHoldType HoldType { get; set; } = GameWeaponHoldType.Pistol;
	[Property, Description("Weapon visual prefab with its grip at the root and a GameWeaponModel muzzle marker. Used for held weapons and pickups.")] public PrefabFile ModelPrefab { get; set; }

	public bool Validate()
	{
		if (string.IsNullOrWhiteSpace(DisplayName)
			|| !float.IsFinite(FireRate) || FireRate <= 0f
			|| BulletsPerShot < 1 || BulletsPerShot > 32
			|| !float.IsFinite(SpreadDegrees) || SpreadDegrees < 0f || SpreadDegrees > 180f
			|| !float.IsFinite(Damage) || Damage <= 0f
			|| !float.IsFinite(Range) || Range <= 0f
			|| !float.IsFinite(ProjectileSpeed) || ProjectileSpeed <= 0f
			|| !float.IsFinite(ProjectileRadius) || ProjectileRadius <= 0f)
		{
			Log.Error($"Weapon '{ResourcePath}' has invalid firing stats.");
			return false;
		}

		if (ModelPrefab == null)
		{
			Log.Error($"Weapon '{ResourcePath}' requires a model prefab.");
			return false;
		}

		return true;
	}
}
