using System;
using System.Linq;

internal static class LootCatalogTests
{
	internal static void Coverage()
	{
		var kinds = Enum.GetValues<LootKind>();
		Check(GameLootCatalog.All.Select(item => item.Id).SequenceEqual(kinds), "Every loot kind needs exactly one definition, preserving existing IDs.");
		foreach (var item in GameLootCatalog.All)
		{
			Check(ReferenceEquals(item, GameLootCatalog.Get(item.Id)), "Lookup must return the shared catalog entry.");
			Check(!string.IsNullOrWhiteSpace(item.Name), "Every loot item needs a name.");
			Check(item.CanBeMagnetAttracted == (item.Effect != LootEffect.Weapon), "Weapons must be excluded from the magnet.");
			if (item.Effect == LootEffect.Weapon)
			{
				Check(!string.IsNullOrWhiteSpace(item.WeaponResourcePath), "Weapon drops need a resource path.");
				Check(item.CanCollect == null && item.Collect == null, "Weapons must not have contact collection callbacks.");
			}
			else if (item.Effect != LootEffect.ReviveFlare)
			{
				Check(item.Amount > 0, "Consumable effects need positive amounts.");
			}
			if (item.Effect != LootEffect.Weapon)
			{
				Check(item.CanCollect != null && item.Collect != null, "Consumables must define eligibility and collection callbacks.");
			}
			if (item.Id != LootKind.Flare)
			{
				Check(item.Rarity.HasValue && GameLootCatalog.Rarities.Any(rarity => rarity.Id == item.Rarity),
					"Ordinary drops need a configured rarity bucket.");
			}
		}

		Check(GameLootCatalog.Get(LootKind.Health).Amount == 50, "Medkits must still heal 50.");
		var armor = GameLootCatalog.Get(LootKind.Armor);
		Check(condition: armor.Amount == 5, message: "Armor must grant five points.");
		Check(condition: armor.Name == "Armor +5", message: "Armor's display name must match its pickup amount.");
		Check(GameLootCatalog.Get(LootKind.RerollDice).Amount == 1, "Dice must still grant one reroll.");
		Check(GameLootCatalog.Get(LootKind.Rifle).WeaponSlot == LootWeaponSlot.Rifle, "Rifle must retain its crate override.");
		Check(GameLootCatalog.Get(LootKind.Shotgun).WeaponSlot == LootWeaponSlot.Shotgun, "Shotgun must retain its crate override.");
		Check(!GameLootCatalog.Get(LootKind.Flare).Rarity.HasValue, "The contextual flare must stay outside the normal rarity buckets.");
		var rarityIds = GameLootCatalog.Rarities.Select(rarity => rarity.Id);
		var expectedRarityIds = Enum.GetValues<GameRarity>();
		Check(rarityIds.SequenceEqual(expectedRarityIds), "Every rarity needs exactly one bucket definition.");
		float totalChance = GameLootCatalog.Rarities.Sum(rarity => rarity.Chance);
		float chanceDeviation = MathF.Abs(totalChance - 1f);
		Check(chanceDeviation < 0.0001f, "Rarity percentages must total 100%.");
		foreach (var rarity in GameLootCatalog.Rarities)
		{
			Check(float.IsFinite(rarity.Chance) && rarity.Chance >= 0f && rarity.Chance <= 1f,
				"Rarity probabilities must be finite and between zero and one.");
			if (rarity.Chance > 0f)
			{
				Check(GameLootCatalog.All.Any(item => item.Rarity == rarity.Id), "Every enabled bucket needs loot.");
			}
		}
	}

