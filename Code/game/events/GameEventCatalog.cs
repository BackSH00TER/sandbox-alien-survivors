using System;
using System.Collections.Generic;
using System.Linq;

public enum GameEventKind
{
	// Quiet is an explicit outcome, not a missing event or an empty rarity.
	None = 0,
	Dropship = 1,
	WeaponAirdrop = 2,
	Boss = 3,
	ThickFog = 4
}

public interface IGameEventWorld
{
	bool TryStartEvent(GameEventKind kind);
}

public sealed class GameEventDefinition
{
	public GameEventKind Id { get; init; }
	public string Label { get; init; }
	public string Icon { get; init; }
	public GameRarity Rarity { get; init; }
	public bool RandomEligible { get; init; } = true;
	public string ArrivalMoment { get; init; }
	public Func<IGameEventWorld, bool> TryStart { get; init; }
}

public readonly record struct GameScheduledEvent(
	GameEventKind Kind,
	float Seconds,
	bool Occurred = false,
	bool IsFixed = false
);

/// <summary>World-event definitions. Timing and rarity weights belong to schedule slots, not event types.</summary>
public static class GameEventCatalog
{
	// Hotload must rebuild definitions and both indexes together when event types change.
	[field: Sandbox.SkipHotload] public static IReadOnlyList<GameEventDefinition> AllEvents { get; } = Array.AsReadOnly(new[]
	{
		new GameEventDefinition
		{
			Id = GameEventKind.None,
			Label = "Quiet interval",
			Icon = "hourglass_empty",
			Rarity = GameRarity.Common,
			TryStart = _ => true
		},
		new GameEventDefinition
		{
			Id = GameEventKind.Dropship,
			Label = "Alien dropship",
			Icon = "flight_land",
			Rarity = GameRarity.Rare,
			TryStart = world => world.TryStartEvent(GameEventKind.Dropship)
		},
		new GameEventDefinition
		{
			Id = GameEventKind.WeaponAirdrop,
			Label = "Weapon airdrop",
			Icon = "flight",
			Rarity = GameRarity.Rare,
			TryStart = world => world.TryStartEvent(GameEventKind.WeaponAirdrop)
		},
		new GameEventDefinition
		{
			Id = GameEventKind.Boss,
			Label = "Boss spawn",
			Icon = "star",
			Rarity = GameRarity.Legendary,
			RandomEligible = false,
			ArrivalMoment = "Boss spawned",
			TryStart = world => world.TryStartEvent(GameEventKind.Boss)
		},
		new GameEventDefinition
		{
			Id = GameEventKind.ThickFog,
			Label = "Thick fog",
			Icon = "foggy",
			Rarity = GameRarity.Common,
			ArrivalMoment = "Thick fog rolled in",
			TryStart = world => world.TryStartEvent(GameEventKind.ThickFog)
		}
	});

	[Sandbox.SkipHotload] private static readonly IReadOnlyDictionary<GameEventKind, GameEventDefinition> ById = AllEvents.ToDictionary(item => item.Id);
	[Sandbox.SkipHotload]
	private static readonly IReadOnlyDictionary<GameRarity, GameEventDefinition[]> RarityEvents = AllEvents
		.Where(item => item.RandomEligible)
		.GroupBy(item => item.Rarity)
		.ToDictionary(
			keySelector: group => group.Key,
			elementSelector: group => group.ToArray()
		);

	static GameEventCatalog()
	{
		foreach (var item in AllEvents)
		{
			if (!Enum.IsDefined(item.Id) || string.IsNullOrWhiteSpace(item.Label)
				|| string.IsNullOrWhiteSpace(item.Icon) || item.TryStart == null
				|| !Enum.IsDefined(item.Rarity))
			{
				throw new InvalidOperationException($"Event '{item.Id}' needs valid metadata, rarity and a start callback.");
			}
		}
	}

	public static GameEventDefinition Get(GameEventKind kind)
	{
		if (!ById.TryGetValue(kind, out var definition))
		{
			throw new ArgumentOutOfRangeException(nameof(kind), "Event kind must have a catalog definition.");
		}

		return definition;
	}

	public static GameEventDefinition Roll(IReadOnlyList<RarityBucket> rarities, Func<float> random)
	{
		ArgumentNullException.ThrowIfNull(random);
		var eligible = EligibleRarities(rarities);
		return RarityDraw.Pick(
			buckets: eligible,
			getCandidates: rarity => RarityEvents[rarity],
			random: random
		);
	}

