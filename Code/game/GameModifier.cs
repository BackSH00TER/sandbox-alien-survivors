public enum GameModifier
{
	AttackSpeed,
	AttackRange,
	Vitality
}

public static class ModifierCatalog
{
	public static string Icon(GameModifier modifier) => modifier switch
	{
		GameModifier.AttackSpeed => "bolt",
		GameModifier.AttackRange => "open_with",
		GameModifier.Vitality => "favorite",
		_ => throw new System.ArgumentOutOfRangeException(nameof(modifier))
	};

	public static string Name(GameModifier modifier) => modifier switch
	{
		GameModifier.AttackSpeed => "Rapid Strikes",
		GameModifier.AttackRange => "Long Reach",
		GameModifier.Vitality => "Vitality",
		_ => throw new System.ArgumentOutOfRangeException(nameof(modifier))
	};

	public static string Description(GameModifier modifier) => modifier switch
	{
		GameModifier.AttackSpeed => "+100% attack rate per stack.",
		GameModifier.AttackRange => "+100% attack range per stack.",
		GameModifier.Vitality => "+100 maximum health and restore 100 health.",
		_ => throw new System.ArgumentOutOfRangeException(nameof(modifier))
	};
}
