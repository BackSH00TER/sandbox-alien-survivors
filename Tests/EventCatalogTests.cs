using System;
using System.Collections.Generic;
using System.Linq;

internal static class EventCatalogTests
{
	internal static void Catalog()
	{
		var ids = GameEventCatalog.AllEvents.Select(item => item.Id);
		var expectedIds = Enum.GetValues<GameEventKind>();
		Check(ids.SequenceEqual(expectedIds), "Every stable event ID must have exactly one definition.");
		foreach (var definition in GameEventCatalog.AllEvents)
		{
			Check(ReferenceEquals(definition, GameEventCatalog.Get(definition.Id)), "Lookup must use the catalog entry.");
			Check(!string.IsNullOrWhiteSpace(definition.Label) && !string.IsNullOrWhiteSpace(definition.Icon),
				"Every event needs timeline metadata.");
			Check(Enum.IsDefined(definition.Rarity), "Every event needs a known rarity.");
		}

		var world = new TestWorld();
		var quiet = GameEventCatalog.Get(GameEventKind.None);
		Check(quiet.TryStart(world) && world.Attempts == 0, "Quiet intervals must not spawn anything.");
		var dropship = GameEventCatalog.Get(GameEventKind.Dropship);
		Check(!dropship.TryStart(world) && world.Attempts == 1, "Failed starts must propagate for retries.");
		world.Success = true;
		Check(dropship.TryStart(world) && world.Attempts == 2, "Successful starts must use the registered callback.");

		var dropships = 0;
		for (var i = 0; i < 10000; i++)
		{
			var definition = Roll(rarityRoll: (i + 0.5f) / 10000f, itemRoll: 0.5f);
			if (definition.Id == GameEventKind.Dropship)
			{
				dropships++;
			}
		}
		Check(dropships == 2500, "Dropships must retain exactly 25%, without renormalizing away quiet intervals.");
		Check(Roll(rarityRoll: 0f, itemRoll: 1f).Id == GameEventKind.None, "Zero selects the common interval.");
		Check(Roll(rarityRoll: 0.74999f, itemRoll: 0f).Id == GameEventKind.None, "Common ends below 75%.");
		Check(Roll(rarityRoll: 0.75f, itemRoll: 0f).Id == GameEventKind.Dropship, "75% starts the rare interval.");
		Check(Roll(rarityRoll: 1f, itemRoll: 1f).Id == GameEventKind.Dropship, "The upper RNG endpoint is supported.");
	}

	internal static void Schedule()
	{
		foreach (var roll in new[] { 0f, 0.5f, 1f })
		{
			var firstSchedule = new GameEventSchedule();
			var firstRolls = new Queue<float>(new[] { 0.75f, 0f, roll });
			var first = firstSchedule.Advance(elapsedSeconds: 0f, random: () => firstRolls.Dequeue());
			Check(first.Count == 1 && first[0].Kind == GameEventKind.Dropship && !first[0].Occurred,
				"The opening slot must use a rarity/item draw, just like recurring slots.");
			Check(first[0].Seconds == 60f + 240f * roll, "Opening dropships must be scheduled uniformly from 60 through 300 seconds.");
			Check(firstRolls.Count == 0, "Opening slots must consume rarity, item and timestamp rolls.");
		}

		var schedule = new GameEventSchedule();
		var openingRolls = new Queue<float>(new[] { 0.74999f, 0f });
		var opening = schedule.Advance(elapsedSeconds: 0f, random: () => openingRolls.Dequeue());
		Check(opening.Count == 0
			&& openingRolls.Count == 0 && schedule.NextRollSeconds == 300f,
			"The opening must remain quiet below the 75% rarity boundary, without drawing a timestamp.");
		Check(schedule.Advance(elapsedSeconds: 0f, random: UnexpectedRoll).Count == 0, "The opening slot must never reroll.");
		Check(schedule.Advance(elapsedSeconds: 299.99f, random: UnexpectedRoll).Count == 0, "No roll before the boundary.");
		var quietRolls = new Queue<float>(new[] { 0f, 0f });
		var quiet = schedule.Advance(elapsedSeconds: 300f, random: () => quietRolls.Dequeue());
		Check(quiet.Count == 0 && quietRolls.Count == 0 && schedule.NextRollSeconds == 600f,
			"Quiet windows consume one rarity and one item roll, but no timestamp.");
		Check(schedule.Advance(elapsedSeconds: 300f, random: UnexpectedRoll).Count == 0, "A boundary must never reroll.");

		var rolls = new Queue<float>(new[] { 1f, 0f, 0f, 1f, 0f, 1f });
		var caughtUp = schedule.Advance(elapsedSeconds: 900f, random: () => rolls.Dequeue());
		Check(caughtUp.Count == 2 && rolls.Count == 0, "Frame skips must catch up every five-minute window.");
		Check(caughtUp[0].Seconds == 600f && caughtUp[1].Seconds == 1200f,
			"Each event must use its own window, including both endpoints.");
		Check(caughtUp.All(item => item.Kind == GameEventKind.Dropship && !item.Occurred),
			"Scheduling multiple ships must not depend on existing ships or mark an event as executed.");
		Check(schedule.NextRollSeconds == 1200f, "Catch-up must preserve the next exact boundary.");
		Check(schedule.Advance(elapsedSeconds: 1199f, random: UnexpectedRoll).Count == 0, "Old windows must stay consumed.");

		var quietRound = new GameEventSchedule(new[]
		{
			new GameEventSlot
			{
				RepeatIntervalSeconds = 300f,
				Rarities = new[] { new RarityBucket(id: GameRarity.Common, chance: 1f) }
			}
		});
		Check(quietRound.Advance(elapsedSeconds: 1800f, random: () => 0f).Count == 0,
			"An entire round can have no ships; there must be no special-case guaranteed arrival.");
	}

