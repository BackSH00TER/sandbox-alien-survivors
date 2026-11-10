# Game timeline

The HUD shows a 780-pixel-wide rolling two-minute window: 18 seconds of history
to the left and 102 seconds of upcoming events to the right. **NOW** stays 15%
from the left edge, reserving most of the timeline for the future;
event icons move left as the host-synchronized round clock advances. Events
dim once their timestamp has passed and leave the view after another 18 seconds.
Time labels show total elapsed minutes and seconds, without a fixed endpoint.

Events beyond the right edge appear as smaller pipeline icons. Their tiny
`min` labels round up the time until they enter the visible window, **not**
the time until they occur. For example, a boss due at 5:00 starts in the
pipeline with `4 min` and enters the view at 3:18.

`GameManager.TimelineEvents` combines the existing boss spawn with replicated
world-event schedules. The boss uses `CountdownTime` and then its actual spawn
time once spawning succeeds. World events use their scheduled time until they
successfully spawn, then their actual arrival time. Failed spawns retry once
per second and do not record a successful occurrence.

## World events and alien dropships

`GameEventSchedule` controls event frequency through `GameEventSlot` definitions;
there is no ship-specific guarantee in the scheduler. `GameEventCatalog` only defines event types,
their rarities, icons, labels, and start callbacks, like the loot/modifier catalogs.
The framework lives in `Code/game/events/`. `GameManager` dispatches catalog
callbacks through the common `IGameEventAction` interface; event implementations
are registered by `GameEventActions`, not hard-coded into the manager.
`GameDropshipEvent` owns the dropship configuration and spawn logic, while
`GameDropship` handles the ship's lifecycle. `GameEventCatalog.AllEvents` exposes
the catalog's definitions.
Every slot draws a rarity using the shared `RarityDraw`, then an equally likely
event in that rarity. Slots have independent timing and rarity weights, so an
early slot can use different rarities from a later or recurring slot.

The current configuration lives in `GameEventSchedule.DefaultSlots`:

| Slot | First roll | Event delay after roll | Repeat interval | Rarity weights |
| --- | --- | --- | --- | --- |
| Opening | Round start | 60–300 seconds | None | Common 75%, Rare 25% |
| Recurring | 300 seconds | 0–300 seconds | 300 seconds | Common 75%, Rare 25% |

Common currently contains an explicit quiet interval (no event); Rare contains
the dropship. Both opening and recurring slots have a 25% dropship chance;
neither guarantees a ship. Quiet is a real Common catalog choice, not an empty
rarity.
Rarities without event definitions are excluded and their weight is redistributed
proportionally among populated, enabled rarities. This does not retry random rolls.
A configuration with no populated positive-weight rarity is rejected explicitly.
New event types can populate those rarities without changing the scheduler.

Configure each slot's `FirstRollSeconds`, `MinimumDelaySeconds`,
`MaximumDelaySeconds`, `RepeatIntervalSeconds`, and `Rarities` in `DefaultSlots`,
or supply a custom slot list to the scheduler constructor. Zero repeat interval
makes a slot one-shot; an empty slot list disables events. The scheduler snapshots
configuration so later list mutations do not alter an active round.
Default definitions are rebuilt from current code and the host creates a new
scheduler at round start. This avoids s&box hotload preserving obsolete static
slot settings or editor-created scheduler state. Start a new round after
changing slot tuning; an already-running round retains its snapshot.

A selected event is scheduled uniformly within its slot's delay window and
immediately appears on the timeline/pipeline. Multiple ships may remain alive.
Frame skips process missed slots chronologically without rerolling consumed
windows. All clients share the host's schedule; clients never roll their own.

Dropships descend for three seconds onto clear, flat ground, then become
shootable. A landed ship deploys one enemy every three seconds from a nearby
ground-level exit, in addition to normal spawns. It persists until destroyed.
Alive players get a green arrow and health/distance label for the nearest ship,
pinned to the edge when off-screen. Dead players retain the red revive-flare
arrow instead. Destruction stops reinforcements, removes the arrow (or retargets
another ship), records a world moment, and drops **100 XP total**, **one health
kit**, and **one armor kit**. XP is spread across at most four larger orbs
(four 25-XP orbs by default), using the shared value-based colors, scaling,
and merging behavior from `GameExperienceOrb`. XP is not awarded to every player
separately. Kits use the existing loot catalog effects.
No reward is granted for cleanup or round end.

The `GameDropshipEvent` component configures health (default 500), deployment
interval, landing radius, and XP (0–1000). The runtime action registry adds it
to the manager object when a round starts, or it can be added beforehand in
the editor to customize settings. Its typed `LootDrops` list is configured in
code and accepts any `LootKind`, with inclusive minimum/maximum counts from
0–20 per entry. Equal bounds give a fixed count; zero bounds disable that drop.
For example, `new GameLootDrop(LootKind.Armor, 1, 3)` drops one to three armor
pickups. The ship snapshots the table on arrival, and rolls each entry once on
destruction. Adding loot never requires a new per-item property or spawn branch.
An optional enemy prefab
supports future heavy reinforcements; unset uses current randomized enemy
variants. Custom prefabs retain their authored variants. No heavy enemy type
is introduced yet. Ship visuals are an assembled prototype using existing
development models.

