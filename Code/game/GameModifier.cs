using System;
using System.Collections.Generic;
using System.Linq;

public enum GameModifier
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
	FireSpreadChance,
	ExtraOption
}

public sealed class ModifierDefinition
{
	public GameModifier Id { get; init; }
	public string Icon { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public float DrawWeight { get; init; } = 1f;
	public int MaxPicks { get; init; } = int.MaxValue;
	public IReadOnlyList<GameModifier> Prerequisites { get; init; } = Array.Empty<GameModifier>();
	public GameDamageType? EffectType { get; init; }
	public DamageEffectStat? EffectStat { get; init; }
	public float Amount { get; init; }

	public bool IsAvailable(Func<GameModifier, int> getModifierPickCount) =>
		getModifierPickCount(Id) < MaxPicks && Prerequisites.All(prerequisite => getModifierPickCount(prerequisite) > 0);
}

public static class ModifierCatalog
{
	public static IReadOnlyList<ModifierDefinition> All { get; } = new[]
	{
		new ModifierDefinition { Id = GameModifier.AttackSpeed, Icon = "bolt", Name = "Rapid Strikes",
			Description = "+100% attack rate per stack." },
		new ModifierDefinition { Id = GameModifier.AttackRange, Icon = "open_with", Name = "Long Reach",
			Description = "+100% attack range per stack." },
		new ModifierDefinition { Id = GameModifier.Vitality, Icon = "favorite", Name = "Vitality",
			Description = "+100 maximum health and restore 100 health." },
		new ModifierDefinition { Id = GameModifier.FireRounds, Icon = "local_fire_department", Name = "Fire Rounds",
			Description = "20% chance per bullet to ignite enemies: 5 damage/second for 5 seconds. One stack by default.",
			EffectType = GameDamageType.Fire, MaxPicks = 1 },
		FireUpgrade(
			id: GameModifier.FireApplicationChance,
			name: "Ignition Chance",
			description: "+10 percentage points fire bullet chance per pick (up to 100%).",
			stat: DamageEffectStat.ApplicationChance,
			amount: 0.1f,
			maxPicks: 8),
		FireUpgrade(
			id: GameModifier.FireDuration,
			name: "Lasting Flames",
			description: "+2 seconds fire duration per pick.",
			stat: DamageEffectStat.Duration,
			amount: 2f),
		FireUpgrade(
			id: GameModifier.FireDamage,
			name: "Hotter Flames",
			description: "+5 fire damage/second per stack per pick.",
			stat: DamageEffectStat.DamagePerSecond,
			amount: 5f),
		FireUpgrade(
			id: GameModifier.FireStacks,
			name: "Layered Flames",
			description: "+1 maximum simultaneous fire stack per pick. Each application expires separately.",
			stat: DamageEffectStat.MaxStacks,
			amount: 1f),
		new ModifierDefinition { Id = GameModifier.FireSpread, Icon = "local_fire_department", Name = "Spreading Flames",
			Description = "Each burning enemy has a 10% chance/second to spread fire to one enemy within 120 units, including burning enemies below the stack cap.",
			EffectType = GameDamageType.Fire, MaxPicks = 1, Prerequisites = new[] { GameModifier.FireRounds } },
		FireUpgrade(
			id: GameModifier.FireSpreadChance,
			name: "Wildfire",
			description: "+10 percentage points fire spread chance per pick (up to 100%).",
			stat: DamageEffectStat.SpreadChance,
			amount: 0.1f,
			maxPicks: 9,
			prerequisite: GameModifier.FireSpread),
		new ModifierDefinition { Id = GameModifier.ExtraOption, Icon = "library_add", Name = "More Choices",
			Description = "Rare: +1 modifier option on future upgrade picks, up to 8 options at rank 5. Limited by eligible upgrades.",
			DrawWeight = 0.2f, MaxPicks = 5 }
	};

	private static ModifierDefinition FireUpgrade(GameModifier id, string name, string description,
		DamageEffectStat stat, float amount, int maxPicks = int.MaxValue, GameModifier prerequisite = GameModifier.FireRounds) =>
		new ModifierDefinition
		{
			Id = id, Icon = "local_fire_department", Name = name, Description = description,
			EffectType = GameDamageType.Fire, EffectStat = stat, Amount = amount,
			MaxPicks = maxPicks, Prerequisites = new[] { prerequisite }
		};

	public static ModifierDefinition Get(GameModifier modifier) =>
		All.FirstOrDefault(definition => definition.Id == modifier) ?? throw new ArgumentOutOfRangeException(nameof(modifier));
	public static string Icon(GameModifier modifier) => Get(modifier).Icon;
	public static string Name(GameModifier modifier) => Get(modifier).Name;
	public static string Description(GameModifier modifier) => Get(modifier).Description;

	public static int OfferSize(Func<GameModifier, int> getModifierPickCount) =>
		3 + Math.Clamp(getModifierPickCount(GameModifier.ExtraOption), 0, Get(GameModifier.ExtraOption).MaxPicks);

	public static IReadOnlyList<GameModifier> RollOffer(Func<GameModifier, int> getModifierPickCount, Func<float> random)
	{
		var available = All.Where(modifier => modifier.IsAvailable(getModifierPickCount)).ToList();
		if (available.Count < 3)
			throw new InvalidOperationException("GameModifier offers require at least three available upgrades.");

		int count = Math.Min(OfferSize(getModifierPickCount), available.Count);
		var result = new List<GameModifier>(count);
		for (int i = 0; i < count; i++)
		{
			var chosen = WeightedDraw.Pick(available, modifier => modifier.DrawWeight, random());
			result.Add(chosen.Id);
			available.Remove(chosen);
		}
		return result;
	}
}
