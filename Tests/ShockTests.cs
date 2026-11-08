using System;
using System.Collections.Generic;
using System.Linq;

internal static class ShockTests
{
	internal static void Progression()
	{
		var picks = new Dictionary<GameModifier, int>();
		int Stacks(GameModifier modifier)
		{
			return picks.GetValueOrDefault(modifier);
		}

		var definition = DamageEffectCatalog.Get(GameDamageType.Electricity);
		var unlock = ModifierCatalog.Get(GameModifier.ShockRounds);
		var upgrades = ModifierCatalog.All.Where(modifier => modifier.EffectType == GameDamageType.Electricity && modifier.Id != unlock.Id);
		Check(unlock.IsAvailable(Stacks), "Shock unlock must be offered initially.");
		Check(upgrades.All(modifier => !modifier.IsAvailable(Stacks)), "Every shock upgrade requires Shock Rounds.");
		picks[GameModifier.ShockRounds] = 1;
		Check(!unlock.IsAvailable(Stacks), "Shock Rounds is a one-time unlock.");
		Check(upgrades.All(modifier => modifier.IsAvailable(Stacks)), "Shock Rounds must unlock every shock upgrade, including chains.");
		var settings = definition.Resolve(Stacks);
		Check(settings.IsInstant && settings.MaxChains == 1, "Shock must be instant with one extra target by default.");
		Near(settings.InstantDamage, 10f);
		Near(settings.ApplicationChance, 0.2f);
		Near(settings.ChainChance, 0.2f);
		Near(settings.ChainRadius, 180f);
		Near(settings.ChainDamageFalloff, 0.1f);
		Near(settings.MinimumChainDamageMultiplier, 0.1f);
		Near(settings.Duration, 0f);
		Near(settings.DamagePerSecond, 0f);
		Near(settings.SpreadChance, 0f);
		for (int rank = 0; rank <= 8; rank++)
		{
			picks[GameModifier.ShockApplicationChance] = rank;
			picks[GameModifier.ShockChainChance] = rank;
			picks[GameModifier.ShockChains] = rank;
			picks[GameModifier.ShockDamage] = rank;
			var upgraded = definition.Resolve(Stacks);
			Near(upgraded.ApplicationChance, 0.2f + rank * 0.1f);
			Near(upgraded.ChainChance, 0.2f + rank * 0.1f);
			Near(upgraded.InstantDamage, 10f + rank * 5f);
			Near(upgraded.ChainRadius, 180f + rank * 60f);
			Check(upgraded.MaxChains == 1 + rank, "Each chain pick must add exactly one additional enemy.");
		}

		Check(!ModifierCatalog.Get(GameModifier.ShockApplicationChance).IsAvailable(Stacks), "Capped proc chance must leave the offer pool.");
		Check(!ModifierCatalog.Get(GameModifier.ShockChainChance).IsAvailable(Stacks), "Capped chain chance must leave the offer pool.");
		Near(settings.InstantDamage, 10f);
		Near(settings.ChainRadius, 180f);
		Check(settings.MaxChains == 1, "Already fired projectiles retain their captured settings.");
		picks[GameModifier.ShockChains] = 0;
		var withoutChainUpgrades = definition.Resolve(Stacks);
		Near(withoutChainUpgrades.ChainRadius, 180f);
		Check(withoutChainUpgrades.MaxChains == 1, "Other shock upgrades must not increase chain count or range.");
		var fire = DamageEffectCatalog.Get(GameDamageType.Fire).Resolve(Stacks);
		Check(!fire.IsInstant && fire.MaxStacks == 1, "Shock picks cannot change fire.");
		Near(fire.DamagePerSecond, 5f);
		Near(fire.ChainDamageFalloff, 0f);
		var scorecard = GameStatCatalog.Sections.Single(section => section.Title == "Upgrades");
		foreach (var modifier in upgrades.Append(unlock))
		{
			Check(scorecard.Rows.Count(row => row.Key == GameStatKeys.ModifierPicked(modifier.Id)) == 1,
				"Each shock modifier must appear once in the scorecard.");
		}
	}

	internal static void Rolls()
	{
		var picks = new Dictionary<GameModifier, int> { [GameModifier.ShockRounds] = 1 };
		int Stacks(GameModifier modifier)
		{
			return picks.GetValueOrDefault(modifier);
		}

		DamageEffectSettings Roll(params float[] values)
		{
			var rolls = new Queue<float>(values);
			var result = DamageEffectCatalog.Roll(Stacks, () => rolls.Dequeue());
			Check(rolls.Count == 0, "A proc must consume only the chosen effect's rolls.");
			return result;
		}

		Check(Roll(0f, 0.1999f)?.Type == GameDamageType.Electricity, "Shock-only unlock must select electricity.");
		Check(Roll(1f, 0.2f) == null, "A failed shock chance must leave the bullet physical.");
		picks[GameModifier.FireRounds] = 1;
		Check(Roll(0.4999f, 0f)?.Type == GameDamageType.Fire, "First half of equal-weight draws must choose fire.");
		Check(Roll(0.5f, 0f)?.Type == GameDamageType.Electricity, "Second half must choose shock.");
		Check(Roll(1f, 0.2f) == null, "A failed shock roll must not retry fire.");
		picks[GameModifier.ShockApplicationChance] = 8;
		Check(Roll(1f)?.Type == GameDamageType.Electricity, "100% shock must skip the chance roll.");
	}

