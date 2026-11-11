# Damage effects

Fire, electric shock and explosion share `DamageEffectCatalog` and immutable
`DamageEffectSettings`. Fire and shock are player-unlockable; explosion is
enemy-only. Only catalog entries with an owned `Unlock` participate in bullet
rolls. Poison and ice remain reserved and are not implemented or offered yet.

## Explosion behavior

Explosion uses the existing `Explosive` damage type for armor and statistics.
Its catalog defaults are a 3-second fuse, 100-unit radius and 40 blast damage.
`Duration` is the actual fuse, `InstantDamage` is the damage at detonation, and
`BlastRadius` distinguishes area explosions from instant chains and timed stacks.
Explosions never tick damage, spread fire-style stacks, or chain shock-style hops.
A zero-duration fuse detonates on the first fixed update after the minimum
0.1-second warning.

`GameExplosionEffect.Apply` attaches the reusable effect to an origin object.
The exploding enemy captures its existing prefab-tunable fuse, radius and damage
into these shared settings on death, then attaches the effect to its ragdoll.
The warning follows the corpse, and the corpse is removed 0.4 seconds after the
blast (or immediately on round end), preserving the existing behavior.
Other callers can leave the origin alive; only the effect and its visual are
removed. `GameEnemy.ApplyDamageEffect` also dispatches explosion settings through
this same implementation without destroying a surviving target.

The host applies one full-damage hit to each active, living, damageable enemy or
player within the radius. Walls block the blast; living entity hierarchies and the
origin hierarchy do not obstruct it. Targets are snapshotted before damage can
spawn additional corpses. Client-visible warning rings and lights use synchronized
fuse/detonation state. An optional attacker supplies damage credit; death explosions
remain uncredited, as before.

Explosion has no player unlock or modifiers and cannot enter bullet rolls, so
fire/shock selection probabilities, upgrade offers and projectile colors are unchanged.

## Common combat and survival modifiers

Existing Rapid Strikes (+100% attack rate) and
Vitality (+100 maximum health and healing) retain their behavior. New modifiers
are all Common; no damage-type conversion modifiers were added.
These combat and survival upgrades use explicit `ModifierDefinition` initializers
with individually editable `Rarity` values, currently set to `GameRarity.Common`.
Long Reach has been removed; weapon range stays at the weapon's configured base.
Its synchronized modifier ID (1) is retired without renumbering other modifiers.

| Upgrade | Benefit per pick |
| --- | --- |
| Heavy Rounds | +20% base weapon damage |
| Fleet Footed | +10% base player movement speed |
| Better Medicine | +25% base health pickup healing |
| Regeneration | +1 health/second while alive and the round is in progress |
| Armor Salvager | +25% base armor pickup amount, rounded up |
| Splash Rounds | +10 percentage points splash chance per bullet; capped at 10 picks |
| Bigger Splash | Requires Splash Rounds; +25 percentage points splash damage |
| Piercing Rounds | +10 percentage points piercing chance per bullet; capped at 10 picks |
| Deep Penetration | Requires Piercing Rounds; +1 additional pierced enemy |
| Slowing Rounds | One-time unlock; direct hits slow movement by 50% for 2 seconds |
| Bonus Volley | +10 percentage points chance per trigger pull and +1 bonus bullet |
| Power Shot | First pick guarantees double damage every 10th trigger pull; each further pick shortens the interval by 1, capped at 10 picks |

Stat percentages add against their base, not the previously upgraded value.
Healing stays capped at maximum health and does not revive dead players.
Player health text displays whole numbers, rounded down; fractional health and
regeneration remain unchanged internally, and the health bar still fills smoothly.
Better Medicine affects pickups only, not regeneration, Vitality, or revives.
Armor eligibility and collection both use the boosted, rounded amount and reject
integer overflow. The first Armor Salvager pick turns a 5-armor pickup into 7.

Splash and piercing each roll once independently when a bullet spawns and capture
their settings for its lifetime. Splash deals 50% of that bullet's damage by
default to **other enemies** within 60 units of each direct enemy hit, using
Explosive damage and respecting walls. It does not trigger effects, slowing,
additional splash, or additional bullets. Heavy Rounds and Power Shots
increase bullet damage and therefore splash damage, not fire/shock effect damage.

Every successful splash impact flashes a thick white ring at the full 60-unit
radius for 0.6 seconds. It appears even when there are no nearby secondary
targets and survives lethal hits. The host broadcasts the impact through the
shooter; each client creates its own temporary, non-networked visual, independent
of debug hitboxes. The ring marks the radius, not guaranteed damage through walls.

