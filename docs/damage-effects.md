# Damage effects

Fire is the first playable effect. Poison and electricity have reserved damage-type
entries; explosive and ice entries are preserved. Only entries in `DamageEffectCatalog`
participate in bullet rolls. Other types are not implemented or offered yet.

## Fire progression and tuning

| Upgrade | Prerequisite | Benefit |
| --- | --- | --- |
| Fire Rounds | None | Unlock fire bullets; one-time pick |
| Ignition Chance | Fire Rounds | +10 percentage points bullet chance, up to 100% |
| Lasting Flames | Fire Rounds | +2 seconds per application |
| Hotter Flames | Fire Rounds | +5 damage/second per application |
| Layered Flames | Fire Rounds | +1 simultaneous stack |
| Spreading Flames | Fire Rounds | Unlock spread; one-time pick |
| Wildfire | Spreading Flames | +10 percentage points spread chance, up to 100% |

Initial fire: 20% bullet chance, 5 seconds, 5 damage/second, one stack.
Spread starts at 10% per second within 120 world units (s&box units are inches).
Defaults and bullet color live in `DamageEffectCatalog`; upgrade increments,
pick caps, prerequisites and offer weights live in `ModifierCatalog`.
Upgrade factory calls use C# named arguments (`stat:`, `amount:`, `prerequisite:`)
to identify each value without introducing a separate options object.
`DamageEffectDefinition.Resolve` adds each matching upgrade's increment multiplied
by its pick count; these player picks are distinct from effect stacks on a target.

Offers contain three distinct eligible modifiers, drawn by `DrawWeight` without
replacement. Unlocks and maxed chance upgrades stop appearing. Existing attack speed,
range and vitality upgrades remain repeatable. All prerequisites must be owned;
the host checks both the offer ID and eligibility when accepting a pick.

## Bullet and effect behavior

- The host chooses one unlocked effect type by its `DrawWeight`, then rolls that
  type's application chance. Failure remains a physical bullet; no other effect
  gets a second chance. With fire alone, this is exactly the displayed fire chance.
- A fire bullet is orange-red on every client. Normal player bullets stay yellow;
  enemy bullets stay magenta. All bullets retain their existing physical impact
  damage. Fire adds timed damage only to surviving enemies, not players or crates.
- Burning enemies show five flickering orange/yellow wisps as placeholder flames.
  `BurningVisual` builds these locally from the synchronized fire stack count, so
  bullet ignition and spread use the same visual on every client. The wisps follow
  the enemy, disappear when fire expires or the enemy dies, and are cleaned up on
  disable. They have no collision and do not alter blood or hit-flash materials.
- Each projectile snapshots its effect settings when fired. Later upgrades affect
  future bullets, not in-flight bullets or already burning enemies.
- Each application has independent expiry and attacker credit. Two stacks deal
  twice the per-stack DPS while both are alive. A 10-second application followed
  five seconds later by another leaves five seconds on the first and ten on the
  second. Applying at cap replaces only the oldest application.
- The cap is shared per damage type on the target, using the incoming application's
  cap to decide whether another stack can be added. A lower-cap player's bullet
  renews the oldest stack without deleting another player's other stacks.
- Damage uses one-second ticks, paying partial intervals on expiry or reapplication
  rather than causing continuous hit-stagger every frame. It integrates elapsed time and never
  continues beyond expiry. Death, disable and round end clear effects. Invulnerability
  consumes elapsed effect time without applying damage.
- Once per second, each burning enemy rolls spread once per effect type, using
  its newest spread-enabled application's settings and original attacker. On
  success, one randomly selected living, damageable enemy within range and line of
  sight receives a fresh application. Already burning enemies qualify only below
  the incoming stack cap; spread never refreshes a full target. Spread cannot hit
  players or crates. Spread can propagate again from newly infected enemies.
- Fire damage and kills use the existing per-type player statistics. Stack counts
  and acquired modifiers are host-synchronized; inventory icons expose upgrade
  descriptions on hover.

## Adding another type

For another timed effect, add its unlock/spread/upgrade modifier definitions and
prerequisites, then add a `DamageEffectDefinition`. Shared rolling, stack lifetimes,
damage attribution and spread logic do not need type-specific branches.
New effect upgrades use the generic synchronized modifier dictionary.
`Entity` separates ticking, spread rolls, target eligibility and line-of-sight checks
into helpers. Its round-manager property caches the scene lookup while the manager
is valid and reacquires it after removal or a scene change.

Instant explosion damage needs a separate impact behavior (area damage, not a timed
stack); do not model it as a fake duration. Electricity-specific stuns or other special
behaviors likewise need an explicit implementation before enabling their catalog entries.

## Validation

The deterministic test executable links the production catalog and stack logic and
uses the installed engine's real `Color` type, without starting an editor or adding
NuGet packages:

```powershell
dotnet run --project .\Tests\DamageFramework.Tests.csproj -p:EngineDirectory="$env:FACEPUNCH_ENGINE"
```

If the shell does not have the engine variable, use
`[Environment]::GetEnvironmentVariable('FACEPUNCH_ENGINE', 'User')` or pass your install
directory explicitly. Tests cover prerequisite gating, weighted draws, offer uniqueness,
proc thresholds, independent expiries, exact damage, stack replacement, spread capacity,
source attribution and cleanup.

For gameplay verification, open this worktree's `.sbproj` in s&box, pick Fire Rounds,
then test fire bullets against enemies and crates. Burning enemies should display
the moving orange/yellow wisps, which disappear after the last fire stack expires.
Pick Layered Flames and Spreading
Flames to verify stacking and spread; Wildfire must not appear before spread is owned.
The editor validates networking and rendering as well as the C# compile.