	internal static void Collection()
	{
		var collector = new TestCollector();
		var health = GameLootCatalog.Get(LootKind.Health);
		Check(!health.CanCollect(collector, health.Amount), "Full health must reject health attraction and collection.");
		collector.Health = 50f;
		Check(health.CanCollect(collector, health.Amount), "Injured collectors must be eligible.");
		Check(collector.EffectCalls == 0, "Eligibility callbacks must not apply effects.");
		Check(health.Collect(collector, health.Amount) && collector.Health == 100f, "Health callback must restore its configured amount.");

		var armor = GameLootCatalog.Get(LootKind.Armor);
		Check(condition: armor.Collect(collector, armor.Amount) && collector.Armor == 5, message: "An armor pickup must grant five points from zero.");
		Check(condition: armor.Collect(collector, armor.Amount) && collector.Armor == 10, message: "Another armor pickup must add five more points.");
		collector.Armor = int.MaxValue - 4;
		Check(condition: !armor.CanCollect(collector, armor.Amount), message: "Armor eligibility must reject capacity below five points.");
		collector.Armor = int.MaxValue;
		Check(!armor.CanCollect(collector, armor.Amount), "Armor eligibility must reject counter overflow.");
		collector.Armor = int.MaxValue - armor.Amount;
		Check(armor.CanCollect(collector, armor.Amount), "Exact remaining armor capacity must be eligible.");
		Check(armor.Collect(collector, armor.Amount) && collector.Armor == int.MaxValue, "Armor callback must forward the configured amount.");

		var dice = GameLootCatalog.Get(LootKind.RerollDice);
		collector.RerollDice = int.MaxValue;
		Check(!dice.CanCollect(collector, dice.Amount), "Dice eligibility must reject counter overflow.");
		collector.RerollDice = 0;
		collector.IsRoundOver = true;
		Check(!dice.CanCollect(collector, dice.Amount), "Round-over collectors cannot receive dice.");
		collector.IsRoundOver = false;
		Check(dice.CanCollect(collector, dice.Amount), "Live-round collectors with capacity must be eligible for dice.");
		Check(dice.Collect(collector, dice.Amount) && collector.RerollDice == dice.Amount, "Dice callback must grant its configured amount.");

		var flare = GameLootCatalog.Get(LootKind.Flare);
		Check(!flare.CanCollect(collector, flare.Amount), "Flares must not be eligible when nobody is dead.");
		collector.HasDeadTeammate = true;
		Check(flare.CanCollect(collector, flare.Amount), "A dead teammate must enable flare eligibility.");
		Check(flare.Collect(collector, flare.Amount) && collector.FlaresFired == 1, "Flare callback must fire one revive flare.");

		collector.AcceptActions = false;
		foreach (var item in GameLootCatalog.All.Where(item => item.Collect != null))
		{
			Check(!item.Collect(collector, item.Amount), "Failed effect application must not report successful collection.");
		}
	}

	internal static void Bonuses()
	{
		var collector = new TestCollector
		{
			Health = 1f,
			MaxHealth = 200f,
			HealthPickupMultiplier = 1.25f,
			ArmorPickupMultiplier = 1.25f
		};
		var health = GameLootCatalog.Get(LootKind.Health);
		Check(health.Collect(collector, health.Amount), "Boosted healing must collect.");
		Check(collector.Health == 63.5f, "One medicine pick must grant 62.5 health from a 50-health pickup.");
		collector.Health = 190f;
		Check(health.Collect(collector, health.Amount) && collector.Health == 200f, "Healing must still clamp to max health.");

		var armor = GameLootCatalog.Get(LootKind.Armor);
		Check(armor.Collect(collector, armor.Amount) && collector.Armor == 7, "One armor pick must round 6.25 up to seven.");
		collector.Armor = int.MaxValue - 6;
		Check(!armor.CanCollect(collector, armor.Amount), "Eligibility must use boosted armor, not the base five.");
		Check(!armor.Collect(collector, armor.Amount), "Direct collection must also reject boosted overflow.");
		collector.Armor = int.MaxValue - 7;
		Check(armor.CanCollect(collector, armor.Amount), "Exact boosted capacity must be eligible.");
		Check(armor.Collect(collector, armor.Amount) && collector.Armor == int.MaxValue, "Exact boosted capacity must collect.");
		collector.ArmorPickupMultiplier = float.MaxValue;
		Check(!armor.CanCollect(collector, armor.Amount), "Amounts beyond integer capacity must be rejected before casting.");
	}