A successful piercing bullet can initially hit two enemies (one additional
target); each Deep Penetration pick adds another. It retains full bullet damage
and direct-hit effects on each target, never directly hits the same enemy twice,
ignores corpses, and still stops at walls or crates. Remaining movement in the
physics tick is traced immediately after piercing. Slowing Rounds refreshes its
duration on surviving direct-hit enemies, affects pursuit and ranged wandering,
and does not stop attacks or stack movement reductions.

Bonus Volley rolls once per successful trigger pull, adding its current rank in
bullets to the weapon's normal pellet count on success, using the weapon's spread.
Its chance caps at 100%, but bullet count continues growing.
Power Shot always doubles all bullet damage in an eligible trigger pull,
then resets the interval. It does not roll a random chance. Only actual trigger
pulls count, not pellets or bonus bullets; progress starts after acquiring the
modifier and survives weapon swaps. Reducing the interval honors existing progress
on the next shot. Shots, regeneration, damage credit, and pickups remain host-authoritative.

The game scene enables `ShouldEndWhenAllDead`, which also defaults to true on new
managers. Solo death opens the summary scoreboard; in multiplayer the round ends
only after every player dies, preserving revives while a teammate remains alive.
Disable the flag only when debugging revives after the entire team is dead.

Fire and electric shock are playable effects. Poison has a reserved damage-type
entry; explosive and ice entries are preserved. Only entries in `DamageEffectCatalog`
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
pick caps, prerequisites, effects, presentation and rarity buckets live in `ModifierCatalog`.
Upgrade factory calls use C# named arguments (`stat:`, `amount:`, `prerequisite:`)
to identify each value without introducing a separate options object.
The fire-upgrade factory also accepts an optional `rarity:` override, defaulting
to `GameRarity.Common`; existing fire upgrades retain their Common rarity.
`DamageEffectDefinition.Resolve` adds each matching upgrade's increment multiplied
by its pick count; these player picks are distinct from effect stacks on a target.

## Electric shock progression and tuning

| Upgrade | Prerequisite | Benefit |
| --- | --- | --- |
| Shock Rounds | None | Unlock shock bullets and one possible chain; one-time pick |
| Static Charge | Shock Rounds | +10 percentage points shock bullet chance, up to 100% |
| High Voltage | Shock Rounds | +5 base electric damage before chain falloff |
| Conductivity | Shock Rounds | +10 percentage points chain chance per hop, up to 100% |
| Chain Reaction | Shock Rounds | +1 additional enemy and +60 units chain range per hop |

Initial shock: 20% bullet chance, 10 instant electric damage, and a 20% chance
to chain to one additional enemy within 180 units. Chaining is included in the
initial unlock, not gated behind a separate modifier. All shock modifiers are Common.
Chance upgrades each stop appearing after eight picks; the other upgrades repeat.

The shared bullet draw chooses one unlocked effect with equal default weight, then
rolls only that effect's application chance. Owning both fire and shock therefore
splits effect selection 50/50; a failed proc stays physical and never retries.
Projectiles capture immutable settings when fired, including shock upgrades.
As with fire, the physical bullet hit is resolved first; effects apply only if
the enemy survives that hit and remains damageable. Crates do not receive effects.

Shock deals its damage immediately, never creates timed stacks, and uses the
`Electricity` damage type for armor, damage credit and scorecard statistics.
The first target receives full shock damage. Each hop loses 10% of the original
shock damage, not 10% of the previous hit: 100%, 90%, 80%, 70%, and so on.
Damage bottoms out at 10% and stays there for further hops; reaching the floor
does not end the chain. For a base 10-damage shock, hits deal 10, 9, 8, ... 1, 1.
High Voltage increases the original damage before these percentages apply.
Falloff is applied before each target's armor, so armor does not reduce the
starting damage used for later hops. These values are captured with the shot
through `ChainDamageFalloff` and `MinimumChainDamageMultiplier`.
`ElectricArmorCost` defaults to one. Each hop independently rolls its chain chance;
failure ends the chain. Successful hops select the nearest active, living,
damageable, visible enemy in range of the previous target. Walls block chains,
and no target is hit twice by the same shock. One chain means two total targets;
each Chain Reaction pick adds one more possible target and 60 units of hop range
(180 units initially, then 240, 300, and so on). The range bonus is bundled with
the chain-count upgrade, not offered as a separate modifier. Its increment lives
in `DamageEffectDefinition.ChainRadiusPerAdditionalChain`. Lethal shock damage can
still chain: the next target is selected before damage creates a ragdoll or destroys
the current enemy. Eligibility is checked again before each hit, so round end stops damage.

