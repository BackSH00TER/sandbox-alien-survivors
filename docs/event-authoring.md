# Adding a world event

World events use one catalog, scheduler, and runtime action registry. Do not add
event-specific clocks, replicated event lists, spawn branches, or retry logic to
[GameManager](../Code/game/GameManager.cs). The boss remains a separate match
objective; this guide covers catalog-driven world events.

## 1. Define the event

Append a stable ID to `GameEventKind` in
[GameEventCatalog](../Code/game/events/GameEventCatalog.cs); do not renumber
existing IDs. Add one entry to `AllEvents` with a label, Material Icons glyph,
rarity, and callback dispatching that ID through `IGameEventWorld`.

The weapon airdrop is an existing example:

```csharp
new GameEventDefinition
{
	Id = GameEventKind.WeaponAirdrop,
	Label = "Weapon airdrop",
	Icon = "flight",
	Rarity = GameRarity.Rare,
	TryStart = world => world.TryStartEvent(GameEventKind.WeaponAirdrop)
}
```

The manager reads this metadata for the timeline and records
`"<label> arrived"` only after the callback succeeds. Do not record that same
arrival again inside the action.

## 2. Implement and register the action

Create a component under [events](../Code/game/events) implementing
`IGameEventAction.TryStart()`. Follow
[GameWeaponAirdropEvent](../Code/game/events/GameWeaponAirdropEvent.cs) for a
delivery or [GameDropshipEvent](../Code/game/events/GameDropshipEvent.cs) for an
event with inspector configuration.

- Require the host and an active round before changing the world.
- Own event-specific settings, validation, placement, and spawning in the action.
- Reuse existing placement and spawning helpers when suitable.
- Log a clear failure and return `false` if the event cannot start. The shared
  manager retries pending events on its one-second retry cadence; do not create
  a separate event-start retry clock or return success when nothing spawned.
- Return `true` only after successful creation, so the schedule can mark the
  occurrence and replace its planned timestamp with the actual arrival time.

Register the action in
[GameEventActions.Create](../Code/game/events/GameEventActions.cs), using
`owner.GetOrAddComponent<YourEventAction>()`. This preserves a component authored
on the manager object for inspector overrides. Adding a new event must not
require an event-type branch in the manager.

## 3. Use the shared schedule

Timing and rarity weights belong to `GameEventSchedule.DefaultSlots`, in
[GameEventCatalog](../Code/game/events/GameEventCatalog.cs), not the event action.
Each slot configures its first roll time, minimum/maximum delay, repeat interval,
and rarity weights. A zero repeat interval makes it one-shot.

A slot draws a rarity, then an equally likely event within that rarity. Adding
another Rare definition therefore divides the existing Rare probability among
more events; it does not add another guaranteed event or increase that rarity's
weight. `None` is an explicit Common quiet outcome, not a failed spawn.
Unpopulated rarities are excluded and their weights redistribute across populated
rarities. With the current defaults, the two Rare events each have a 12.5%
chance per slot and quiet has a 75% chance.

Keep `DefaultSlots` built in its getter and create the scheduler in host
`OnStart`. s&box hotload preserves cached static and component state; a new round
must snapshot the current tuning. Changing defaults does not retune a round
already in progress. Clients consume replicated schedules and never roll events.

## 4. Keep lifecycle and presentation separate

Use a runtime component under [events](../Code/game/events) for movement,
landing, combat, rewards, and cleanup. See
[GameWeaponAirdrop](../Code/game/events/GameWeaponAirdrop.cs) and
[GameDropship](../Code/game/events/GameDropship.cs).

Network-spawn gameplay objects on the host. Replicate state needed by clients,
and keep client-built visual children local and unsaved. Isolate temporary
assembled geometry behind a visual-building method, as
[GameSupplyCrate.BuildPrototypeVisuals](../Code/game/GameSupplyCrate.cs) does,
so replacing it with a real model does not touch gameplay initialization.

Record lifecycle moments, such as landing or destruction, through
`GameManager.RecordRoundMoment` on the host when they happen. Stop event activity
when the round ends. Grant rewards only for the intended gameplay condition,
not scene teardown or round-end cleanup. Reuse
[GameLootDrop](../Code/game/GameLootDrop.cs),
[GameLootCatalog](../Code/game/GameLootCatalog.cs), and
[GameExperienceOrb](../Code/game/GameExperienceOrb.cs) for rewards where applicable.

For world pointers, add a target-selection wrapper under
[markers](../Code/game/markers) and render
[GameObjectiveMarker](../Code/game/markers/GameObjectiveMarker.razor).
Supply visibility, target/origin positions, label, icon, and color rather than
duplicating projection, edge clamping, distance rendering, or arrow styling.
[GameWeaponCrateMarkers](../Code/game/markers/GameWeaponCrateMarkers.razor)
shows multiple targets; the dropship and revive-flare wrappers select the nearest.
Preserve s&box Razor's typed bindings, for example `Visible="@(true)"` and
`EdgeY="@(0.34f)"`, rather than passing literal strings to those properties.
Wire the wrapper into [GameHUD](../Code/game/GameHUD.razor) for the intended
player states. Timeline metadata requires no event-specific HUD layout.

## 5. Validate

Extend [EventCatalogTests](../Tests/EventCatalogTests.cs) to cover the new ID,
metadata, callback dispatch, failure propagation, exact rarity/item boundaries,
and opening/recurring slot selection. Update probability expectations for other
events sharing its rarity. Keep quiet outcomes, consumed slots, catch-up timing,
and invalid inputs covered. Add reward tests when reward rules change.

Run the existing test executable and full game build:

```powershell
dotnet run --project Tests\DamageFramework.Tests.csproj -p:EngineDirectory="<s&box install>"
dotnet build Code\alien_survivors.csproj
```

The editor generates the game project. Also verify the actual s&box compile:
engine Razor generation can report errors that the standalone build misses.
In a fresh running round, check successful dispatch, failed-start retries without
duplicate arrivals, timeline entries, lifecycle behavior, pointers, rewards, and
round-end cancellation. Test multiplayer replication when possible, and report
anything not exercised. See [game timeline](game-timeline.md) for current gameplay
tuning and behavior.