	internal static void Drops()
	{
		var counts = Enum.GetValues<LootKind>().ToDictionary(kind => kind, _ => 0);
		for (var flare = 0; flare < 100; flare++)
		{
			for (var rarity = 0; rarity < 100; rarity++)
			{
				for (var common = 0; common < 2; common++)
				{
					var item = Roll(
						flareChance: 0.25f,
						flareRoll: (flare + 0.5f) / 100f,
						rarityRoll: (rarity + 0.5f) / 100f,
						itemRoll: (common + 0.5f) / 2f
					);
					counts[item.Id]++;
				}
			}
		}

		Check(counts[LootKind.Flare] == 5000, "Flare roll must retain priority and its 25% contextual chance.");
		Check(counts[LootKind.Armor] == 1500, "Rare must have a 10% chance after the flare roll fails.");
		Check(counts[LootKind.Rifle] == 0 && counts[LootKind.Shotgun] == 0, "Supply crates must never drop weapons.");
		foreach (var kind in new[] { LootKind.Health, LootKind.RerollDice })
		{
			Check(counts[kind] == 6750, "Common items must be equally likely within their 90% bucket.");
		}

		var boundary = Roll(
			flareChance: 0f,
			flareRoll: 0f,
			rarityRoll: 0f,
			itemRoll: 1f
		);
		Check(boundary.Id == LootKind.RerollDice, "Zero flare chance excludes flares and the last common interval includes one.");
		var rareBoundary = Roll(
			flareChance: 0f,
			flareRoll: 0.5f,
			rarityRoll: 0.9f,
			itemRoll: 0f
		);
		Check(rareBoundary.Id == LootKind.Armor, "The 90% boundary must start the rare bucket.");
		var finalBoundary = Roll(
			flareChance: 0f,
			flareRoll: 1f,
			rarityRoll: 1f,
			itemRoll: 1f
		);
		Check(finalBoundary.Id == LootKind.Armor, "Endpoint one must select the final enabled rarity, never a zero-chance bucket.");
		var noFlareCounts = Enum.GetValues<LootKind>().ToDictionary(kind => kind, _ => 0);
		for (var rarity = 0; rarity < 100; rarity++)
		{
			for (var common = 0; common < 2; common++)
			{
				var item = Roll(
					flareChance: 0f,
					flareRoll: 0.5f,
					rarityRoll: (rarity + 0.5f) / 100f,
					itemRoll: (common + 0.5f) / 2f
				);
				noFlareCounts[item.Id]++;
			}
		}
		Check(noFlareCounts[LootKind.Flare] == 0 && noFlareCounts[LootKind.Armor] == 20, "No-death drops must exclude flares and retain 10% armor.");
		Check(noFlareCounts[LootKind.Health] == 90 && noFlareCounts[LootKind.Rifle] == 0
			&& noFlareCounts[LootKind.Shotgun] == 0 && noFlareCounts[LootKind.RerollDice] == 90,
			"Each common consumable must have a 45% no-flare probability.");
		for (var item = 0; item <= 100; item++)
		{
			var rare = Roll(
				flareChance: 0f,
				flareRoll: 0.5f,
				rarityRoll: 0.95f,
				itemRoll: item / 100f
			);
			Check(rare.Rarity == GameRarity.Rare, "Item selection must never change the chosen rarity.");
		}
	}