Shock bullets are blue. A host-broadcast, local-only jagged cyan arc flashes at
the initial impact and between chained targets, then removes itself after 0.4 seconds.
This visual lifetime does not delay damage or subsequent chain hops.
No scene or prefab changes are required.

### Central modifier catalog

Loot and modifiers share `GameRarity` IDs and display names through immutable
`RarityBucket` entries. Each catalog owns its own probability table; identical
90%/10% settings do not couple their future balancing. `RarityDraw.Pick` shares
only the mechanics: skip zero-chance buckets, choose a weighted rarity and then a
uniform item, consuming one random roll for each stage. Callers supply the buckets
and candidates; an empty selected bucket fails explicitly rather than retrying.
Loot keeps its static configuration validation and contextual flare pre-roll.
Modifiers remove temporarily empty buckets before calling the helper, preserving
their renormalization policy. Effects, eligibility, distinct offers and rerolls
remain in their respective catalogs.

`ModifierCatalog.All` is the typed source for stable modifier IDs, names, icons,
descriptions, inventory classes, rarity, caps, prerequisites and effect increments.
`ModifierDefinition.ApplyTo` records a stack through `IModifierRecipient`, then runs
any immediate effect callback: Rapid Strikes refreshes the pending attack after
the new stack is recorded, and Vitality grants 100 maximum health and healing.
Passive effects resolve from owned picks using catalog amounts; fire settings
remain in `DamageEffectCatalog`. The player retains its existing synchronized
counters and host/offer validation. Both upgrade panels and the scorecard consume
the same catalog metadata.

Offers start with three distinct eligible modifiers. For each card, first draw a
rarity from `ModifierCatalog.Rarities`, then draw uniformly from that bucket's
remaining eligible modifiers, without replacement. Common is **90%** and Rare is
**10%**; Uncommon, Epic and Legendary are defined at 0% and disabled. More Choices
is the only Rare modifier; all other current modifiers, including Vacuum, are Common.
These are per-card probabilities while both buckets have eligible cards, not
per-offer probabilities. This intentionally changes the old balance: More Choices
used to have weight 0.2 versus 1 per common card (3.85% on the initial first card);
it now has a fixed 10% first-card chance, and a 27.1% chance to appear in an initial
three-card offer. Adding or unlocking Common modifiers no longer dilutes Rare odds.

Before each draw, omit disabled buckets and buckets without remaining eligible
cards, then renormalize the remaining chances. This includes depletion within an
offer, capped upgrades and forced reroll replacements. For example, after selecting
More Choices once in an offer, all remaining cards come from Common. This runtime
eligibility policy differs from loot's static configuration rule: an enabled
modifier bucket still requires at least one configured entry, but all its entries
may temporarily be capped, locked or already offered. Catalog chances must total
100%; malformed catalog data and invalid rolls fail explicitly.

**More Choices** is a rare, prerequisite-free modifier with five ranks:
each pick adds one option to future offers (including queued level-ups), up to eight.
At rank five it stops appearing. If fewer eligible
modifiers exist than the slot count, offers show all eligible modifiers without duplicates.
The host synchronizes the full offer and validates membership before accepting a pick.
Cards wrap into rows, and number keys 1 through the current offer size select a card.
Unlocks and maxed chance upgrades stop appearing. Existing attack speed,
range and vitality upgrades remain repeatable. All prerequisites must be owned;
the host checks both the offer ID and eligibility when accepting a pick.

## Reroll Dice

Crates drop an equally likely number of items between their configured `MinDrops`
and `MaxDrops` (1-3 by default), rolling each item's kind independently. Multiple
pickups are spaced around the crate, while a single pickup stays centered.
Destroyed crates additionally drop 0-3 equally likely XP orbs, each worth 1 XP,
scattered 24-48 units from the crate on the ground plane. This separate roll does
not change the existing loot counts or item probabilities.
Crates can drop an ivory die with dark pips. It joins health, rifle and shotgun in
the equally weighted common pool: each has a 22.5% chance when no flare is rolled,
while armor keeps its separate 10% chance. A flare roll still takes priority.
Walking over a die automatically grants one reroll to that living player. Dice
accumulate for the round, persist through death/revive and weapon changes, and use
the normal pickup range, line-of-sight and expiration rules.

