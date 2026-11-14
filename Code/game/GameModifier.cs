using System;
using System.Collections.Generic;
using System.Linq;

public enum GameModifier
{
	AttackSpeed = 0,
	Vitality = 2,
	FireRounds = 3,
	FireApplicationChance = 4,
	FireDuration = 5,
	FireDamage = 6,
	FireStacks = 7,
	FireSpread = 8,
	FireSpreadChance = 9,
	ExtraOption = 10,
	Vacuum = 11,
	ShockRounds = 12,
	ShockApplicationChance = 13,
	ShockDamage = 14,
	ShockChainChance = 15,
	ShockChains = 16,
	AttackDamage = 17,
	MoveSpeed = 18,
	HealthPickupBonus = 19,
	HealthRegeneration = 20,
	ArmorPickupBonus = 21,
	SplashChance = 22,
	SplashDamage = 23,
	PierceChance = 24,
	PierceTargets = 25,
	SlowingRounds = 26,
	ExtraBullets = 27,
	EmpoweredShots = 28,
	CritChance = 29,
	CritDamage = 30
}

public interface IModifierRecipient
{
	int ModifierStacks(GameModifier modifier);
	void AddModifierStack(GameModifier modifier);
	void RefreshAttackCooldown();
	void IncreaseMaxHealth(float amount);
}

public sealed class ModifierDefinition
{
	public GameModifier Id { get; init; }
	public string Icon { get; init; }
	public string Name { get; init; }
	public string Description { get; init; }
	public GameRarity Rarity { get; init; }
	public string InventoryClass { get; init; } = "";
	public int MaxPicks { get; init; } = int.MaxValue;
	public IReadOnlyList<GameModifier> Prerequisites { get; init; } = Array.Empty<GameModifier>();
	public GameDamageType? EffectType { get; init; }
	public DamageEffectStat? EffectStat { get; init; }
	public float Amount { get; init; }
	public Action<IModifierRecipient, float> OnPicked { get; init; }

	public bool IsAvailable(Func<GameModifier, int> getModifierPickCount)
	{
		return getModifierPickCount(Id) < MaxPicks
			&& Prerequisites.All(prerequisite => getModifierPickCount(prerequisite) > 0);
	}

	public void ApplyTo(IModifierRecipient recipient)
	{
		if (!IsAvailable(recipient.ModifierStacks))
		{
			throw new InvalidOperationException($"Modifier '{Id}' is locked or capped.");
		}

		recipient.AddModifierStack(Id);
		OnPicked?.Invoke(recipient, Amount);
	}
}

public static class ModifierCatalog
{
	public static IReadOnlyList<RarityBucket> Rarities { get; } = Array.AsReadOnly(new[]
	{
		new RarityBucket(id: GameRarity.Common, chance: 0.9f),
		new RarityBucket(id: GameRarity.Uncommon, chance: 0f),
		new RarityBucket(id: GameRarity.Rare, chance: 0.1f),
		new RarityBucket(id: GameRarity.Epic, chance: 0f),
		new RarityBucket(id: GameRarity.Legendary, chance: 0f)
	});