	internal static void Slots()
	{
		var firstDefaults = GameEventSchedule.DefaultSlots;
		var nextDefaults = GameEventSchedule.DefaultSlots;
		Check(!ReferenceEquals(firstDefaults, nextDefaults) && !ReferenceEquals(firstDefaults[0], nextDefaults[0]),
			"Round defaults must be rebuilt from current code, not cached static values preserved by engine hotload.");
		var common = new List<RarityBucket> { new(id: GameRarity.Common, chance: 1f) };
		var rare = new List<RarityBucket> { new(id: GameRarity.Rare, chance: 1f) };
		var slots = new List<GameEventSlot>
		{
			new()
			{
				FirstRollSeconds = 0f,
				RepeatIntervalSeconds = 100f,
				Rarities = common
			},
			new()
			{
				FirstRollSeconds = 50f,
				MinimumDelaySeconds = 20f,
				MaximumDelaySeconds = 40f,
				RepeatIntervalSeconds = 100f,
				Rarities = rare
			}
		};
		var schedule = new GameEventSchedule(slots);
		common.Clear();
		common.Add(new RarityBucket(id: GameRarity.Rare, chance: 1f));
		rare.Clear();
		slots.Clear();
		var rolls = new Queue<float>(new[] { 0f, 0f, 0f, 0f, 0.25f, 0f, 0f, 0f, 0f, 0.75f });
		var events = schedule.Advance(elapsedSeconds: 150f, random: () => rolls.Dequeue());
		Check(events.Count == 2 && events[0].Seconds == 75f && events[1].Seconds == 185f,
			"Slots must retain their own timing and rarity snapshots, independent of subsequent configuration edits.");
		Check(rolls.Count == 0 && schedule.NextRollSeconds == 200f,
			"Catch-up must interleave due slots chronologically and preserve independent cadences.");
		Check(schedule.Advance(elapsedSeconds: 150f, random: UnexpectedRoll).Count == 0, "Consumed slots must not reroll.");

		var noSlots = new GameEventSchedule(Array.Empty<GameEventSlot>());
		Check(noSlots.Advance(elapsedSeconds: 1000f, random: UnexpectedRoll).Count == 0,
			"An explicitly empty slot configuration must disable events.");

		var oneShot = new GameEventSchedule(new[]
		{
			new GameEventSlot
			{
				FirstRollSeconds = 10f,
				MinimumDelaySeconds = 2f,
				MaximumDelaySeconds = 4f,
				Rarities = new[] { new RarityBucket(id: GameRarity.Rare, chance: 1f) }
			}
		});
		Check(oneShot.Advance(elapsedSeconds: 9f, random: UnexpectedRoll).Count == 0, "Slots must honor custom start times.");
		var once = oneShot.Advance(elapsedSeconds: 10f, random: () => 0.5f);
		Check(once.Count == 1 && once[0].Seconds == 13f, "Slot delays must determine event timestamps.");
		Check(oneShot.Advance(elapsedSeconds: 1000f, random: UnexpectedRoll).Count == 0
			&& float.IsPositiveInfinity(oneShot.NextRollSeconds), "Zero repeat intervals must execute only once.");
	}

