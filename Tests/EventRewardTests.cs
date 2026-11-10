using System;
using System.Linq;

internal static class EventRewardTests
{
	internal static void LootCounts()
	{
		foreach (var kind in Enum.GetValues<LootKind>())
		{
			var fixedDrop = new GameLootDrop(
				kind: kind,
				minimumCount: 2,
				maximumCount: 2
			);
			Check(fixedDrop.Kind == kind && fixedDrop.RollCount(UnexpectedRoll) == 2,
				"Every catalog item must support fixed counts without consuming randomness.");
		}

		var disabled = new GameLootDrop(
			kind: LootKind.Health,
			minimumCount: 0,
			maximumCount: 0
		);
		Check(disabled.RollCount(UnexpectedRoll) == 0, "Zero counts must disable a drop.");

		var randomDrop = new GameLootDrop(
			kind: LootKind.Armor,
			minimumCount: 1,
			maximumCount: 3
		);
		foreach (var count in new[] { 1, 2, 3 })
		{
			var calls = 0;
			var actual = randomDrop.RollCount((minimum, maximum) =>
			{
				calls++;
				Check(minimum == 1 && maximum == 4, "The upper configured count must be inclusive.");
				return count;
			});
			Check(actual == count && calls == 1, "Random loot counts must consume one roll without rerolling.");
		}
	}

	internal static void Experience()
	{
		for (var amount = 0; amount <= 1000; amount++)
		{
			var orbs = ExperienceOrbRules.SplitReward(amount);
			Check(orbs.Length == Math.Min(4, amount), "Rewards must use at most four larger orbs.");
			Check(orbs.Sum() == amount && orbs.All(value => value > 0),
				"Reward splitting must preserve exact XP without zero-value orbs.");
			if (orbs.Length > 0)
			{
				Check(orbs.Max() - orbs.Min() <= 1, "Remainder XP must be distributed evenly.");
			}
		}
		var defaultReward = ExperienceOrbRules.SplitReward(100);
		Check(defaultReward.SequenceEqual(new[] { 25, 25, 25, 25 }),
			"The default dropship reward must spawn four 25-XP orbs using shared value-based visuals.");
	}

	internal static void Invalid()
	{
		foreach (var counts in new[] { (-1, 1), (2, 1), (0, 21) })
		{
			Throws<ArgumentOutOfRangeException>(() => new GameLootDrop(
				kind: LootKind.Health,
				minimumCount: counts.Item1,
				maximumCount: counts.Item2
			));
		}
		Throws<ArgumentOutOfRangeException>(() => new GameLootDrop(
			kind: (LootKind)99,
			minimumCount: 1,
			maximumCount: 1
		));
		var drop = new GameLootDrop(
			kind: LootKind.Health,
			minimumCount: 1,
			maximumCount: 3
		);
		Throws<ArgumentNullException>(() => drop.RollCount(null));
		Throws<ArgumentOutOfRangeException>(() => drop.RollCount((_, _) => 0));
		Throws<ArgumentOutOfRangeException>(() => drop.RollCount((_, _) => 4));
		Throws<ArgumentOutOfRangeException>(() => ExperienceOrbRules.SplitReward(-1));
	}

	private static int UnexpectedRoll(int minimum, int maximum)
	{
		throw new Exception("Fixed counts must not consume randomness.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new Exception(message);
		}
	}

	private static void Throws<T>(Action action) where T : Exception
	{
		try
		{
			action();
		}
		catch (T)
		{
			return;
		}
		throw new Exception($"Expected {typeof(T).Name}.");
	}
}
