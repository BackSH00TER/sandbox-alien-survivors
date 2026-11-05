using System;
using System.Linq;

internal static class RarityDrawTests
{
	private static readonly RarityBucket[] Buckets =
	{
		new(id: GameRarity.Common, chance: 0.6f),
		new(id: GameRarity.Uncommon, chance: 0f),
		new(id: GameRarity.Rare, chance: 0.4f)
	};

	internal static void Presentation()
	{
		var ids = Enum.GetValues<GameRarity>();
		var expectedNames = new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
		for (int index = 0; index < ids.Length; index++)
		{
			var bucket = new RarityBucket(id: ids[index], chance: 0f);
			Check((int)bucket.Id == index && bucket.Name == expectedNames[index], "Shared rarity IDs and names must remain stable.");
			var loot = GameLootCatalog.Rarities.Single(rarity => rarity.Id == bucket.Id);
			var modifier = ModifierCatalog.GetRarity(bucket.Id);
			Check(loot.Name == bucket.Name && modifier.Name == bucket.Name, "Both catalogs must use shared presentation.");
			Check(!ReferenceEquals(loot, modifier), "Catalogs must retain independently tunable probability tables.");
		}
	}

	internal static void Draws()
	{
		int common = 0;
		int firstRare = 0;
		int secondRare = 0;
		for (int rarity = 0; rarity < 100; rarity++)
		{
			for (int item = 0; item < 2; item++)
			{
				string result = Draw(rarityRoll: (rarity + 0.5f) / 100f, itemRoll: item);
				switch (result)
				{
					case "common":
						common++;
						break;
					case "rare-a":
						firstRare++;
						break;
					case "rare-b":
						secondRare++;
						break;
					default:
						throw new InvalidOperationException("Unexpected rarity draw result.");
				}
			}
		}
		Check(common == 120 && firstRare == 40 && secondRare == 40, "Custom bucket probabilities and uniform item selection must be exact.");

		string boundary = Draw(rarityRoll: 0.6f, itemRoll: 0f);
		Check(boundary == "rare-a", "The next positive bucket must include its lower boundary.");
		string endpoint = Draw(rarityRoll: 1f, itemRoll: 1f);
		Check(endpoint == "rare-b", "Inclusive endpoints must select the last enabled bucket and its last item.");

		int randomCalls = 0;
		int candidateCalls = 0;
		var onlyRare = new[] { Buckets[2] };
		string selected = RarityDraw.Pick(
			buckets: onlyRare,
			getCandidates: rarity =>
			{
				candidateCalls++;
				Check(rarity == GameRarity.Rare, "Only the supplied eligible bucket can be selected.");
				return new[] { "selected" };
			},
			random: () =>
			{
				randomCalls++;
				return 0f;
			}
		);
		Check(selected == "selected" && randomCalls == 2 && candidateCalls == 1,
			"Draws must renormalize supplied buckets, consume two rolls and resolve only the selected bucket.");
	}

	internal static void Invalid()
	{
		Reject<ArgumentOutOfRangeException>(() => new RarityBucket(id: (GameRarity)(-1), chance: 0.5f));
		foreach (float value in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity })
		{
			Reject<ArgumentOutOfRangeException>(() => new RarityBucket(id: GameRarity.Common, chance: value));
			Reject<ArgumentOutOfRangeException>(() => Draw(rarityRoll: value, itemRoll: 0f));
			Reject<ArgumentOutOfRangeException>(() => Draw(rarityRoll: 0f, itemRoll: value));
		}

		var disabled = new[] { new RarityBucket(id: GameRarity.Common, chance: 0f) };
		Reject<ArgumentOutOfRangeException>(() => RarityDraw.Pick(
			buckets: disabled,
			getCandidates: _ => new[] { "unreachable" },
			random: () => 0f
		));
		int calls = 0;
		Reject<InvalidOperationException>(() => RarityDraw.Pick(
			buckets: Buckets,
			getCandidates: _ => Array.Empty<string>(),
			random: () =>
			{
				calls++;
				return 0f;
			}
		));
		Check(calls == 1, "An empty selected bucket must fail, not retry or silently redistribute its chance.");
	}

	private static string Draw(float rarityRoll, float itemRoll)
	{
		int calls = 0;
		return RarityDraw.Pick(
			buckets: Buckets,
			getCandidates: rarity => rarity switch
			{
				GameRarity.Common => new[] { "common" },
				GameRarity.Rare => new[] { "rare-a", "rare-b" },
				_ => throw new InvalidOperationException("Disabled buckets must never resolve candidates.")
			},
			random: () => calls++ == 0 ? rarityRoll : itemRoll
		);
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
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
}