	internal static RarityBucket[] EligibleRarities(IReadOnlyList<RarityBucket> rarities)
	{
		ArgumentNullException.ThrowIfNull(rarities);
		if (rarities.Any(bucket => bucket == null)
			|| rarities.Select(bucket => bucket.Id).Distinct().Count() != rarities.Count)
		{
			throw new ArgumentException("Event slots require non-null, distinct rarity buckets.", nameof(rarities));
		}

		// Removing unpopulated buckets redistributes their weight without rerolling.
		var eligible = rarities.Where(bucket => bucket.Chance > 0f && RarityEvents.ContainsKey(bucket.Id)).ToArray();
		if (eligible.Length == 0)
		{
			throw new ArgumentException("Event slots need a positive weight for at least one populated rarity.", nameof(rarities));
		}

		return eligible;
	}
}

public sealed class GameEventSlot
{
	public float FirstRollSeconds { get; init; }
	public float MinimumDelaySeconds { get; init; }
	public float MaximumDelaySeconds { get; init; }
	/// <summary>Zero makes this a one-shot slot.</summary>
	public float RepeatIntervalSeconds { get; init; }
	public IReadOnlyList<RarityBucket> Rarities { get; init; }
}

/// <summary>A guaranteed one-shot arrival with an inclusive reservation against random starts.</summary>
public sealed class GameFixedEvent
{
	public GameEventKind Kind { get; init; }
	/// <summary>Total elapsed round seconds at which this event is due to start.</summary>
	public float StartAtSeconds { get; init; }
	public float PaddingBeforeSeconds { get; init; }
	public float PaddingAfterSeconds { get; init; }

	public bool Contains(float seconds)
	{
		return seconds >= StartAtSeconds - PaddingBeforeSeconds && seconds <= StartAtSeconds + PaddingAfterSeconds;
	}
}

/// <summary>Host-clock scheduling, independent of frame rate and of whether earlier ships are still alive.</summary>
public sealed class GameEventSchedule
{
	public const float IntervalSeconds = 300f;
	public static IReadOnlyList<GameFixedEvent> DefaultFixedEvents
	{
		get
		{
			return Array.AsReadOnly(new[]
			{
				new GameFixedEvent
				{
					Kind = GameEventKind.Boss,
					StartAtSeconds = 300f,
					PaddingBeforeSeconds = 30f,
					PaddingAfterSeconds = 30f
				},
				// Temporary fog playtest: remove this entry to restore random-only weather.
				new GameFixedEvent
				{
					Kind = GameEventKind.ThickFog,
					StartAtSeconds = 30f
				}
			});
		}
	}

	// Hotload preserves cached static values, so build configuration from current code for each round.
	public static IReadOnlyList<GameEventSlot> DefaultSlots
	{
		get
		{
			return Array.AsReadOnly(new[]
			{
				new GameEventSlot
				{
					FirstRollSeconds = 0f,
					MinimumDelaySeconds = 60f,
					MaximumDelaySeconds = 300f,
					Rarities = Array.AsReadOnly(new[]
					{
						new RarityBucket(id: GameRarity.Common, chance: 0.75f),
						new RarityBucket(id: GameRarity.Rare, chance: 0.25f)
					})
				},
				new GameEventSlot
				{
					FirstRollSeconds = IntervalSeconds,
					MaximumDelaySeconds = IntervalSeconds,
					RepeatIntervalSeconds = IntervalSeconds,
					Rarities = Array.AsReadOnly(new[]
					{
						new RarityBucket(id: GameRarity.Common, chance: 0.75f),
						new RarityBucket(id: GameRarity.Rare, chance: 0.25f)
					})
				}
			});
		}
	}

	private sealed class SlotState
	{
		public GameEventSlot Definition { get; init; }
		public RarityBucket[] Rarities { get; init; }
		public float NextRollSeconds { get; set; }
	}

	private readonly List<SlotState> _slots = new();
	private readonly List<GameFixedEvent> _fixedEvents = new();
	private bool _fixedEventsScheduled;
	public float NextRollSeconds => _slots.Count == 0 ? float.PositiveInfinity : _slots.Min(slot => slot.NextRollSeconds);

