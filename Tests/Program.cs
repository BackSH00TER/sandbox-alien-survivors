using System;
using System.Collections.Generic;
using System.Linq;

var tests = new (string Name, Action Run)[]
{
	("catalog coverage and prerequisites", Catalog),
	("offers are eligible and distinct", Offers),
	("extra options scale, cap and respect eligibility", ExtraOptions),
	("modifier metadata and stable IDs are complete", ModifierCatalogTests.Coverage),
	("modifier rarity-first draws preserve exact probabilities", ModifierCatalogTests.Rarities),
	("empty modifier buckets renormalize, including rerolls", ModifierCatalogTests.EmptyBuckets),
	("modifier effects apply through the catalog", ModifierCatalogTests.Effects),
	("new modifier progression, caps and prerequisites", ShotModifierTests.Progression),
	("bullet procs capture independent immutable settings", ShotModifierTests.Bullets),
	("bonus volleys roll once per trigger and cap their chance", ShotModifierTests.Volleys),
	("power shots guarantee double damage and shorten their interval", ShotModifierTests.PowerShots),
	("invalid shot rolls are rejected", ShotModifierTests.Invalid),
	("critical chance progression and bullet damage are captured once", ShotModifierTests.CriticalHits),
	("critical effects preserve fire ticks, spread and shock chains", ShotModifierTests.CriticalEffects),
	("pickup bonuses use scaled amounts and armor capacity", LootCatalogTests.Bonuses),
	("invalid modifier draws and IDs are rejected", ModifierCatalogTests.Invalid),
	("vacuum range scales, caps and uses common rarity", Vacuum),
	("rerolls change the set without unlocking unavailable modifiers", Rerolls),
	("expanded rerolls preserve size and eligible alternatives", ExpandedRerolls),
	("reroll stat appears once in the upgrades scorecard", RerollStat),
	("weighted draw boundaries", Weights),
	("shared rarity presentation and independent tuning", RarityDrawTests.Presentation),
	("shared rarity draws preserve probabilities and random consumption", RarityDrawTests.Draws),
	("shared rarity draws reject invalid inputs and empty selected buckets", RarityDrawTests.Invalid),
	("loot catalog coverage, effects and magnet flags", LootCatalogTests.Coverage),
	("loot callbacks apply effects and enforce eligibility", LootCatalogTests.Collection),
	("loot rarity buckets preserve exact probabilities", LootCatalogTests.Drops),
	("weapon crates drop only weapons with valid boundaries", LootCatalogTests.WeaponDrops),
	("loot catalog preserves prototype visuals", LootCatalogTests.Visuals),
	("invalid loot rolls and kinds are rejected", LootCatalogTests.Invalid),
	("enemy aggro switches only after the cooldown", EnemyAggroTests.Cooldown),
	("enemy aggro handles unavailable targets", EnemyAggroTests.Unavailable),
	("enemy prefabs contain one typed enemy and preserve their tuning", EnemyPrefabTests.Variants),
	("gameplay spawner references all four enemy prefabs", EnemyPrefabTests.SpawnerReferences),
	("XP orbs merge only within fixed short reach without overflow", ExperienceOrbTests.Reach),
	("XP orbs share eight value-based colors and capped size growth", ExperienceOrbTests.Appearance),
	("XP orb presentation rejects invalid values", ExperienceOrbTests.Invalid),
	("fire chance, defaults and upgrade caps", FireRolls),
	("shock progression and immutable settings", ShockTests.Progression),
	("mixed damage effect rolls never retry", ShockTests.Rolls),
	("shock chains respect limits, rolls and unique targets", ShockTests.Chains),
	("shock damage falls linearly to a ten percent floor", ShockTests.Falloff),
	("shock chain validation and timed isolation", ShockTests.Invalid),
	("instant effects may omit chain settings", ShockTests.OptionalChains),
	("explosion defaults and enemy-only bullet isolation", ExplosionTests.Catalog),
	("explosion settings reject mixed behaviors and timed stacks", ExplosionTests.Invalid),
	("independent expiries and exact stacked damage", Expiries),
	("whole-second ticks and partial renewal damage", TickCadence),
	("oldest replacement and attacker credit", Replacement),
	("spread accepts burning targets only below cap", Spread),
	("clearing and advancing cannot repeat damage", Clear),
	("invalid settings are rejected", Invalid),
	("timeline positions, dimming and pipeline boundaries", Timeline),
	("event catalog metadata, callbacks and exact rarity probabilities", EventCatalogTests.Catalog),
	("event slots draw opening and recurring events from configuration", EventCatalogTests.Schedule),
	("weapon airdrops use shared rare slots without guaranteed arrivals", EventCatalogTests.WeaponAirdrops),
	("event slots own timing, rarity weights and independent cadences", EventCatalogTests.Slots),
	("event slots redistribute empty rarity weights", EventCatalogTests.EmptyRarities),
	("fixed events guarantee one boss at five minutes without random draws", EventCatalogTests.FixedEvents),
	("fixed event reservations skip collisions and retain exact boundaries", EventCatalogTests.Reservations),
	("fixed events reject invalid schedules and overlapping locks", EventCatalogTests.InvalidFixedEvents),
	("event catalog and schedule reject invalid inputs", EventCatalogTests.Invalid),
	("event loot supports fixed and inclusive random counts for every catalog item", EventRewardTests.LootCounts),
	("event XP rewards preserve exact totals in larger orbs", EventRewardTests.Experience),
	("event rewards reject invalid counts, kinds and rolls", EventRewardTests.Invalid),
	("stat history uses half-open 15-second buckets and independent keys", StatHistoryTests.Buckets),
	("stat history carries quiet buckets and final partial totals", StatHistoryTests.Series),
	("stat history handles zero duration, exact boundaries and snapshots", StatHistoryTests.Boundaries),
	("a million stat events retain only bucket aggregates", StatHistoryTests.Volume),
	("level history uses exact-time steps and starting level", StatHistoryTests.Levels),
	("cumulative enemy breakdowns align and sum to the total", StatHistoryTests.Breakdowns),
	("stat history rejects invalid input", StatHistoryTests.Invalid)
};
foreach (var test in tests)
{
	test.Run();
	Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} tests passed.");

