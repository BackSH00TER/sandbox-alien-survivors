# Game timeline

The HUD shows a rolling two-minute window: one minute of history to the left
and one minute of upcoming events to the right. **NOW** stays in the center;
event icons move left as the host-synchronized round clock advances. Events
dim once their timestamp has passed and leave the view after another minute.
Time labels show total elapsed minutes and seconds, without a fixed endpoint.

Events beyond the right edge appear as smaller pipeline icons. Their tiny
`min` labels round up the time until they enter the visible window, **not**
the time until they occur. For example, a boss due at 5:00 starts in the
pipeline with `4 min` and enters the view at 4:00.

`GameManager.TimelineEvents` is the event source. Currently it exposes only
the existing boss spawn, using `CountdownTime` and then the actual spawn time
once spawning succeeds. Future event types can supply their timestamp,
Material Icons glyph, and label as `GameTimelineEvent` values without adding
event-specific layout logic.

The timeline does not introduce recurring bosses or new gameplay events.
Defeating the boss still ends the match; the clock freezes at round end.
The boss health/status display and stunned-player indicator remain below
the timeline.

Timeline boundary and pipeline calculations are covered by the existing
test executable:

```powershell
dotnet run --project Tests\DamageFramework.Tests.csproj -p:EngineDirectory="<s&box install>"
```
