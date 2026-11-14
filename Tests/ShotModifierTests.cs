using System;
using System.Collections.Generic;
using System.Linq;

internal static class ShotModifierTests
{
	internal static void Progression()
	{
		var picks = new Dictionary<GameModifier, int>();
		int Stacks(GameModifier id)
		{
			return picks.GetValueOrDefault(id);
		}
		foreach (var definition in ModifierCatalog.All.Where(definition => definition.Id >= GameModifier.AttackDamage))
		{
			Check(definition.Rarity == GameRarity.Common, "Every new modifier must be Common.");
			Check(definition.IsAvailable(Stacks) == (definition.Prerequisites.Count == 0), "Prerequisites must gate offers.");
		}
		foreach (var definition in ModifierCatalog.All.Where(definition => definition.Id >= GameModifier.AttackDamage))
		{
			picks[definition.Id] = 1;
		}
		Near(ModifierCatalog.Bonus(GameModifier.AttackDamage, Stacks), 0.2f);
		Near(ModifierCatalog.Bonus(GameModifier.MoveSpeed, Stacks), 0.1f);
		Near(ModifierCatalog.Bonus(GameModifier.HealthPickupBonus, Stacks), 0.25f);
		Near(ModifierCatalog.Bonus(GameModifier.ArmorPickupBonus, Stacks), 0.25f);
		Near(ModifierCatalog.Bonus(GameModifier.HealthRegeneration, Stacks), 1f);
		Check(ModifierCatalog.Get(GameModifier.SplashDamage).IsAvailable(Stacks), "Splash chance must unlock splash damage.");
		Check(ModifierCatalog.Get(GameModifier.PierceTargets).IsAvailable(Stacks), "Pierce chance must unlock target count.");

		foreach (var id in new[] { GameModifier.SplashChance, GameModifier.PierceChance, GameModifier.EmpoweredShots })
		{
			var definition = ModifierCatalog.Get(id);
			Check(definition.MaxPicks == 10, "Chance and power interval upgrades must cap at ten picks.");
			picks[id] = 10;
			Check(!definition.IsAvailable(Stacks), "Capped picks must leave the offer pool.");
		}
		Check(!ModifierCatalog.Get(GameModifier.SlowingRounds).IsAvailable(Stacks), "Slow is a one-time unlock.");
		picks[GameModifier.HealthRegeneration] = 5;
		Near(ModifierCatalog.Bonus(GameModifier.HealthRegeneration, Stacks), 5f);
	}

	internal static void Bullets()
	{
		var picks = new Dictionary<GameModifier, int>();
		int Stacks(GameModifier id)
		{
			return picks.GetValueOrDefault(id);
		}
		int rolls = 0;
		float Random()
		{
			rolls++;
			return 0f;
		}
		var baseline = GameShotModifiers.CaptureBullet(Stacks, Random);
		Check(!baseline.Splash && baseline.AdditionalTargets == 0 && baseline.SlowDuration == 0f && rolls == 1,
			"Baseline bullets must roll only their innate critical chance.");
		picks[GameModifier.SplashChance] = 1;
		picks[GameModifier.PierceChance] = 1;
		picks[GameModifier.SlowingRounds] = 1;
		var first = GameShotModifiers.CaptureBullet(Stacks, Random);
		Check(first.Splash && first.AdditionalTargets == 1 && rolls == 4, "Splash, pierce and critical chance each roll once independently.");
		Near(first.SplashDamageMultiplier, 0.5f);
		Near(first.SlowDuration, 2f);
		Near(GameShotModifiers.SlowMultiplier, 0.5f);
		Near(GameShotModifiers.SplashRadius, 60f);

		picks[GameModifier.SplashDamage] = 2;
		picks[GameModifier.PierceTargets] = 3;
		var upgraded = GameShotModifiers.CaptureBullet(Stacks, Random);
		Check(upgraded.AdditionalTargets == 4, "Three target upgrades must allow four additional enemies.");
		Near(upgraded.SplashDamageMultiplier, 1f);
		Check(first.AdditionalTargets == 1 && first.SplashDamageMultiplier == 0.5f, "In-flight settings must remain unchanged.");
		var failures = GameShotModifiers.CaptureBullet(Stacks, () => 0.1f);
		Check(!failures.Splash && failures.AdditionalTargets == 0, "The exact chance boundary must fail without retrying.");

		int index = 0;
		var mixed = GameShotModifiers.CaptureBullet(Stacks, () => index++ == 0 ? 0f : 1f);
		Check(mixed.Splash && mixed.AdditionalTargets == 0, "Splash success must not guarantee piercing.");
		picks[GameModifier.SplashChance] = 10;
		picks[GameModifier.PierceChance] = 10;
		var guaranteed = GameShotModifiers.CaptureBullet(Stacks, () => 1f);
		Check(guaranteed.Splash && guaranteed.AdditionalTargets == 4, "100% chances include endpoint one.");
	}

