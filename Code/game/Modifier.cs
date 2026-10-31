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

	public bool IsAvailable(Func<Modifier, int> getModifierPickCount) =>
		getModifierPickCount(Id) < MaxPicks && Prerequisites.All(prerequisite => getModifierPickCount(prerequisite) > 0);
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
		FireUpgrade(
			id: Modifier.FireApplicationChance,
			name: "Ignition Chance",
			description: "+10 percentage points fire bullet chance per pick (up to 100%).",
			stat: DamageEffectStat.ApplicationChance,
			amount: 0.1f,
			maxPicks: 8),
		FireUpgrade(
			id: Modifier.FireDuration,
			name: "Lasting Flames",
			description: "+2 seconds fire duration per pick.",
			stat: DamageEffectStat.Duration,
			amount: 2f),
		FireUpgrade(
			id: Modifier.FireDamage,
			name: "Hotter Flames",
			description: "+5 fire damage/second per stack per pick.",
			stat: DamageEffectStat.DamagePerSecond,
			amount: 5f),
		FireUpgrade(
			id: Modifier.FireStacks,
			name: "Layered Flames",
			description: "+1 maximum simultaneous fire stack per pick. Each application expires separately.",
			stat: DamageEffectStat.MaxStacks,
			amount: 1f),
		new ModifierDefinition { Id = Modifier.FireSpread, Icon = "local_fire_department", Name = "Spreading Flames",
			Description = "Each burning enemy has a 10% chance/second to spread fire to one enemy within 120 units, including burning enemies below the stack cap.",
			EffectType = DamageType.Fire, MaxPicks = 1, Prerequisites = new[] { Modifier.FireRounds } },
		FireUpgrade(
			id: Modifier.FireSpreadChance,
			name: "Wildfire",
			description: "+10 percentage points fire spread chance per pick (up to 100%).",
			stat: DamageEffectStat.SpreadChance,
			amount: 0.1f,
			maxPicks: 9,
			prerequisite: Modifier.FireSpread)
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
		All.FirstOrDefault(definition => definition.Id == modifier) ?? throw new ArgumentOutOfRangeException(nameof(modifier));
	public static string Icon(Modifier modifier) => Get(modifier).Icon;
	public static string Name(Modifier modifier) => Get(modifier).Name;
	public static string Description(Modifier modifier) => Get(modifier).Description;

	public static IReadOnlyList<Modifier> RollOffer(Func<Modifier, int> getModifierPickCount, Func<float> random)
	{
		var available = All.Where(modifier => modifier.IsAvailable(getModifierPickCount)).ToList();
		if (available.Count < 3)
			throw new InvalidOperationException("Modifier offers require at least three available upgrades.");

		var result = new List<Modifier>();
		for (int i = 0; i < 3; i++)
		{
			var chosen = WeightedDraw.Pick(available, modifier => modifier.DrawWeight, random());
			result.Add(chosen.Id);
			available.Remove(chosen);
		}
		return result;
	}
}
