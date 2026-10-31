using Sandbox;

/// <summary>Base weapon stats; survivor modifiers are applied without changing the shared resource.</summary>
[AssetType(Name = "Weapon", Extension = "weapon", Category = "weapons")]
public sealed partial class GameWeaponDefinition : GameResource
{
	[Property] public string DisplayName { get; set; } = "Pistol";
	[Property, Range(0.1f, 20f)] public float FireRate { get; set; } = 1f;
	[Property, Range(1, 32)] public int BulletsPerShot { get; set; } = 1;
	[Property, Range(0f, 180f), Description("Total angle of an evenly spaced multi-bullet fan. A single bullet fires straight.")]
	public float SpreadDegrees { get; set; } = 0f;
	[Property, Range(1f, 1000f)] public float Damage { get; set; } = 10f;
	[Property, Range(1f, 10000f)] public float Range { get; set; } = 600f;
	[Property, Range(1f, 10000f)] public float ProjectileSpeed { get; set; } = 450f;
	[Property, Range(0.1f, 16f)] public float ProjectileRadius { get; set; } = 2f;
	[Property]
	public Sandbox.Citizen.CitizenAnimationHelper.HoldTypes HoldType { get; set; }
		= Sandbox.Citizen.CitizenAnimationHelper.HoldTypes.Pistol;
	[Property, Description("Optional model aligned to hold_R, with a muzzle attachment for bullet spawning. Leave empty for a prototype model matching the hold type.")]
	public Model WorldModel { get; set; }

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

		return true;
	}
}