	public static IReadOnlyList<ModifierDefinition> All { get; } = Array.AsReadOnly(new[]
	{
		new ModifierDefinition
		{
			Id = GameModifier.AttackSpeed,
			Icon = "bolt",
			Name = "Rapid Strikes",
			Description = "+100% attack rate per stack.",
			InventoryClass = "attackspeed",
			Rarity = GameRarity.Common,
			Amount = 1f,
			OnPicked = (recipient, amount) => recipient.RefreshAttackCooldown()
		},
		new ModifierDefinition
		{
			Id = GameModifier.Vitality,
			Icon = "favorite",
			Name = "Vitality",
			Description = "+100 maximum health and restore 100 health.",
			InventoryClass = "vitality",
			Rarity = GameRarity.Common,
			Amount = 100f,
			OnPicked = (recipient, amount) => recipient.IncreaseMaxHealth(amount)
		},
		new ModifierDefinition
		{
			Id = GameModifier.FireRounds,
			Icon = "local_fire_department",
			Name = "Fire Rounds",
			Description = "20% chance per bullet to ignite enemies: 5 damage/second for 5 seconds. One stack by default.",
			InventoryClass = "damage-effect",
			Rarity = GameRarity.Common,
			EffectType = GameDamageType.Fire,
			MaxPicks = 1
		},
		FireUpgrade(
			id: GameModifier.FireApplicationChance,
			name: "Ignition Chance",
			description: "+10 percentage points fire bullet chance per pick (up to 100%).",
			stat: DamageEffectStat.ApplicationChance,
			amount: 0.1f,
			maxPicks: 8
		),
		FireUpgrade(
			id: GameModifier.FireDuration,
			name: "Lasting Flames",
			description: "+2 seconds fire duration per pick.",
			stat: DamageEffectStat.Duration,
			amount: 2f
		),
		FireUpgrade(
			id: GameModifier.FireDamage,
			name: "Hotter Flames",
			description: "+5 fire damage/second per stack per pick.",
			stat: DamageEffectStat.DamagePerSecond,
			amount: 5f
		),
		FireUpgrade(
			id: GameModifier.FireStacks,
			name: "Layered Flames",
			description: "+1 maximum simultaneous fire stack per pick. Each application expires separately.",
			stat: DamageEffectStat.MaxStacks,
			amount: 1f
		),
		new ModifierDefinition
		{
			Id = GameModifier.FireSpread,
			Icon = "local_fire_department",
			Name = "Spreading Flames",
			Description = "Each burning enemy has a 10% chance/second to spread fire to one enemy within 120 units, including burning enemies below the stack cap.",
			InventoryClass = "damage-effect",
			Rarity = GameRarity.Common,
			EffectType = GameDamageType.Fire,
			MaxPicks = 1,
			Prerequisites = Array.AsReadOnly(new[] { GameModifier.FireRounds })
		},
		FireUpgrade(
			id: GameModifier.FireSpreadChance,
			name: "Wildfire",
			description: "+10 percentage points fire spread chance per pick (up to 100%).",
			stat: DamageEffectStat.SpreadChance,
			amount: 0.1f,
			maxPicks: 9,
			prerequisite: GameModifier.FireSpread
		),
		new ModifierDefinition
		{
			Id = GameModifier.ExtraOption,
			Icon = "library_add",
			Name = "More Choices",
			Description = "Rare: +1 modifier option on future upgrade picks.",
			Rarity = GameRarity.Rare,
			Amount = 1f,
			MaxPicks = 5
		},
		new ModifierDefinition
		{
			Id = GameModifier.Vacuum,
			Icon = "air",
			Name = "Vacuum",
			Description = "Common: +20% base XP collection range per pick.",
			Rarity = GameRarity.Common,
			Amount = 0.2f,
			MaxPicks = 5
		},
		new ModifierDefinition
		{
			Id = GameModifier.ShockRounds,
			Icon = "electric_bolt",
			Name = "Shock Rounds",
			Description = "20% shock bullet chance: 10 instant electric damage, with a 20% chance to chain once within 180 units.",
			InventoryClass = "damage-effect",
			Rarity = GameRarity.Common,
			EffectType = GameDamageType.Electricity,
			MaxPicks = 1
		},
		ShockUpgrade(
			id: GameModifier.ShockApplicationChance,
			name: "Static Charge",
			description: "+10 percentage points shock bullet chance per pick (up to 100%).",
			stat: DamageEffectStat.ApplicationChance,
			amount: 0.1f,
			maxPicks: 8
		),
		ShockUpgrade(
			id: GameModifier.ShockDamage,
			name: "High Voltage",
			description: "+5 base electric damage per pick. Each hop loses 10% of the base damage, down to a 10% minimum.",
			stat: DamageEffectStat.InstantDamage,
			amount: 5f
		),
		ShockUpgrade(
			id: GameModifier.ShockChainChance,
			name: "Conductivity",
			description: "+10 percentage points chain chance per hop per pick (up to 100%).",
			stat: DamageEffectStat.ChainChance,
			amount: 0.1f,
			maxPicks: 8
		),
		ShockUpgrade(
			id: GameModifier.ShockChains,
			name: "Chain Reaction",
			description: "+1 additional enemy and +60 units range per shock chain hop per pick. Each hop rolls its own chance; walls block chains.",
			stat: DamageEffectStat.MaxChains,
			amount: 1f
		),
		new ModifierDefinition
		{
			Id = GameModifier.AttackDamage,
			Icon = "swords",
			Name = "Heavy Rounds",
			Description = "+20% base weapon damage per pick.",
			Rarity = GameRarity.Common,
			Amount = 0.2f
		},
		new ModifierDefinition
		{
			Id = GameModifier.MoveSpeed,
			Icon = "directions_run",
			Name = "Fleet Footed",
			Description = "+10% base movement speed per pick.",
			Rarity = GameRarity.Common,
			Amount = 0.1f
		},
		new ModifierDefinition
		{
			Id = GameModifier.HealthPickupBonus,
			Icon = "healing",
			Name = "Better Medicine",
			Description = "+25% base health pickup healing per pick. Does not affect regeneration or revives.",
			Rarity = GameRarity.Common,
			Amount = 0.25f
		},
		new ModifierDefinition
		{
			Id = GameModifier.HealthRegeneration,
			Icon = "health_and_safety",
			Name = "Regeneration",
			Description = "Restore 1 health per second per pick while alive.",
			Rarity = GameRarity.Common,
			Amount = 1f
		},
		new ModifierDefinition
		{
			Id = GameModifier.ArmorPickupBonus,
			Icon = "shield",
			Name = "Armor Salvager",
			Description = "+25% base armor pickup amount per pick, rounded up.",
			Rarity = GameRarity.Common,
			Amount = 0.25f
		},
		new ModifierDefinition
		{
			Id = GameModifier.SplashChance,
			Icon = "blur_on",
			Name = "Splash Rounds",
			Description = "+10 percentage points splash chance per bullet per pick (up to 100%). Deals 50% bullet damage to other enemies within 60 units; walls block splash.",
			Rarity = GameRarity.Common,
			Amount = 0.1f,
			MaxPicks = 10
		},
		new ModifierDefinition
		{
			Id = GameModifier.SplashDamage,
			Icon = "flare",
			Name = "Bigger Splash",
			Description = "+25 percentage points splash damage per pick, based on bullet damage. Does not increase radius.",
			Rarity = GameRarity.Common,
			Amount = 0.25f,
			Prerequisites = Array.AsReadOnly(new[] { GameModifier.SplashChance })
		},
		new ModifierDefinition
		{
			Id = GameModifier.PierceChance,
			Icon = "double_arrow",
			Name = "Piercing Rounds",
			Description = "+10 percentage points piercing chance per bullet per pick (up to 100%). A successful bullet pierces one additional enemy; walls and crates stop it.",
			Rarity = GameRarity.Common,
			Amount = 0.1f,
			MaxPicks = 10
		},
		new ModifierDefinition
		{
			Id = GameModifier.PierceTargets,
			Icon = "keyboard_double_arrow_right",
			Name = "Deep Penetration",
			Description = "+1 additional enemy per successful piercing bullet per pick.",
			Rarity = GameRarity.Common,
			Amount = 1f,
			Prerequisites = Array.AsReadOnly(new[] { GameModifier.PierceChance })
		},
		new ModifierDefinition
		{
			Id = GameModifier.SlowingRounds,
			Icon = "hourglass_bottom",
			Name = "Slowing Rounds",
			Description = "Direct bullet hits slow enemy movement by 50% for 2 seconds. Further hits refresh the duration; attacks are not stunned.",
			Rarity = GameRarity.Common,
			Amount = 2f,
			MaxPicks = 1
		},
		new ModifierDefinition
		{
			Id = GameModifier.ExtraBullets,
			Icon = "filter_none",
			Name = "Bonus Volley",
			Description = "Per trigger pull: +10 percentage points chance (up to 100%) and +1 bonus bullet per pick. Bonus bullets use weapon spread.",
			Rarity = GameRarity.Common,
			Amount = 0.1f
		},
		new ModifierDefinition
		{
			Id = GameModifier.EmpoweredShots,
			Icon = "crisis_alert",
			Name = "Power Shot",
			Description = "Every 10th trigger pull deals double bullet damage, guaranteed. Each further pick reduces the interval by 1, down to every shot.",
			Rarity = GameRarity.Common,
			Amount = 1f,
			MaxPicks = 10
		},
		new ModifierDefinition
		{
			Id = GameModifier.CritChance,
			Icon = "gps_fixed",
			Name = "Critical Chance",
			Description = "+10 percentage points critical chance per bullet per pick, from 5% up to 55%. A critical bullet also boosts its splash, fire and shock damage.",
			Rarity = GameRarity.Common,
			Amount = 0.1f,
			MaxPicks = 5
		},
		new ModifierDefinition
		{
			Id = GameModifier.CritDamage,
			Icon = "auto_fix_high",
			Name = "Critical Damage",
			Description = "+50 percentage points critical damage per pick: 200% to 250%, then 300%, and so on.",
			Rarity = GameRarity.Common,
			Amount = 0.5f
		}
	});

