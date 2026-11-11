using System;

public sealed class GameBulletModifiers
{
	public bool Splash { get; init; }
	public float SplashDamageMultiplier { get; init; }
	public int AdditionalTargets { get; init; }
	public float SlowDuration { get; init; }
}

/// <summary>Host-side shot rules shared by gameplay and deterministic tests.</summary>
public static class GameShotModifiers
{
	public const float SplashRadius = 60f;
	public const float SlowMultiplier = 0.5f;

	public static bool Roll(float chance, Func<float> random)
	{
		if (!float.IsFinite(chance) || chance < 0f || chance > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(chance));
		}
		if (chance == 0f)
		{
			return false;
		}

		ArgumentNullException.ThrowIfNull(random);
		var roll = random();
		if (!float.IsFinite(roll) || roll < 0f || roll > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(random), "Shot rolls must be between zero and one.");
		}

		return chance == 1f || roll < chance;
	}

	public static float Chance(GameModifier modifier, Func<GameModifier, int> stacks)
	{
		var bonus = ModifierCatalog.Bonus(modifier, stacks);
		return MathF.Min(1f, bonus);
	}

	public static int ExtraBulletCount(Func<GameModifier, int> stacks, Func<float> random)
	{
		var chance = Chance(GameModifier.ExtraBullets, stacks);
		return Roll(chance, random) ? stacks(GameModifier.ExtraBullets) : 0;
	}

	public static GameBulletModifiers CaptureBullet(Func<GameModifier, int> stacks, Func<float> random)
	{
		var splashChance = Chance(GameModifier.SplashChance, stacks);
		var splash = Roll(splashChance, random);
		var splashBonus = ModifierCatalog.Bonus(GameModifier.SplashDamage, stacks);
		var pierceChance = Chance(GameModifier.PierceChance, stacks);
		var piercing = Roll(pierceChance, random);
		var extraTargets = ModifierCatalog.Bonus(GameModifier.PierceTargets, stacks);
		var slowDuration = ModifierCatalog.Bonus(GameModifier.SlowingRounds, stacks);
		return new GameBulletModifiers
		{
			Splash = splash,
			SplashDamageMultiplier = 0.5f + splashBonus,
			AdditionalTargets = piercing ? 1 + (int)extraTargets : 0,
			SlowDuration = slowDuration
		};
	}

	public static float DamageMultiplier(
		Func<GameModifier, int> stacks,
		ref int shotsSincePowerShot
	)
	{
		var damageBonus = ModifierCatalog.Bonus(GameModifier.AttackDamage, stacks);
		var multiplier = 1f + damageBonus;
		var ranks = (int)ModifierCatalog.Bonus(GameModifier.EmpoweredShots, stacks);
		if (ranks == 0)
		{
			shotsSincePowerShot = 0;
			return multiplier;
		}

		shotsSincePowerShot++;
		if (shotsSincePowerShot >= 11 - ranks)
		{
			shotsSincePowerShot = 0;
			multiplier *= 2f;
		}

		return multiplier;
	}
}