static void Timeline()
{
	var boss = new GameTimelineEvent(
		Seconds: 300f,
		Icon: "star",
		Label: "Boss spawn"
	);
	Check(boss.MinutesUntilView(elapsedSeconds: 0f) == 4, "Pipeline counts minutes until entering view, not spawning.");
	Near(actual: GameTimelineEvent.ViewDurationSeconds, expected: 120f);
	Near(actual: GameTimelineEvent.PastSeconds, expected: 18f);
	Near(actual: GameTimelineEvent.FutureSeconds, expected: 102f);
	Check(boss.MinutesUntilView(elapsedSeconds: 197f) == 1, "A partial minute must round up.");
	Check(!boss.IsInView(elapsedSeconds: 197f), "Future events outside the window belong in the pipeline.");
	Check(boss.IsInView(elapsedSeconds: 198f), "Right edge must be included.");
	Check(boss.MinutesUntilView(elapsedSeconds: 198f) == 0, "In-view events must leave the pipeline.");
	Near(actual: boss.PositionPercent(elapsedSeconds: 198f), expected: 100f);
	Near(actual: boss.PositionPercent(elapsedSeconds: 300f), expected: 15f);
	Check(!boss.IsPast(elapsedSeconds: 300f), "An event at Now must remain bright.");
	Check(boss.IsPast(elapsedSeconds: 300.01f), "An event must dim immediately after passing Now.");
	Near(actual: boss.PositionPercent(elapsedSeconds: 309f), expected: 7.5f);
	Near(actual: boss.PositionPercent(elapsedSeconds: 318f), expected: 0f);
	Check(boss.IsInView(elapsedSeconds: 318f), "Left edge must be included.");
	Check(!boss.IsInView(elapsedSeconds: 318.01f), "Old events must leave the moving window.");
	Check(boss.MinutesUntilView(elapsedSeconds: 318.01f) == 0, "Past events must not return to the pipeline.");
	var lateEvent = boss with
	{
		Seconds = 86400f
	};
	Near(actual: lateEvent.PositionPercent(elapsedSeconds: 86400f), expected: 15f);
	Check(lateEvent.IsInView(elapsedSeconds: 86400f), "The timeline must not have a fixed end.");
}

