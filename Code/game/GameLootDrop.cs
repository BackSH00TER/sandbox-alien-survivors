using System;

/// <summary>A catalog item with a fixed count or an inclusive random count range.</summary>
public sealed class GameLootDrop
{
	public LootKind Kind { get; }
	public int MinimumCount { get; }
	public int MaximumCount { get; }

	public GameLootDrop(
		LootKind kind,
		int minimumCount,
		int maximumCount
	)
	{
		if (!Enum.IsDefined(kind))
		{
			throw new ArgumentOutOfRangeException(nameof(kind));
		}
		if (minimumCount < 0 || maximumCount < minimumCount || maximumCount > 20)
		{
			throw new ArgumentOutOfRangeException(nameof(maximumCount), "Loot counts require 0 <= minimum <= maximum <= 20.");
		}

		Kind = kind;
		MinimumCount = minimumCount;
		MaximumCount = maximumCount;
	}

	public int RollCount(Func<int, int, int> random)
	{
		ArgumentNullException.ThrowIfNull(random);
		if (MinimumCount == MaximumCount)
		{
			return MinimumCount;
		}

		var count = random(MinimumCount, MaximumCount + 1);
		if (count < MinimumCount || count > MaximumCount)
		{
			throw new ArgumentOutOfRangeException(nameof(random), "Loot count rolls must stay within the configured range.");
		}
		return count;
	}
}
