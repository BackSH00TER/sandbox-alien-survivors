using System;
using System.Collections.Generic;
using System.Linq;

internal static class ModifierCatalogTests
{
	internal static void Coverage()
	{
		var ids = Enum.GetValues<GameModifier>();
		var catalogIds = ModifierCatalog.All.Select(definition => definition.Id);
		Check(catalogIds.SequenceEqual(ids), "Every modifier needs one ordered definition.");
		for (int index = 0; index < ids.Length; index++)
		{
			Check((int)ids[index] == index, "Existing synchronized modifier IDs must remain stable.");
		}

		foreach (var definition in ModifierCatalog.All)
		{
			var lookup = ModifierCatalog.Get(definition.Id);
			Check(ReferenceEquals(definition, lookup), "Lookup must return the shared definition.");
			Check(!string.IsNullOrWhiteSpace(definition.Name) && !string.IsNullOrWhiteSpace(definition.Icon)
				&& !string.IsNullOrWhiteSpace(definition.Description), "Every modifier needs presentation metadata.");
			var rarity = ModifierCatalog.GetRarity(definition.Rarity);
			Check(rarity.Chance > 0f, "Every current modifier must belong to an enabled rarity.");
			Check(definition.Rarity == (definition.Id == GameModifier.ExtraOption ? GameRarity.Rare : GameRarity.Common),
				"Only More Choices should be rare.");
			if (definition.EffectType.HasValue)
			{
				Check(definition.InventoryClass == "damage-effect", "Damage upgrades must retain their inventory class.");
			}
		}

		var rarityIds = ModifierCatalog.Rarities.Select(rarity => rarity.Id);
		var expectedRarityIds = Enum.GetValues<GameRarity>();
		Check(rarityIds.SequenceEqual(expectedRarityIds), "Every rarity needs one ordered definition.");
		float totalChance = ModifierCatalog.Rarities.Sum(rarity => rarity.Chance);
		Near(totalChance, 1f);
		var common = ModifierCatalog.GetRarity(GameRarity.Common);
		var rare = ModifierCatalog.GetRarity(GameRarity.Rare);
		Near(common.Chance, 0.9f);
		Near(rare.Chance, 0.1f);
		foreach (var rarity in ModifierCatalog.Rarities.Where(rarity => rarity.Id is not GameRarity.Common and not GameRarity.Rare))
		{
			Near(rarity.Chance, 0f);
		}
	}

	internal static void Rarities()
	{
		var ids = Enum.GetValues<GameModifier>();
		var counts = ids.ToDictionary(id => id, _ => 0);
		for (int rarity = 0; rarity < 100; rarity++)
		{
			for (int item = 0; item < 100; item++)
			{
				var first = FirstDraw(
					stacks: _ => 0,
					rarityRoll: rarity / 100f,
					itemRoll: item / 100f
				);
				counts[first]++;
			}
		}

		foreach (var definition in ModifierCatalog.All)
		{
			int expected = 0;
			if (definition.IsAvailable(_ => 0))
			{
				expected = definition.Rarity == GameRarity.Rare ? 1000 : 1800;
			}
			Check(counts[definition.Id] == expected, $"Unexpected exact first-card probability for {definition.Id}.");
		}

		var lastCommon = FirstDraw(
			stacks: _ => 0,
			rarityRoll: 0.89999f,
			itemRoll: 1f
		);
		Check(lastCommon == GameModifier.Vacuum, "The common upper boundary must select its last item.");
		var firstRare = FirstDraw(
			stacks: _ => 0,
			rarityRoll: 0.9f,
			itemRoll: 0f
		);
		Check(firstRare == GameModifier.ExtraOption, "The rare lower boundary must be inclusive.");
		var secondCommon = FirstDraw(
			stacks: _ => 0,
			rarityRoll: 0f,
			itemRoll: 0.2f
		);
		Check(secondCommon == GameModifier.AttackRange, "Items within the common bucket must be uniform.");
		var unlockedRare = FirstDraw(
			stacks: id => id is GameModifier.FireRounds or GameModifier.FireSpread ? 1 : 0,
			rarityRoll: 0.9f,
			itemRoll: 1f
		);
		Check(unlockedRare == GameModifier.ExtraOption, "Unlocking more common upgrades must not dilute rare probability.");

		var random = new Random(101);
		const int trials = 20000;
		int rareOffers = 0;
		for (int trial = 0; trial < trials; trial++)
		{
			var offer = ModifierCatalog.RollOffer(_ => 0, () => (float)random.NextDouble());
			if (offer.Contains(GameModifier.ExtraOption))
			{
				rareOffers++;
			}
		}
		float frequency = (float)rareOffers / trials;
		Check(MathF.Abs(frequency - 0.271f) < 0.015f, $"Expected 1 - 0.9^3 rare offers, got {frequency:P2}.");
	}