static void Check(bool condition, string message)
{
	if (!condition)
		throw new InvalidOperationException(message);
}

static void Near(float actual, float expected) =>
	Check(MathF.Abs(actual - expected) < 0.0001f, $"Expected {expected}, got {actual}.");

static DamageEffectSettings Settings(int cap = 1, float duration = 10f, float dps = 5f) =>
	new(GameDamageType.Fire, 0.2f, duration, dps, cap, 0.1f, 120f);

static void Catalog()
{
	Check(ModifierCatalog.All.Select(modifier => modifier.Id).SequenceEqual(Enum.GetValues<GameModifier>()), "Missing or duplicate modifier.");
	var implementedTypes = DamageEffectCatalog.All.Select(effect => effect.Type);
	var expectedTypes = new[] { GameDamageType.Fire, GameDamageType.Electricity, GameDamageType.Explosive };
	Check(implementedTypes.SequenceEqual(expectedTypes), "Fire, electricity and explosion must be implemented.");
	var picks = new Dictionary<GameModifier, int>();
	int Stacks(GameModifier modifier) => picks.GetValueOrDefault(modifier);
	Check(ModifierCatalog.Get(GameModifier.FireRounds).IsAvailable(Stacks), "Fire unlock must be available.");
	Check(!ModifierCatalog.Get(GameModifier.FireSpread).IsAvailable(Stacks), "Spread must require fire.");
	Check(!ModifierCatalog.Get(GameModifier.FireDamage).IsAvailable(Stacks), "Damage upgrade must require fire.");
	Check(!ModifierCatalog.Get(GameModifier.FireSpreadChance).IsAvailable(Stacks), "Wildfire must require spread.");
	picks[GameModifier.FireRounds] = 1;
	Check(!ModifierCatalog.Get(GameModifier.FireRounds).IsAvailable(Stacks), "Unlock must be one-time.");
	Check(ModifierCatalog.Get(GameModifier.FireSpread).IsAvailable(Stacks), "Spread should unlock.");
	Check(!ModifierCatalog.Get(GameModifier.FireSpreadChance).IsAvailable(Stacks), "Fire alone cannot unlock Wildfire.");
	picks[GameModifier.FireSpread] = 1;
	Check(ModifierCatalog.Get(GameModifier.FireSpreadChance).IsAvailable(Stacks), "Wildfire should unlock.");
	picks[GameModifier.FireApplicationChance] = 8;
	picks[GameModifier.FireSpreadChance] = 9;
	Check(!ModifierCatalog.Get(GameModifier.FireApplicationChance).IsAvailable(Stacks), "Maxed chance must not be offered.");
	Check(!ModifierCatalog.Get(GameModifier.FireSpreadChance).IsAvailable(Stacks), "Maxed spread must not be offered.");
}