	internal static void WeaponDrops()
	{
		var counts = GameLootCatalog.All.ToDictionary(item => item.Id, _ => 0);
		for (var index = 0; index < 1000; index++)
		{
			var roll = (index + 0.5f) / 1000f;
			var item = GameLootCatalog.RollWeapon(random: () => roll);
			Check(condition: item.Effect == LootEffect.Weapon, message: "Weapon crates must exclude every consumable, including contextual flares.");
			counts[item.Id]++;
		}

		Check(condition: counts[LootKind.Rifle] == 500 && counts[LootKind.Shotgun] == 500, message: "Weapon crate drops must choose weapons equally.");
		var first = GameLootCatalog.RollWeapon(random: () => 0f);
		var boundary = GameLootCatalog.RollWeapon(random: () => 0.5f);
		var last = GameLootCatalog.RollWeapon(random: () => 1f);
		Check(condition: first.Id == LootKind.Rifle, message: "Zero must choose the first weapon.");
		Check(condition: boundary.Id == LootKind.Shotgun && last.Id == LootKind.Shotgun, message: "Weapon interval boundaries and endpoint one must be valid.");
		foreach (var invalidRoll in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => GameLootCatalog.RollWeapon(random: () => invalidRoll));
		}
	}

	internal static void Visuals()
	{
		foreach (var item in GameLootCatalog.All)
		{
			Check(item.VisualParts.Count > 0, "Every item needs visible geometry.");
			foreach (var part in item.VisualParts)
			{
				Check(!string.IsNullOrWhiteSpace(part.Name) && !string.IsNullOrWhiteSpace(part.ModelPath), "Visual parts need names and model paths.");
				Check(part.Size.x > 0f && part.Size.y > 0f && part.Size.z > 0f, "Visual dimensions must be positive.");
			}
		}

		Check(GameLootCatalog.Get(LootKind.Health).VisualParts.Count == 3, "Medkit must retain its box and two cross parts.");
		Check(GameLootCatalog.Get(LootKind.Armor).VisualParts.Count == 3, "Armor must retain its three parts.");
		Check(GameLootCatalog.Get(LootKind.Flare).VisualParts.Count == 3, "Flare must retain its three parts.");
		Check(GameLootCatalog.Get(LootKind.Rifle).VisualParts.Count == 4 && GameLootCatalog.Get(LootKind.Shotgun).VisualParts.Count == 4,
			"Weapon prototypes must retain their four parts.");
		var pips = GameLootCatalog.Get(LootKind.RerollDice).VisualParts.Where(part => part.Name == "Die Pip").ToArray();
		Check(pips.Length == 21 && pips.All(part => part.ScaleToModelWidth && part.Size.x == 3.5f), "Dice must retain 21 model-scaled pips.");
		Check(pips.Count(part => part.Position.z == 11f) == 1 && pips.Count(part => part.Position.z == -11f) == 6,
			"Die top and bottom must retain one and six pips.");
		Check(pips.Count(part => part.Position.x == 11f) == 2 && pips.Count(part => part.Position.x == -11f) == 5,
			"Die front and back must retain two and five pips.");
		Check(pips.Count(part => part.Position.y == 11f) == 3 && pips.Count(part => part.Position.y == -11f) == 4,
			"Die left and right must retain three and four pips.");
	}

	internal static void Invalid()
	{
		Reject(() => GameLootCatalog.Get((LootKind)999));
		foreach (var chance in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => GameLootCatalog.Roll(flareChance: chance, random: () => 0.5f));
		}
		foreach (var roll in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Reject(() => GameLootCatalog.Roll(flareChance: 0f, random: () => roll));
			Reject(() => Roll(
				flareChance: 0f,
				flareRoll: 0.5f,
				rarityRoll: roll,
				itemRoll: 0.5f
			));
			Reject(() => Roll(
				flareChance: 0f,
				flareRoll: 0.5f,
				rarityRoll: 0.5f,
				itemRoll: roll
			));
		}
	}

	private static LootDefinition Roll(
		float flareChance,
		float flareRoll,
		float rarityRoll,
		float itemRoll
	)
	{
		var rolls = new[] { flareRoll, rarityRoll, itemRoll };
		var index = 0;
		return GameLootCatalog.Roll(flareChance: flareChance, random: () => rolls[index++]);
	}

	private static void Reject(Action action)
	{
		try
		{
			action();
		}
		catch (ArgumentOutOfRangeException)
		{
			return;
		}

		throw new InvalidOperationException("Invalid loot input must throw.");
	}

	private static void Check(bool condition, string message)
	{
		if (!condition)
		{
			throw new InvalidOperationException(message);
		}
	}

	private sealed class TestCollector : ILootCollector
	{
		public float Health { get; set; } = 100f;
		public float MaxHealth { get; set; } = 100f;
		public int Armor { get; set; }
		public int RerollDice { get; set; }
		public float HealthPickupMultiplier { get; set; } = 1f;
		public float ArmorPickupMultiplier { get; set; } = 1f;
		public bool IsRoundOver { get; set; }
		public bool HasDeadTeammate { get; set; }
		public bool AcceptActions { get; set; } = true;
		public int EffectCalls { get; private set; }
		public int FlaresFired { get; private set; }

		public bool RestoreHealth(float amount)
		{
			EffectCalls++;
			if (!AcceptActions)
			{
				return false;
			}
			Health = MathF.Min(MaxHealth, Health + amount);
			return true;
		}

		public bool AddArmor(int amount)
		{
			EffectCalls++;
			if (!AcceptActions)
			{
				return false;
			}
			Armor += amount;
			return true;
		}

		public bool AddRerollDice(int amount)
		{
			EffectCalls++;
			if (!AcceptActions)
			{
				return false;
			}
			RerollDice += amount;
			return true;
		}

		public bool FireReviveFlare()
		{
			EffectCalls++;
			if (!AcceptActions)
			{
				return false;
			}
			FlaresFired++;
			return true;
		}
	}
}
