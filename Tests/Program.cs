using System;
using System.Collections.Generic;
using System.Linq;

var tests = new (string Name, Action Run)[]
{
	("catalog coverage and prerequisites", Catalog),
	("offers are eligible and distinct", Offers),
	("weighted draw boundaries", Weights),
	("fire chance, defaults and upgrade caps", FireRolls),
	("independent expiries and exact stacked damage", Expiries),
	("whole-second ticks and partial renewal damage", TickCadence),
	("oldest replacement and attacker credit", Replacement),
	("spread accepts burning targets only below cap", Spread),
	("clearing and advancing cannot repeat damage", Clear),
	("invalid settings are rejected", Invalid)
};
foreach (var test in tests)
{
	test.Run();
	Console.WriteLine($"PASS {test.Name}");
}
Console.WriteLine($"{tests.Length} tests passed.");

static void Check(bool condition, string message)
{
	if (!condition)
		throw new InvalidOperationException(message);
}

static void Near(float actual, float expected) =>
	Check(MathF.Abs(actual - expected) < 0.0001f, $"Expected {expected}, got {actual}.");

static DamageEffectSettings Settings(int cap = 1, float duration = 10f, float dps = 5f) =>
	new(DamageType.Fire, 0.2f, duration, dps, cap, 0.1f, 120f);

static void Catalog()
{
	Check(ModifierCatalog.All.Select(modifier => modifier.Id).SequenceEqual(Enum.GetValues<Modifier>()), "Missing or duplicate modifier.");
	Check(DamageEffectCatalog.All.Count == 1 && DamageEffectCatalog.All[0].Type == DamageType.Fire, "Only fire is implemented.");
	var picks = new Dictionary<Modifier, int>();
	int Stacks(Modifier modifier) => picks.GetValueOrDefault(modifier);
	Check(ModifierCatalog.Get(Modifier.FireRounds).IsAvailable(Stacks), "Fire unlock must be available.");
	Check(!ModifierCatalog.Get(Modifier.FireSpread).IsAvailable(Stacks), "Spread must require fire.");
	Check(!ModifierCatalog.Get(Modifier.FireDamage).IsAvailable(Stacks), "Damage upgrade must require fire.");
	Check(!ModifierCatalog.Get(Modifier.FireSpreadChance).IsAvailable(Stacks), "Wildfire must require spread.");
	picks[Modifier.FireRounds] = 1;
	Check(!ModifierCatalog.Get(Modifier.FireRounds).IsAvailable(Stacks), "Unlock must be one-time.");
	Check(ModifierCatalog.Get(Modifier.FireSpread).IsAvailable(Stacks), "Spread should unlock.");
	Check(!ModifierCatalog.Get(Modifier.FireSpreadChance).IsAvailable(Stacks), "Fire alone cannot unlock Wildfire.");
	picks[Modifier.FireSpread] = 1;
	Check(ModifierCatalog.Get(Modifier.FireSpreadChance).IsAvailable(Stacks), "Wildfire should unlock.");
	picks[Modifier.FireApplicationChance] = 8;
	picks[Modifier.FireSpreadChance] = 9;
	Check(!ModifierCatalog.Get(Modifier.FireApplicationChance).IsAvailable(Stacks), "Maxed chance must not be offered.");
	Check(!ModifierCatalog.Get(Modifier.FireSpreadChance).IsAvailable(Stacks), "Maxed spread must not be offered.");
}

static void Offers()
{
	var random = new Random(42);
	foreach (var unlocked in new[] { false, true })
	{
		int Stacks(Modifier modifier) => unlocked && modifier is Modifier.FireRounds or Modifier.FireSpread ? 1 : 0;
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
	Check(options.Count == 3 && options.All(modifier => ModifierCatalog.Get(modifier).MaxPicks == int.MaxValue), "Repeatable fallbacks must remain.");
}

static void Weights()
{
	var options = new[] { 1, 3 };
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0f) == 1, "First weighted bucket.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0.2499f) == 1, "First bucket upper edge.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 0.25f) == 3, "Second bucket lower edge.");
	Check(WeightedDraw.Pick(options, optionWeight => optionWeight, 1f) == 3, "Inclusive RNG endpoint.");
	bool rejected = false;
	try { WeightedDraw.Pick(options, _ => 0f, 0f); }
	catch (ArgumentOutOfRangeException) { rejected = true; }
	Check(rejected, "Invalid weights must fail explicitly.");
}

static void FireRolls()
{
	Check(DamageEffectCatalog.Roll(_ => 0, () => throw new Exception("Locked effects must not draw.")) == null, "Locked fire rolled.");
	var picks = new Dictionary<Modifier, int> { [Modifier.FireRounds] = 1 };
	int Stacks(Modifier modifier) => picks.GetValueOrDefault(modifier);
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
	picks[Modifier.FireSpread] = 1;
	picks[Modifier.FireApplicationChance] = 8;
	picks[Modifier.FireSpreadChance] = 9;
	picks[Modifier.FireDuration] = 2;
	picks[Modifier.FireDamage] = 2;
	picks[Modifier.FireStacks] = 2;
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
	Check(effects.Count(DamageType.Fire, 5f) == 2, "Two applications should coexist.");
	effects.Advance(10f, Damage);
	Check(effects.Count(DamageType.Fire, 10f) == 1, "First must expire without refreshing second.");
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
	void Damage(TimedDamageStack<string> stack, float amount) { damage += amount; ticks++; }
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
		() => new(DamageType.Fire, 2f, 5f, 5f, 1, 0f, 120f),
		() => new(DamageType.Fire, 0.2f, 5f, 5f, 1, -0.1f, 120f)
	})
	{
		try { create(); }
		catch (ArgumentOutOfRangeException) { failures++; }
	}
	Check(failures == 5, "Invalid settings must fail explicitly.");
}