static void Offers()
{
	var random = new Random(42);
	foreach (var unlocked in new[] { false, true })
	{
		int Stacks(GameModifier modifier) => unlocked && modifier is GameModifier.FireRounds or GameModifier.FireSpread ? 1 : 0;
		for (int i = 0; i < 1000; i++)
		{
			var offer = ModifierCatalog.RollOffer(Stacks, () => (float)random.NextDouble());
			Check(offer.Count == 3 && offer.Distinct().Count() == 3, "Offers must have three distinct choices.");
			Check(offer.All(modifier => ModifierCatalog.Get(modifier).IsAvailable(Stacks)), "Offered a locked or capped upgrade.");
		}
	}
	var maxed = ModifierCatalog.All.ToDictionary(modifier => modifier.Id,
		modifier => modifier.MaxPicks == int.MaxValue ? 0 : modifier.MaxPicks);
	var options = ModifierCatalog.RollOffer(modifier => maxed[modifier], () => 1f);
	Check(options.Count == Math.Min(8, ModifierCatalog.All.Count(modifier => modifier.IsAvailable(id => maxed[id])))
		&& options.All(modifier => ModifierCatalog.Get(modifier).MaxPicks == int.MaxValue), "Repeatable fallbacks must remain.");
}

static void ExtraOptions()
{
	var random = new Random(73);
	var picks = new Dictionary<GameModifier, int>();
	int Stacks(GameModifier modifier) => picks.GetValueOrDefault(modifier);
	var extra = ModifierCatalog.Get(GameModifier.ExtraOption);
	Check(extra.MaxPicks == 5 && extra.Prerequisites.Count == 0, "More Choices must have five ranks and no prerequisite.");
	foreach (bool unlocked in new[] { false, true })
	{
		picks.Clear();
		if (unlocked)
		{
			picks[GameModifier.FireRounds] = 1;
			picks[GameModifier.FireSpread] = 1;
		}
		for (int rank = 0; rank <= 5; rank++)
		{
			picks[GameModifier.ExtraOption] = rank;
			Check(ModifierCatalog.OfferSize(Stacks) == 3 + rank, "Each rank must add exactly one slot.");
			Check(extra.IsAvailable(Stacks) == (rank < 5), "More Choices must stop appearing at rank five.");
			int eligible = ModifierCatalog.All.Count(modifier => modifier.IsAvailable(Stacks));
			for (int i = 0; i < 1000; i++)
			{
				var offer = ModifierCatalog.RollOffer(Stacks, () => (float)random.NextDouble());
				Check(offer.Count == Math.Min(3 + rank, eligible), "Wrong expanded offer size.");
				Check(offer.Distinct().Count() == offer.Count, "Expanded offers cannot duplicate cards.");
				Check(offer.All(modifier => ModifierCatalog.Get(modifier).IsAvailable(Stacks)), "Expanded offer contains an ineligible upgrade.");
			}
		}
	}
	Check(ModifierCatalog.RollOffer(Stacks, () => 0f).Count == 8, "Fully unlocked rank five must offer eight choices.");
	picks[GameModifier.ExtraOption] = 100;
	Check(ModifierCatalog.OfferSize(Stacks) == 8, "Offer size cannot exceed eight.");
	picks[GameModifier.ExtraOption] = -1;
	Check(ModifierCatalog.OfferSize(Stacks) == 3, "Offer size cannot fall below three.");
}

