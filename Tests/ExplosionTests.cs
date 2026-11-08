using System;
using System.Linq;

internal static class ExplosionTests
{
	internal static void Catalog()
	{
		var definition = DamageEffectCatalog.Get(GameDamageType.Explosive);
		var settings = definition.Resolve(_ => 0);
		Check(!definition.Unlock.HasValue, "Enemy explosions must not have a player unlock.");
		Check(settings.IsExplosion && !settings.IsInstant && !settings.IsTimed,
			"An explosion must be distinct from an instant chain or timed stack.");
		Check(settings.Duration == 3f && settings.BlastRadius == 100f && settings.InstantDamage == 40f,
			"Explosion defaults must preserve the enemy's fuse, radius and blast damage.");
		Check(settings.ApplicationChance == 1f && settings.DamagePerSecond == 0f && settings.MaxStacks == 1,
			"Death explosions are guaranteed single blasts, not damage-over-time.");
		Check(settings.SpreadChance == 0f && settings.SpreadRadius == 0f
			&& settings.ChainChance == 0f && settings.MaxChains == 0 && settings.ChainRadius == 0f,
			"Explosions must not inherit spread or chaining.");
		Check(!ModifierCatalog.All.Any(modifier => modifier.EffectType == GameDamageType.Explosive),
			"The enemy-only refactor must not introduce explosion upgrades.");

		var physical = DamageEffectCatalog.Roll(
			getModifierPickCount: _ => 0,
			random: () => throw new InvalidOperationException("No unlocks must consume no random rolls.")
		);
		Check(physical == null, "Enemy explosion availability must not unlock player effects.");
		foreach (var draw in new[] { 0f, 0.4999f, 0.5f, 1f })
		{
			var calls = 0;
			var rolled = DamageEffectCatalog.Roll(
				getModifierPickCount: _ => 8,
				random: () =>
				{
					calls++;
					return draw;
				}
			);
			var expected = draw < 0.5f ? GameDamageType.Fire : GameDamageType.Electricity;
			Check(rolled.Type == expected, "Adding an enemy effect must preserve exact 50/50 fire/shock boundaries.");
			Check(calls == 1, "Guaranteed fire/shock applications must still consume only the selection roll.");
		}

		var upgraded = definition.Resolve(_ => 8);
		Check(upgraded.Duration == settings.Duration && upgraded.BlastRadius == settings.BlastRadius
			&& upgraded.InstantDamage == settings.InstantDamage,
			"Player upgrades must not alter enemy explosion settings.");
		var immediate = Settings(fuse: 0f);
		Check(immediate.IsExplosion && immediate.Duration == 0f, "An explosion may have a zero-duration fuse.");
	}

	internal static void Invalid()
	{
		foreach (var invalid in new[] { -1f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
		{
			Reject<ArgumentOutOfRangeException>(() => Settings(fuse: invalid));
			Reject<ArgumentOutOfRangeException>(() => Settings(radius: invalid));
			Reject<ArgumentOutOfRangeException>(() => Settings(damage: invalid));
		}

		Reject<ArgumentOutOfRangeException>(() => Settings(radius: 0f));
		Reject<ArgumentOutOfRangeException>(() => Settings(damage: 0f));
		Reject<ArgumentOutOfRangeException>(() => Settings(dps: 1f));
		Reject<ArgumentOutOfRangeException>(() => Settings(maxStacks: 2));
		Reject<ArgumentOutOfRangeException>(() => Settings(spreadChance: 0.1f));
		Reject<ArgumentOutOfRangeException>(() => Settings(spreadRadius: 10f));
		Reject<ArgumentOutOfRangeException>(() => Settings(chainChance: 0.1f));
		Reject<ArgumentOutOfRangeException>(() => Settings(maxChains: 1));
		Reject<ArgumentOutOfRangeException>(() => Settings(chainRadius: 10f));
		Reject<ArgumentOutOfRangeException>(() => Settings(type: GameDamageType.Fire));

		var settings = Settings();
		var effects = new TimedDamageEffects<string>();
		Reject<ArgumentException>(() => effects.Apply(
			settings: settings,
			source: "exploder",
			now: 0f
		));
		Check(effects.Stacks.Count == 0, "An explosion must never enter the timed damage queue.");
		Reject<ArgumentException>(() => DamageEffectChain.Apply(
			initialTarget: "enemy",
			settings: settings,
			canHit: _ => true,
			findNext: (_, _) => null,
			hit: (_, _) => throw new InvalidOperationException("An explosion must not deal chain damage."),
			random: () => throw new InvalidOperationException("An explosion must not roll chains.")
		));
	}

	private static DamageEffectSettings Settings(
		float fuse = 3f,
		float radius = 100f,
		float damage = 40f,
		float dps = 0f,
		int maxStacks = 1,
		float spreadChance = 0f,
		float spreadRadius = 0f,
		float chainChance = 0f,
		int maxChains = 0,
		float chainRadius = 0f,
		GameDamageType type = GameDamageType.Explosive
	)
	{
		return new DamageEffectSettings(
			type: type,
			applicationChance: 1f,
			duration: fuse,
			damagePerSecond: dps,
			maxStacks: maxStacks,
			spreadChance: spreadChance,
			spreadRadius: spreadRadius,
			instantDamage: damage,
			chainChance: chainChance,
			maxChains: maxChains,
			chainRadius: chainRadius,
			blastRadius: radius
		);
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void Reject<TException>(Action action) where TException : Exception
	{
		try
		{
			action();
		}
		catch (TException)
		{
			return;
		}

		throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
	}
}
