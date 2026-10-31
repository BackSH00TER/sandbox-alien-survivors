using System;
using System.Collections.Generic;
using System.Linq;

public enum Modifier
{
	AttackSpeed,
	AttackRange,
	Vitality,
	FireRounds,
	FireApplicationChance,
	FireDuration,
	FireDamage,
	FireStacks,
	FireSpread,
	FireSpreadChance
}

public sealed class ModifierDefinition
{
	public Modifier Id { get; init; }
	public string Icon { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public float DrawWeight { get; init; } = 1f;
	public int MaxPicks { get; init; } = int.MaxValue;
	public IReadOnlyList<Modifier> Prerequisites { get; init; } = Array.Empty<Modifier>();
	public DamageType? EffectType { get; init; }
	public DamageEffectStat? EffectStat { get; init; }
	public float Amount { get; init; }

	public bool IsAvailable(Func<Modifier, int> stacks) =>
		stacks(Id) < MaxPicks && Prerequisites.All(x => stacks(x) > 0);
}

public static class ModifierCatalog
{
	public static IReadOnlyList<ModifierDefinition> All { get; } = new[]
	{
		new ModifierDefinition { Id = Modifier.AttackSpeed, Icon = "bolt", Name = "Rapid Strikes",
			Description = "+100% attack rate per stack." },
		new ModifierDefinition { Id = Modifier.AttackRange, Icon = "open_with", Name = "Long Reach",
			Description = "+100% attack range per stack." },
		new ModifierDefinition { Id = Modifier.Vitality, Icon = "favorite", Name = "Vitality",
			Description = "+100 maximum health and restore 100 health." },
		new ModifierDefinition { Id = Modifier.FireRounds, Icon = "local_fire_department", Name = "Fire Rounds",
			Description = "20% chance per bullet to ignite enemies: 5 damage/second for 5 seconds. One stack by default.",
			EffectType = DamageType.Fire, MaxPicks = 1 },
		FireUpgrade(Modifier.FireApplicationChance, "Ignition Chance", "+10 percentage points fire bullet chance per pick (up to 100%).",
			DamageEffectStat.ApplicationChance, 0.1f, 8),
		FireUpgrade(Modifier.FireDuration, "Lasting Flames", "+2 seconds fire duration per pick.",
			DamageEffectStat.Duration, 2f),
		FireUpgrade(Modifier.FireDamage, "Hotter Flames", "+5 fire damage/second per stack per pick.",
			DamageEffectStat.DamagePerSecond, 5f),
		FireUpgrade(Modifier.FireStacks, "Layered Flames", "+1 maximum simultaneous fire stack per pick. Each application expires separately.",
			DamageEffectStat.MaxStacks, 1f),
		new ModifierDefinition { Id = Modifier.FireSpread, Icon = "local_fire_department", Name = "Spreading Flames",
			Description = "Each burning enemy has a 10% chance/second to spread fire to one enemy within 120 units, including burning enemies below the stack cap.",
			EffectType = DamageType.Fire, MaxPicks = 1, Prerequisites = new[] { Modifier.FireRounds } },
		FireUpgrade(Modifier.FireSpreadChance, "Wildfire", "+10 percentage points fire spread chance per pick (up to 100%).",
			DamageEffectStat.SpreadChance, 0.1f, 9, Modifier.FireSpread)
	};

	private static ModifierDefinition FireUpgrade(Modifier id, string name, string description,
		DamageEffectStat stat, float amount, int maxPicks = int.MaxValue, Modifier prerequisite = Modifier.FireRounds) =>
		new ModifierDefinition
		{
			Id = id, Icon = "local_fire_department", Name = name, Description = description,
			EffectType = DamageType.Fire, EffectStat = stat, Amount = amount,
			MaxPicks = maxPicks, Prerequisites = new[] { prerequisite }
		};

	public static ModifierDefinition Get(Modifier modifier) =>
		All.FirstOrDefault(x => x.Id == modifier) ?? throw new ArgumentOutOfRangeException(nameof(modifier));
	public static string Icon(Modifier modifier) => Get(modifier).Icon;
	public static string Name(Modifier modifier) => Get(modifier).Name;
	public static string Description(Modifier modifier) => Get(modifier).Description;

	public static IReadOnlyList<Modifier> RollOffer(Func<Modifier, int> stacks, Func<float> random)
	{
		var available = All.Where(x => x.IsAvailable(stacks)).ToList();
		if (available.Count < 3)
			throw new InvalidOperationException("Modifier offers require at least three available upgrades.");

		var result = new List<Modifier>();
		for (int i = 0; i < 3; i++)
		{
			var chosen = WeightedDraw.Pick(available, x => x.DrawWeight, random());
			result.Add(chosen.Id);
			available.Remove(chosen);
		}
		return result;
	}
}
