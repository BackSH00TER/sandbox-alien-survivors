using System;
using System.Collections.Generic;
using System.Linq;

public enum GameRarity
{
	Common = 0,
	Uncommon = 1,
	Rare = 2,
	Epic = 3,
	Legendary = 4
}

public sealed class RarityBucket
{
	public GameRarity Id { get; }
	public float Chance { get; }
	public string Name => Id switch
	{
		GameRarity.Common => "Common",
		GameRarity.Uncommon => "Uncommon",
		GameRarity.Rare => "Rare",
		GameRarity.Epic => "Epic",
		GameRarity.Legendary => "Legendary",
		_ => throw new ArgumentOutOfRangeException(nameof(Id))
	};

	public RarityBucket(GameRarity id, float chance)
	{
		if (!Enum.IsDefined(id))
		{
			throw new ArgumentOutOfRangeException(nameof(id), "Rarity must have a shared display definition.");
		}
		if (!float.IsFinite(chance) || chance < 0f || chance > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(chance), "Rarity chance must be finite and between zero and one.");
		}

		Id = id;
		Chance = chance;
	}
}

/// <summary>Draws a rarity, then a uniform item. Callers decide which buckets and items are eligible.</summary>
public static class RarityDraw
{
	public static T Pick<T>(
		IReadOnlyList<RarityBucket> buckets,
		Func<GameRarity, IReadOnlyList<T>> getCandidates,
		Func<float> random
	)
	{
		ArgumentNullException.ThrowIfNull(buckets);
		ArgumentNullException.ThrowIfNull(getCandidates);
		ArgumentNullException.ThrowIfNull(random);

		var active = buckets.Where(bucket => bucket.Chance > 0f).ToArray();
		float rarityRoll = random();
		var bucket = WeightedDraw.Pick(
			options: active,
			weight: definition => definition.Chance,
			roll: rarityRoll
		);
		var candidates = getCandidates(bucket.Id);
		if (candidates == null || candidates.Count == 0)
		{
			throw new InvalidOperationException($"Selected rarity '{bucket.Id}' has no eligible items.");
		}

		float itemRoll = random();
		return WeightedDraw.Pick(
			options: candidates,
			weight: _ => 1f,
			roll: itemRoll
		);
	}
}