Health, armor, reroll dice and flare drops also use the XP magnet's 150-unit base
attraction range and proximity-based movement speed. Vacuum upgrades increase this
range by 20% per rank, up to 300 units at rank five, just like XP. Loot only moves
toward active, living players with line of sight; its existing contact collection
rules still apply. Attraction and contact collection share eligibility checks:
full-health players do not attract health, flares stay put while nobody is dead,
and armor/dice are not attracted to players whose counters are full.
Health drops can still move toward another nearby injured player.
Weapon drops are never attracted or automatically collected and still require
interaction. Magnet flags now live only in `GameLootCatalog`, rather than in
weapon resources or optional spawn arguments.
`GamePickupMagnet` owns the shared base attraction range, cubic speed curve and
XP contact defaults; experience orbs no longer expose or pass per-orb magnet tuning.
Loot retains its larger 32-unit contact radius and 16-unit target height.
Loot expiration is unchanged.

### Central loot catalog

`GameLootCatalog.All` defines every loot item in one typed table: its stable
`LootKind` ID, display name, effect and amount, explicit `CanBeMagnetAttracted`
boolean, rarity bucket, collection callbacks, weapon resource binding, and visual model parts.
The existing procedural prototype models are represented as named parts with
model paths, positions, dimensions and tints; the die's pip layout is generated
once by the catalog. All prototype geometry builders are grouped at the bottom of
the catalog, keeping the item table short. Pickups render these same parts on each client.

Crates use `GameLootCatalog.Roll`: the contextual revive-flare roll comes first,
then a rarity bucket is drawn using `GameLootCatalog.Rarities`, then an item is
drawn uniformly from that bucket. Common is 90% (health, rifle, shotgun and dice)
and Rare is 10% (armor), preserving the existing balance. Uncommon, Epic and
Legendary are defined at 0% for future balancing. Bucket chances must total 100%;
zero-chance buckets are disabled, and every enabled bucket must contain an item.
An empty enabled bucket is a configuration error, not a reroll or an implicit
redistribution of its chance. Adding an item only changes the odds *within* its
bucket; it does not dilute any other rarity. These percentages apply after a
failed flare roll. Flare eligibility and its time-dependent chance remain in
`GameCrateSpawner` so dead-player counts, flares already in play, and existing
scene tuning keep working. The flare is the one-off pre-roll outside the rarity
buckets (its `Rarity` is unset); normal items only specify their rarity and need
no per-item drop-rule setting. Rifle/shotgun `WeaponSlot` bindings retain the
spawner's custom weapon overrides; resource paths supply defaults.

Both crates and debug commands explicitly resolve a catalog definition and pass
it to `GameLootPickup.Spawn(groundPosition: ..., definition: ...)`. There is no
per-call magnet default or inferred weapon policy. Collection and attraction use
the entry's `CanCollect` callback; successful contact invokes its `Collect`
callback with the configured amount. `ILootCollector` exposes the player
capabilities those callbacks need, keeping catalog rules testable without the
engine. `GamePlayer` implements the contract using its existing health, armor,
dice and revive-flare operations. Pickups retain host, round-state, range and
line-of-sight checks and only disappear after successful effect application.
Each armor pickup grants 5 armor points and is displayed as `Armor +5`.
The display name for weapons still comes from
their actual weapon resource. To add an item using an existing effect, append a
stable ID to `LootKind` and add a catalog entry with its settings and visual parts.
New behavior is registered through the entry's eligibility/application callbacks
rather than a pickup-component effect switch.
Modifier offers use their own rarity catalog and the runtime empty-bucket
handling described above; loot probabilities and flare handling are unchanged.

The HUD always shows the local player's remaining Reroll Dice. While choosing an
upgrade, click the reroll button or press R (`RerollModifiers` input action) to spend
one die. Rerolling preserves the pending choice count, owned upgrades and More Choices
slot count (up to eight, limited by the eligible pool), draws
distinct eligible modifiers and changes at least one card, rather than only changing
their order. If a draw repeats the old set, one card is replaced by a rarity-first
eligible alternative outside the old offer. The button is disabled with no dice, while dead/round-over,
or if the current offer already contains every eligible modifier; no die is spent in those cases.
The host validates the offer ID, ownership, round state and balance; a successful
reroll invalidates the previous offer so duplicate requests cannot spend another die
or select an old card. New rounds start with zero dice.
Each successful reroll increments the player's round stat `times_rerolled`, shown
as "Times rerolled" in the scorecard's Upgrades section. Like other upgrade rows,
it is hidden if every player has zero. Rejected requests and dice pickups do not count.

## Bullet and effect behavior

- The host chooses one unlocked effect type by its `DrawWeight`, then rolls that
  type's application chance. Failure remains a physical bullet; no other effect
  gets a second chance. With fire alone, this is exactly the displayed fire chance.
- A fire bullet is orange-red on every client. Normal player bullets stay yellow;
  enemy bullets stay magenta. All bullets retain their existing physical impact
  damage. Fire adds timed damage only to surviving enemies, not players or crates.
