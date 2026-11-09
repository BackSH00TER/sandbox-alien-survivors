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

`GameManager.TimelineEvents` is the event source. It exposes the existing boss
spawn, using `CountdownTime` and then the actual spawn time once spawning succeeds,
and weapon airdrops at **1:00, 5:00, 10:00, then every five minutes** while the
round remains in progress. Three upcoming airdrops are always scheduled; each
successful launch adds the next one. A delayed launch uses its actual launch time
on the timeline. Event types supply their timestamp,
Material Icons glyph, and label as `GameTimelineEvent` values without adding
event-specific layout logic.

The timeline does not introduce recurring bosses.
Defeating the boss still ends the match; the clock freezes at round end.
`ShouldEndWhenAllDead` is enabled by default and in the game scene: a solo
death or a full-team wipe opens the round results. A teammate dying while
another player is alive still allows revives. Disable this setting in the
GameManager inspector only when debugging revives.
The boss health/status display and stunned-player indicator remain below
the timeline.

## Weapon airdrops and crates

A host-spawned aircraft flies over clear ground selected by the crate spawner,
releases cargo after three seconds, and delivers a blue-and-gold weapon crate
after five seconds. The aircraft leaves after six seconds. Clear-ground selection
avoids players, existing crates, and other incoming deliveries. Failed placement
or spawning is logged and retried; no event is recorded as successful before it
actually happens. Inbound deliveries stop when the round ends.

Weapon crates have 30 health and drop **1–3 weapons**, each independently and
equally chosen from the rifle and shotgun, plus **0–3 XP orbs**. Repeats are
possible. They never drop consumables or revive flares, and do not consume regular
crate respawn slots. Regular supply crates now exclude weapons: after the
unchanged contextual flare roll, they retain 90% common (health or reroll dice,
equally likely) and 10% rare (armor) loot, plus their existing XP orbs.

Living players see a gold **Weapon crate** arrow and distance for each landed,
unopened crate. Like the revive-flare pointer, it hovers above visible targets or
pins to the screen edge for off-screen targets. Breaking the crate removes its
pointer. Dead players retain their existing nearest-flare pointer.
Actual airdrop launches and crate landings are recorded as world moments in the
round results.

The default boss countdown is still 60 seconds. Airdrops only occur if the match
lasts long enough; this feature does not extend the round or delay the boss.

## Round results

The results screen opens on **Timeline**; **Summary** preserves the scorecard.
Basic mode exposes **Damage dealt**, **Enemies killed**, and **Level**, with
damage selected initially. Choose one stat family at a time; switching families
replaces the previous selection and resets its breakdown. **Advanced** exposes
the remaining totals and individual upgrade counts; empty optional rows stay
hidden. Returning to Basic keeps the selected advanced stat's button visible.
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