	internal static void Chains()
	{
		var settings = Settings(chance: 1f, chains: 1);
		var hits = Run(settings, rolls: Array.Empty<float>());
		Check(hits.SequenceEqual(new[] { "A", "B" }), "One default chain means two total damaged enemies.");
		settings = Settings(chance: 1f, chains: 3);
		hits = Run(settings, rolls: Array.Empty<float>());
		Check(hits.SequenceEqual(new[] { "A", "B", "C", "D" }), "Three chains must hit four unique targets.");
		settings = Settings(chance: 0.2f, chains: 3);
		hits = Run(settings, rolls: new[] { 0.1999f, 0.2f });
		Check(hits.SequenceEqual(new[] { "A", "B" }), "A failed per-hop roll must stop the chain immediately.");
		hits = Run(settings, rolls: new[] { 0.2f });
		Check(hits.SequenceEqual(new[] { "A" }), "A failed first hop must still damage the initial target.");
		hits = Run(
			settings: Settings(chance: 0f, chains: 10),
			rolls: Array.Empty<float>()
		);
		Check(hits.SequenceEqual(new[] { "A" }), "Zero chain chance must not draw or hop.");
		hits = Run(
			settings: Settings(chance: 1f, chains: 10),
			rolls: Array.Empty<float>(),
			findNext: (_, _) => "A"
		);
		Check(hits.SequenceEqual(new[] { "A" }), "A repeated target must never receive damage twice.");
		hits = Run(
			settings: Settings(chance: 1f, chains: 10),
			rolls: Array.Empty<float>(),
			findNext: (current, _) => current == "A" ? "B" : "A"
		);
		Check(hits.SequenceEqual(new[] { "A", "B" }), "A chain must not bounce back to its first target after hitting another enemy.");
		hits = Run(
			settings: Settings(chance: 1f, chains: 10),
			rolls: Array.Empty<float>(),
			findNext: (_, _) => null
		);
		Check(hits.Count == 1, "No nearby visible enemy must end a chain.");
		hits = Run(
			settings: Settings(chance: 1f, chains: 10),
			rolls: Array.Empty<float>(),
			canHit: target => target != "B"
		);
		Check(hits.Count == 1, "A target that becomes ineligible must not be damaged.");
		hits = Run(
			settings: Settings(chance: 1f, chains: 0),
			rolls: Array.Empty<float>()
		);
		Check(hits.Count == 1, "A zero-hop effect must damage only its initial target.");

		var originAlive = true;
		var nextAlive = true;
		hits.Clear();
		settings = Settings(chance: 1f, chains: 1);
		DamageEffectChain.Apply(
			initialTarget: "A",
			settings: settings,
			canHit: target => target == "A" || nextAlive,
			findNext: (_, _) => originAlive ? "B" : throw new InvalidOperationException("Origin must be inspected before lethal damage."),
			hit: (target, _) =>
			{
				hits.Add(target);
				originAlive = false;
				nextAlive = false;
			},
			random: () => throw new InvalidOperationException("Guaranteed hops must not roll.")
		);
		Check(hits.SequenceEqual(new[] { "A" }), "A selected target must be rechecked after the previous hit ends the round or removes it.");
	}

	internal static void Falloff()
	{
		var definition = DamageEffectCatalog.Get(GameDamageType.Electricity);
		var targets = Enumerable.Range(0, 13).Select(index => $"Enemy {index}").ToArray();
		var expectedDamage = new[] { 10f, 9f, 8f, 7f, 6f, 5f, 4f, 3f, 2f, 1f, 1f, 1f, 1f };
		foreach (int damagePicks in new[] { 0, 2 })
		{
			var picks = new Dictionary<GameModifier, int>
			{
				[GameModifier.ShockRounds] = 1,
				[GameModifier.ShockChainChance] = 8,
				[GameModifier.ShockChains] = 11,
				[GameModifier.ShockDamage] = damagePicks
			};
			var settings = definition.Resolve(modifier => picks.GetValueOrDefault(modifier));
			var hits = new List<float>();
			DamageEffectChain.Apply(
				initialTarget: targets[0],
				settings: settings,
				canHit: _ => true,
				findNext: (_, visited) => targets.FirstOrDefault(target => !visited.Contains(target)),
				hit: (_, damage) => hits.Add(damage),
				random: () => throw new InvalidOperationException("Guaranteed chains must not roll.")
			);
			Check(hits.Count == expectedDamage.Length, "The damage floor must not stop a chain with targets and hops remaining.");
			var upgradeMultiplier = damagePicks == 0 ? 1f : 2f;
			for (int index = 0; index < expectedDamage.Length; index++)
			{
				Near(hits[index], expectedDamage[index] * upgradeMultiplier);
			}

			var totalDamage = hits.Sum();
			Near(totalDamage, 58f * upgradeMultiplier);
		}
	}

