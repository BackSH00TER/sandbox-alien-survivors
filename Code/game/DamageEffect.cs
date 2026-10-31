using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox;

public enum DamageEffectStat
{
	ApplicationChance,
	Duration,
	DamagePerSecond,
	MaxStacks,
	SpreadChance
}

public sealed class DamageEffectDefinition
{
	public DamageType Type { get; init; }
	public Modifier Unlock { get; init; }
	public Modifier SpreadUnlock { get; init; }
	public Color BulletColor { get; init; }
	public float DrawWeight { get; init; } = 1f;
	public float ApplicationChance { get; init; }
	public float Duration { get; init; }
	public float DamagePerSecond { get; init; }
	public int MaxStacks { get; init; } = 1;
	public float SpreadChance { get; init; }
	public float SpreadRadius { get; init; }

	/// <summary>Builds a shot's immutable effect settings from the base values and the player's upgrade picks.</summary>
	public DamageEffectSettings Resolve(Func<Modifier, int> getModifierPickCount)
	{
		// Sum each matching upgrade's increment multiplied by its pick count.
		// These are player upgrade picks, not the timed effect stacks currently on an enemy.
		float UpgradeBonus(DamageEffectStat stat) => ModifierCatalog.All
			.Where(modifier => modifier.EffectType == Type && modifier.EffectStat == stat)
			.Sum(modifier => modifier.Amount * getModifierPickCount(modifier.Id));

		var spreadUnlocked = getModifierPickCount(SpreadUnlock) > 0;
		var effectiveSpreadChance = spreadUnlocked
			? Math.Clamp(SpreadChance + UpgradeBonus(DamageEffectStat.SpreadChance), 0f, 1f)
			: 0f;

		return new DamageEffectSettings(
			type: Type,
			applicationChance: Math.Clamp(ApplicationChance + UpgradeBonus(DamageEffectStat.ApplicationChance), 0f, 1f),
			duration: Duration + UpgradeBonus(DamageEffectStat.Duration),
			damagePerSecond: DamagePerSecond + UpgradeBonus(DamageEffectStat.DamagePerSecond),
			maxStacks: MaxStacks + (int)UpgradeBonus(DamageEffectStat.MaxStacks),
			spreadChance: effectiveSpreadChance,
			spreadRadius: SpreadRadius);
	}
}

public static class DamageEffectCatalog
{
	// Only implemented effects belong here; enum entries alone never enter the bullet draw.
	public static IReadOnlyList<DamageEffectDefinition> All { get; } = new[]
	{
		new DamageEffectDefinition
		{
			Type = DamageType.Fire,
			Unlock = Modifier.FireRounds,
			SpreadUnlock = Modifier.FireSpread,
			BulletColor = new Color(1f, 0.25f, 0.05f),
			ApplicationChance = 0.2f,
			Duration = 5f,
			DamagePerSecond = 5f,
			SpreadChance = 0.1f,
			SpreadRadius = 120f
		}
	};

	public static DamageEffectDefinition Get(DamageType type) => All.First(effect => effect.Type == type);

	/// <summary>Chooses one unlocked effect, then rolls its application chance; a failed roll leaves the bullet physical.</summary>
	public static DamageEffectSettings Roll(Func<Modifier, int> getModifierPickCount, Func<float> random)
	{
		var unlockedEffects = All.Where(effect => getModifierPickCount(effect.Unlock) > 0).ToList();
		if (unlockedEffects.Count == 0)
			return null;

		// Only the chosen type gets a chance check; do not retry another effect when it fails.
		var definition = WeightedDraw.Pick(unlockedEffects, effect => effect.DrawWeight, random());
		var settings = definition.Resolve(getModifierPickCount);
		return settings.ApplicationChance >= 1f || random() < settings.ApplicationChance ? settings : null;
	}
}
