public enum Modifier
{
	AttackSpeed,
	AttackRange,
	Vitality
}

public static class ModifierCatalog
{
	public static string Name(Modifier modifier) => modifier switch
	{
		Modifier.AttackSpeed => "Rapid Strikes",
		Modifier.AttackRange => "Long Reach",
		Modifier.Vitality => "Vitality",
		_ => throw new System.ArgumentOutOfRangeException(nameof(modifier))
	};

	public static string Description(Modifier modifier) => modifier switch
	{
		Modifier.AttackSpeed => "+100% attack rate per stack.",
		Modifier.AttackRange => "+100% attack range per stack.",
		Modifier.Vitality => "+100 maximum health and restore 100 health.",
		_ => throw new System.ArgumentOutOfRangeException(nameof(modifier))
	};
}