	internal static void Volleys()
	{
		int rank = 0;
		int rolls = 0;
		int Stacks(GameModifier id)
		{
			return id == GameModifier.ExtraBullets ? rank : 0;
		}
		float Random()
		{
			rolls++;
			return 0f;
		}
		Check(GameShotModifiers.ExtraBulletCount(Stacks, Random) == 0 && rolls == 0, "Baseline trigger pulls must not roll.");
		for (rank = 1; rank <= 12; rank++)
		{
			int before = rolls;
			Check(GameShotModifiers.ExtraBulletCount(Stacks, Random) == rank, "Each rank adds a bonus bullet.");
			Check(rolls == before + 1, "A volley rolls once, not once per bonus bullet.");
			Near(GameShotModifiers.Chance(GameModifier.ExtraBullets, Stacks), MathF.Min(1f, rank * 0.1f));
		}
		rank = 1;
		Check(GameShotModifiers.ExtraBulletCount(Stacks, () => 0.1f) == 0, "Failed volleys must not add bullets.");
		rank = 12;
		Check(GameShotModifiers.ExtraBulletCount(Stacks, () => 1f) == 12, "Bullet counts continue scaling after chance reaches 100%.");
	}

	internal static void PowerShots()
	{
		int rank = 0;
		int counter = 0;
		int Stacks(GameModifier id)
		{
			if (id == GameModifier.EmpoweredShots)
			{
				return rank;
			}
			return id == GameModifier.AttackDamage ? 2 : 0;
		}
		float Damage()
		{
			return GameShotModifiers.DamageMultiplier(
				stacks: Stacks,
				shotsSincePowerShot: ref counter
			);
		}
		Near(Damage(), 1.4f);
		Check(counter == 0, "Shots before acquiring Power Shot must not count.");
		for (rank = 1; rank <= 10; rank++)
		{
			counter = 0;
			int interval = 11 - rank;
			for (int cycle = 0; cycle < 3; cycle++)
			{
				for (int shot = 1; shot < interval; shot++)
				{
					Near(Damage(), 1.4f);
				}
				Near(Damage(), 2.8f);
				Check(counter == 0, "Every eligible trigger pull must guarantee double damage and reset the counter.");
			}
		}
		rank = 1;
		counter = 8;
		rank = 3;
		Near(Damage(), 2.8f);
		Check(counter == 0, "Reducing the interval must honor existing progress on the next shot.");
	}