Objective UI lives in `Code/game/markers/`. `GameDropshipMarker` and
`GameReviveFlareMarker` select targets and supply presentation data to the shared
`GameObjectiveMarker`. The shared component only projects a world position,
clamps the arrow to screen edges, and renders the supplied label, icon, and
color; it contains no target-type branches. New marker types can reuse it without
changing existing markers.

Arrival, landing, and destruction are recorded as actual round-results moments.
Scheduled events after round end do not execute. There are no recurring bosses.
Defeating the boss still ends the match; the clock freezes at round end.
With the current default boss at 1:00, some dropships may not arrive before a
quick boss victory; increase `CountdownTime` in the inspector for longer rounds.
`ShouldEndWhenAllDead` is enabled by default and in the game scene: a solo
death or a full-team wipe opens the round results. A teammate dying while
another player is alive still allows revives. Disable this setting in the
GameManager inspector only when debugging revives.
The boss health/status display and stunned-player indicator remain below
the timeline.

## Round results

The results screen opens on **Timeline**; **Summary** preserves the scorecard.
Basic mode exposes **Damage dealt**, **Enemies killed**, and **Level**, with
damage selected initially. Choose one stat family at a time; switching families
replaces the previous selection and resets its breakdown. **Advanced** exposes
the remaining combat and survival totals; upgrade counts and rerolls are only
shown in the Summary scorecard, not offered as chart metrics. Empty optional
rows stay hidden. Returning to Basic keeps the selected advanced stat's button visible.
Player buttons toggle all lines and moments belonging to that player.

Each view has exactly one labeled raw-value axis, shared across visible players.
Axis tick labels round to the nearest whole number; underlying values, graph
scaling, legend totals, and line-point tooltips retain their precision.
The legend identifies each player's total line by color and shows its final value.
Kills can show a stacked **By enemy type** breakdown. Damage dealt can show
**By enemy type** or **By damage type**; these are alternatives, not combined,
because they each partition the same total. Damage taken supports **By damage
type**. Boss damage is already included in the enemy-type stack, not added again.
Bars show cumulative category totals, not activity within an interval, so they
use the same scale as the total line. At each snapshot, player stacks appear
side by side; category colors are shared across players, and outlines match
player line colors. Segment tooltips show only `Player - Category - Amount`,
with the amount rounded to the nearest whole number. Up to twelve evenly spaced bucket-end snapshots
(including the final endpoint) keep bars readable on long rounds; total lines
retain every 15-second point. Hiding all players shows an explicit empty-state
message; key-moment filters remain independent.
Time labels are elapsed round time, not wall-clock time.

Level starts at one and changes in steps at the exact host-clock time of each
level-up, including multiple levels earned simultaneously. These sparse level
changes are replicated with the frozen round results, not combat-bucketed.
Level-ups and modifier consumption remain separate: choosing an upgrade later
does not move the level-up time or the upgrade icon.

The host aggregates each stat as it is recorded into sparse, half-open 15-second
buckets (`[0,15)`, `[15,30)`, etc.). No raw combat event list is retained.
Stat storage is O(players * active stats * duration / 15), with O(1) updates per stat
event, plus O(milestones) for the much rarer timestamped moments.
At round end, the host publishes frozen bucket deltas and exact
milestone timestamps through replicated collections. Clients build only the
selected stats' cumulative series, carry totals across quiet buckets, and
include the final partial bucket. A bucket's total is plotted at its end;
lines between points do not imply exact within-bucket timing. An event exactly
at round end is included in the final endpoint. Subsequent combat cannot
change the frozen results.

Modifier acquisitions (including repeat stacks), deaths, and revives retain
their exact host-clock time. Boss spawn is recorded only after a successful
spawn, not at its scheduled time. Boss defeat and round start/end are recorded
as world moments. Buttons filter world, modifier, and survival markers; hiding
a player also hides their moments. Click a vertical marker or the chronological
moment list to inspect it, including coincident events.
Upgrade icons reuse the modifier catalog's Material Icons glyphs and overlay
the plot at the exact acquisition time, colored for their player. Nearby
acquisitions stack into separate lanes; a scrollable overlay keeps dense
clusters accessible without covering the whole plot. Hiding modifiers or
a player also hides their icons. Repeated acquisitions retain separate icons.
Deaths show skull icons at their exact timestamps in the same overlay, with
player-colored borders. The Deaths / revives filter also hides these skulls.

Compared with one-minute buckets, 15-second buckets use at most four times as
many aggregates and chart points, but still do not retain raw combat events.
Ten minutes produce 41 points per player/stat, including the starting point.

New gameplay events should call `GameManager.RecordRoundMoment(label)` on the
host when they actually happen. This is separate from `TimelineEvents`, which
may include scheduled events that never occurred.

Timeline boundary and pipeline calculations are covered by the existing
test executable, alongside 15-second boundaries, partial/instant rounds, quiet
buckets, snapshot isolation, invalid inputs, and a million-event aggregation
check (forty retained buckets and forty-one chart points), exact-time level
steps, and cumulative category/total alignment:

```powershell
dotnet run --project Tests\DamageFramework.Tests.csproj -p:EngineDirectory="<s&box install>"
```
