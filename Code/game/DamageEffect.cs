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

	public DamageEffectSettings Resolve(Func<Modifier, int> stacks)
	{
		float Bonus(DamageEffectStat stat) => ModifierCatalog.All
			.Where(x => x.EffectType == Type && x.EffectStat == stat)
			.Sum(x => x.Amount * stacks(x.Id));

		return new DamageEffectSettings(Type,
			Math.Clamp(ApplicationChance + Bonus(DamageEffectStat.ApplicationChance), 0f, 1f),
			Duration + Bonus(DamageEffectStat.Duration),
			DamagePerSecond + Bonus(DamageEffectStat.DamagePerSecond),
			MaxStacks + (int)Bonus(DamageEffectStat.MaxStacks),
			stacks(SpreadUnlock) > 0 ? Math.Clamp(SpreadChance + Bonus(DamageEffectStat.SpreadChance), 0f, 1f) : 0f,
			SpreadRadius);
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

	public static DamageEffectDefinition Get(DamageType type) => All.First(x => x.Type == type);

	public static DamageEffectSettings Roll(Func<Modifier, int> stacks, Func<float> random)
	{
		var available = All.Where(x => stacks(x.Unlock) > 0).ToList();
		if (available.Count == 0)
			return null;

		var definition = WeightedDraw.Pick(available, x => x.DrawWeight, random());
		var settings = definition.Resolve(stacks);
		return settings.ApplicationChance >= 1f || random() < settings.ApplicationChance ? settings : null;
	}
}
