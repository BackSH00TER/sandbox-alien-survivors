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
pick caps, prerequisites, effects, presentation and rarity buckets live in `ModifierCatalog`.
Upgrade factory calls use C# named arguments (`stat:`, `amount:`, `prerequisite:`)
to identify each value without introducing a separate options object.
The fire-upgrade factory also accepts an optional `rarity:` override, defaulting
to `GameRarity.Common`; existing fire upgrades retain their Common rarity.
`DamageEffectDefinition.Resolve` adds each matching upgrade's increment multiplied
by its pick count; these player picks are distinct from effect stacks on a target.

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
directory explicitly. Tests cover prerequisite gating, exact rarity boundaries and
uniform within-bucket draws, shared rarity names and independent probability tables,
random-call counts, empty-bucket renormalization (including reroll replacements),
catalog effect callbacks, invalid IDs/rolls, offer uniqueness, More Choices ranks
and caps, small eligible pools, rare-modifier offer frequency,
proc thresholds, independent expiries, exact damage, stack replacement, spread capacity,
source attribution and cleanup.

For gameplay verification, open this worktree's `.sbproj` in s&box, pick Fire Rounds,
then test fire bullets against enemies and crates. Burning enemies should display
the moving orange/yellow wisps, which disappear after the last fire stack expires.
Pick Layered Flames and Spreading
Flames to verify stacking and spread; Wildfire must not appear before spread is owned.
The editor validates networking and rendering as well as the C# compile.
Pick More Choices through all five ranks: future offers should grow from three to
eight cards when enough upgrades are unlocked. Verify keys 4-8, mouse selection,
wrapped card rows, queued picks, and a remote player's synchronized offer. With fire
locked, verify the picker shows fewer cards rather than duplicates.
