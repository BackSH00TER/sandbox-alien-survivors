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
		Check(!baseline.Splash && baseline.AdditionalTargets == 0 && baseline.SlowDuration == 0f && rolls == 0,
			"Unmodified bullets must not proc or consume extra random rolls.");
		picks[GameModifier.SplashChance] = 1;
		picks[GameModifier.PierceChance] = 1;
		picks[GameModifier.SlowingRounds] = 1;
		var first = GameShotModifiers.CaptureBullet(Stacks, Random);
		Check(first.Splash && first.AdditionalTargets == 1 && rolls == 2, "Splash and pierce each roll once independently.");
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