	internal static void EmptyRarities()
	{
		var rarities = new[]
		{
			new RarityBucket(id: GameRarity.Common, chance: 0.3f),
			new RarityBucket(id: GameRarity.Uncommon, chance: 0.6f),
			new RarityBucket(id: GameRarity.Rare, chance: 0.1f)
		};
		var ships = 0;
		for (var i = 0; i < 10000; i++)
		{
			var definition = Roll(
				rarityRoll: (i + 0.5f) / 10000f,
				itemRoll: 0f,
				rarities: rarities
			);
			if (definition.Id == GameEventKind.Dropship)
			{
				ships++;
			}
		}
		Check(ships == 2500, "Empty rarity weight must redistribute proportionally among populated buckets.");
		var rareSlot = new GameEventSchedule(new[]
		{
			new GameEventSlot
			{
				Rarities = new[]
				{
					new RarityBucket(id: GameRarity.Legendary, chance: 0.9f),
					new RarityBucket(id: GameRarity.Rare, chance: 0.1f)
				}
			}
		});
		var events = rareSlot.Advance(elapsedSeconds: 0f, random: () => 0f);
		Check(events.Count == 1 && events[0].Kind == GameEventKind.Dropship,
			"The scheduler must use the same empty-bucket redistribution as catalog draws.");
	}

	internal static void Invalid()
	{
		Throws<ArgumentOutOfRangeException>(() => GameEventCatalog.Get((GameEventKind)99));
		var rarities = GameEventSchedule.DefaultSlots[0].Rarities;
		Throws<ArgumentNullException>(() => GameEventCatalog.Roll(rarities: rarities, random: null));
		Throws<ArgumentNullException>(() => GameEventCatalog.Roll(rarities: null, random: () => 0f));
		Throws<ArgumentException>(() => GameEventCatalog.Roll(rarities: Array.Empty<RarityBucket>(), random: () => 0f));
		foreach (var buckets in new[]
		{
			new RarityBucket[] { null },
			new[] { new RarityBucket(id: GameRarity.Common, chance: 0f) },
			new[] { new RarityBucket(id: GameRarity.Uncommon, chance: 1f) },
			new[]
			{
				new RarityBucket(id: GameRarity.Common, chance: 0.5f),
				new RarityBucket(id: GameRarity.Common, chance: 0.5f)
			}
		})
		{
			Throws<ArgumentException>(() => GameEventCatalog.Roll(rarities: buckets, random: UnexpectedRoll));
			Throws<ArgumentException>(() => new GameEventSchedule(new[] { new GameEventSlot { Rarities = buckets } }));
		}
		foreach (var value in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Throws<ArgumentOutOfRangeException>(() => Roll(rarityRoll: value, itemRoll: 0f));
			Throws<ArgumentOutOfRangeException>(() => Roll(rarityRoll: 0f, itemRoll: value));
			var schedule = new GameEventSchedule();
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: 0f, random: () => value));
			var timeRolls = new Queue<float>(new[] { 1f, 0f, value });
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: 0f, random: () => timeRolls.Dequeue()));
		}

		foreach (var elapsed in new[] { -1f, float.NaN, float.PositiveInfinity })
		{
			var schedule = new GameEventSchedule();
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: elapsed, random: UnexpectedRoll));
		}
		var missingRandom = new GameEventSchedule();
		Throws<ArgumentNullException>(() => missingRandom.Advance(elapsedSeconds: 0f, random: null));
		foreach (var slot in new[]
		{
			null,
			new GameEventSlot { FirstRollSeconds = -1f, Rarities = rarities },
			new GameEventSlot { FirstRollSeconds = float.NaN, Rarities = rarities },
			new GameEventSlot { MinimumDelaySeconds = -1f, Rarities = rarities },
			new GameEventSlot { MinimumDelaySeconds = 10f, MaximumDelaySeconds = 5f, Rarities = rarities },
			new GameEventSlot { MaximumDelaySeconds = float.PositiveInfinity, Rarities = rarities },
			new GameEventSlot { RepeatIntervalSeconds = -1f, Rarities = rarities },
			new GameEventSlot { RepeatIntervalSeconds = float.NaN, Rarities = rarities },
			new GameEventSlot { Rarities = null }
		})
		{
			Throws<ArgumentException>(() => new GameEventSchedule(new[] { slot }));
		}
	}

	private static GameEventDefinition Roll(
		float rarityRoll,
		float itemRoll,
		IReadOnlyList<RarityBucket> rarities = null
	)
	{
		var rolls = new Queue<float>(new[] { rarityRoll, itemRoll });
		var buckets = rarities ?? GameEventSchedule.DefaultSlots[1].Rarities;
		var definition = GameEventCatalog.Roll(rarities: buckets, random: () => rolls.Dequeue());
		Check(rolls.Count == 0, "Each catalog draw must consume exactly two independent rolls.");
		return definition;
	}

	private static float UnexpectedRoll()
	{
		throw new Exception("No random roll was expected.");
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

	private sealed class TestWorld : IGameEventWorld
	{
		public int Attempts { get; private set; }
		public bool Success { get; set; }

		public bool TryStartEvent(GameEventKind kind)
		{
			Check(kind == GameEventKind.Dropship, "The callback must dispatch the selected event kind.");
			Attempts++;
			return Success;
		}
	}
}