	public GameEventSchedule(
		IReadOnlyList<GameEventSlot> slots = null,
		IReadOnlyList<GameFixedEvent> fixedEvents = null
	)
	{
		foreach (var fixedEvent in fixedEvents ?? DefaultFixedEvents)
		{
			if (fixedEvent == null || !Enum.IsDefined(fixedEvent.Kind) || fixedEvent.Kind == GameEventKind.None
				|| !float.IsFinite(fixedEvent.StartAtSeconds) || fixedEvent.StartAtSeconds < 0f
				|| !float.IsFinite(fixedEvent.PaddingBeforeSeconds) || fixedEvent.PaddingBeforeSeconds < 0f
				|| !float.IsFinite(fixedEvent.PaddingAfterSeconds) || fixedEvent.PaddingAfterSeconds < 0f
				|| !float.IsFinite(fixedEvent.StartAtSeconds + fixedEvent.PaddingAfterSeconds))
			{
				throw new ArgumentException("Fixed events require an executable kind and finite non-negative times and padding.", nameof(fixedEvents));
			}

			var start = fixedEvent.StartAtSeconds - fixedEvent.PaddingBeforeSeconds;
			var end = fixedEvent.StartAtSeconds + fixedEvent.PaddingAfterSeconds;
			if (_fixedEvents.Any(existing => start <= existing.StartAtSeconds + existing.PaddingAfterSeconds
				&& end >= existing.StartAtSeconds - existing.PaddingBeforeSeconds))
			{
				throw new ArgumentException("Fixed event reservations must not overlap, including their boundaries.", nameof(fixedEvents));
			}

			_fixedEvents.Add(fixedEvent);
		}

		foreach (var slot in slots ?? DefaultSlots)
		{
			if (slot == null || !float.IsFinite(slot.FirstRollSeconds) || slot.FirstRollSeconds < 0f
				|| !float.IsFinite(slot.MinimumDelaySeconds) || slot.MinimumDelaySeconds < 0f
				|| !float.IsFinite(slot.MaximumDelaySeconds) || slot.MaximumDelaySeconds < slot.MinimumDelaySeconds
				|| !float.IsFinite(slot.RepeatIntervalSeconds) || slot.RepeatIntervalSeconds < 0f
				|| !float.IsFinite(slot.FirstRollSeconds + slot.MaximumDelaySeconds)
				|| (slot.RepeatIntervalSeconds > 0f && slot.FirstRollSeconds + slot.RepeatIntervalSeconds <= slot.FirstRollSeconds))
			{
				throw new ArgumentException("Event slots require finite non-negative times, maximum delay >= minimum delay, and a repeat interval that advances the clock.", nameof(slots));
			}

			// Active rounds retain a snapshot even when callers edit their configuration lists.
			var rarities = GameEventCatalog.EligibleRarities(slot.Rarities);
			_slots.Add(new SlotState
			{
				Definition = slot,
				Rarities = rarities,
				NextRollSeconds = slot.FirstRollSeconds
			});
		}
	}

	public IReadOnlyList<GameScheduledEvent> Advance(float elapsedSeconds, Func<float> random)
	{
		if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
		}
		ArgumentNullException.ThrowIfNull(random);

		var scheduled = new List<GameScheduledEvent>();
		if (!_fixedEventsScheduled)
		{
			foreach (var fixedEvent in _fixedEvents)
			{
				scheduled.Add(new GameScheduledEvent(
					Kind: fixedEvent.Kind,
					Seconds: fixedEvent.StartAtSeconds,
					Occurred: false,
					IsFixed: true
				));
			}
			_fixedEventsScheduled = true;
		}

		if (elapsedSeconds < NextRollSeconds)
		{
			return scheduled;
		}

		while (true)
		{
			// Interleave overdue slots by due time; configuration order breaks ties.
			SlotState next = null;
			foreach (var slot in _slots)
			{
				if (slot.NextRollSeconds <= elapsedSeconds
					&& (next == null || slot.NextRollSeconds < next.NextRollSeconds))
				{
					next = slot;
				}
			}
			if (next == null)
			{
				break;
			}

			var definition = GameEventCatalog.Roll(rarities: next.Rarities, random: random);
			if (definition.Id != GameEventKind.None)
			{
				var seconds = RollTime(
					start: next.NextRollSeconds + next.Definition.MinimumDelaySeconds,
					duration: next.Definition.MaximumDelaySeconds - next.Definition.MinimumDelaySeconds,
					random: random
				);
				if (!IsReserved(seconds))
				{
					scheduled.Add(new GameScheduledEvent(definition.Id, seconds));
				}
			}

			// Quiet outcomes also consume the slot, but never consume a timestamp roll.
			next.NextRollSeconds = next.Definition.RepeatIntervalSeconds > 0f
				? next.NextRollSeconds + next.Definition.RepeatIntervalSeconds
				: float.PositiveInfinity;
		}

		return scheduled;
	}

	public bool IsReserved(float seconds)
	{
		if (!float.IsFinite(seconds) || seconds < 0f)
		{
			throw new ArgumentOutOfRangeException(nameof(seconds));
		}

		return _fixedEvents.Any(fixedEvent => fixedEvent.Contains(seconds));
	}

	private static float RollTime(
		float start,
		float duration,
		Func<float> random
	)
	{
		var roll = random();
		if (!float.IsFinite(roll) || roll < 0f || roll > 1f)
		{
			throw new ArgumentOutOfRangeException(nameof(random), "Event time rolls must be between zero and one.");
		}

		return start + duration * roll;
	}
}
