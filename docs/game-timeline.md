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

`GameManager.TimelineEvents` is the event source. Currently it exposes only
the existing boss spawn, using `CountdownTime` and then the actual spawn time
once spawning succeeds. Future event types can supply their timestamp,
Material Icons glyph, and label as `GameTimelineEvent` values without adding
event-specific layout logic.

The timeline does not introduce recurring bosses or new gameplay events.
Defeating the boss still ends the match; the clock freezes at round end.
`ShouldEndWhenAllDead` is enabled by default and in the game scene: a solo
death or a full-team wipe opens the round results. A teammate dying while
another player is alive still allows revives. Disable this setting in the
GameManager inspector only when debugging revives.
The boss health/status display and stunned-player indicator remain below
the timeline.

## Round results

The results screen opens on **Timeline**; **Summary** preserves the scorecard.
Basic mode exposes damage dealt and enemies killed, with both selected initially.
Stat buttons toggle independently, so any combination can be plotted together.
**Advanced** exposes the remaining stat catalog, including damage breakdowns
and upgrades; empty optional rows stay hidden. Returning to Basic preserves
selected advanced stats and keeps their buttons visible so they can be removed.
Player buttons toggle all lines and moments belonging to that player.

Compatible stats share a labeled raw-value axis on the left: all damage stats
use **Damage**, kills/deaths/revives/rerolls/upgrade acquisitions use **Count**,
and armor gained uses **Armor**. Each axis uses one scale across its selected
stats and visible players, keeping the display to at most three axes.
Damage cannot dwarf kill counts. Different axes share the round timeline, not
units or a numerical scale; equal line heights across axes do not mean equal values.
The legend identifies each player/stat line by color and shows its final total.
Hover a point for its stat, actual total, and bucket endpoint. Several selected
axes expand the horizontally scrollable chart. Deselecting every stat or player
shows an explicit empty-state message; key-moment filters remain independent.
Time labels are elapsed round time, not wall-clock time.

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

Compared with one-minute buckets, 15-second buckets use at most four times as
many aggregates and chart points, but still do not retain raw combat events.
Ten minutes produce 41 points per player/stat, including the starting point.

New gameplay events should call `GameManager.RecordRoundMoment(label)` on the
host when they actually happen. This is separate from `TimelineEvents`, which
may include scheduled events that never occurred.

Timeline boundary and pipeline calculations are covered by the existing
test executable, alongside 15-second boundaries, partial/instant rounds, quiet
buckets, snapshot isolation, invalid inputs, and a million-event aggregation
check (forty retained buckets and forty-one chart points):

```powershell
dotnet run --project Tests\DamageFramework.Tests.csproj -p:EngineDirectory="<s&box install>"
```