- Bosses take damage and show hit feedback without interrupting movement, punch
  windup, or their charged stun attack. Their zero `HitStaggerDuration` disables
  gameplay stagger; regular enemies retain their configured hit stagger.
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
- Fire ticks use `GameEntity.TakeDamage`, so they respect the existing armor rules:
  each application's damage tick spends `FireArmorCost`, with partial protection
  controlled by `ArmorDamageOverflow`. Physical bullet impact spends physical armor
  separately; a surviving enemy can still ignite after armor blocks that impact.
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

Runtime combat types follow the upstream `Game*` naming (`GameEntity`, `GameEnemy`,
`GamePlayer`, `GameModifier`, `GameDamageType`). Modifier UI lives in
`GameModifierPicker` and `GameModifierInventory`; the test project links the renamed
catalog and damage-type source files.

For another timed effect, add its unlock/spread/upgrade modifier definitions and
prerequisites, then add a `DamageEffectDefinition`. Shared rolling, stack lifetimes,
damage attribution and spread logic do not need type-specific branches.
New effect upgrades use the generic synchronized modifier dictionary.
`GameEntity` separates ticking, spread rolls, target eligibility and line-of-sight checks
into helpers. Its round-manager property caches the scene lookup while the manager
is valid and reacquires it after removal or a scene change.

Instant effects use `InstantDamage`, with zero duration/DPS and no timed spread.
Chain settings are optional: omitted numeric properties default to zero and disable
chaining. A non-chaining instant effect may omit its chain radius; enabled chaining
requires a positive radius. Nullable chain fields are not needed.
`GameEnemy.ApplyDamageEffect` is the public entry point, so projectile impacts select
an enemy before applying an effect; players and crates do not expose that method.
`DamageEffectChain` selects each next target before dealing damage, then rechecks
eligibility before hitting it. This supports lethal hits without chaining through a
new ragdoll. Explosion settings dispatch to `GameExplosionEffect` for the delayed
area blast rather than entering either the chain or timed-stack implementation.
Stuns likewise need an explicit implementation.

## Validation

The deterministic test executable links the production catalog, chain and stack logic and
uses the installed engine's real `Color` type, without starting an editor or adding
NuGet packages:

```powershell
dotnet run --project .\Tests\DamageFramework.Tests.csproj -p:EngineDirectory="$env:FACEPUNCH_ENGINE"
```

If the shell does not have the engine variable, use
`[Environment]::GetEnvironmentVariable('FACEPUNCH_ENGINE', 'User')` or pass your install
directory explicitly. Tests cover prerequisite gating, exact rarity boundaries and
uniform within-bucket draws, shared rarity names and independent probability tables,
random-call counts, empty-bucket renormalization (including reroll replacements),
catalog effect callbacks, invalid IDs/rolls, offer uniqueness, More Choices ranks
and caps, small eligible pools, rare-modifier offer frequency,
proc thresholds, mixed fire/shock draws without retries, shock progression, per-hop
chance thresholds, unique targets, exact chain limits, post-hit eligibility,
independent expiries, exact damage, stack replacement, spread capacity, source
attribution and cleanup, explosion defaults, bullet-pool exclusion, and rejection
of mixed explosion/stack/chain settings.

For gameplay verification, open this worktree's `.sbproj` in s&box, pick Fire Rounds,
then test fire bullets against enemies and crates. Burning enemies should display
the moving orange/yellow wisps, which disappear after the last fire stack expires.
Pick Shock Rounds to check blue bullets and brief cyan arcs. With Conductivity
maxed, the base shock must hit at most two enemies; each Chain Reaction pick adds
one target and 60 units of range per hop. Check walls, range, armor, lethal hops, round end, original-player electric
damage credit, and that shocks never leave timed stacks or lingering arc objects.
Kill an exploding enemy to verify its 3-second corpse warning and single blast:
nearby enemies and players take 40 explosive damage, targets beyond 100 units
or behind walls remain unharmed, and five armor blocks the hit. Check that the
corpse disappears after the 0.4-second blast visual and that round end cancels
pending explosions without damage.
Pick Layered Flames and Spreading
Flames to verify stacking and spread; Wildfire must not appear before spread is owned.
The editor validates networking and rendering as well as the C# compile.
Pick More Choices through all five ranks: future offers should grow from three to
eight cards when enough upgrades are unlocked. Verify keys 4-8, mouse selection,
wrapped card rows, queued picks, and a remote player's synchronized offer. With fire
locked, verify the picker shows fewer cards rather than duplicates.