static void Vacuum()
{
	var vacuum = ModifierCatalog.Get(GameModifier.Vacuum);
	Check(vacuum.MaxPicks == 5 && vacuum.Prerequisites.Count == 0, "Vacuum must have five ranks and no prerequisite.");
	Check(vacuum.Name == "Vacuum" && vacuum.Description == "Common: +20% base XP collection range per pick.", "Vacuum must describe its range increase.");
	Check(vacuum.Rarity == GameRarity.Common, "Vacuum must use the common bucket.");
	Near(vacuum.Amount, 0.2f);
	for (int rank = 0; rank <= 5; rank++)
	{
		int Stacks(GameModifier modifier)
		{
			if (modifier >= GameModifier.AttackDamage)
			{
				return ModifierCatalog.Get(modifier).MaxPicks;
			}
			return modifier == GameModifier.Vacuum ? rank : 0;
		}

		float multiplier = ModifierCatalog.ExperienceCollectionRangeMultiplier(Stacks);
		Near(multiplier, 1f + rank * 0.2f);
		Near(150f * multiplier, 150f + rank * 30f);
		Check(vacuum.IsAvailable(Stacks) == (rank < 5), "Vacuum must stop appearing at rank five.");
		var offer = ModifierCatalog.RollOffer(Stacks, () => 1f);
		Check(offer.Contains(GameModifier.Vacuum) == (rank < 5), "Offers must include eligible Vacuum and exclude capped Vacuum.");
		if (rank == 5)
		{
			var rerolled = ModifierCatalog.RollOffer(
				getModifierPickCount: Stacks,
				random: () => 1f,
				previousOffer: offer
			);
			Check(!rerolled.Contains(GameModifier.Vacuum), "Rerolls must exclude capped Vacuum.");
		}
	}

	Near(ModifierCatalog.ExperienceCollectionRangeMultiplier(_ => 100), 2f);
	Near(ModifierCatalog.ExperienceCollectionRangeMultiplier(_ => -1), 1f);
	Near(ModifierCatalog.ExperienceCollectionRangeMultiplier(modifier => modifier == GameModifier.AttackDamage ? 5 : 0), 1f);
	var upgrades = GameStatCatalog.Sections.Single(section => section.Title == "Upgrades");
	var vacuumKey = GameStatKeys.ModifierPicked(GameModifier.Vacuum);
	Check(upgrades.Rows.Single(row => row.Key == vacuumKey).Label == "Vacuum", "The scorecard must include Vacuum.");
}

static void Weights()
{
	var options = new[] { 1, 3 };
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0f) == 1, "First weighted bucket.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0.2499f) == 1, "First bucket upper edge.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0.25f) == 3, "Second bucket lower edge.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 1f) == 3, "Inclusive RNG endpoint.");
	bool rejected = false;
	try
	{
		WeightedDraw.Pick(options, _ => 0f, 0f);
	}
	catch (ArgumentOutOfRangeException) { rejected = true; }
	Check(rejected, "Invalid weights must fail explicitly.");
}

static void Rerolls()
{
	int NoPicks(GameModifier modifier) => 0;
	var previous = ModifierCatalog.RollOffer(NoPicks, () => 0f);
	var rerolled = ModifierCatalog.RollOffer(NoPicks, () => 0f, previousOffer: previous);
	Check(ModifierCatalog.HasAlternativeOffer(NoPicks, previous), "A fresh player must have alternative eligible options.");
	Check(rerolled.Count == 3 && rerolled.Distinct().Count() == 3, "Rerolls must keep three distinct cards.");
	Check(rerolled.Any(modifier => !previous.Contains(modifier)), "Repeating the RNG must still change a card.");
	Check(rerolled.All(modifier => ModifierCatalog.Get(modifier).IsAvailable(NoPicks)), "Rerolls cannot bypass prerequisites.");
	var random = new Random(81);
	for (int draw = 0; draw < 1000; draw++)
	{
		var next = ModifierCatalog.RollOffer(NoPicks, () => (float)random.NextDouble(), previous);
		Check(next.Any(modifier => !previous.Contains(modifier)), "A reroll cannot merely reorder cards.");
		Check(next.Distinct().Count() == 3, "Rerolls cannot contain duplicates.");
		previous = next;
	}
	var maxed = ModifierCatalog.All.ToDictionary(modifier => modifier.Id, modifier => modifier.MaxPicks);
	maxed[GameModifier.AttackSpeed] = 0;
	maxed[GameModifier.AttackDamage] = 0;
	maxed[GameModifier.Vitality] = 0;
	int MaxedPicks(GameModifier modifier) => maxed[modifier];
	var onlyOffer = ModifierCatalog.RollOffer(MaxedPicks, () => 0f);
	Check(!ModifierCatalog.HasAlternativeOffer(MaxedPicks, onlyOffer), "With only three eligible cards, reroll must be unavailable.");
}