	internal static void EmptyBuckets()
	{
		var rareFirst = ModifierCatalog.RollOffer(_ => 0, () => 1f);
		Check(rareFirst[0] == GameModifier.ExtraOption && rareFirst.Distinct().Count() == 3,
			"Removing the sole rare card must renormalize subsequent draws to common.");

		int CappedRare(GameModifier id)
		{
			return id == GameModifier.ExtraOption ? 5 : 0;
		}
		var onlyCommon = ModifierCatalog.RollOffer(CappedRare, () => 1f);
		Check(onlyCommon.Count == 5 && onlyCommon.All(id => ModifierCatalog.Get(id).Rarity == GameRarity.Common),
			"A capped rare bucket must not stall or shorten a full-pool offer.");
		Check(!ModifierCatalog.HasAlternativeOffer(CappedRare, onlyCommon), "A full eligible pool has no reroll alternative.");

		int Expanded(GameModifier id)
		{
			return id == GameModifier.ExtraOption ? 4 : 0;
		}
		var commonFirst = ModifierCatalog.RollOffer(Expanded, () => 0f);
		Check(commonFirst.Count == 6 && commonFirst[^1] == GameModifier.ExtraOption,
			"Exhausting all common cards must leave the rare bucket drawable.");

		int FiveSlots(GameModifier id)
		{
			return id == GameModifier.ExtraOption ? 2 : 0;
		}
		var previous = ModifierCatalog.RollOffer(FiveSlots, () => 0f);
		var rerolled = ModifierCatalog.RollOffer(
			getModifierPickCount: FiveSlots,
			random: () => 0f,
			previousOffer: previous
		);
		Check(!previous.Contains(GameModifier.ExtraOption) && rerolled.Contains(GameModifier.ExtraOption),
			"A forced reroll replacement with only a rare alternative must renormalize to rare.");
		Check(rerolled.Count == 5 && rerolled.Distinct().Count() == 5, "Replacement must preserve size and distinctness.");
	}

	internal static void Effects()
	{
		var recipient = new TestRecipient();
		var attackSpeed = ModifierCatalog.Get(GameModifier.AttackSpeed);
		attackSpeed.ApplyTo(recipient);
		Check(recipient.ModifierStacks(GameModifier.AttackSpeed) == 1 && recipient.Refreshes == 1,
			"Attack speed must add a stack and refresh the pending attack.");
		float attackRateMultiplier = ModifierCatalog.AttackRateMultiplier(recipient.ModifierStacks);
		Near(attackRateMultiplier, 2f);
		var attackRange = ModifierCatalog.Get(GameModifier.AttackRange);
		attackRange.ApplyTo(recipient);
		float attackRangeMultiplier = ModifierCatalog.AttackRangeMultiplier(recipient.ModifierStacks);
		Near(attackRangeMultiplier, 2f);
		Check(recipient.Refreshes == 1, "Other upgrades must not refresh attack cooldown.");
		var vitality = ModifierCatalog.Get(GameModifier.Vitality);
		vitality.ApplyTo(recipient);
		Near(recipient.HealthGranted, 100f);

		foreach (var definition in ModifierCatalog.All.Where(definition => definition.Id >= GameModifier.FireRounds))
		{
			if (definition.Id == GameModifier.FireSpreadChance)
			{
				Check(recipient.ModifierStacks(GameModifier.FireSpread) == 1, "Spread prerequisite must already be applied.");
			}
			definition.ApplyTo(recipient);
			Check(recipient.ModifierStacks(definition.Id) == 1, "Every upgrade must record exactly one pick.");
		}
		Check(ModifierCatalog.OfferSize(recipient.ModifierStacks) == 4, "More Choices must affect the next offer.");
		float collectionMultiplier = ModifierCatalog.ExperienceCollectionRangeMultiplier(recipient.ModifierStacks);
		Near(collectionMultiplier, 1.2f);
		Check(recipient.Refreshes == 1, "Passive upgrades must not alter cooldown.");
		Near(recipient.HealthGranted, 100f);
		var fireRounds = ModifierCatalog.Get(GameModifier.FireRounds);
		Reject<InvalidOperationException>(() => fireRounds.ApplyTo(recipient));
		Check(recipient.ModifierStacks(GameModifier.FireRounds) == 1, "Rejected picks cannot change stack counts.");
		var locked = new TestRecipient();
		var fireDamage = ModifierCatalog.Get(GameModifier.FireDamage);
		Reject<InvalidOperationException>(() => fireDamage.ApplyTo(locked));
		Check(locked.ModifierStacks(GameModifier.FireDamage) == 0, "Locked picks cannot apply effects.");
	}

	internal static void Invalid()
	{
		Reject<ArgumentOutOfRangeException>(() => ModifierCatalog.Get((GameModifier)(-1)));
		Reject<ArgumentOutOfRangeException>(() => ModifierCatalog.GetRarity((GameRarity)(-1)));
		foreach (float roll in new[] { -0.01f, 1.01f, float.NaN, float.PositiveInfinity })
		{
			Reject<ArgumentOutOfRangeException>(() => FirstDraw(
				stacks: _ => 0,
				rarityRoll: roll,
				itemRoll: 0f
			));
			Reject<ArgumentOutOfRangeException>(() => FirstDraw(
				stacks: _ => 0,
				rarityRoll: 0f,
				itemRoll: roll
			));
		}
		Reject<InvalidOperationException>(() => ModifierCatalog.RollOffer(_ => int.MaxValue, () => 0f));
	}

	private static GameModifier FirstDraw(
		Func<GameModifier, int> stacks,
		float rarityRoll,
		float itemRoll
	)
	{
		int draws = 0;
		var offer = ModifierCatalog.RollOffer(stacks, () => draws++ % 2 == 0 ? rarityRoll : itemRoll);
		return offer[0];
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

	private sealed class TestRecipient : IModifierRecipient
	{
		private readonly Dictionary<GameModifier, int> _stacks = new();
		public int Refreshes { get; private set; }
		public float HealthGranted { get; private set; }

		public int ModifierStacks(GameModifier modifier)
		{
			return _stacks.GetValueOrDefault(modifier);
		}

		public void AddModifierStack(GameModifier modifier)
		{
			_stacks[modifier] = ModifierStacks(modifier) + 1;
		}

		public void RefreshAttackCooldown()
		{
			Check(ModifierStacks(GameModifier.AttackSpeed) == 1, "Refresh must see the newly added stack.");
			Refreshes++;
		}

		public void IncreaseMaxHealth(float amount)
		{
			Check(ModifierStacks(GameModifier.Vitality) == 1, "Vitality must record its stack before healing.");
			HealthGranted += amount;
		}
	}
}