	private static readonly IReadOnlyDictionary<GameModifier, ModifierDefinition> ById = All.ToDictionary(definition => definition.Id);
	private static readonly IReadOnlyDictionary<GameRarity, RarityBucket> ByRarity = Rarities.ToDictionary(rarity => rarity.Id);

	static ModifierCatalog()
	{
		var modifierIds = All.Select(definition => definition.Id);
		var expectedModifierIds = Enum.GetValues<GameModifier>();
		var rarityIds = Rarities.Select(rarity => rarity.Id);
		var expectedRarityIds = Enum.GetValues<GameRarity>();
		if (!modifierIds.SequenceEqual(expectedModifierIds) || !rarityIds.SequenceEqual(expectedRarityIds))
		{
			throw new InvalidOperationException("Every modifier and rarity needs exactly one ordered catalog entry.");
		}

		foreach (var definition in All)
		{
			if (string.IsNullOrWhiteSpace(definition.Name) || string.IsNullOrWhiteSpace(definition.Icon)
				|| string.IsNullOrWhiteSpace(definition.Description) || definition.InventoryClass == null
				|| !ByRarity.ContainsKey(definition.Rarity) || definition.MaxPicks <= 0
				|| !float.IsFinite(definition.Amount) || definition.Amount < 0f
				|| definition.Prerequisites == null
				|| definition.Prerequisites.Any(id => !ById.ContainsKey(id) || id == definition.Id))
			{
				throw new InvalidOperationException($"Modifier '{definition.Id}' has invalid metadata, rarity, amount, cap or prerequisites.");
			}
			if (definition.EffectType.HasValue && !Enum.IsDefined(definition.EffectType.Value))
			{
				throw new InvalidOperationException($"Modifier '{definition.Id}' needs a valid damage type.");
			}
			if (definition.EffectStat.HasValue
				&& (!definition.EffectType.HasValue || !Enum.IsDefined(definition.EffectStat.Value)))
			{
				throw new InvalidOperationException($"Modifier '{definition.Id}' needs a damage type and a valid effect stat.");
			}
		}

		foreach (var rarity in Rarities)
		{
			if (rarity.Chance > 0f && !All.Any(definition => definition.Rarity == rarity.Id))
			{
				throw new InvalidOperationException($"Enabled rarity '{rarity.Id}' needs at least one modifier.");
			}
		}

		var totalChance = Rarities.Sum(rarity => rarity.Chance);
		if (MathF.Abs(totalChance - 1f) > 0.0001f)
		{
			throw new InvalidOperationException("Modifier rarity chances must total 100%.");
		}
	}