static void RerollStat()
{
	var section = GameStatCatalog.Sections.Single(section => section.Title == "Upgrades");
	var row = section.Rows.Single(row => row.Key == GameStatKeys.TimesRerolled);
	Check(row.Label == "Times rerolled", "The reroll count needs a readable scorecard label.");
	Check(GameStatCatalog.Sections.SelectMany(section => section.Rows).Count(row => row.Key == GameStatKeys.TimesRerolled) == 1,
		"The scorecard must not duplicate the reroll stat.");
	Check(section.Rows.Count == ModifierCatalog.All.Count + 1, "All existing modifier rows must remain.");
}

static void ExpandedRerolls()
{
	var picks = new Dictionary<GameModifier, int>
	{
		[GameModifier.FireRounds] = 1
	};
	int Stacks(GameModifier modifier)
	{
		return picks.GetValueOrDefault(modifier);
	}

	for (int rank = 1; rank < 5; rank++)
	{
		picks[GameModifier.ExtraOption] = rank;
		var previous = ModifierCatalog.RollOffer(Stacks, () => 0f);
		Check(ModifierCatalog.HasAlternativeOffer(Stacks, previous), "Unlocked expanded offers must have alternatives.");
		for (int draw = 0; draw < 100; draw++)
		{
			var next = ModifierCatalog.RollOffer(
				getModifierPickCount: Stacks,
				random: () => 0f,
				previousOffer: previous
			);
			Check(next.Count == 3 + rank, "Reroll lost bonus choices.");
			Check(next.Distinct().Count() == next.Count, "Expanded reroll duplicated a card.");
			Check(next.Any(modifier => !previous.Contains(modifier)), "Expanded reroll must change the set.");
			Check(next.All(modifier => ModifierCatalog.Get(modifier).IsAvailable(Stacks)), "Expanded reroll bypassed eligibility.");
			previous = next;
		}
	}

	picks[GameModifier.ExtraOption] = 5;
	picks[GameModifier.FireSpread] = 1;
	var fullyUnlocked = ModifierCatalog.RollOffer(Stacks, () => 0f);
	Check(fullyUnlocked.Count == 8, "Fully unlocked rank five must show eight choices.");
	Check(ModifierCatalog.HasAlternativeOffer(Stacks, fullyUnlocked), "Vacuum must add an eligible reroll alternative.");
	picks[GameModifier.Vacuum] = 5;
	foreach (var modifier in ModifierCatalog.All.Where(modifier => modifier.EffectType == GameDamageType.Electricity))
	{
		picks[modifier.Id] = modifier.MaxPicks;
	}
	foreach (var modifier in ModifierCatalog.All.Where(modifier => modifier.Id >= GameModifier.AttackDamage))
	{
		picks[modifier.Id] = modifier.MaxPicks;
	}

	fullyUnlocked = ModifierCatalog.RollOffer(Stacks, () => 0f);
	Check(!ModifierCatalog.HasAlternativeOffer(Stacks, fullyUnlocked), "All seven eligible cards leave no reroll alternative.");

	picks.Clear();
	foreach (var modifier in ModifierCatalog.All.Where(modifier => modifier.Id >= GameModifier.AttackDamage))
	{
		picks[modifier.Id] = modifier.MaxPicks;
	}
	picks[GameModifier.ExtraOption] = 5;
	var allEligible = ModifierCatalog.RollOffer(Stacks, () => 0f);
	Check(allEligible.Count == 5, "Locked effects must leave five eligible choices at rank five.");
	Check(!ModifierCatalog.HasAlternativeOffer(Stacks, allEligible), "Reroll must be disabled when every eligible choice is shown.");
	var noAlternative = ModifierCatalog.RollOffer(
		getModifierPickCount: Stacks,
		random: () => 1f,
		previousOffer: allEligible
	);
	Check(noAlternative.Count == 5 && noAlternative.All(allEligible.Contains), "A full-pool offer cannot invent an alternative.");
}

