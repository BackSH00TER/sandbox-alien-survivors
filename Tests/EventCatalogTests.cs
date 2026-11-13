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
		foreach (var kind in new[] { GameEventKind.Dropship, GameEventKind.WeaponAirdrop, GameEventKind.Boss })
		{
			var definition = GameEventCatalog.Get(kind);
			var attempts = world.Attempts;
			world.Success = false;
			Check(!definition.TryStart(world) && world.Attempts == attempts + 1, "Failed starts must propagate for retries.");
			Check(world.LastKind == kind, "Callbacks must dispatch the selected event kind.");
			world.Success = true;
			Check(definition.TryStart(world) && world.Attempts == attempts + 2, "Successful starts must use the registered callback.");
		}

		var counts = expectedIds.ToDictionary(kind => kind, _ => 0);
		for (var rarity = 0; rarity < 100; rarity++)
		{
			for (var item = 0; item < 100; item++)
			{
				var definition = Roll(rarityRoll: (rarity + 0.5f) / 100f, itemRoll: (item + 0.5f) / 100f);
				counts[definition.Id]++;
			}
		}
		Check(counts[GameEventKind.None] == 7500, "Quiet intervals must retain the full 75% common weight.");
		Check(counts[GameEventKind.Dropship] == 1250 && counts[GameEventKind.WeaponAirdrop] == 1250,
			"Both rare events must share the 25% rare weight equally.");
		Check(Roll(rarityRoll: 0f, itemRoll: 1f).Id == GameEventKind.None, "Zero selects the common interval.");
		Check(Roll(rarityRoll: 0.74999f, itemRoll: 0f).Id == GameEventKind.None, "Common ends below 75%.");
		Check(Roll(rarityRoll: 0.75f, itemRoll: 0f).Id == GameEventKind.Dropship, "75% starts the rare interval.");
		Check(Roll(rarityRoll: 0.75f, itemRoll: 0.49999f).Id == GameEventKind.Dropship, "The first half of Rare selects a dropship.");
		Check(Roll(rarityRoll: 0.75f, itemRoll: 0.5f).Id == GameEventKind.WeaponAirdrop, "The second half of Rare selects a weapon airdrop.");
		Check(Roll(rarityRoll: 1f, itemRoll: 1f).Id == GameEventKind.WeaponAirdrop, "The upper RNG endpoint is supported.");
		Check(counts[GameEventKind.Boss] == 0 && !GameEventCatalog.Get(GameEventKind.Boss).RandomEligible,
			"The fixed boss must never dilute random event probabilities.");
		Check(GameEventCatalog.Get(GameEventKind.Boss).ArrivalMoment == "Boss spawned",
			"The shared arrival path must preserve the boss's round moment.");

		var allRarities = Enum.GetValues<GameRarity>()
			.Select(rarity => new RarityBucket(id: rarity, chance: 1f)).ToArray();
		foreach (var rarityRoll in new[] { 0f, 0.5f, 1f })
		{
			foreach (var itemRoll in new[] { 0f, 0.5f, 1f })
			{
				var definition = Roll(
					rarityRoll: rarityRoll,
					itemRoll: itemRoll,
					rarities: allRarities
				);
				Check(definition.Id != GameEventKind.Boss,
					"Even enabling every rarity must never make the fixed-only boss randomly eligible.");
			}
		}
	}

	internal static void WeaponAirdrops()
	{
		foreach (var timeRoll in new[] { 0f, 0.5f, 1f })
		{
			var schedule = RandomSchedule();
			var openingRolls = new Queue<float>(new[] { 1f, 0.5f, timeRoll });
			var opening = schedule.Advance(elapsedSeconds: 0f, random: () => openingRolls.Dequeue());
			Check(opening.Count == 1 && opening[0].Kind == GameEventKind.WeaponAirdrop && !opening[0].Occurred,
				"Weapon airdrops must be scheduled by the shared opening slot, not a separate guarantee.");
			Check(opening[0].Seconds == 60f + 240f * timeRoll && openingRolls.Count == 0,
				"Weapon airdrops must use the shared opening delay and consume exactly three rolls.");
			Check(schedule.Advance(elapsedSeconds: 60f, random: UnexpectedRoll).Count == 0,
				"One minute must not inject an extra guaranteed weapon event.");

			var recurringRolls = new Queue<float>(new[] { 1f, 1f, timeRoll });
			var recurring = schedule.Advance(elapsedSeconds: 300f, random: () => recurringRolls.Dequeue());
			Check(recurring.Count == 1 && recurring[0].Kind == GameEventKind.WeaponAirdrop && !recurring[0].Occurred,
				"Recurring slots must be able to select weapon airdrops.");
			Check(recurring[0].Seconds == 300f + 300f * timeRoll && recurringRolls.Count == 0,
				"Weapon airdrops must use recurring slot timing rather than fixed arrival times.");
			Check(schedule.Advance(elapsedSeconds: 300f, random: UnexpectedRoll).Count == 0,
				"Consumed weapon slots must not roll twice.");
		}
	}

	internal static void Schedule()
	{
		foreach (var roll in new[] { 0f, 0.5f, 1f })
		{
			var firstSchedule = RandomSchedule();
			var firstRolls = new Queue<float>(new[] { 0.75f, 0f, roll });
			var first = firstSchedule.Advance(elapsedSeconds: 0f, random: () => firstRolls.Dequeue());
			Check(first.Count == 1 && first[0].Kind == GameEventKind.Dropship && !first[0].Occurred,
				"The opening slot must use a rarity/item draw, just like recurring slots.");
			Check(first[0].Seconds == 60f + 240f * roll, "Opening dropships must be scheduled uniformly from 60 through 300 seconds.");
			Check(firstRolls.Count == 0, "Opening slots must consume rarity, item and timestamp rolls.");
		}

		var schedule = RandomSchedule();
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

		var quietRound = RandomSchedule(new[]
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
		var schedule = RandomSchedule(slots);
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

		var noSlots = RandomSchedule(Array.Empty<GameEventSlot>());
		Check(noSlots.Advance(elapsedSeconds: 1000f, random: UnexpectedRoll).Count == 0,
			"An explicitly empty slot configuration must disable events.");

		var oneShot = RandomSchedule(new[]
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
		var rareSlot = RandomSchedule(new[]
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
			Throws<ArgumentException>(() => RandomSchedule(new[] { new GameEventSlot { Rarities = buckets } }));
		}
		foreach (var value in new[] { -0.1f, 1.1f, float.NaN, float.PositiveInfinity })
		{
			Throws<ArgumentOutOfRangeException>(() => Roll(rarityRoll: value, itemRoll: 0f));
			Throws<ArgumentOutOfRangeException>(() => Roll(rarityRoll: 0f, itemRoll: value));
			var schedule = RandomSchedule();
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: 0f, random: () => value));
			var timeRolls = new Queue<float>(new[] { 1f, 0f, value });
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: 0f, random: () => timeRolls.Dequeue()));
		}

		foreach (var elapsed in new[] { -1f, float.NaN, float.PositiveInfinity })
		{
			var schedule = RandomSchedule();
			Throws<ArgumentOutOfRangeException>(() => schedule.Advance(elapsedSeconds: elapsed, random: UnexpectedRoll));
		}
		var missingRandom = RandomSchedule();
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
			Throws<ArgumentException>(() => RandomSchedule(new[] { slot }));
		}
	}

	internal static void FixedEvents()
	{
		var defaults = GameEventSchedule.DefaultFixedEvents;
		var nextDefaults = GameEventSchedule.DefaultFixedEvents;
		Check(!ReferenceEquals(defaults, nextDefaults) && !ReferenceEquals(defaults[0], nextDefaults[0]),
			"Fixed defaults must also rebuild current tuning for each new round.");
		Check(defaults.Count == 1 && defaults[0].Kind == GameEventKind.Boss && defaults[0].StartAtSeconds == 300f,
			"The match must guarantee exactly one boss at five minutes.");
		Check(defaults[0].PaddingBeforeSeconds == 30f && defaults[0].PaddingAfterSeconds == 30f,
			"The default boss reserves thirty seconds on either side.");

		foreach (var roll in new[] { 0f, 0.75f, 1f })
		{
			var schedule = new GameEventSchedule();
			var opening = schedule.Advance(elapsedSeconds: 0f, random: () => roll);
			Check(opening.Count(item => item.Kind == GameEventKind.Boss) == 1,
				"The boss must appear in the opening timeline regardless of the random draw.");
			var boss = opening.Single(item => item.Kind == GameEventKind.Boss);
			Check(boss.Seconds == 300f && boss.IsFixed && !boss.Occurred,
				"Fixed scheduling must not mark the boss as already spawned.");
			var later = schedule.Advance(elapsedSeconds: 1800f, random: () => roll);
			Check(later.All(item => item.Kind != GameEventKind.Boss),
				"Catch-up and recurring slots must never inject another boss.");
		}

		var fixedOnly = new GameEventSchedule(slots: Array.Empty<GameEventSlot>());
		var initial = fixedOnly.Advance(elapsedSeconds: 0f, random: UnexpectedRoll);
		Check(initial.Count == 1 && initial[0].Kind == GameEventKind.Boss && initial[0].Seconds == 300f,
			"The boss's one fixed occurrence must be planned at round start without RNG.");
		foreach (var elapsed in new[] { 299f, 300f, 301f, 600f })
		{
			Check(fixedOnly.Advance(elapsedSeconds: elapsed, random: UnexpectedRoll).Count == 0,
				"Crossing the boss deadline must not schedule duplicate occurrences.");
		}

		var lateSchedule = new GameEventSchedule(slots: Array.Empty<GameEventSlot>());
		var caughtUp = lateSchedule.Advance(elapsedSeconds: 500f, random: UnexpectedRoll);
		Check(caughtUp.Count == 1 && caughtUp[0].Seconds == 300f && caughtUp[0].IsFixed,
			"A first advance after the deadline must retain the fixed event and original timestamp without RNG.");
		Check(lateSchedule.Advance(elapsedSeconds: 600f, random: UnexpectedRoll).Count == 0,
			"Fixed events are emitted only once, even without random slots.");
	}

	internal static void Reservations()
	{
		var reservations = new List<GameFixedEvent>
		{
			new()
			{
				Kind = GameEventKind.Boss,
				StartAtSeconds = 300f,
				PaddingBeforeSeconds = 30f,
				PaddingAfterSeconds = 30f
			}
		};
		foreach (var seconds in new[] { 269.99f, 270f, 300f, 330f, 330.01f })
		{
			var schedule = new GameEventSchedule(
				slots: new[]
				{
					new GameEventSlot
					{
						MinimumDelaySeconds = seconds,
						MaximumDelaySeconds = seconds,
						Rarities = new[] { new RarityBucket(id: GameRarity.Rare, chance: 1f) }
					}
				},
				fixedEvents: reservations
			);
			var rolls = new Queue<float>(new[] { 0f, 0f, 0f });
			var events = schedule.Advance(elapsedSeconds: 0f, random: () => rolls.Dequeue());
			var blocked = seconds >= 270f && seconds <= 330f;
			Check(events.Count == (blocked ? 1 : 2) && events.Count(item => item.IsFixed) == 1,
				"Random timestamps in the inclusive reservation must be skipped, never the guaranteed event.");
			Check(rolls.Count == 0, "A collision consumes its original draw without rerolling.");
			Check(schedule.IsReserved(seconds) == blocked,
				"Runtime start checks must use the same lock boundaries as scheduling.");
			Check(schedule.Advance(elapsedSeconds: 1000f, random: UnexpectedRoll).Count == 0,
				"A blocked one-shot slot must stay consumed.");
		}

		var lockedEvent = reservations[0];
		var snapshot = new GameEventSchedule(
			slots: Array.Empty<GameEventSlot>(),
			fixedEvents: reservations
		);
		reservations.Clear();
		Check(snapshot.IsReserved(270f) && snapshot.IsReserved(330f),
			"Mutating the caller's fixed-event list must not remove reservations.");
		Check(!snapshot.IsReserved(269f) && !snapshot.IsReserved(331f),
			"Only start times inside the reservation are locked, not existing event lifetimes.");

		var pointLock = new GameEventSchedule(
			slots: Array.Empty<GameEventSlot>(),
			fixedEvents: new[] { new GameFixedEvent { Kind = GameEventKind.Dropship, StartAtSeconds = 10f } }
		);
		Check(pointLock.IsReserved(10f) && !pointLock.IsReserved(9.99f) && !pointLock.IsReserved(10.01f),
			"Zero padding must reserve only the exact timestamp, for any executable event kind.");

		var recurring = new GameEventSchedule(
			slots: new[]
			{
				new GameEventSlot
				{
					FirstRollSeconds = 270f,
					RepeatIntervalSeconds = 30f,
					Rarities = new[] { new RarityBucket(id: GameRarity.Rare, chance: 1f) }
				}
			},
			fixedEvents: new[] { lockedEvent }
		);
		var catchUp = recurring.Advance(elapsedSeconds: 360f, random: () => 0f);
		Check(catchUp.Count == 2 && catchUp[1].Seconds == 360f && recurring.NextRollSeconds == 390f,
			"Catch-up must skip every locked window and preserve the recurring cadence after the lock.");
	}

	internal static void InvalidFixedEvents()
	{
		foreach (var fixedEvent in new[]
		{
			null,
			new GameFixedEvent { Kind = GameEventKind.None },
			new GameFixedEvent { Kind = (GameEventKind)99 },
			new GameFixedEvent { Kind = GameEventKind.Boss, StartAtSeconds = -1f },
			new GameFixedEvent { Kind = GameEventKind.Boss, StartAtSeconds = float.NaN },
			new GameFixedEvent { Kind = GameEventKind.Boss, StartAtSeconds = float.PositiveInfinity },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingBeforeSeconds = -1f },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingBeforeSeconds = float.NaN },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingBeforeSeconds = float.PositiveInfinity },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingAfterSeconds = -1f },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingAfterSeconds = float.NaN },
			new GameFixedEvent { Kind = GameEventKind.Boss, PaddingAfterSeconds = float.PositiveInfinity },
			new GameFixedEvent { Kind = GameEventKind.Boss, StartAtSeconds = float.MaxValue, PaddingAfterSeconds = float.MaxValue }
		})
		{
			Throws<ArgumentException>(() => new GameEventSchedule(fixedEvents: new[] { fixedEvent }));
		}

		foreach (var seconds in new[] { 270f, 300f, 330f })
		{
			Throws<ArgumentException>(() => new GameEventSchedule(fixedEvents: new[]
			{
				new GameFixedEvent
				{
					Kind = GameEventKind.Boss,
					StartAtSeconds = 300f,
					PaddingBeforeSeconds = 30f,
					PaddingAfterSeconds = 30f
				},
				new GameFixedEvent { Kind = GameEventKind.Dropship, StartAtSeconds = seconds }
			}));
		}

		var schedule = new GameEventSchedule();
		foreach (var seconds in new[] { -1f, float.NaN, float.PositiveInfinity })
		{
			Throws<ArgumentOutOfRangeException>(() => schedule.IsReserved(seconds));
		}
		Throws<ArgumentException>(() => GameEventCatalog.Roll(
			rarities: new[] { new RarityBucket(id: GameRarity.Legendary, chance: 1f) },
			random: UnexpectedRoll
		));
	}

	private static GameEventSchedule RandomSchedule(IReadOnlyList<GameEventSlot> slots = null)
	{
		return new GameEventSchedule(slots: slots, fixedEvents: Array.Empty<GameFixedEvent>());
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
		public GameEventKind LastKind { get; private set; }

		public bool TryStartEvent(GameEventKind kind)
		{
			Check(kind is GameEventKind.Dropship or GameEventKind.WeaponAirdrop or GameEventKind.Boss,
				"Only executable event kinds may reach the world dispatcher.");
			LastKind = kind;
			Attempts++;
			return Success;
		}
	}
}