	private static ModifierDefinition FireUpgrade(
		GameModifier id,
		string name,
		string description,
		DamageEffectStat stat,
		float amount,
		int maxPicks = int.MaxValue,
		GameModifier prerequisite = GameModifier.FireRounds,
		GameRarity rarity = GameRarity.Common
	)
	{
		return new ModifierDefinition
		{
			Id = id,
			Icon = "local_fire_department",
			Name = name,
			Description = description,
			InventoryClass = "damage-effect",
			Rarity = rarity,
			EffectType = GameDamageType.Fire,
			EffectStat = stat,
			Amount = amount,
			MaxPicks = maxPicks,
			Prerequisites = Array.AsReadOnly(new[] { prerequisite })
		};
	}

	public static float Bonus(GameModifier modifier, Func<GameModifier, int> getModifierPickCount)
	{
		var definition = Get(modifier);
		var ranks = Math.Clamp(
			value: getModifierPickCount(modifier),
			min: 0,
			max: definition.MaxPicks
		);
		return definition.Amount * ranks;
	}

	private static ModifierDefinition ShockUpgrade(
		GameModifier id,
		string name,
		string description,
		DamageEffectStat stat,
		float amount,
		int maxPicks = int.MaxValue
	)
	{
		return new ModifierDefinition
		{
			Id = id,
			Icon = "electric_bolt",
			Name = name,
			Description = description,
			InventoryClass = "damage-effect",
			Rarity = GameRarity.Common,
			EffectType = GameDamageType.Electricity,
			EffectStat = stat,
			Amount = amount,
			MaxPicks = maxPicks,
			Prerequisites = Array.AsReadOnly(new[] { GameModifier.ShockRounds })
		};
	}