static void FireRolls()
{
	Check(DamageEffectCatalog.Roll(_ => 0, () => throw new Exception("Locked effects must not draw.")) == null, "Locked fire rolled.");
	var picks = new Dictionary<GameModifier, int> { [GameModifier.FireRounds] = 1 };
	int Stacks(GameModifier modifier) => picks.GetValueOrDefault(modifier);
	DamageEffectSettings Roll(float chance)
	{
		var rolls = new Queue<float>(new[] { 0f, chance });
		return DamageEffectCatalog.Roll(Stacks, () => rolls.Dequeue());
	}
	var fire = Roll(0.1999f) ?? throw new Exception("Below 20% must apply.");
	Check(Roll(0.2f) == null && Roll(0.999f) == null, "Failed proc must stay physical.");
	Near(fire.Duration, 5f);
	Near(fire.DamagePerSecond, 5f);
	Near(fire.ApplicationChance, 0.2f);
	Near(fire.SpreadChance, 0f);
	Check(fire.MaxStacks == 1, "Default cap.");
	picks[GameModifier.FireSpread] = 1;
	picks[GameModifier.FireApplicationChance] = 8;
	picks[GameModifier.FireSpreadChance] = 9;
	picks[GameModifier.FireDuration] = 2;
	picks[GameModifier.FireDamage] = 2;
	picks[GameModifier.FireStacks] = 2;
	var upgraded = DamageEffectCatalog.All[0].Resolve(Stacks);
	Near(upgraded.ApplicationChance, 1f);
	Near(upgraded.SpreadChance, 1f);
	Near(upgraded.Duration, 9f);
	Near(upgraded.DamagePerSecond, 15f);
	Check(upgraded.MaxStacks == 3, "Stack upgrade must raise cap.");
	Near(fire.Duration, 5f);
	Near(fire.SpreadChance, 0f);
	Check(Roll(0.999f) != null, "100% must proc.");
	Check(Roll(1f) != null, "100% must proc even at an inclusive RNG endpoint.");
}

static void Expiries()
{
	var effects = new TimedDamageEffects<string>();
	var totals = new Dictionary<string, float>();
	void Damage(TimedDamageStack<string> stack, float amount) =>
		totals[stack.Source] = totals.GetValueOrDefault(stack.Source) + amount;
	effects.Apply(Settings(2), "first", 0f);
	effects.Advance(5f, Damage);
	effects.Apply(Settings(2), "second", 5f);
	Check(effects.Count(GameDamageType.Fire, 5f) == 2, "Two applications should coexist.");
	effects.Advance(10f, Damage);
	Check(effects.Count(GameDamageType.Fire, 10f) == 1, "First must expire without refreshing second.");
	Near(totals["first"], 50f);
	Near(totals["second"], 25f);
	effects.Advance(20f, Damage);
	Near(totals["second"], 50f);
	Check(effects.Stacks.Count == 0, "No damage beyond expiry.");
	var fractional = new TimedDamageEffects<string>();
	float amount = 0f;
	fractional.Apply(Settings(duration: 0.25f), "source", 0f);
	fractional.Advance(1f, (_, value) => amount += value);
	Near(amount, 1.25f);
}

static void Replacement()
{
	var effects = new TimedDamageEffects<string>();
	effects.Apply(Settings(2), "oldest", 0f);
	effects.Advance(2f, (_, _) => { });
	effects.Apply(Settings(2), "middle", 2f);
	effects.Advance(4f, (_, _) => { });
	effects.Apply(Settings(2), "newest", 4f);
	Check(effects.Stacks.Select(effectStack => effectStack.Source).SequenceEqual(new[] { "middle", "newest" }), "Replace only oldest.");
	Near(effects.Stacks[0].ExpiresAt, 12f);
	Near(effects.Stacks[1].ExpiresAt, 14f);
	var single = new TimedDamageEffects<string>();
	single.Apply(Settings(), "old", 0f);
	single.Advance(5f, (_, _) => { });
	single.Apply(Settings(), "new", 5f);
	Check(single.Stacks.Count == 1 && single.Stacks[0].Source == "new", "Renew default one-stack fire.");
	Near(single.Stacks[0].ExpiresAt, 15f);
}