	internal static void Invalid()
	{
		foreach (var chance in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => GameShotModifiers.Roll(chance, () => 0f));
		}
		foreach (var roll in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => GameShotModifiers.Roll(0.5f, () => roll));
		}
	}

	internal static void CriticalHits()
	{
		var picks = new Dictionary<GameModifier, int>();
		int Stacks(GameModifier id)
		{
			return picks.GetValueOrDefault(id);
		}
		void CheckStats(float expectedChance, float expectedDamage)
		{
			var chance = GameShotModifiers.CritChance(Stacks);
			var damage = GameShotModifiers.CritDamage(Stacks);
			Near(actual: chance, expected: expectedChance);
			Near(actual: damage, expected: expectedDamage);
		}
		CheckStats(expectedChance: 0.05f, expectedDamage: 2f);
		var chanceUpgrade = ModifierCatalog.Get(GameModifier.CritChance);
		var damageUpgrade = ModifierCatalog.Get(GameModifier.CritDamage);
		Check(chanceUpgrade.IsAvailable(Stacks) && damageUpgrade.IsAvailable(Stacks),
			"Both critical upgrades must be available without prerequisites.");
		for (int rank = 1; rank <= 5; rank++)
		{
			picks[GameModifier.CritChance] = rank;
			CheckStats(expectedChance: 0.05f + rank * 0.1f, expectedDamage: 2f);
		}
		Check(!chanceUpgrade.IsAvailable(Stacks), "Five critical-chance picks must leave the offer pool.");
		picks[GameModifier.CritChance] = 100;
		CheckStats(expectedChance: 0.55f, expectedDamage: 2f);
		picks[GameModifier.CritChance] = -1;
		CheckStats(expectedChance: 0.05f, expectedDamage: 2f);
		foreach (int rank in new[] { 0, 5 })
		{
			picks[GameModifier.CritChance] = rank;
			int criticalCount = 0;
			for (int sample = 0; sample < 100; sample++)
			{
				var roll = (sample + 0.5f) / 100f;
				var bullet = GameShotModifiers.CaptureBullet(stacks: Stacks, random: () => roll);
				if (bullet.CriticalDamageMultiplier > 1f)
				{
					criticalCount++;
				}
			}
			Check(criticalCount == 5 + rank * 10, "Critical chance must produce exactly 5% or 55% on evenly spaced rolls.");
		}
		picks.Clear();

		picks[GameModifier.SplashChance] = 1;
		picks[GameModifier.PierceChance] = 1;
		for (int successfulRoll = 0; successfulRoll < 3; successfulRoll++)
		{
			var randomValues = new[] { 1f, 1f, 1f };
			randomValues[successfulRoll] = 0f;
			int rollIndex = 0;
			var bullet = GameShotModifiers.CaptureBullet(
				stacks: Stacks,
				random: () => randomValues[rollIndex++]
			);
			Check(rollIndex == 3, "Splash, piercing and critical chance must each draw a separate random value.");
			Check(bullet.Splash == (successfulRoll == 0), "Only the splash roll may determine splash success.");
			Check(bullet.AdditionalTargets == (successfulRoll == 1 ? 1 : 0), "Only the piercing roll may determine piercing success.");
			Near(actual: bullet.CriticalDamageMultiplier, expected: successfulRoll == 2 ? 2f : 1f);
		}
		picks.Clear();

		int rolls = 0;
		float Random()
		{
			rolls++;
			return 0.049f;
		}
		var critical = GameShotModifiers.CaptureBullet(stacks: Stacks, random: Random);
		Near(critical.CriticalDamageMultiplier, 2f);
		Check(rolls == 1, "Each baseline bullet must consume exactly one critical roll.");
		var boundary = GameShotModifiers.CaptureBullet(stacks: Stacks, random: () => 0.05f);
		Near(boundary.CriticalDamageMultiplier, 1f);
		var failed = GameShotModifiers.CaptureBullet(stacks: Stacks, random: () => 1f);
		Near(failed.CriticalDamageMultiplier, 1f);

		picks[GameModifier.CritDamage] = 1;
		CheckStats(expectedChance: 0.05f, expectedDamage: 2.5f);
		picks[GameModifier.CritDamage] = 2;
		CheckStats(expectedChance: 0.05f, expectedDamage: 3f);
		picks[GameModifier.CritChance] = 1;
		CheckStats(expectedChance: 0.15f, expectedDamage: 3f);
		var upgraded = GameShotModifiers.CaptureBullet(stacks: Stacks, random: () => 0.1f);
		Near(upgraded.CriticalDamageMultiplier, 3f);
		Near(critical.CriticalDamageMultiplier, 2f);
		var enemyBullet = new GameBulletModifiers();
		Near(enemyBullet.CriticalDamageMultiplier, 1f);

		picks[GameModifier.AttackDamage] = 2;
		picks[GameModifier.EmpoweredShots] = 10;
		picks[GameModifier.SplashChance] = 10;
		picks[GameModifier.PierceChance] = 10;
		int counter = 0;
		var weaponMultiplier = GameShotModifiers.DamageMultiplier(
			stacks: Stacks,
			shotsSincePowerShot: ref counter
		);
		var combined = GameShotModifiers.CaptureBullet(stacks: Stacks, random: () => 0f);
		var bulletDamage = 10f * weaponMultiplier * combined.CriticalDamageMultiplier;
		Near(bulletDamage, 84f);
		Near(bulletDamage * combined.SplashDamageMultiplier, 42f);
		Check(combined.AdditionalTargets == 1, "Critical damage must preserve piercing targets.");
	}

	internal static void CriticalEffects()
	{
		int Stacks(GameModifier id)
		{
			return id switch
			{
				GameModifier.FireRounds or GameModifier.FireSpread or GameModifier.ShockRounds => 1,
				GameModifier.ShockChainChance => 8,
				_ => 0
			};
		}
		var fire = DamageEffectCatalog.Get(GameDamageType.Fire).Resolve(Stacks);
		var criticalFire = fire.WithDamageMultiplier(multiplier: 2.5f);
		Near(fire.DamagePerSecond, 5f);
		Near(criticalFire.DamagePerSecond, 12.5f);
		Near(criticalFire.Duration, fire.Duration);
		Near(criticalFire.ApplicationChance, fire.ApplicationChance);
		Near(criticalFire.SpreadChance, fire.SpreadChance);
		Near(criticalFire.SpreadRadius, fire.SpreadRadius);
		Check(criticalFire.MaxStacks == fire.MaxStacks, "Critical fire must preserve stack limits.");
		var normalFire = fire.WithDamageMultiplier(multiplier: 1f);
		Check(ReferenceEquals(fire, normalFire), "Noncritical effects must remain unchanged.");
		var effects = new TimedDamageEffects<string>();
		effects.Apply(settings: criticalFire, source: "shooter", now: 0f);
		float total = 0f;
		effects.Advance(now: 1f, dealDamage: (stack, damage) => total += damage);
		Near(total, 12.5f);
		effects.Advance(now: 5f, dealDamage: (stack, damage) => total += damage);
		Near(total, 62.5f);
		var spread = new TimedDamageEffects<string>();
		spread.Apply(settings: criticalFire, source: "shooter", now: 5f);
		float spreadDamage = 0f;
		spread.Advance(now: 6f, dealDamage: (stack, damage) => spreadDamage += damage);
		Near(spreadDamage, 12.5f);

		var shock = DamageEffectCatalog.Get(GameDamageType.Electricity).Resolve(Stacks);
		var criticalShock = shock.WithDamageMultiplier(multiplier: 2.5f);
		Near(shock.InstantDamage, 10f);
		Near(criticalShock.InstantDamage, 25f);
		Near(criticalShock.ChainChance, shock.ChainChance);
		Near(criticalShock.ChainRadius, shock.ChainRadius);
		Near(criticalShock.ChainDamageFalloff, shock.ChainDamageFalloff);
		Near(criticalShock.MinimumChainDamageMultiplier, shock.MinimumChainDamageMultiplier);
		Check(criticalShock.MaxChains == shock.MaxChains, "Critical shock must preserve chain limits.");
		var hits = new List<float>();
		DamageEffectChain.Apply(
			initialTarget: "first",
			settings: criticalShock,
			canHit: _ => true,
			findNext: (previous, visited) => "second",
			hit: (target, damage) => hits.Add(damage),
			random: () => throw new InvalidOperationException("Guaranteed chains must not reroll critical chance.")
		);
		Check(hits.Count == 2, "Critical shock must damage the initial target and one chain target.");
		Near(hits[0], 25f);
		Near(hits[1], 22.5f);
		foreach (var multiplier in new[] { 0f, -1f, float.NaN, float.PositiveInfinity, float.MaxValue })
		{
			Reject(() => fire.WithDamageMultiplier(multiplier));
			Reject(() => shock.WithDamageMultiplier(multiplier));
		}
	}

	private static void Reject(Action action)
	{
		try
		{
			action();
		}
		catch (ArgumentOutOfRangeException)
		{
			return;
		}
		throw new InvalidOperationException("Invalid shot input must throw.");
	}

	private static void Near(float actual, float expected)
	{
		var difference = MathF.Abs(actual - expected);
		Check(difference < 0.0001f, $"Expected {expected}, got {actual}.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}
}