	public static ModifierDefinition Get(GameModifier modifier)
	{
		if (!ById.TryGetValue(modifier, out var definition))
		{
			throw new ArgumentOutOfRangeException(nameof(modifier), "Modifier must have a catalog definition.");
		}
		return definition;
	}

	public static RarityBucket GetRarity(GameRarity rarity)
	{
		if (!ByRarity.TryGetValue(rarity, out var definition))
		{
			throw new ArgumentOutOfRangeException(nameof(rarity), "Rarity must have a catalog definition.");
		}
		return definition;
	}

	public static string Icon(GameModifier modifier) => Get(modifier).Icon;
	public static string Name(GameModifier modifier) => Get(modifier).Name;
	public static string Description(GameModifier modifier) => Get(modifier).Description;

	public static float AttackRateMultiplier(Func<GameModifier, int> getModifierPickCount)
	{
		var definition = Get(GameModifier.AttackSpeed);
		return 1f + definition.Amount * getModifierPickCount(definition.Id);
	}

	public static float ExperienceCollectionRangeMultiplier(Func<GameModifier, int> getModifierPickCount)
	{
		var definition = Get(GameModifier.Vacuum);
		int ranks = getModifierPickCount(GameModifier.Vacuum);
		int bonus = Math.Clamp(
			value: ranks,
			min: 0,
			max: definition.MaxPicks
		);
		return 1f + definition.Amount * bonus;
	}

	public static int OfferSize(Func<GameModifier, int> getModifierPickCount)
	{
		var definition = Get(GameModifier.ExtraOption);
		int ranks = getModifierPickCount(GameModifier.ExtraOption);
		int bonus = Math.Clamp(
			value: ranks,
			min: 0,
			max: definition.MaxPicks
		);
		return 3 + (int)definition.Amount * bonus;
	}

	public static bool HasAlternativeOffer(Func<GameModifier, int> getModifierPickCount, IReadOnlyList<GameModifier> currentOffer)
	{
		return All.Any(modifier => GetRarity(modifier.Rarity).Chance > 0f
			&& modifier.IsAvailable(getModifierPickCount) && !currentOffer.Contains(modifier.Id));
	}

	public static IReadOnlyList<GameModifier> RollOffer(
		Func<GameModifier, int> getModifierPickCount,
		Func<float> random,
		IReadOnlyList<GameModifier> previousOffer = null
	)
	{
		var available = All.Where(modifier => GetRarity(modifier.Rarity).Chance > 0f
			&& modifier.IsAvailable(getModifierPickCount)).ToList();
		if (available.Count < 3)
		{
			throw new InvalidOperationException("GameModifier offers require at least three available upgrades.");
		}

		int requestedCount = OfferSize(getModifierPickCount);
		int count = Math.Min(requestedCount, available.Count);
		var result = new List<GameModifier>(count);
		for (int i = 0; i < count; i++)
		{
			var chosen = Draw(available, random);
			result.Add(chosen.Id);
			available.Remove(chosen);
		}
		// Changing card order alone is not a new offer. Replace one card if the draw repeats the same set.
		if (previousOffer != null && result.All(previousOffer.Contains))
		{
			var alternatives = available.Where(modifier => !previousOffer.Contains(modifier.Id)).ToList();
			if (alternatives.Count > 0)
			{
				var replacement = Draw(alternatives, random);
				result[count - 1] = replacement.Id;
			}
		}
		return result;
	}

	private static ModifierDefinition Draw(IReadOnlyList<ModifierDefinition> available, Func<float> random)
	{
		// Exhausted or ineligible buckets have no mass in this draw, including reroll replacements.
		var rarities = Rarities.Where(rarity => rarity.Chance > 0f
			&& available.Any(modifier => modifier.Rarity == rarity.Id)).ToArray();
		return RarityDraw.Pick(
			buckets: rarities,
			getCandidates: rarity => available.Where(modifier => modifier.Rarity == rarity).ToArray(),
			random: random
		);
	}
}