	internal static void Invalid()
	{
		foreach (var chance in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject<ArgumentOutOfRangeException>(() => Settings(chance: chance, chains: 1));
		}

		Reject<ArgumentOutOfRangeException>(() => Settings(chance: 1f, chains: -1));
		foreach (var invalid in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject<ArgumentOutOfRangeException>(() => Settings(
				chance: 1f,
				chains: 1,
				falloff: invalid
			));
			Reject<ArgumentOutOfRangeException>(() => Settings(
				chance: 1f,
				chains: 1,
				minimumMultiplier: invalid
			));
		}

		Reject<ArgumentOutOfRangeException>(() => Settings(
			chance: 1f,
			chains: 1,
			minimumMultiplier: 0f
		));
		Reject<ArgumentOutOfRangeException>(() => Run(
			settings: Settings(chance: 0.2f, chains: 1),
			rolls: new[] { float.NaN }
		));
		var effects = new TimedDamageEffects<string>();
		var shock = Settings(chance: 1f, chains: 1);
		Reject<ArgumentException>(() => effects.Apply(
			settings: shock,
			source: "shooter",
			now: 0f
		));
		Check(effects.Stacks.Count == 0, "Instant shocks must never become timed damage stacks.");
	}

	internal static void OptionalChains()
	{
		var definition = new DamageEffectDefinition
		{
			Type = GameDamageType.Electricity,
			ApplicationChance = 1f,
			InstantDamage = 10f
		};
		var settings = definition.Resolve(_ => 0);
		Check(settings.IsInstant && settings.MaxChains == 0, "Instant effects may omit chaining entirely.");
		Near(settings.ChainChance, 0f);
		Near(settings.ChainRadius, 0f);
		var hits = Run(settings, rolls: Array.Empty<float>());
		Check(hits.SequenceEqual(new[] { "A" }), "Omitted chaining must hit only the initial target without random rolls.");
		var enabledWithoutRadius = new DamageEffectDefinition
		{
			Type = GameDamageType.Electricity,
			ApplicationChance = 1f,
			InstantDamage = 10f,
			ChainChance = 0.2f,
			MaxChains = 1
		};
		Reject<ArgumentOutOfRangeException>(() => enabledWithoutRadius.Resolve(_ => 0));
	}

	private static List<string> Run(
		DamageEffectSettings settings,
		float[] rolls,
		Func<string, IReadOnlySet<string>, string> findNext = null,
		Func<string, bool> canHit = null
	)
	{
		var random = new Queue<float>(rolls);
		var targets = new[] { "A", "B", "C", "D" };
		var hits = new List<string>();
		DamageEffectChain.Apply(
			initialTarget: "A",
			settings: settings,
			canHit: canHit ?? (_ => true),
			findNext: findNext ?? ((_, visited) => targets.FirstOrDefault(target => !visited.Contains(target))),
			hit: (target, _) => hits.Add(target),
			random: () => random.Dequeue()
		);
		Check(random.Count == 0, "Chains must consume exactly one random roll per attempted non-guaranteed hop.");
		return hits;
	}

	private static DamageEffectSettings Settings(
		float chance,
		int chains,
		float falloff = 0.1f,
		float minimumMultiplier = 0.1f
	)
	{
		return new DamageEffectSettings(
			type: GameDamageType.Electricity,
			applicationChance: 0.2f,
			duration: 0f,
			damagePerSecond: 0f,
			maxStacks: 1,
			spreadChance: 0f,
			spreadRadius: 0f,
			instantDamage: 10f,
			chainChance: chance,
			maxChains: chains,
			chainRadius: 180f,
			chainDamageFalloff: falloff,
			minimumChainDamageMultiplier: minimumMultiplier
		);
	}

	private static void Reject<T>(Action action) where T : Exception
	{
		try
		{
			action();
		}
		catch (T)
		{
			return;
		}

		throw new InvalidOperationException($"Expected {typeof(T).Name}.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private static void Near(float actual, float expected)
	{
		Check(MathF.Abs(actual - expected) < 0.0001f, $"Expected {expected}, got {actual}.");
	}
}