static void TickCadence()
{
	var effects = new TimedDamageEffects<string>();
	float damage = 0f;
	int ticks = 0;
	void Damage(TimedDamageStack<string> stack, float amount)
	{
		damage += amount;
		ticks++;
	}
	effects.Apply(Settings(duration: 2.5f), "source", 0f);
	effects.Advance(0.5f, Damage);
	Check(ticks == 0, "Fire must not cause a hit reaction every frame.");
	effects.Advance(1f, Damage);
	Check(ticks == 1, "First tick at one second.");
	effects.Advance(1.5f, Damage);
	Check(ticks == 1, "Keep the remaining fractional time.");
	effects.Advance(2f, Damage);
	Check(ticks == 2, "Second tick at two seconds.");
	effects.Advance(3f, Damage);
	Check(ticks == 3, "Expiry pays the final half second.");
	Near(damage, 12.5f);
	effects.Apply(Settings(), "old", 4f);
	effects.Advance(4.5f, Damage, flush: true);
	effects.Apply(Settings(), "new", 4.5f);
	Near(damage, 15f);
	Check(effects.Stacks.Single().Source == "new", "Renewal must preserve damage earned by the old application.");
}

static void Spread()
{
	var target = new TimedDamageEffects<string>();
	Check(target.CanSpreadTo(Settings(), 0f), "Unburning enemy can be infected.");
	target.Apply(Settings(2), "source", 0f);
	Check(!target.CanSpreadTo(Settings(), 1f), "One-stack cap cannot refresh via spread.");
	Check(target.CanSpreadTo(Settings(2), 1f), "Burning enemy below cap can stack via spread.");
	target.Apply(Settings(2), "source", 1f);
	Check(!target.CanSpreadTo(Settings(2), 2f), "Spread cannot replace a full stack.");
	Check(target.CanSpreadTo(Settings(2), 10f), "Expired stacks no longer block spread.");
}

static void Clear()
{
	var effects = new TimedDamageEffects<string>();
	float damage = 0f;
	effects.Apply(Settings(), "source", 0f);
	effects.Advance(1f, (_, amount) => damage += amount);
	effects.Advance(1f, (_, amount) => damage += amount);
	effects.Advance(0.5f, (_, amount) => damage += amount);
	Near(damage, 5f);
	effects.Clear();
	effects.Advance(20f, (_, amount) => damage += amount);
	Near(damage, 5f);
	Check(effects.Stacks.Count == 0, "Clearing must remove all state.");
	var dying = new TimedDamageEffects<string>();
	dying.Apply(Settings(2), "first", 0f);
	dying.Apply(Settings(2), "second", 0f);
	dying.Advance(1f, (_, _) => dying.Clear());
	Check(dying.Stacks.Count == 0, "Death during damage callback must not invalidate enumeration.");
}

static void Invalid()
{
	int failures = 0;
	foreach (var create in new Func<DamageEffectSettings>[]
	{
		() => Settings(duration: float.NaN),
		() => Settings(dps: float.PositiveInfinity),
		() => Settings(cap: 0),
		() => new(GameDamageType.Fire, 2f, 5f, 5f, 1, 0f, 120f),
		() => new(GameDamageType.Fire, 0.2f, 5f, 5f, 1, -0.1f, 120f)
	})
	{
		try
		{
			create();
		}
		catch (ArgumentOutOfRangeException) { failures++; }
	}
	Check(failures == 5, "Invalid settings must fail explicitly.");
}
