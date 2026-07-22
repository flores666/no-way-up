# Line Zero architecture

## Current 3D scene structure

`res://scenes/3d/Main3D.tscn` is the configured application entry point. `Main3D`
is the composition root and explicitly binds one player, scene-owned noise system,
camera, level adapters, shared objective/power models, and event-driven Canvas UI.

```text
Main3D
├── TestLevel3D
│   ├── authored NavigationRegion3D and primitive world collision
│   ├── TunnelMutant3D
│   ├── pickups, container, hazard, and exposure zones
│   ├── PowerController3D, FuseBox3D, and powered light
│   └── EmergencyDoor3D and ObjectiveExitZone3D
├── Player3D
│   ├── normal/Crawl physical profiles
│   ├── four fixed gameplay sensors
│   ├── health and inventory components
│   └── aim, flashlight, footstep, and firearm adapters
├── NoiseSystem3D
├── fixed TopDownCamera3D and occlusion controller
└── event-driven gameplay UI and completion panel
```

Shared gameplay authority stays under `src/Gameplay`. Godot-specific spatial work
is split between `src/World2D` and `src/World3D`; there is no controller with a
2D/3D branch. `Main3D` caches every authored dependency during `_Ready`, and no hot
path searches the scene tree. The full hierarchy, collision map, and validation
boundary are documented in [`3d-migration.md`](3d-migration.md).

## Preserved legacy 2D scene structure

`res://scenes/main/Main.tscn` remains a directly runnable regression reference and composes
`MetroLevel01` as the current gameplay level. `res://scenes/main/TestMain.tscn`
uses the same composition root, player, noise system, and UI but instantiates the
technical `TestLevel` instead.

```text
Main / TestMain
├── World2D
│   ├── AmbientDarkness
│   ├── NoiseSystem2D
│   ├── PlayableLevel
│   │   ├── NavigationRegion2D
│   │   ├── Floor / train / landmark greybox visuals
│   │   ├── WallCollisions
│   │   ├── PowerSystems
│   │   │   ├── PowerCircuitComponent
│   │   │   └── PowerControlledLight2D instances
│   │   ├── LightExposureZones
│   │   ├── Interactions
│   │   │   ├── MaintenanceFuseBox
│   │   │   ├── EmergencyExitDoor
│   │   │   ├── loot containers and pickups
│   │   │   └── inspectable landmarks
│   │   ├── Hazards
│   │   ├── ExitZones
│   │   └── Mutants
│   └── Player
│       ├── normal and crawl movement colliders
│       ├── dedicated lighting, hazard, and objective sensors
│       ├── inventory / health / stamina
│       ├── pistol / flashlight / footstep emitters
│       └── Camera2D
└── UI
    ├── gameplay HUDs
    ├── inventory and loot transfer
    ├── interaction feedback
    └── EscapeCompletePanel
```

`PlayableLevelController2D` is the shared explicit level-composition adapter.
`TestLevelController2D` and `MetroLevelController2D` are sealed level types that
inherit it. The controller validates the navigation region, resolves the single
power circuit, fuse box, powered emergency door, objective exit zone, authored
mutants, hazards, level noise emitters, and every powered light once during
initialization. It exposes those dependencies to `Main` without process-time scene
tree searches.

`Main` resolves `%PlayableLevel` rather than a concrete level type. It binds the
scene-owned noise system, player target contracts, objective model, power circuit,
all power-controlled lights, emergency door, exit zone, and event-driven UI. This
keeps the TestLevel and MetroLevel01 interchangeable without adding a scene-loading
framework or service locator.

### MetroLevel01 structure and flow

`res://scenes/levels/MetroLevel01.tscn` contains a western ticket hall/concourse,
a high-risk central platform, a stopped three-car train with one traversable car,
a northern service corridor, a northeastern electrical room, a crawl-only cable
duct, track-side hazards, optional loot spaces, and a western emergency-exit route.

The replacement fuse is stored in the traversable train maintenance car. The one
existing fuse box and power circuit are in the electrical room. Restoring power
switches on the electrical-room, service-junction, and emergency-corridor light
instances; their exposure zones become active through the existing
`PowerControlledLight2D` behavior. The player then backtracks to the powered door
and completes through the dedicated `PlayerObjectiveSensor2D` and existing terminal
completion flow.

The crawl duct has a physical clear width larger than the crawl collider but smaller
than the normal movement collider. It provides an alternate connection from the
ticket hall to the service route and contains optional battery loot. Mutant
navigation excludes the duct, so the full-height eastern service stair remains the
AI route. Platform columns, train cars, ticket booths, corners, and narrow corridor
segments provide hearing attenuation, line-of-sight breaks, and combat cover.

Gameplay landmark text is authored as a `Label` below a positioned `Node2D` anchor.
The anchor owns world placement and the Label owns only local centering offsets. This
prevents direct Control children from collapsing onto one offset rectangle. The
normal gameplay composition disables the technical DebugHud and mutant health/death
labels; `TestMain.tscn` keeps technical telemetry available explicitly.

`TestLevel.tscn` remains unchanged as the compact technical map for regression
scenarios. `TestMain.tscn` keeps it directly runnable after `Main.tscn` changed to
MetroLevel01.

Greybox limitations are deliberate: primitive art only, no human NPC content, no
new quest framework, no dynamic level streaming, and navigation polygons require
manual review after layout edits.

## Source folders

| Folder | Responsibility |
| --- | --- |
| `src/Core` | Small composition scripts for application-level scenes. |
| `src/Data` | Typed Godot `Resource` definitions containing reusable scalar configuration. |
| `src/Gameplay/Interaction` | Dimension-independent interaction semantics and typed results. |
| `src/Gameplay/Health` | Plain health state, typed damage/change values, ownership, and the health-owning node component. |
| `src/Gameplay/Flashlight` | Validated static flashlight configuration, plain runtime charge/state, immutable results, and battery replacement coordination. |
| `src/Gameplay/Combat` | Reusable firearm definitions, magazine/reload state, and immutable operation results without 2D dependencies. |
| `src/Gameplay/Enemies` | Static mutant configuration and the dimension-independent typed state vocabulary. |
| `src/Gameplay/Movement` | Plain stamina state/results plus the typed movement-mode vocabulary and source contract. |
| `src/Gameplay/Noise` | Immutable typed noise metadata and the dimension-neutral listener contract; no coordinates or AI transitions. |
| `src/Gameplay/Perception` | The dimension-free visibility contract, categories, and immutable state snapshot. |
| `src/Gameplay/Items` | Static reusable item definitions, typed use effects, use context/results, and the use service. |
| `src/Gameplay/Inventory` | Fixed-capacity slots, add/remove/transfer rules, typed results, ownership, and the inventory-owning node component. |
| `src/World2D` | Code coupled to Godot 2D nodes, coordinates, movement, collision, and presentation. |
| `src/World2D/Hazards` | Reusable 2D overlap detection and timed environmental damage presentation. |
| `src/World2D/Combat` | Mouse-facing hitscan, reload timing, tracer presentation, and damageable 2D targets. |
| `src/World2D/Enemies` | Mutant navigation, perception, movement, melee timing, and world presentation. |
| `src/World2D/Noise` | 2D noise occurrences, scene-owned propagation, acoustic rays, listener adapters, and distance-based footsteps. |
| `src/World2D/Levels` | Shared playable-level composition, objective-exit integration, and explicit mutant target/noise binding. |
| `src/World2D/Perception` | Player visibility composition and reusable ambient-light Area2D zones. |
| `src/World2D/Interaction` | 2D detection, scoring, interactable areas, and demonstration behavior. |
| `src/World2D/Items` | Interactable 2D world-pickup presentation and removal. |
| `src/World3D` | Top-down 3D movement, camera, collision constants, and occlusion adapters. |
| `src/World3D/Interaction` | 3D candidate sensing, pickups/containers, fuse box, and emergency door adapters. |
| `src/World3D/Hazards` | Fixed 3D hazard sensing and bounded damage-zone timing. |
| `src/World3D/Flashlight` / `Perception` | SpotLight presentation and deterministic fixed-sensor visibility adapters. |
| `src/World3D/Noise` | Vector3 occurrences, bounded acoustic queries, and distance footstep emission. |
| `src/World3D/Combat` | Muzzle-authoritative hitscan, reload timing, damage transaction, and primitive shot presentation. |
| `src/World3D/Enemies` | NavigationAgent3D mutant movement, perception, hearing, melee, and terminal behavior. |
| `src/World3D/Objectives` / `Power` | Fixed objective sensor, exit zone, circuit owner, and powered presentation. |
| `src/UI` | Canvas-based developer and game user interfaces. |
| `data/player` | Inspector-editable resource instances, currently the default movement tuning. |
| `data/flashlight` | Static flashlight resource instances, currently the Service Flashlight. |
| `data/items` | Scrap, Battery, Filter, Medkit, and 9mm ammunition definitions plus reusable use-effect resources. |
| `data/weapons` | Static firearm definitions, currently the Service Pistol. |
| `data/enemies` | Validated static enemy definitions, currently the Tunnel Mutant. |
| `data/navigation` | Authored navigation polygons for the technical TestLevel and gameplay MetroLevel01 scenes. |
| `data/inventory` | Typed static seed-entry resources for sample containers, including medkits and locker ammunition. |
| `scenes/main` | Default Metro gameplay composition plus the directly runnable technical TestMain composition. |
| `scenes/player` | Reusable player presentation and controller scene. |
| `scenes/levels` | Reusable 2D level scenes. |
| `scenes/interactables` | Reusable sliding-door, inspectable-object, and loot-container scenes. |
| `scenes/hazards` | Reusable 2D environmental damage-zone presentation. |
| `scenes/combat` | Reusable greybox damageable-target presentation. |
| `scenes/enemies` | Reusable composed 2D mutant presentation. |
| `scenes/items` | Reusable 2D world-pickup scene. |
| `scenes/3d` | Configured 3D main, player, technical level, adapters, enemy, hazards, and debug UI. |
| `scenes/ui` | Reusable UI scene fragments. |
| `assets/generated` | Text-based assets made specifically for the project. |

`src/Gameplay` contains the small interaction, inventory, health, flashlight,
item-use, firearm-state, enemy-data, stamina, movement-mode, visibility, and noise-
metadata
contracts that remain valid across presentations.
It is not a generic gameplay framework, item database, AI framework, blackboard,
behavior tree, or capability registry.

## Health foundation

### HealthModel and typed changes

`HealthModel` is a plain C# class with no `Node2D`, vector, collision, or UI
dependency. Construction requires a maximum of at least one and initializes
current health to that maximum. The player configures 100 maximum health.
The model exposes `MaxHealth`, `CurrentHealth`, `IsAlive`, and `IsDead`, while all
mutation remains behind positive `ApplyDamage` and `ApplyHealing` requests.

Damage and healing clamp to `0..MaxHealth`. Damage at zero, healing at maximum,
and healing after death return immutable no-op results and emit nothing. This
stage deliberately has no resurrection path. Every `HealthChangeResult` reports
the previous and current values, requested and applied amounts, whether a change
occurred, and whether it caused death. `DamageInfo` validates a positive amount
and may carry a source `Node` plus an optional non-blank damage description; it
contains no spatial type.

Successful event order is explicit:

- damage emits `Changed`, then `Damaged`, then `Died` when the transition is
  lethal;
- healing emits `Changed`, then `Healed`;
- `Died` is guarded by the model and fires exactly once when positive health first
  reaches zero;
- refused or clamped-to-no-change requests emit no events.

The model validates constructor, request, and result invariants immediately so an
invalid zero/negative request or inconsistent change cannot silently propagate.

### Health ownership and component

`IHealthOwner` exposes only a `HealthModel`. `HealthComponent` is a reusable,
dimension-neutral Godot `Node` that validates its exported `MaxHealth` and creates
one model in `_Ready`. `PlayerController2D` implements the ownership interface by
exposing the composed component's initialized model; it contains no damage,
healing, or death mutation logic.

### DamageZone2D adapter

`DamageZone2D` is the 2D presentation adapter for environmental damage. Its
`Area2D` detects the explicitly bound `PlayerHazardSensor2D`, creates independent
per-target interval state, optionally applies one immediate entry hit, and sends
typed `DamageInfo` requests to the sensor's `HealthModel`. It supports simultaneous
targets, de-duplicates the same sensor or health model, removes exits immediately,
prunes invalid, dead, completed, or freed targets, and disables physics processing
when no targets remain.

Periodic timing is elapsed-time based rather than frame-count based. Each tracked
target owns `AccumulatedSeconds`, initialized to zero after the separate entry hit.
For every finite positive physics `delta`:

1. add `delta` to `AccumulatedSeconds`;
2. while `AccumulatedSeconds >= TickIntervalSeconds`, apply one periodic hit and
   subtract exactly one interval;
3. stop after four hits in that physics update;
4. retain every unapplied whole interval plus the fractional remainder for later
   updates.

A one-second interval with `3.25` seconds accumulated therefore applies three ticks
and retains approximately `0.25` seconds. If more than four ticks are due, four are
applied now and the remaining debt is carried into later updates, still limited to
four per update. Invalid or non-positive `delta` advances no timer. Exiting removes
the target and its complete pending timer. The entry hit never consumes or advances
the periodic accumulator.

The reusable scene uses a layer-zero area with a dedicated player-hazard-sensor
mask, so neither the solid body nor normal/Crawl collider changes can alter overlap
state. The test level places a primitive striped exposed-electrical floor. Its
10-point immediate hit and one-second interval are presentation configuration; the
shared health model does not know which dimension detected the overlap.

## Item and inventory foundation

### ItemDefinition

`ItemDefinition` is a typed Godot `Resource` containing only static data: stable
`Id`, display name, description, maximum stack size, and an optional typed
`ItemUseEffectDefinition`. It contains no runtime quantity, coordinate, texture,
or 2D presentation state. The current resources are `scrap` (maximum 20), `replacement_fuse` (maximum 1),
`battery` (maximum 5), `filter` (maximum 3), `medkit` (maximum 3), and
`pistol_ammo` (maximum 30). Scrap, Battery, Filter, and 9mm Ammunition have no use
effect; the Field Medkit references one reusable healing effect.

Stable IDs are used to decide whether two definitions represent the same item.
This avoids depending only on resource-object identity and is also the value that
future persistence data must record. Slot indices, quantities, and item IDs will
need persistence later; live scene nodes and resource references should not be
serialized as save identity.

### InventoryModel and slots

`InventoryModel` is a plain C# class with a fixed capacity supplied at construction.
It exposes its slots through `IReadOnlyList<InventorySlot>` and is independent from
`Node2D`, controls, collision, and world coordinates. Slot setters are private and
mutation is restricted to the model.

Slot invariants are validated during mutation:

- an empty slot has no item and quantity zero;
- a populated slot has a validated item and positive quantity;
- quantity never exceeds that item's maximum stack size.

`TryRemoveFromSlot` is the model's public controlled-removal operation. It validates
the slot index, positive request, current slots, and then removes no more than the
request or available stack. Removing the complete quantity clears both the item
reference and quantity. Its immutable `InventoryRemoveResult` reports the request,
removed quantity, unfulfilled request, and source remainder. A successful removal
emits exactly one `Changed` event; an empty-slot request changes nothing and emits
nothing. Slot setters remain inaccessible, so UI code cannot set slot state.

`TryAdd` first walks existing non-full stacks with the same stable item ID. It then
walks empty slots and creates stacks no larger than the definition's limit. Its
immutable `InventoryAddResult` reports requested, added, and remaining quantities,
plus full-success and nothing-added states. Excess quantities are returned to the
caller rather than discarded. One `Changed` event is raised after the complete
operation only when at least one unit was added.

`CountByItemId` validates the stable ID and reports the total across every matching
stack. `TryRemoveByItemId` validates a positive request, consumes matching stacks
in ascending slot order, removes no more than requested and available, and returns
an immutable `InventoryItemRemovalResult` with requested, removed, unfulfilled,
and remaining item quantities. Even when several stacks change, observers receive
exactly one `Changed` event after the inventory is consistent. No-match removal
changes nothing and emits nothing. Reloading uses these operations rather than a
separate ammunition store or direct slot access.

### Static inventory contents

`InventorySeedEntry` is a typed Godot `Resource` containing an `ItemDefinition`
and a positive initialization quantity. It has no runtime slot state and no 2D
types. `InventoryComponent` constructs its model, validates every exported seed
entry, and applies entries through the normal merge-first `TryAdd` rules. A null
entry, missing item, invalid quantity, or configuration that exceeds capacity
fails during scene initialization with a clear error. Runtime transfers never
write quantities back into seed resources. The player has no configured entries,
so its 12-slot inventory remains empty at startup.

### Inventory transfer transaction

`InventoryModel.TryTransferTo` is the only transfer mutation used by the panel. It
validates both distinct models, the source index, the positive request, all slots,
and the item definition before changing state. A bounded direct-loop calculation
then determines capacity from compatible partial stacks followed by empty slots.
The calculation never sums beyond the request, which keeps even unreasonable
integer requests overflow-safe.

After capacity is known, the model fills matching destination stacks first, then
empty slots, and removes exactly the same quantity from the selected source stack.
Both inventories are consistent before observers are notified. A successful
transaction raises exactly one `Changed` event on each model; a zero-capacity or
empty-source result raises none. This single logical operation prevents UI code
from adding first and guessing whether source removal will succeed.

Quantity conservation is structural: the destination receives
`TransferredQuantity`, and the source loses that exact value. Therefore, for the
stable item ID, source plus destination quantity is identical before and after a
complete, partial, or failed transfer. `InventoryTransferResult` is immutable and
reports the request, transferred quantity, untransferred request, source remainder,
full-success state, and nothing-transferred state.

### Inventory ownership

`InventoryComponent` is a dimension-independent Godot `Node` that creates and owns
one model using its exported slot capacity and optional static contents. The player
scene configures 12 slots.
`PlayerController2D` implements the small `IInventoryOwner` interface and exposes
the model without adding inventory state to `InteractionContext` or mixing stacking
logic into movement, aiming, or flashlight code.

### Item-use contract and service

`ItemUseEffectDefinition` is an abstract typed `Resource`. Its small contract
validates static effect configuration, checks whether an `ItemUseContext` is
eligible, applies the effect, and returns an immutable `ItemUseResult`. The base
contract has no `HealthModel`, 2D, UI, or medkit dependency. The context contains
only the valid actor `Node`, inventory model, and source slot index. Results expose
success, item consumption, a non-blank user message, and an optional positive
applied amount.

`HealingItemUseEffectDefinition` is the first concrete effect. Its exported heal
amount must be positive. It requires an `IHealthOwner`, refuses dead actors and
full health, calls `HealthModel.ApplyHealing`, and reports the actual restored
amount. The Field Medkit resource configures 35 requested healing. The effect
changes health but never removes inventory itself.

`ItemUseService.TryUseFromSlot` owns the cross-model operation:

1. validate the actor, inventory, index, slot, item definition, and effect;
2. return a normal failure for an empty slot or item without an effect;
3. call the effect's eligibility check before any mutation;
4. apply the eligible effect exactly once;
5. verify the effect did not consume or replace the source stack;
6. remove exactly one source item through `TryRemoveFromSlot` only after success;
7. return a consumed result carrying the effect's message and applied amount.

Expected refusal paths therefore consume nothing. A successful use raises exactly
one inventory `Changed` event through controlled removal, and the final unit clears
the slot. An unexpected effect that mutates inventory or claims to consume an item
throws clearly rather than leaving health and quantity silently desynchronized.
The current synchronous request path also means each button activation produces
at most one effect application and one consumption.

### World pickups

`WorldItemPickup2D` extends the existing `Interactable2D` adapter. It obtains the
initiating actor from `InteractionContext`, requires `IInventoryOwner`, and calls
the shared model's `TryAdd` method. A full pickup immediately becomes unavailable,
disables its interaction shape, and queues itself for deletion. A partial pickup
keeps the returned remainder in the world and updates its prompt and label. A full
inventory leaves the pickup unchanged.

The item definition, inventory model, slots, stack rules, ownership interface, and
UI are not 2D-specific. Only the pickup's `Area2D`, collision shape, polygons,
label, and world position belong to `World2D`. A future `WorldItemPickup3D` can use
an `Area3D` or other 3D interaction adapter while calling the same inventory API.

### Inventory containers

`IInventoryContainer` extends `IInventoryOwner` with only a player-facing display
name. It contains no scene, UI, coordinate, or 2D types. `LootContainer2D` adapts
that contract to `Interactable2D`, delegates storage to a composed
`InventoryComponent`, and generates `Search {ContainerDisplayName}` prompts. It
remains interactable when empty so items can be stored in it. Each world adapter owns
one monotonic `HasBeenSearched` flag: the first successful inventory-capable interaction
emits the configured interaction noise and marks that container searched; subsequent
openings remain fully accessible but emit no noise. Failed interactions and transfer-panel
close actions do not change this flag or publish world noise.

The reusable world scene supplies a greybox locker body, an interaction-layer
`Area2D`, and an optional solid world-layer `StaticBody2D`. These presentation and
collision nodes are deliberately 2D-specific. A future `LootContainer3D` can
compose the same component, seed entries, inventory model, container contract,
transfer operation, and Canvas UI while replacing only the world adapter and scene.

## Firearm combat foundation

### Static definition and runtime state

`FirearmDefinition` is a typed static `Resource`: stable ID, display name,
ammunition item definition, magazine capacity, damage, fire interval, reload
duration, and scalar range. It validates every invariant but contains no magazine
quantity. `ServicePistol.tres` configures `service_pistol`, `pistol_ammo`, an
eight-round magazine, 25 damage, a 0.25-second interval, a 1.2-second reload, and
700 world units of range.

`FirearmState` is plain C# with no node, vector, physics, input, timer, or UI
dependency. It owns one validated definition, current magazine ammunition, and
reload state. It reports `CanFire`, whether ammunition exists, and rounds needed.
Its operations consume one round, begin reload using a supplied reserve count,
complete reload with supplied rounds, or cancel reload. Magazine ammunition is
always within `0..MagazineCapacity`; no rejected operation changes state or emits
`Changed`. Successful fire, reload start, reload completion, and active reload
cancellation each emit exactly one event. Immutable `FirearmShotResult` and
`ReloadResult` values expose status, quantities, state changes, and messages.

The service pistol starts with three magazine rounds in `Player.tscn`. This is
runtime state configured on the controller, not data written into the shared
resource.

### Ammunition and reload transaction

`PistolAmmo.tres` is an ordinary non-usable `ItemDefinition` with stable ID
`pistol_ammo` and a maximum stack of 30. Pickups, merge-first stacking, twelve
player slots, container seeds, Take/Store transfers, UI rendering, and capacity
rules are exactly the existing inventory paths. The test level supplies pickups of
12 and 20 rounds plus 16 rounds in the Maintenance Locker.

Reload is asynchronous presentation coordination around synchronous model
operations. `PlayerWeaponController2D` begins `FirearmState` reload and starts its
one-shot timer without blocking the main thread. At timeout it re-counts the
current reserve, takes the smaller of current reserve and magazine need, removes
that exact amount through one deterministic `TryRemoveByItemId` transaction, and
completes state with exactly the removed rounds. Reserve changes during the timer
are therefore honored. Full and partial reloads preserve
`magazine + player reserve`; a canceled reload stops the timer and removes nothing.

Opening normal inventory, opening loot transfer, disabling gameplay, or dying
cancels reload deterministically. Repeated `R` requests while reloading, a full
magazine, or no reserve return normal unchanged results.

### PlayerWeaponController2D and hitscan

`PlayerWeaponController2D` is a separate player component. `PlayerController2D`
explicitly supplies the already-initialized player, inventory model, and health
model; exported weapon data and required muzzle, tracer, and timer nodes are
validated independently. The controller exposes its reusable `FirearmState`,
typed shot/reload events, short message requests, `SetCombatInputEnabled`, and
`CancelReload`. It contains no inventory UI logic.

Fire uses the `fire` Input Map action and only reacts to press events, so holding
the left mouse button cannot produce automatic fire. A monotonic timestamp enforces
the definition's interval. Before ammunition mutation, the controller validates the segment from the stable
player-side `WeaponOrigin` through the desired `MuzzlePoint` plus a one-unit clearance
margin against World bodies with inside-collider handling enabled. Any obstruction
returns typed `MuzzleObstructed` and produces no ammunition change, cooldown, tracer,
damage, or gunshot noise. Empty, reloading, interval, disabled, and dead requests
likewise consume nothing; empty messages retain their existing short rate limit.

For a valid shot, `AimPivot` supplies the mouse-facing direction. The physical ray
starts at `WeaponOrigin`, extends to the same configured endpoint previously derived
from the muzzle and range, checks World and DamageableTarget layers, handles an
inside-collider origin, and excludes the firing player's RID. The nearest collision
still ends the shot. Tracer and gunshot noise use that same player-side origin, so a
muzzle transformed inside or beyond a thin wall cannot bypass it. A collider may own
`IHealthOwner` directly or through its immediate parent; no scene-tree search occurs.
Damage, fire interval, ammunition, reload, and hearing configuration are unchanged.

### DamageableTarget2D

`DamageableTarget2D` is a reusable `StaticBody2D` implementing `IHealthOwner`
through a composed 100-health `HealthComponent`. It has no AI. Health events update
its world label, briefly flash its primitive body after a hit, and switch to a
destroyed palette at zero. Destroyed targets receive no later health mutation.
Their solid collider is intentionally retained: the wreck remains a movement and
ray obstacle instead of disappearing or allowing later shots to pass through.

The exposed corridor target demonstrates four 25-point hits. The second target is
placed behind the solid lower corridor wall; shooting from the corridor hits the
wall first and leaves that target at full health.

## Stage 8 corrections finalized in Stage 9

Three Stage 8 defects are fixed without changing the state vocabulary or creating
new global coordination:

- Direct valid sight is evaluated before timed/noise behavior and always selects
  `Chase` or in-range `Attack`. The navigation retry timestamp now gates only route
  assignment; a stuck visible mutant remains in `Chase` instead of reverting to
  patrol or investigation.
- `LostTargetGraceSeconds` has one definition. After a previously visible target is
  lost, the mutant keeps `Chase` for that duration while following the fixed last
  visible point. Valid sight can immediately restore chase/attack. Only expiry
  enters `ChaseLastKnownPosition`; unrelated noise cannot restart the timer.
- `NoiseHudController` binds to player health. Death stops the one-shot timer,
  immediately displays `SILENT`, and makes later/stale callbacks unable to present
  a pre-death noise level.

Stage 10 also corrects three Stage 9 movement defects without changing the stamina
model or noise propagation system:

- sprint drain and the effective Sprint mode now require meaningful post-
  `MoveAndSlide` displacement, so wall pressure neither spends stamina nor retains
  the sprint visibility multiplier;
- footsteps use accumulated traveled distance and retain normalized cycle progress
  across C/Z/mode transitions instead of resetting a timer;
- depletion latches the sprint request until the action is released. Recovery can
  proceed while Shift stays held, but reaching the restart threshold cannot clear
  that latch.

Stage 11 corrects three confirmed Stage 10 defects while preserving the same
movement modes, collision layers, noise propagation API, and Input Map semantics:

- `PlayerFootstepNoiseEmitter2D` accumulates normalized physical progress, physical
  distance, and acoustic contribution for each traveled segment using the mode that
  was active for that segment. A nearly completed Sprint segment therefore cannot
  be reclassified as a quiet Crawl/Crouch/Walk step by changing mode before the
  threshold.
- One physics sample processes every completed cycle. Stage 12 publishes at most
  three ordinary occurrences and never sums later cycles into a louder aggregate.
  Each emitted occurrence is independently capped to the configured Sprint
  intensity, deterministic ordinal descriptions prevent same-frame deduplication,
  and the final fractional cycle remains accumulated. Invalid delta, initialization,
  reload, and teleport-like displacement produce no occurrence.
- A Crawl exit performs the existing immediate normal-shape clearance check and a
  second check inside the deferred callback immediately before normal activation.
  If the second check fails, the callback atomically restores Crawl posture,
  velocity limit, requested/active profile, and one-collider invariant, then emits
  one rejection only when the original request asked for feedback.

## Player movement, stamina, and stealth

### Typed movement tuning and modes

`PlayerMovementSettings` remains a scalar Godot `Resource`; it stores no runtime
stamina, posture, collision node, or accumulated distance. `Validate` rejects
non-finite or non-positive values and enforces
`CrawlSpeed < CrouchSpeed < WalkSpeed < SprintSpeed`. It also requires crawl
visibility below crouch visibility, crawl footstep intensity below crouch
intensity, a crawl step-distance multiplier greater than one, a positive actual-
movement epsilon, a finite non-negative recovery delay, and minimum sprint stamina
within `0..MaximumStamina`.

The default resource configures crawl/walk-family speeds `77 / 121 / 220 / 341`,
acceleration 1250, deceleration 1550, maximum stamina 100, drain 25/second,
recovery 18/second, a 0.75-second recovery delay, restart threshold 10, crawl
visibility `0.40`, crawl footstep intensity multiplier `0.20`, crawl step-distance
multiplier `2.0`, and actual-movement epsilon `0.05`.

`MovementMode` has `Walk`, `Crouch`, `Sprint`, and `Crawl`. The controller separates
persistent normal/crouch/crawl posture from transient effective Sprint. `C` toggles
Walk/Crouch except while crawling. `Z` enters Crawl from either normal posture and
requests Crouch on exit. Entering Crawl or another explicit posture ends sprint and
requires Shift release. A fresh Shift press while crawling requests a clearance-
checked exit to Walk; only a successful exit allows the held sprint session to
start. Modal UI accepts no posture input, but ordinary inventory remembers Crouch
or Crawl. Death accepts no later movement-mode input.

`PlayerController2D` reads one `Input.GetVector`, caps it at unit length, selects one
configured target speed, applies the existing acceleration/deceleration and
`Velocity.MoveToward`, then calls `MoveAndSlide`. The real position delta after that
call classifies actual movement. A sprint request may select sprint target speed,
but only a delta at or above `MinimumActualMovementDistance` produces the effective
Sprint mode, visibility, event, and drain. A blocked request therefore settles on
Walk without alternating events on collision jitter, while its held sprint session
can resume naturally when displacement becomes possible.

### Crawl collision profiles and clearance

`Player.tscn` owns two separately instantiated `Shape2D` resources under explicitly
named `NormalCollisionShape` and `CrawlCollisionShape` nodes. Normal is used by Walk,
Crouch, and Sprint; only Crawl uses the smaller footprint. `_Ready` requires both
nodes, non-null distinct shape resources, and a World collision mask, then
establishes the normal-only startup invariant.

Profile requests are coalesced and applied deferred outside the locked physics
step. Movement is held at zero while a change is pending. Each completed callback
assigns both disabled flags as one operation and validates that exactly one profile
is active. Repeated Z input, modal input changes, and death therefore cannot strand
the player with both colliders active or both disabled. Death preserves a valid
current posture/profile rather than forcing an unsafe stand.

Before Crawl can exit, the controller creates `PhysicsShapeQueryParameters2D` from
the normal shape and its current global transform. It queries the current
`PhysicsDirectSpaceState2D` with the player's collision mask, bodies enabled, areas
disabled, and the player's own RID excluded. Interaction areas, pickups, and damage
triggers cannot reject a posture; every body treated as solid by the player's
movement mask can. The same query runs again in the deferred callback immediately
before the normal profile is enabled. A non-empty result at either point preserves
or restores Crawl, its smaller collider, movement mode, and velocity cap. Only the
original accepted exit request can produce one typed `PostureChangeRejected` event;
`Main` routes `Cannot stand here.` to the existing message presenter and never
performs the physics test or shape switch itself.

The smaller shape is a top-down footprint abstraction, not vertical height
simulation. Entering Crawl does not move the body, and blocked exits are never
resolved by teleportation, forced overlap, wall changes, or collider disabling.

### Plain stamina model and controller policy

`StaminaModel` is a sealed plain C# object with no Godot, input, vector, process, or
UI dependency. Construction requires positive finite `Maximum` and starts `Current`
at that value. `Consume` and `Restore` accept only positive finite requests, clamp
to `0..Maximum`, and return an immutable `StaminaChangeResult` containing previous,
current, requested, applied, and derived `Changed` values. A clamped no-op emits
nothing. A real mutation emits `Changed` exactly once, followed by `Depleted` only
for `>0 -> 0` or `RecoveredFromEmpty` only for `0 -> >0`.

The 2D controller owns one model instance and applies delta-scaled drain only after
a sprint request produces meaningful actual displacement. Raw direction, target
velocity, and non-zero velocity against a wall are insufficient. Reaching zero
changes mode to Walk in the same physics tick, ends the held sprint session, and
sets a release latch. Recovery can cross 10 while Shift remains held, but the latch
clears only on a real action release; a subsequent press may start a new session.

Recovery accounts only for time after the configured delay from the last request
that actually consumed stamina; crossing the delay within a physics tick does not
recover for the pre-delay fraction. It continues while walking, crouching,
crawling, idle, blocked, or input-blocked UI is open. Crawl never consumes stamina.
The health death event permanently stops recovery for the current run.

### Visibility target contract

`IVisibilityTarget` contains only a positive scalar `VisibilityMultiplier`; it has
no `Node2D`, vector, ray, or navigation type. `PlayerController2D` preserves this
contract but delegates it to `PlayerVisibilityController2D`, which combines the
existing posture values (`0.40` Crawl, `0.65` Crouch, `1.00` Walk, `1.15` Sprint)
with authored ambient exposure and actual flashlight on/off state. `Main` supplies
the same typed target alongside the node and health owner. Mutants validate the
final scalar and multiply only their sight range. Hearing, FOV, wall rays, attack
range, target memory, and close-range rules remain unchanged; death remains the
separate target-health gate. The dimension-free contract and visibility snapshot
can be reused by a future 3D controller while only zone and light presentation
adapters change.

## Mutant enemy slice

### Static definition and explicit state

`MutantDefinition` is a typed `Resource` containing only static scalar tuning:
stable ID, display name, maximum health, movement speed and acceleration, sight
range and field of view, perception and chase refresh intervals, lost-target grace,
hearing sensitivity and minimum audible intensity, investigation/search timing,
stuck time and minimum progress, attack range, damage and cooldown, and patrol wait
duration. Validation rejects empty names, non-positive values, non-finite numeric
values, FOV outside `(0, 360]`, and attack range beyond sight range. It stores no
node, target, timer, position, runtime health, or current state and contains no 2D
type.

`TunnelMutant.tres` configures `tunnel_mutant`: 75 health, 90 movement speed, 500
acceleration, 320 sight range, a 110-degree FOV, 0.12-second perception, a
0.15-second chase-path refresh, two seconds of lost-target grace, 1.0 hearing
sensitivity, 0.2 minimum audible intensity, a two-second investigation wait, a
five-second search/travel ceiling, 1.25-second stuck windows, eight-unit minimum
progress, 34 attack range, 15 damage, a one-second cooldown, and a one-second patrol
wait.

`MutantState` is a typed enum with distinct `Investigate` and
`ChaseLastKnownPosition` meanings. Investigation follows an anonymous heard point;
search follows the player's last confirmed point. Keeping both explicit prevents
ordinary noise from becoming exact player knowledge and gives each behavior its own
entry, timeout, display, and exit rules. `MutantController2D.TransitionTo` owns all
entry/exit work, ignores same-state transitions, publishes `StateChanged` once per
real transition, and makes `Dead` terminal.

Perception checks, validated player damage, and delivered noises now record stimuli
before `UpdateDecisionState` resolves one effective state. The priority is terminal
death, direct visible living target (`Attack` by range or `Chase`), confirmed target
memory inside `LostTargetGraceSeconds`, last-known-position search, accepted
investigation, then patrol/idle. A noise callback never changes state directly.
While confirmed pursuit is active, lower-priority noise is discarded without
touching navigation, search time, attack cooldown, or target memory. A noise emitted
by the bound living target may refresh the last-known point during grace, but the
state remains `Chase`. Validated damage from that target records its current position
and immediately resolves to `Chase` or the already-valid in-range `Attack`; it never
passes through `Investigate`. Environmental damage does not reveal the player.

Noises received between two physics decisions are reduced to one deterministic
stimulus. Bound-target noise is preferred while confirmed pursuit is active;
otherwise the highest calculated kind/intensity/proximity score wins, with the
lowest sequence ID as a stable tie-break. Search still ignores ordinary footsteps
but may replace expired visual memory with a stronger gunshot investigation.

| From | Condition | To |
| --- | --- | --- |
| `Idle` | A configured patrol route exists at startup or after a search | `Patrol` |
| `Idle` / `Patrol` | An audible, accepted occurrence arrives | `Investigate` |
| `Investigate` | A significantly higher-priority recent noise arrives | `Investigate` with the new point |
| `Investigate` | Living player passes range, FOV, and line-of-sight checks | `Chase` or `Attack` by range |
| `Investigate` | Arrival plus alert wait, travel timeout, navigation failure, or repeated stall | `Patrol` or `Idle` |
| `Idle` / `Patrol` | Living player passes range, FOV, and line-of-sight checks | `Chase` |
| `Chase` | Living, visible player is within 34 units | `Attack` |
| `Attack` | Visible player leaves melee range | `Chase` |
| `Attack` | Sight is lost after a known visible position | `Chase` toward the fixed last-known point |
| `Chase` | Sight remains lost for less than `LostTargetGraceSeconds` | `Chase` toward the fixed last-known point |
| `Chase` | Sight remains lost for `LostTargetGraceSeconds` | `ChaseLastKnownPosition` |
| `ChaseLastKnownPosition` | Player is seen again | `Chase` or `Attack` by range |
| `ChaseLastKnownPosition` | A strong gunshot is selected as the best pending stimulus | `Investigate` |
| `Idle` / `Patrol` / expired pursuit | Best pending relevant noise is resolved | `Investigate` |
| `Chase` / `Attack` / lost-target grace | Any lower-priority noise | unchanged; bound-target noise may update memory only |
| Any living non-dead state | Validated damage from the bound living player | `Chase` or preserved visible in-range `Attack` |
| `ChaseLastKnownPosition` | Arrival, five-second maximum, navigation failure, or repeated stall | `Patrol` or `Idle` |
| Any living state | Mutant health reaches zero | `Dead` |
| Any living pursuit state | Bound player dies or exits the tree | `Idle` |

### Composition, navigation, and patrol

`Main` resolves the existing `%Player` and `%PlayableLevel` once. After child scenes are
ready, it calls `PlayableLevelController2D.BindMutantTargets(player, player, player)`.
The level controller forwards that typed `Node2D`/`IHealthOwner`/
`IVisibilityTarget` set to every validated
direct mutant child; zero, one, two, or more direct mutants are valid. A non-mutant
direct child fails initialization clearly. A mutant rejects missing, duplicate,
freed, or self bindings; it subscribes once to target death and tree exit and
unsubscribes on its own exit or death. No AI frame searches a group or the scene
tree, and the shared definition never depends on `PlayerController2D`.

`TestLevelNavigation.tres` is a static `NavigationPolygon` inset for the mutant's
14-unit radius. It covers the starting room, corridor, doorway path, and side room
while excluding the solid boundaries, lower corridor wall, both solid loot
containers, and both permanent target dummies. These static cutouts avoid runtime
rebaking and keep paths from deliberately crossing permanent props. The level
validates that the region is enabled, has navigation layers, and contains walkable
polygons. Stage 10 extends the starting room west through a 24-unit maintenance
opening, a narrow duct, and an open pocket with one existing Battery pickup. Those
coordinates remain entirely outside the unchanged navigation polygon, and the
mutant's 28-unit body profile cannot cross the physical opening. Mutants therefore
cannot navigate or push into the crawl route; no dynamic rebake or special AI state
is involved.
Each mutant validates and owns a `NavigationAgent2D`; it waits until the navigation
map has synchronized and owns its current point before submitting a deferred
destination.

The patrol route is an exported array of offsets converted once to world points
relative to spawn. The corridor mutant visits its two points in array order, waits
one second at each, and wraps deterministically. The guard has no points and starts
idle. Patrol destinations change only on point advance. Visible chase destinations
refresh every 0.15 seconds; after sight loss, chase refreshes only the fixed
last-known destination, and search assigns that same point on state entry. While a
target is active, physics processing reads the agent's next path
position, accelerates velocity toward it, and calls `MoveAndSlide`. Idle, waits,
attack, stopped navigation, and death have no navigation intent or stale velocity.
The body never teleports, visual facing follows meaningful movement while the root
collision shape remains unrotated, and crowd avoidance is deliberately disabled.

`StopNavigation` now clears the desired and active-target flags, clears path-sample
and expected-motion state, assigns the agent's target to the mutant's current
position, zeroes velocity, and resets progress recovery. While navigation actually
expects movement, the controller measures displacement over a configured
1.25-second window. Eight units is meaningful progress. The first failed window
forces a fresh target submission and emits a typed repath event; a second failed
window abandons investigation/search, skips a blocked patrol point after its normal
wait, or stops a stuck chase route and starts a bounded reassignment cooldown.
The cooldown never changes the state out of `Chase`: current direct sight remains
authoritative, and only route submission waits. Finished-but-not-reached navigation
is treated as failure rather than successful exact arrival.

The sliding door remains physical geometry and the navigation polygon is not
dynamically rebaked. A closed door can therefore invalidate a static path that
crosses the doorway. This is the documented dynamic-navigation limitation; the
same bounded repath/abandon mechanism prevents permanent pushing without adding
`NavigationObstacle2D` nodes that would not affect the current non-avoidance agent
configuration.

### Sight, search, and melee

Perception runs every 0.12 seconds rather than every physics frame. A detection
requires a bound living target, squared distance within `SightRange ×
VisibilityMultiplier`, facing dot product inside the unchanged 110-degree cone,
and an unobstructed physics ray using the World mask. The multiplier affects range
only. The mutant's RID is stored once in a reused exclusion array. Walls, the door,
target dummies, containers, and other World bodies block sight; the target itself
or its immediate collider child is accepted if included by a customized mask. The
flashlight is not consulted.

Every successful sight sample records the player's current position, marks the
memory as confirmed, and resets one meaningful timer. If sight is then lost from
chase or attack, the mutant remains in `Chase` for exactly
`LostTargetGraceSeconds` and navigates only toward that fixed last-visible point—
never the hidden target's live position. Reacquisition selects `Chase` or `Attack`
immediately by current range. Unrelated noise cannot alter or restart this timer.
A noise whose source is the already-confirmed bound player may update the remembered
world point during grace, but it cannot change state. Only after grace expires does
the controller enter `ChaseLastKnownPosition` (`SEARCH`). A selected gunshot may then
redirect search into anonymous investigation; ordinary footsteps cannot replace
confirmed visual memory.

Search terminates on target-distance/agent arrival, finished-but-unreached
navigation, repeated lack of progress, or `MaximumSearchSeconds`, so an unreachable
exact point cannot create the Stage 7 infinite `SEARCH` bug. A validated pistol
damage source seeds confirmed player memory and resolves directly to `Chase`; it no
longer produces an intermediate search or investigation transition. Unrelated
damage does not identify the player. Direct visible detection is checked before
noise/timed behavior and always enters pursuit even while navigation retry is
blocked; the retry timestamp gates only destination assignment. Navigation target
submission also ignores sub-pixel-equivalent destinations, preventing state-neutral
stimuli from restarting an unchanged route.

Attack entry stops navigation and velocity. A short one-shot timer provides a
0.18-second color/scale telegraph, with only one pending strike at a time. At timer
completion the controller rechecks target lifetime, health, 34-unit distance, and
current wall line of sight before issuing exactly one
`DamageInfo(15, mutant, "MutantMelee")`. A monotonic timestamp enforces the real
one-second cooldown. Leaving attack, losing the target, mutant death, target death,
or target removal stops the timer and clears the pending strike, preventing delayed
or duplicated damage.

### Noise, hearing, and investigation

`src/Gameplay/Noise` defines the shared vocabulary. Immutable `NoiseEvent` carries
an active source `Node`, `NoiseKind`, positive finite intensity, monotonic sequence,
timestamp, and optional validated description. It deliberately has no `Vector2`.
`NoiseOccurrence2D` adds the validated world position only in the 2D adapter, and
`PerceivedNoise2D` reports listener-specific distance, effective radius, attenuated
intensity, and occlusion. `INoiseListener` contains only source/liveness/hearing
tuning; `INoiseListener2D` adds the node position and typed delivery callback. A
future 3D occurrence/system can reuse the metadata and listener policy with
`Vector3` without adding dimension branches to the event.

One `NoiseSystem2D` lives under `Main/World2D`. `Main` only coordinates bindings:
the player binds its weapon and footstep components, while the level controller
registers its cached mutant list and binds the selected direct interaction emitters.
There is no static event bus, autoload, service locator, per-frame hearing scan, or
per-emission scene-tree search. Registration rejects duplicates, unregistration is
typed/idempotent, and delivery prunes freed listeners. Each occurrence is processed
immediately and discarded; identical source/kind/intensity/position/collider/
description emissions in one process frame return the first occurrence rather than
publishing or delivering twice.

The physical hearing radius is deterministic:

```text
perceived intensity = original intensity × wall attenuation ^ barrier count
effective radius = base radius × perceived intensity × listener sensitivity
```

Current base radii are 130 for footsteps, 180 for interactions, and 650 for
gunshots. Tunnel mutants use sensitivity `1.0` and minimum audible intensity `0.2`.
The system rejects listeners outside the unoccluded radius before querying physics.
For each remaining listener it intersects one reusable `SegmentShape2D` against
mask 1 (`World`), reusing one RID-exclusion array and excluding supplied origin,
source, and listener colliders. Results are deduplicated by `(collider_id,
shape_index)` because the test level deliberately groups many independent wall
shapes under one `StaticBody2D`. At most 16 raw intersections are requested and at
most four distinct barriers are processed. Each barrier applies the configured
factor `0.5`, so one through four walls retain `50%`, `25%`, `12.5%`, and `6.25%`
of the original intensity. The attenuated intensity and radius are checked again.
The system does not trace reflections, materials, portals, rooms, alternate paths,
or frequency bands. Thus several walls usually suppress footsteps, while a nearby
gunshot remains stronger because its unchanged base radius is five times larger.

Each living mutant scores a delivered occurrence locally. Gunshot kind weight is
highest, interaction is medium, and footstep is lowest; perceived intensity and
normalized proximity add to the score. The accepted score decays with age, and a
new occurrence must exceed it by a fixed replacement margin. This allows a strong
recent interaction or gunshot to redirect an investigation while preventing
similar repeated footsteps from flickering the destination. Sequence IDs are
processed once. Dead mutants, self/descendant sources, current direct sight, chase,
and attack reject investigation; each mutant otherwise evaluates the same
occurrence independently with its own position and tuning.

Acceptance stores only the heard position and enters `Investigate`; it does not
grant player identity or permission to attack. The state follows ordinary
`NavigationAgent2D` movement and facing, waits at the point for
`InvestigationDurationSeconds`, and returns to the same patrol route or idle. The
same arrival, maximum-travel, failure, and two-window stuck exits apply. A fresh
perception sample of the living player always overrides investigation with direct
chase/attack logic.

`PlayerFootstepNoiseEmitter2D` is a dedicated child component initialized with the
player, existing health model, and validated movement settings. It reads the actual
post-movement position and typed effective mode; it neither recalculates input nor
uses velocity as proof of travel. Crawl, crouch, walk, and sprint use distance/
intensity pairs `220 / 0.20`, `132 / 0.45`, `110 / 1.0`, and `90 / 1.8`.

One normalized `0..1` step cycle advances by
`segment distance / segment-mode threshold`. For the same consumed segment the
emitter also accumulates physical distance and
`normalized segment progress × segment-mode intensity`. A mode transition does not
reset any accumulator. When a cycle completes, its emitted intensity is the sum of
all contributing segments rather than the tuning of the final mode. This removes
the mode-switch downgrade exploit while retaining deterministic proportional
progress.

A single physics sample may complete multiple cycles. The emitter records the first
three cycle intensities individually and combines every later completed cycle into
one fourth occurrence with summed intensity, preserving equivalent acoustic
strength while bounding event delivery. Fractional progress remains for the next
sample. Initialization records position without emission; a displacement larger
than three times plausible sprint travel for that delta is treated as teleport-like
and resets the partial cycle. Invalid/non-positive delta also resets without
emission. Stationary/blocked travel below the shared epsilon, disabled gameplay
input, and death emit nothing. Normalized input prevents diagonal over-generation.

`PlayerWeaponController2D` emits exactly one gunshot after one accepted round is
consumed and the hit/miss ray completes. Empty magazine, fire interval, modal
combat blocking, and death paths return before emission. `SlidingDoor2D` emits one
interaction when its one open action starts; `LootContainer2D` emits one for each
successful player inventory-search action. Terminals, inspection messages, panel
controls, and transfer operations remain silent. Emitters receive
`NoiseSystem2D` explicitly and contain no mutant references.

`NoiseHudController` subscribes to `NoiseSystem2D.NoiseEmitted` and the bound player
health model. It filters the player/descendant source, maps crawl/crouch/walk
footsteps to `LOW`, sprint footsteps and interactions to `MEDIUM`, and gunshots to
`LOUD`, then
restarts one 1.2-second timer that returns to `SILENT`. The death callback marks the
HUD terminal for the current player, stops that timer, and synchronously sets
`SILENT`; subsequent emissions are rejected and a queued timeout can only repeat
the silent reset. It is player feedback, not a mutant detection meter, and reveals
no listener state. It performs no frame or inventory polling.

### Health, feedback, and death

`TunnelMutant2D.tscn` composes the existing `HealthComponent` at 75 maximum and
implements `IHealthOwner` by exposing that model. The current pistol therefore
produces exactly `75 -> 50 -> 25 -> 0`. Health events—not frame polling—show the
world label after damage, apply a brief body flash, and mark `MUTANT DEAD`. The
optional state label is disabled in the reusable scene and enabled on the two test
instances for manual validation.

Death enters `Dead` once, stops navigation and every behavior timer, clears patrol
wait/search state, cancels the attack telegraph, zeroes velocity, unsubscribes from
the target, disables the collider and all collision layers/masks, and recolors the
visible primitive corpse. The node remains in the scene but is non-blocking and no
longer reachable by pistol rays. `HealthModel` rejects further mutation at zero, so
even a retained direct reference cannot create later health changes. There is no
free, loot, respawn, or corpse interaction path.

When player health publishes its single death event, every living mutant cancels
pending melee and enters stationary `Idle`. `Main` remains authoritative for panel
closure and permanent current-run player input locking; a later UI close cannot
restore movement, interaction, or combat. The player controller ends sprint, clears
velocity, and stops stamina recovery; the noise HUD resets immediately and the
stamina HUD changes to its disabled presentation.

## Interaction contract

`IInteractable` contains no `Vector2`, `Area2D`, `PhysicsBody2D`, or ray-query
types. It exposes a prompt, availability check, and interaction operation.
`InteractionContext` identifies the initiating actor as a Godot `Node`, which is
valid for both 2D and 3D presentations. `InteractionResult` optionally carries one
short message in a typed form instead of an unstructured dictionary.

`Interactable2D` adapts that shared contract to a monitorable `Area2D`. It validates
the interaction collision layer and shape, exposes a 2D interaction position, and
provides an explicit priority. Door and terminal behavior remain in their own
controllers rather than the base class.

## PlayerInteractor2D responsibilities

`PlayerInteractor2D` is a component of the player scene and is separate from
`PlayerController2D`. It:

- listens for nearby `Interactable2D` areas;
- removes areas that leave range or are freed;
- applies a strict center-distance limit even when detection shapes overlap;
- selects one available target using distance, mouse-facing alignment, and priority;
- checks world line of sight before accepting a candidate;
- processes the `interact` Input Map action;
- emits prompt changes and optional messages through typed C# events;
- publishes the executed `IInteractable`, context, and result through a typed
  interaction-completed event;
- supports explicit input disabling that clears the prompt and restores the best
  nearby target when re-enabled.

The detector recomputes scores during physics processing because the player,
camera, mouse aim, and objects may move. It reuses its candidate list and does not
use LINQ or rebuild UI nodes.

### Deterministic candidate selection

Candidate score weights are 55% distance, 40% facing alignment, and 5% explicit
priority. Equal scores use the Godot instance identifier as a deterministic
tie-breaker. The current target remains selected until another candidate exceeds
its score by the configured switch threshold (0.08 by default). This hysteresis
prevents prompt flicker around nearly equal scores.

The aim direction comes from the existing `AimPivot`. When the mouse is directly
over the player, Stage 1 keeps the pivot's previous valid rotation, so interaction
selection also retains a stable facing direction.

## Collision layers and masks

| Layer | Purpose | Users |
| --- | --- | --- |
| 1 — `World` | Solid movement, sight, and simple acoustic occlusion | Player, mutants, walls, sliding-door body, loot-container bodies, target dummies, sight rays, and noise rays |
| 2 — `Interaction` | Non-solid discoverable areas | Sliding-door, inspectable, world-pickup, and loot-container roots |
| 3 — `DamageableTarget` | Explicit hitscan target classification | Mutant and damageable target-dummy roots, and the pistol ray |
| 4 — `LightExposureSensor` | Non-solid player exposure sensing | The fixed player sensor and authored light-exposure zones |
| 5 — `PlayerHazardSensor` | Non-solid stable environmental-hazard sensing | The fixed player hazard sensor and damage zones |

- The player body uses layer 1 and mask 1.
- Walls and the sliding `AnimatableBody2D` use layer 1 and mask 1.
- Interactable `Area2D` roots use layer 2, mask 0, and remain monitorable.
- Loot-container `StaticBody2D` children use layer 1 and mask 1, so they block the
  player while remaining valid line-of-sight hits for their owning interactable.
- The player's `DetectionArea` uses layer 0, mask 2, and is not monitorable.
- The player's `LightExposureSensor2D` uses layer 4 (`8`), mask 0, is
  monitorable, and has a fixed 10-unit circular shape independent of the normal and
  Crawl movement colliders. Light-exposure zones use layer 0, mask 8, and monitor
  areas only.
- The player's `PlayerHazardSensor2D` uses layer 5 (`16`), mask 0, is
  monitorable, and owns a fixed 10-unit circular shape independent of both movement
  profiles. Damage-zone areas use layer 0, mask 16, are not monitorable, and listen
  only for that sensor through `AreaEntered`/`AreaExited`; they never observe the
  World-layer `CharacterBody2D`.
- Target-dummy roots use layers 1 and 3 (`5`) with mask 1. Layer 1 makes them solid;
  layer 3 deliberately classifies them for combat queries.
- Living mutant roots also use layers 1 and 3 (`5`) with mask 1. They collide with
  World geometry, block World sight rays, and are classified for pistol hits without
  creating an Interaction-layer prompt. Death sets layer and mask to zero and
  disables the shape, leaving a visible non-blocking corpse.
- The pistol ray uses mask 5 (World plus DamageableTarget) and excludes the player
  body RID. The first returned body or area ends the shot, including ordinary walls.
- Mutant perception and melee-validation rays use mask 1 (World) and reuse an
  exclusion array containing that mutant's own body RID. An unrelated World body
  blocks the ray, so neither detection nor melee passes through walls.
- `NoiseSystem2D` uses mask 1 and intersects the direct acoustic segment with a
  bounded set of World collision shapes. It excludes the supplied emission-origin
  body, source body, and current listener body, deduplicates `(collider_id,
  shape_index)`, processes at most four barriers, and multiplies attenuation once
  per barrier. It does not calculate reflections or material response.
- The player interaction line-of-sight ray checks only layer 1 and excludes the
  player's body RID.
  A solid collider belonging to the selected interaction object is accepted, which
  allows the closed door itself to be targeted without allowing interaction through
  unrelated walls.

## UI communication

The main scene is the explicit composition point. It subscribes
`InteractionPromptController` and `InteractionMessageController` to typed events
from `PlayerInteractor2D`, and unsubscribes when leaving the tree. It obtains the
inventory through `IInventoryOwner` and health through `IHealthOwner`, then binds
the health and inventory views explicitly. It also resolves the composed weapon
controller, binds `WeaponHudController` to its state and the same inventory, and
routes weapon messages through the existing timed message presenter. It also binds
`NoiseHudController` to the scene-owned noise stream, player source, and player
health, and binds `StaminaHudController` to the plain stamina model,
`IMovementModeSource`, and health model. It resolves the dedicated flashlight
controller, binds `FlashlightHudController` to its model and the same player
inventory, and handles the controller's replacement request through one
`FlashlightBatteryService`. The same composition pass resolves the active
`PlayableLevelController2D` and supplies the already resolved
player/health/visibility set to every cached mutant exactly once. When an executed target implements
`IInventoryContainer`, `Main` verifies the interaction actor's `IInventoryOwner`,
passes the two models and container name to `LootTransferPanelController`, and
tracks the active container only as a validated node for lifetime cleanup.
Containers do not search for UI, open panels, contain transfer rules, or use
absolute scene paths.

The prompt controller changes its label only when target or prompt state changes.
The message controller owns one reusable one-shot `Timer`; repeated interactions
stop and restart that timer instead of creating overlapping timers.
The inventory panel subscribes to the model's single `Changed` event, refreshes its
12 `ItemList` rows only after changes, and unsubscribes when leaving the tree. It
shows empty slots, maintains the selected populated slot or chooses the nearest
valid replacement after mutation, and displays the selected definition's name and
description. Its Use button is disabled for no selection, an empty slot, a missing
effect, or a dead actor. The panel only emits typed `UseRequested(slotIndex)` and
re-validates the slot before doing so; it never mutates health or inventory.
The inventory toggle is handled in `_Input`, before focused `ItemList` or `Button`
controls can consume Tab. This preserves reliable open/close behavior while UI
focus is inside the panel. An `OpenStateChanged` event tells `Main` to block world
and combat input for the panel's complete visible lifetime.

`Main` receives that request, calls its owned `ItemUseService` with the player and
player inventory, and forwards the result message to the existing timed message
controller. Health and inventory events independently refresh their bound views,
so `Main` does not manually rewrite either view. `HealthHudController` subscribes
to `HealthModel.Changed`, updates its bar and numeric text immediately, and removes
the subscription on rebinding or tree exit. These views do not poll per frame.

`WeaponHudController` subscribes once to `FirearmState.Changed` and
`InventoryModel.Changed`. It derives reserve ammunition with `CountByItemId` and
shows the definition name, magazine/reserve counts, `RELOADING...`, empty state,
or the disabled death state. Rebinding and tree exit remove both subscriptions.
No frame polling or controller-to-UI lookup is used.

`NoiseHudController` is likewise event-only. It subscribes once to emitted
occurrences and player death, ignores non-player sources, restarts one silence
timer, and removes both subscriptions on exit. It does not poll movement, inventory,
weapons, or mutants and does not expose whether any listener accepted the
occurrence.

`StaminaHudController` is bound explicitly rather than finding the player. It
subscribes to `StaminaModel.Changed`, `IMovementModeSource.MovementModeChanged`, and
player death. Those events update one progress bar, `STAMINA current / maximum`,
and `MODE: WALK/CROUCH/SPRINT/CRAWL` with no process callback. The progress bar uses
the exact `StaminaModel.Current`; numeric current stamina uses deterministic
one-decimal `MidpointRounding.AwayFromZero`. `Main` also supplies the configured
`MinimumStaminaToStartSprint` explicitly. If normal rounding would display that
threshold or maximum before the model has actually reached it, the HUD clamps the
text to the lower tenth. Consequently a real value below either gameplay boundary
cannot be presented as though it had already reached it. Death changes the existing
panel to a disabled
presentation; tree exit removes all three subscriptions. Its compact left-column placement begins below the noise HUD and does not intersect
the health, weapon, inventory, transfer, prompt, or message layouts. The technical
debug HUD is disabled in the gameplay composition.

`FlashlightHudController` is also event-only. `Main` binds the explicit
`FlashlightModel` and player `InventoryModel`; the HUD subscribes once to each
`Changed` event, derives reserve count through stable item ID `battery`, and removes
both subscriptions on rebind or tree exit. Rebinding the same pair refreshes without
adding handlers. It shows `FLASHLIGHT`, the charge bar and numeric value,
`ON`, `OFF`, `ON/OFF · LOW`, `ON/OFF · CRITICAL`, `EMPTY`, or `DEAD`, and never mutates either model. Its compact anchored left-column rectangle begins below the stamina panel and avoids
the health, weapon, noise, interaction, inventory, and transfer layouts.

`LootTransferPanelController` binds directly to the two supplied models, renders
every fixed slot in two `ItemList` controls, and subscribes once to each model's
`Changed` event. Closing or rebinding removes both subscriptions. Selection and UI
refresh never mutate inventories. The four buttons only choose source, slot, and
request size before calling the shared transaction. A local status label reports
success, partial transfer, full destination, or invalid selection.

The transfer panel is modal without pausing the scene tree. `Main` centralizes one
authoritative rule: movement, interaction, and combat are enabled only while the
player is alive and neither inventory nor transfer UI blocks them. The ordinary
inventory toggle remains enabled while its own panel is open so the fixed `_Input`
Tab path can close it. On transfer open it closes and disables that regular panel.
Either UI clears player velocity, ends sprint, blocks movement-mode, new F/B
flashlight requests, world interaction, and combat input, cancels reload, suppresses
footsteps, and hides the
prompt. Crouch and Crawl remain remembered through normal inventory and become
effective again after close. Mouse and keyboard UI continue to process normally;
C/Z events cannot reach the controller while a blocking panel is active. The Close
button or built-in `ui_cancel` action emits `Closed`;
`Main` re-evaluates the same rule rather than blindly enabling input. If the active
container emits `TreeExiting`, the same close path unsubscribes the UI without
retaining a freed node reference.

Neither normal inventory nor loot transfer pauses the scene tree. Consequently
mutant perception, navigation, investigation/search timeouts, melee cooldowns, and
an already-on flashlight's charge drain remain on world time while either panel is
visible. Stamina recovery also continues
because the player is alive and no drain occurs, but velocity stays zero and no
footstep can pass the actual-travel/input gates. Those player policies remain
centralized in `Main` and do not leak modal or GUI state into the mutant controller.
Focused controls cannot toggle crouch/crawl/sprint, interact, generate combat,
toggle the flashlight, or replace a battery for a panel's full visible lifetime.
When a panel closes, the flashlight controller requires both F and B to be released
before accepting either action, so the close event cannot leak into gameplay.

When combat is re-enabled, the weapon controller suppresses fire through the
current process frame. Consequently the mouse event that closes a button-owned UI
cannot become a pistol shot later in the same input dispatch, and semi-automatic
handling has no queued or held shot to release afterward.

On the player's single `Died` event, `Main` records dead state before closing either
panel, detaches the active container, disables the inventory toggle, disables the
player, interactor, weapon, and flashlight controllers, turns the flashlight off,
cancels reload, marks the weapon and flashlight HUDs disabled, clears the prompt
through the interactor, and presents
`You died.` through the existing message UI. Mutants independently observe the same
health model's death event and cancel chase/attack. Because every later close path uses
the centralized alive/modal rule, closing a panel after death cannot re-enable
world or combat input. Movement, velocity, interaction, and combat gates also leave
the footstep/weapon emitters unable to publish after death. Independent health-bound
HUD callbacks immediately force noise to `SILENT`, cancel its timer, stop stamina
recovery, and mark stamina disabled. Stage 11 intentionally provides no respawn, scene reload, or game-over menu.

## Stage 11 flashlight resource management

### Definition and plain runtime model

`FlashlightDefinition` is a typed Godot `Resource` containing static data only:
stable ID/name, maximum charge, drain per second, low/critical thresholds, the
existing Battery definition, and charge restored per Battery. Validation requires
positive finite maximum/drain/restore values,
`0 <= critical < low < maximum`, a non-empty ID/name, and the exact stable item ID
`battery`. `StandardFlashlight.tres` configures `100 / 1 / 25 / 10 / 100`. It never
stores current charge or on/off state.

`FlashlightModel` is a sealed plain C# object with no node, input, physics, vector,
light, timer, or UI dependency. It starts full and accepts the composed startup-on
flag. `TryTurnOn`, `TurnOff`, `Toggle`, `Drain`, and `RestoreCharge` validate requests,
clamp charge to `0..MaximumCharge`, prevent turn-on at zero, drain only while on, and
automatically turn off at depletion. Negative, zero, NaN, and infinite charge
requests are programming errors. Immutable state/charge results expose requested,
applied, previous/current charge, previous/current on state, and crossing flags.

One logical state mutation invokes `Changed` exactly once; clamped/no-op operations
invoke nothing. Downward `> threshold -> <= threshold` transitions emit
`LowChargeReached` and `CriticalChargeReached` once per crossing. Restoration above
a threshold rearms its future downward crossing naturally because no separate latch
is needed. `Depleted` emits only on `>0 -> 0`, after the single logical `Changed`, and
cannot repeat while charge remains zero.

### 2D controller and battery transaction

`PlayerFlashlightController2D` is the only owner of the flashlight model and F/B
input. It is attached to the existing aim-pivot flashlight visual, applies model
state by changing that visual root's visibility, and drains
`DrainPerSecond × physics delta` while the actor is alive and the model is on. It
contains no inventory UI, mutant reference, scene-tree search, or collision logic.
Walk, Crouch, Sprint, and Crawl all use the same component and PointLight2D; posture
changes neither mutate charge nor duplicate input.

The controller publishes one `BatteryReplacementRequested` event for a non-echo B
press. `Main` supplies the already composed model and player inventory to
`FlashlightBatteryService`; `Main` does not inspect slots or perform charge mutation.
The service first rejects dead/input-blocked, reentrant, effectively full,
non-useful-restoration, and no-reserve cases. `InventoryModel` prepares a stable
single-item removal plan while `FlashlightModel` prepares the exact previous/current
charge and applied amount. Both plans are revalidated immediately before a synchronous
commit. The Battery removal and charge restoration run without callbacks, awaits, or
public notifications between them. Only after both models are consistent does the
service publish one inventory `Changed` and one flashlight `Changed` through the
shared `SafeEventPublisher`. A failing inventory HUD therefore cannot stop flashlight
notification delivery, and a failing flashlight HUD cannot undo or duplicate the
already committed Battery consumption. A success reports one consumed Battery and
the exact calculated restoration; all rejected paths mutate neither model and publish
nothing. Key echo rejection plus synchronous reentrancy protection prevents one input
dispatch from applying twice.

Messages are stable: `Battery replaced.`, `Flashlight battery is already full.`,
`No spare batteries.`, and `Cannot replace battery now.` Automatic replacement at
zero is deliberately absent. Existing Battery pickups, container seeds, stacking,
transfers, and item definition remain unchanged; the test level already contains
multiple Batteries, so no additional content was inserted.

`scenes/validation/FlashlightBatteryServiceValidation.tscn` is the focused headless
entry point for this transaction. It verifies missing-Battery and near-full no-op
paths, player ineligibility, exact one-Battery/one-charge success, a duplicate attempt
at full charge, and isolated throwing inventory/flashlight subscribers. Run it with:

```bash
godot --headless --path . scenes/validation/FlashlightBatteryServiceValidation.tscn
```

### Modal UI, death, and future 3D reuse

`Main` remains the composition and enable-state root. Normal inventory and loot
transfer disable F/B but do not pause the scene tree. The chosen world-time policy
therefore keeps charge draining while a light was already on; the modal UI cannot
change its state or reserve. Re-enabling input sets a release latch until both F and
B are physically up. Death marks the actor dead before panel-close callbacks,
turns the model off immediately, stops future drain, marks the HUD `DEAD`, and makes
later close/rebind paths unable to enable F/B.

A future 3D adapter can instantiate the same definition/model and invoke the same
battery service against the same inventory. Only `PlayerFlashlightController2D`,
PointLight2D visibility, mouse-facing aim, and 2D scene composition need replacement
with a 3D controller and SpotLight3D presentation. No generic energy framework or
2D/3D conditional was introduced.

## Stage 12 light-aware visibility and Stage 11 fixes

### Honest charge display and near-full policy

`FlashlightModel` exposes a small full-charge epsilon capped at `0.05` charge units
and scaled down for unusually small definitions. `IsEffectivelyFull` and
`CalculateRestorableCharge` use the same epsilon. `FlashlightHudController` uses
one-decimal `MidpointRounding.AwayFromZero`; it never uses `Math.Ceiling`. Therefore
a charge that displays as full is also rejected by replacement, and useful charge
below that display boundary remains visible.

Flashlight status contains two dimensions whenever charge is non-empty: power state
and warning state. Normal values are `ON`/`OFF`; threshold values are
`ON · LOW`, `OFF · LOW`, `ON · CRITICAL`, and `OFF · CRITICAL`. `EMPTY` and `DEAD`
remain terminal presentation states.

### Battery replacement transaction

The replacement service performs all expected refusal checks before mutation:
actor/input eligibility, reentrancy, useful capacity, and stable-ID Battery count.
It then invokes internal non-notifying inventory removal and non-notifying charge
restoration. Both model states are complete before `InventoryModel.PublishChanged`
and `FlashlightModel.PublishChanged` run. Observers can therefore query either model
from either callback and see the completed transaction. Expected failure paths call
neither mutation and publish no event; there is no broad rollback catch.

### Bounded long-frame footsteps

The footstep emitter accumulates normalized step progress and weighted acoustic
contribution per movement segment. Every completed cycle is converted into a
bounded-intensity pending occurrence. A fixed FIFO debt retains up to 24 completed
steps, and each physics update publishes at most three oldest occurrences. The final
fractional cycle remains separate from that debt, so normal and low-FPS movement
produce the same total completed footsteps once the bounded queue drains.

Each pending intensity is capped to `SprintFootstepIntensity`; no aggregate event
exists. If a sustained severe stall fills the hard debt bound, newer excess cycles
are safely clamped instead of creating an unbounded delayed burst. Initialization,
non-finite/non-positive or over-one-second delta, teleport-like displacement, death,
disabled gameplay input, and explicit emission disable clear both fractional state
and pending debt without noise.

### Visibility composition

`PlayerVisibilityController2D` is explicitly initialized by the player with
`IMovementModeSource`, `PlayerMovementSettings`, `FlashlightModel`, and
`HealthModel`. It has no mutant reference and performs no physics/UI polling. It
subscribes to movement-mode changes, flashlight changes, and death, then exposes:

```text
final = postureMultiplier × ambientLightMultiplier × flashlightMultiplier
```

Posture is fixed at Walk `1.00`, Crouch `0.65`, Sprint `1.15`, and the existing
Crawl resource value `0.40`. Ambient defaults to `1.00`; flashlight is `1.00` off
or `1.45` on. The positive finite result is categorized deterministically:
`HIDDEN < 0.55`, `DIM < 0.85`, `VISIBLE < 1.30`, otherwise `EXPOSED`.
`VisibilityChanged` is emitted only when the effective multiplier, category, or
actor life state changes.

`PlayerController2D` still satisfies `IVisibilityTarget`, but delegates the value to
this component. Existing mutant code continues calculating `base sight range ×
VisibilityMultiplier` and retains its health gate, FOV cone, world ray, close-range
rule, target memory, direct-sight priority, hearing, and navigation recovery.
Visibility never creates instant detection and never erases memory.

### Ambient exposure zones and HUD

`LightExposureZone2D` is a reusable layer-zero `Area2D` with exported
`DisplayName`, positive finite `VisibilityMultiplier`, and integer `ZonePriority`. It
listens only for the player's dedicated `LightExposureSensor2D` on collision layer
4 (`8` as a bit mask), using `AreaEntered`/`AreaExited`; it no longer observes the
`CharacterBody2D` or either movement collision profile. The sensor is an explicit
child of `Player.tscn`, resolves exported `PlayerVisibilityController2D` and shape
`NodePath` values once through `RequiredNodePathResolver`, and owns a constant
10-unit circular `CollisionShape2D`. It is monitorable but does not
monitor anything itself, does not participate in movement collision, and is never
changed by Walk, Crouch, Sprint, or Crawl transitions.

Multiple zones are stored without LINQ; highest priority wins, and equal priority is
resolved by ordinal name then node path. Invalid/freed sensors and zones are removed
safely and no active zone means `1.00` ambient exposure. Because posture changes only
replace the separate normal/crawl movement shapes, pressing C or Z while stationary
on a zone boundary cannot produce ambient-zone enter/exit events; actual movement of
the fixed sensor is required.

The test level places a `0.70` dark maintenance zone and a `1.25` bright corridor
zone with simple polygon overlays. These are authored gameplay values, not samples
of rendered pixels, PointLight2D energy, screenshots, shaders, shadows, or beam
intersection.

The dark maintenance visual and gameplay rectangle use one authoritative shape.
`DarkMaintenanceArea` instantiates the standard `160 × 120` rectangle at world
position `(-660, 0)` with scale `(2.25, 2.0)`, so its final world-space bounds are
`X = -840..-480` and `Y = -120..120`. `DarkMaintenanceOverlay` uses
`LightExposureZoneOverlay2D`, which receives an explicit exported `NodePath` to that
`CollisionShape2D`, resolves it once, and converts its four world-space corners into
the polygon's local
coordinates once during `_Ready()`. It performs no frame polling. Consequently the
fixed 10-unit `LightExposureSensor2D` crosses the gameplay boundary at the same
visible edge, and no dark-looking top or bottom strip falls back to ambient `1.00`.

`VisibilityHudController` binds explicitly through `Main`, subscribes once to
`VisibilityChanged`, and presents category plus numeric multiplier. It unbinds on
exit and displays `DEAD` from the same state event. It never asks whether a specific
mutant sees the player.

## Current 2D-specific code

`PlayerController2D` is deliberately 2D-specific. It depends on `CharacterBody2D`,
`Vector2`, `Node2D`, `Camera2D`, the 2D input vector, and
`MoveAndSlide`. The player and level `.tscn` files are also 2D presentation.

Mouse aiming rotates only `AimPivot`. The `CharacterBody2D` root and both movement
collision shapes remain axis-aligned, so facing direction cannot change physical
collision. `PlayerFlashlightController2D` and its PointLight2D are children of that
pivot and therefore follow both the player and cursor without entering the movement
controller's input or charge rules.

`LightExposureSensor2D` is a separate unrotated child of the player root. Its fixed
shape follows only player translation and is not one of the movement controller's
normal/crawl profiles, so posture changes cannot affect ambient overlap state.

`PlayerHazardSensor2D` is a second separate unrotated player-root child. It uses
its own layer and fixed 10-unit circle, references the existing `HealthComponent`
explicitly, and is never enabled, disabled, resized, or replaced by posture logic.
Consequently hazard occupancy changes only when player translation moves this sensor
across a damage-zone boundary. Repeated stationary C/Z transitions preserve both the
current tracked target and its elapsed periodic accumulator.

The `PlayerMovementSettings` resource is not tied to a scene node or coordinate
dimension. Its four speeds, acceleration/deceleration, stamina tuning, crawl
visibility/noise/distance multipliers, and actual-movement epsilon are scalar
values, so a future 3D controller can consume the same rules if those values remain
appropriate for the 3D scale. `StaminaModel`, `StaminaChangeResult`, `MovementMode`,
`IMovementModeSource`, and `IVisibilityTarget` likewise contain no 2D type. Input,
velocity, actual displacement, clearance, collision-profile application, and the
decision of when active sprint occurs remain adapter responsibilities in
`PlayerController2D`. The current normal-shape direct-space query and footprint
switch are intentionally 2D-specific; a future 3D controller must implement an
equivalent `Shape3D` clearance test appropriate to its own capsule/body geometry.

Interaction candidate detection is intentionally 2D-specific. It depends on
`Area2D`, `CircleShape2D`, `Vector2`, the 2D aim pivot, and
`PhysicsDirectSpaceState2D`. None of those types leak into `IInteractable`,
`InteractionContext`, or `InteractionResult`.

The loot-container scene is likewise 2D-specific because its interaction area,
solid body, collision shapes, polygons, and position are presentation concerns.
Its inventory component, seed data, transfer model, shared contract, and transfer
panel remain free of those dependencies.

`DamageZone2D` and its hazard scene are also presentation-specific: overlap
detection, `Area2D`, shapes, polygons, warning label, and placement belong to the
2D world. The player-side adapter is the explicitly bound `PlayerHazardSensor2D`,
which resolves explicit `HealthComponent` and shape `NodePath` values once and owns
one constant-size child shape.
Damage zones detect only that dedicated layer and track the sensor plus its
`HealthModel`; normal/Crawl movement-shape changes cannot create hazard exit or entry
events. Immediate-entry damage and elapsed-time interval ticks remain zone
responsibilities. The zone applies at most four catch-up ticks per physics update,
retains overdue intervals and fractional remainder, and clears all pending time on
exit. Dead health models and the permanently damage-disabled completed player are
rejected and removed from tracking. `DamageInfo`, `HealthModel`, `IHealthOwner`, health
results, use effects, and the medkit definition do not depend on those 2D types.

`PlayerWeaponController2D` is presentation-specific because it reads a `Node2D`
aim pivot and muzzle, builds `Vector2` ray endpoints, queries
`PhysicsDirectSpaceState2D`, excludes a 2D body RID, and draws `Line2D` feedback.
`DamageableTarget2D` likewise owns a `StaticBody2D`, 2D collision, primitive
visuals, and world label. `FirearmDefinition`, `FirearmState`, shot/reload results,
inventory ammunition, `HealthModel`, and `DamageInfo` remain reusable.

`MutantController2D` is presentation-specific because it owns `CharacterBody2D`,
`NavigationAgent2D`, `Vector2` patrol, investigation, and last-known positions, 2D
direct-space rays, `MoveAndSlide`, primitive `CanvasItem` feedback, and world labels.
Its visual pivot turns without rotating the collision root. `MutantDefinition`,
`MutantState`, `IVisibilityTarget`, `HealthModel`, `DamageInfo`, health results, and
`IHealthOwner` contain no dependency on that navigation or presentation
implementation.

`NoiseSystem2D`, `NoiseOccurrence2D`, `PerceivedNoise2D`, and the footstep component
are presentation-specific because they use `Node2D`, `Vector2`, World-layer 2D
physics rays, and 2D movement. `NoiseEvent`, `NoiseKind`, and the base listener
contract contain no spatial vector. A future adapter can attach a `Vector3`
occurrence and compute 3D distance/occlusion without changing emitter meaning or
putting mutant transitions into the propagation system.

## Future dimension-independent systems

Resource quantities, quest state, objectives, station state, and persistence
models should be plain gameplay/domain code or typed Godot resources.
They should not depend on `CharacterBody2D`, `Node2D`, `Vector2`, `CanvasItem`, or
concrete 2D scenes. The current health model, firearm definitions/state/results,
mutant definition and state vocabulary, item definitions and use effects, inventory
state, transfer/consumption behavior, seed data, stamina state/results,
movement-mode and visibility contracts, noise metadata, and ownership contracts
already follow this boundary. The flashlight definition/model/results and battery
service now follow it as well; only their current 2D input/light adapter is spatial.

Presentation adapters may read those systems and show them in a 2D or 3D scene.
The gameplay state itself should not know whether the player is represented by a
sprite or a 3D model. The same rule applies to future save data: save domain state,
not direct scene-node references.

This does not require a generic framework today. Boundaries should be added with
each real system, using composition and the smallest API needed by its consumers.

## Completed 3D migration boundary

The 3D version adds presentation and movement scenes alongside the preserved 2D
implementation. `PlayerController2D` and `PlayerController3D` remain separate
adapters; neither contains a 2D/3D branch.

| Legacy 2D element | Current 3D element |
| --- | --- |
| `CharacterBody2D` | `CharacterBody3D` |
| `Camera2D` | fixed orthographic `TopDownCamera3D` |
| Normal/crawl `CollisionShape2D` profiles and 2D shape query | distinct `CollisionShape3D` profiles and an owner-excluding clearance query |
| `PlayerFlashlightController2D` and `PointLight2D` | `PlayerFlashlightController3D` and `SpotLight3D`, reusing the definition/model/service |
| 2D level scenes | modular scenes under `scenes/3d` |
| `PlayerController2D` | `PlayerController3D` |
| 2D movement/input adapter | XZ camera-relative adapter consuming shared movement modes and stamina |
| `WorldItemPickup2D` | `WorldItemPickup3D` |
| `LootContainer2D` | `LootContainer3D` |
| `DamageZone2D` | `DamageZone3D` |
| `PlayerWeaponController2D` | `PlayerWeaponController3D` |
| `DamageableTarget2D` | 3D `IHealthOwner` targets |
| `MutantController2D` and `NavigationAgent2D` | `MutantController3D` and `NavigationAgent3D` |
| 2D mutant sight ray and `Vector2` routes | 3D physics ray and `Vector3` routes |
| `NoiseSystem2D` and `NoiseOccurrence2D` | `NoiseSystem3D` and `NoiseOccurrence3D` |
| Health, damage, flashlight charge/replacement, stamina, movement-mode/visibility contracts, noise metadata/kinds, mutant state/configuration, firearm, inventory, and item-use models/resources | Remain reusable |

The implemented sequence was:

1. Keep existing dimension-independent data and gameplay assemblies unchanged.
2. Add `World3D/PlayerController3D.cs` and a new 3D player scene.
3. Build modular 3D level and gameplay adapters while retaining shared state.
4. Compose each completed dependency phase through `Main3D` and validate it.
5. Add the full objective loop and technical-level contract.
6. Run all legacy and 3D regression gates, then make Main3D the configured entry.

`PlayerInteractor3D` calls the same `IInteractable` contract and publishes to the
same Canvas UI. `PlayerWeaponController3D` reuses firearm state, ammunition,
reload, damage, and weapon HUD while replacing only aim/muzzle physics and shot
presentation. `MutantController3D` reuses validated configuration, health,
visibility policy, noise kinds, and typed state while owning only 3D navigation,
queries, transforms, and feedback. Runtime references and mutable state remain in
controller/model instances, never shared Resources.


## Stage 13 objective progression and powered escape

### Objective ownership

`ObjectiveStage` and `ObjectiveProgressModel` live in `src/Gameplay/Objectives`.
They contain no Godot node, UI, physics, vector, inventory, or scene reference. The
model starts at `FindFuse`; `TryAdvanceTo` accepts only the immediately following
stage and emits one typed `Changed(previous, current)` callback after mutation.
`Completed` is terminal. `Main` owns the single model, binds `ObjectiveHudController`,
and performs only composition-level transitions from inventory acquisition, circuit
restoration, emergency-door opening, and exit completion.

### Replacement fuse transaction

`ReplacementFuse.tres` is a normal `ItemDefinition` (`replacement_fuse`, stack 1,
no use effect). The Maintenance Fuse Crate seeds it through the ordinary
`InventorySeedEntry`; all movement between the crate, player, and other containers
uses `InventoryModel.TryTransferTo`. No fuse-specific slot or UI code exists.

`FuseInstallationService` coordinates `InventoryModel`, `PowerCircuitModel`, and
the explicitly supplied `ObjectiveProgressModel`. It validates re-entry, actor
eligibility, the exact `RestorePower` stage, circuit state, and item availability
before mutation. Inventory and circuit models then create internal prepared plans.
The service revalidates both plans and commits them synchronously without callbacks,
awaits, or public notifications between mutations. Expected rejection therefore
occurs before either model changes. Both states are complete before observers receive
one inventory notification, one circuit `Changed`, and one `PowerRestored`.

Transaction-critical model events use the shared `SafeEventPublisher`. It enumerates
each subscriber separately, catches only exceptions thrown by that external
subscriber, reports the failure through `System.Diagnostics.Trace`, and continues
remaining callbacks. Inventory UI failure therefore cannot stop circuit publication;
a circuit presentation failure cannot stop `PowerRestored`; and an objective HUD
failure cannot stop other objective subscribers. This is subscriber isolation, not a
broad transaction catch or rollback path.

`FuseBox2D` is only the 2D adapter. It receives the circuit, objective model, and
noise system through explicit binding, obtains inventory from the interaction actor,
delegates mutation to the service, updates its indicator from circuit events, and
emits one 1.25 interaction noise after successful installation. It has no `Main`,
HUD, mutant, or scene-tree lookup dependency.

### Focused fuse transaction validation

`scenes/validation/FuseInstallationServiceValidation.tscn` is a headless validation
entry point. It verifies missing-fuse rejection, duplicate installation, exact one
fuse/one power activation, notification counts, and isolation of throwing inventory,
circuit, power-restored, and objective subscribers. The critical power subscriber
must still advance `RestorePower -> OpenExit`. Run it with:

```bash
godot --headless --path . scenes/validation/FuseInstallationServiceValidation.tscn
```

### Circuit and powered presentation

`PowerCircuitComponent` owns one dimension-independent `PowerCircuitModel`. The
model exposes only `HasInstalledFuse`, `IsPowered`, `Changed`, and `PowerRestored`;
it knows nothing about inventory, lights, doors, UI, or coordinates. Installation
is one-way and idempotent.

`SlidingDoor2D` retains its existing movement implementation for the powered emergency door and adds optional
`RequiresPower` configuration plus explicit `BindPowerCircuit`. Ordinary instances
remain unpowered-independent. A powered instance stays interactable while offline
so it can return a clear message, but it does not start its tween, disable collision,
emit door noise, or advance progression. The first accepted powered opening may emit `OpeningStarted` for presentation, but
progression does not observe that event. After the tween reaches its authored target
and the blocking collision is disabled, the door emits one typed `Opened(door)`
event. `Main` accepts that event only from the explicitly bound emergency door and
then moves `OpenExit` to `ReachExit`. Killed, interrupted, deleted, or incomplete
tweens emit no completion event. The explicit `DoorState` machine allows interaction
only in `Closed`; a successful natural finish snaps to the target, disables physical
and interaction collision, enters terminal `Open`, and publishes through the one-shot
event guard. This prevents both duplicate transitions and repeated door opening.

`PowerControlledLight2D` binds to the circuit, applies the current state immediately
for late binding, and subscribes to `Changed`. It toggles a built-in `PointLight2D`,
an industrial overlay/indicator, and `LightExposureZone2D.SetExposureEnabled`. The
zone tracks occupants while disabled, so restoration updates a player already in
the area without polling. Existing priority selection then combines powered ambient
`1.35` with posture and flashlight multipliers.

### Exit and terminal input state

`ObjectiveExitZone2D` is an Area2D bound explicitly to the objective model. It
retains its normal `BodyEntered` completion path and also detects the player's
constant-size `PlayerObjectiveSensor2D` on the dedicated non-solid objective-sensor
layer. The sensor resolves explicit player and shape `NodePath` dependencies and is
independent of the normal/Crawl movement collision profiles.

The zone subscribes to `ObjectiveProgressModel.Changed`. When the stage becomes
`ReachExit`, it performs one event-driven `GetOverlappingAreas` inspection, selects
a valid living player sensor deterministically, and attempts completion immediately.
Thus an early entrant does not complete out of order but also does not lose the
opportunity when the emergency door later finishes opening. `AreaEntered` and the
preserved `BodyEntered` path use the same one-shot completion method. The zone
unsubscribes from both overlap signals and the model event in `_ExitTree`; no overlap
polling occurs in `_Process` or `_PhysicsProcess`.

`Main` handles that terminal event by setting `_isPrototypeCompleted` before closing
modal panels. The same centralized `RefreshGameplayInputState` then permanently
disables player movement/posture, interaction, weapon input, flashlight input, and
inventory toggling. `PlayerFootstepNoiseEmitter2D.SetEmissionEnabled(false)` resets
its partial cycle and prevents any later player footstep. The completion panel is a
UI-only presentation with a Quit button; it does not pause or replace the scene.
Death and completion remain separate terminal reasons, and neither can be undone by
a later inventory/transfer close event.


### Scene reference and terminal-flow invariants

Godot scenes persist the four affected cross-node dependencies as `NodePath` values.
`RequiredNodePathResolver` is the single resolution boundary: it rejects an empty
path, wrong node type, missing node, or inactive node during `_Ready`. Sensor consumers
do not dereference nullable backing fields; `TryGetVisibilityController`,
`TryGetHealth`, and `TryGetPlayer` guard all overlap callbacks. This removes the
startup `InvalidOperationException`/later `NullReferenceException` cascade without
hiding invalid authoring.

`Main` subscribes to `PowerRestored`, emergency-door `Opened`, and
`EscapeCompleted` before binding components that may synchronize current state. Door
completion advances `OpenExit → ReachExit`; `ObjectiveExitZone2D` then inspects its
current fixed-sensor overlaps from the objective event and can immediately advance
`ReachExit → Completed`. Both the door and exit zone use one-shot terminal guards and
safe event publication.

### Test-level composition

`PlayableLevelController2D` resolves the unique power component, fuse box, emergency
door, powered lighting, and exit zone once during `_Ready` and exposes them to
`Main`. Its existing direct `Interactions` pass still binds all `INoiseEmitter2D`
instances, including both ordinary/powered doors and the fuse box. The replacement
fuse is beyond the existing crawl passage; the exit bay is a small barrier/door
addition inside the existing side room, preserving all previous enemies, hazards,
loot, targets, navigation data, visibility zones, and ordinary door behavior.

### Excluded from Stage 13

Stage 13 intentionally excludes persistence, multiple or removable circuits,
generators, electrical puzzles, keycards, dialogue, quest logs, optional objectives,
rewards, multiple levels, scene transitions, checkpoints, cinematics, audio,
multiplayer, 3D scenes, and rendered-pixel light sampling.

## Stage 12 manual verification sequence

The full operator-facing 40-step flow is kept in `README.md`. Its ordering is
intentional; the architecture-specific checkpoints are:

1. Start from a fresh main scene and verify all five bound HUDs show health,
   weapon, `NOISE: SILENT`, `STAMINA 100.0 / 100.0`, and flashlight status with `MODE: WALK`; exactly the
   normal profile is active.
2. Compare axial/diagonal movement in Walk/Crouch/Sprint/Crawl. Confirm terminal
   speeds `220 / 121 / 341 / 77`, no diagonal advantage, and mode events only on
   actual changes.
3. Sprint in open space and into a wall. Only meaningful real displacement may
   drain stamina or select effective Sprint; clearing the wall while Shift remains
   held resumes the same sprint session.
4. Sprint to zero. Confirm exact clamping, same-tick Walk fallback, 0.75-second
   delay, 18/second recovery, no automatic restart while Shift remains held, and
   restart only after release/re-press at or above 10.
5. Rapidly switch C/Z/modes while traveling. Distance/intensity pairs must remain
   `220/0.20`, `132/0.45`, `110/1.0`, and `90/1.8`; proportional cycle progress
   survives changes with neither silent travel nor duplicate/stationary steps.
6. Enter the west duct. Normal and mutant profiles must fail the 24-unit opening;
   Crawl must traverse it. A blocked Z/Shift exit stays Crawl and emits one
   `Cannot stand here.` per press; the open pocket restores normal profile safely.
7. Compare clear-sight ranges `128 / 208 / 320 / 368` for Crawl/Crouch/Walk/Sprint,
   then repeat outside FOV and across a World wall. Only range changes.
8. Enter direct sight from patrol/investigation and during a chase-route retry.
   State must be `CHASE`/`ATTACK` immediately even when destination reassignment is
   cooling down.
9. Break established sight. Observe two complete seconds of `CHASE` toward the
   fixed last-known point, then `SEARCH`; add an unrelated audible noise and confirm
   it neither changes nor extends grace. Reacquire in melee range and confirm
   immediate `ATTACK`.
10. Recheck search termination, stuck recovery, investigation anonymity, duct nav
    exclusion, wall/FOV sight, and attack windup final wall/range validation.
11. Open normal inventory while crouched and crawling, then loot transfer. Confirm
    zero velocity, no C/Z/sprint/footstep leak, continued stamina recovery and AI
    time, remembered posture, and reliable focused-control Tab.
12. Re-run pistol, reload, pickup, transfer, container, door, hazard, target, medkit,
    mutant behavior, and the duct Battery to establish Stage 1–9 compatibility.
13. Test F toggle, on-only drain, zero-charge auto-off, low/critical downward
    crossings, and restoration above thresholds. Every operation must preserve
    `0..MaximumCharge` and one logical change notification.
14. Collect existing Batteries, replace below full, then retry at full and with no
    reserve. Success removes exactly one item and emits one event per model; failures
    mutate neither. Repeat rapidly and through both modal panels.
15. Keep the flashlight on while inventory/transfer is visible: F/B remain blocked
    but world-time drain continues. Close while F/B is held and confirm the release
    latch prevents same-event leakage.
16. With a noise timer, stamina recovery, and partial flashlight charge active, die
    while crawling. `SILENT`, `STAMINA DISABLED`, and flashlight `DEAD`/off must be
    synchronous; after panel close and later F/B/C/Z input, drain, movement,
    profiles, interaction, combat, and player noise remain safe and disabled.

## Deliberately excluded from Stage 12

Rendered-pixel light sampling, shadow geometry analysis, beam-direction reactions, light switches, generators, night vision, suppressors, multiple or
weapon-mounted flashlight types, automatic replacement, battery quality or partial
battery items, charging stations, generators, crafting, overheating, durability,
dynamic shadows, advanced lighting, flashlight audio, persistence, multiplayer,
and 3D scenes are intentionally absent.

The transfer interface intentionally offers only one-unit and complete-stack
requests. Drag and drop, arbitrary stack splitting, editable quantities, sorting,
filtering, slot rearrangement, dropping, and a hotbar would require additional
interaction and validation rules. Crawl changes a top-down body footprint, speed,
footstep tuning, and effective sight range only; it is not vertical-height
simulation. There is no prone combat animation, crawl-specific item restriction,
crawl stamina cost, vent loading transition, ladder, climbing, crawling enemy,
crawling under moving objects, aim-based collision rotation, jump, vault, dodge,
roll, leaning, cover, sprint attack, stamina cost for combat/interactions/carrying,
exhaustion damage, breathing, heartbeat, animation, or audio. Additional
firearms and advanced combat systems are absent: additional mutant species or an
enemy inheritance hierarchy, suppressors, light-level visibility meters,
camouflage, sound materials, frequency bands, echoes, reflections, room/portal
acoustics, ranged enemies, pack
coordination, alerts/shared blackboards, mutant door operation, dynamic navigation
rebaking, procedural spawning, waves, loot drops, knockback, stagger, stun, player
melee weapons, weapon switching,
equipment slots, holstering, weapon pickups, dropped weapons, automatic weapons,
shotgun pellets, projectile physics, additional melee systems, recoil patterns, procedural spread,
attachments, armor, resistances, critical hits, body parts, durability, repair,
crafting, vendors, upgrades, shell casings, audio, polished animation, camera shake,
status effects, regeneration, advanced medical mechanics, random loot, and loot
tables are absent. The only firearm is the equipped Service Pistol, target dummies
remain non-AI, and enemy scope is limited to one tunnel-mutant definition with one
melee attack. The test level currently places two instances, but composition does
not encode that count.

Saving and loading remain deferred until the project defines stable persistence
data and lifecycle rules; live nodes and runtime resource references are not
treated as save data.

## Automated regression architecture

Automated validation is implemented as an internal test adapter around the existing
architecture rather than a parallel gameplay framework. `IFeatureTestSuite` is the
small suite boundary; `FeatureTestRunner` selects suites from command-line user
arguments, executes them sequentially, aggregates failures, and returns a CI-safe
process exit code. `FeatureTestContext` gives every asynchronous case a temporary
scene root and frees it in `finally`, preventing physics bodies, areas, timers,
listeners, and subscriptions from leaking into later tests.

Dimension-independent models and services are exercised directly. Physics-sensitive
features instantiate the actual production scenes and use the real Godot physics
server: muzzle clearance, first-hit hitscan, stable hazard/light/objective sensors,
footstep debt, wall attenuation, mutant FOV timing, door tween completion, and
objective-zone overlap. HUD suites bind the real controllers to models and inspect
the actual labels/progress bars after event delivery. The `scene-contracts` suite
validates authored resources and explicit `NodePath` dependencies, compares the
world-space dark overlay and gameplay-zone bounds, verifies required Input Map
actions, and smoke-loads `Main.tscn` until `Main.IsInitialized` is true.

The harness has no plugin dependency, global test bus, per-frame assertion polling,
or copied production algorithm. Suite selection and usage are documented in
`docs/testing.md` and exposed through `scripts/test-all.sh`,
`scripts/test-feature.sh`, and `scripts/list-tests.sh`.

## Stage 14 stabilization and permanent engineering policy

### Authoritative event data

`NoiseHudController` classifies the completed `NoiseOccurrence2D`. Footstep and
interaction severity use `NoiseEvent.Intensity`; `NoiseKind.Gunshot` is always LOUD.
The controller does not query `PlayerController2D.CurrentMovementMode`, because a
single completed step may contain weighted distance from several movement modes.
Death stops the fade timer and immediately restores SILENT.

### Objective sensor exclusivity

`ObjectiveExitZone2D` has collision layer `0` and mask
`CollisionLayers2D.PlayerObjectiveSensor` only. It subscribes only to `AreaEntered`.
Movement `CharacterBody2D` overlaps and normal/Crawl shape changes are deliberately
outside the completion contract. On the event-driven transition to `ReachExit`, the
zone performs one bounded inspection of current overlapping objective sensors so an
early entrant retains the opportunity to complete. Completion remains living-player,
ordered-stage, and one-shot guarded.

### Safe publication order

`HealthModel`, `StaminaModel`, and `FirearmState` use `SafeEventPublisher`. State is
mutated once before publication. Invocation-list entries are called independently and
subscriber exceptions are logged with event and method context. Meaningful ordering is
preserved: health publishes `Changed`, then `Damaged`, then terminal `Died`; stamina
publishes `Changed` before threshold events. A failure never retries or rolls back the
completed state mutation.

### Reload transaction

`FirearmReloadService` is the sole multi-model reload-completion operation. It:

1. validates reload state, magazine capacity, ammo ID, and reserve availability;
2. computes the exact load amount;
3. prepares immutable inventory slices and a firearm completion plan;
4. revalidates both plans;
5. mutates inventory and firearm without public notifications;
6. verifies exact ammunition conservation;
7. publishes inventory and firearm changes independently.

The service has a reentrancy guard. A canceled reload never reaches the transaction.
A reserve disappearing before timer completion rejects the transaction; the controller
then ends the separate reload-in-progress state without consuming ammunition.

### Healing-item transaction

Item effect Resources are configuration only. `HealingItemUseEffectDefinition` stores
and validates `HealAmount`; it does not mutate actors. `ItemUseService` validates the
living health owner, selected slot, item/effect, useful capacity, exact source item,
and both prepared plans. It then removes one source item and applies one healing change
without notifications, followed by independent inventory and health publication. Full
health, death, empty/invalid slots, unsupported effects, stale plans, and reentrant use
change neither gameplay model.

### 3D parity of the stabilization rules

The migration carries the same guarantees into 3D rather than reimplementing them
in UI or scene scripts. `NoiseHudController` consumes the dimension-neutral noise
event emitted by `NoiseSystem3D`; `ObjectiveExitZone3D` listens only to
`PlayerObjectiveSensor3D`; firearm/target damage uses `FirearmDischargeService` as
an atomic two-model commit; and fuse installation continues through the existing
inventory/circuit transaction. `MutantDecisionRules` is the one 3D state-priority
path, so a gunshot cannot downgrade Chase.

3D hazard, perception, chase-refresh, and footstep cadence preserve elapsed
remainder behind bounded work limits. All four gameplay sensors retain their shape
when normal/Crawl movement profiles change. `Main3D` latches death/completion and
closes modal UI without ever restoring movement, aim, combat, interaction, item use,
hazards, footsteps, or mutant AI.

### Permanent rules for future stages

1. **Architecture:** gameplay state/rules are plain C#; nodes adapt Godot; UI owns no
   rules; `Main` is the composition root; dependencies are explicit; no service
   locator, global bus, mutable static gameplay state, or process-time tree search.
2. **Transactions:** validate all preconditions, calculate the whole result, prepare
   every mutation, commit all models silently, then publish; return immutable typed
   results; never expose intermediate state or use exception rollback normally.
3. **Events:** mutate once; publish completed facts once; invoke subscribers safely;
   log failures; preserve ordering; subscribe/unsubscribe symmetrically; do not use an
   event as a command when a direct call is clearer.
4. **Time:** accumulate elapsed time, subtract intervals, preserve remainder, bound
   catch-up, and document excess-debt handling.
5. **Physics:** movement colliders are physical only; hazards/light/objectives use
   constant sensors with explicit layers; exclude owners from queries; handle origins
   near/inside geometry; never let muzzle or sensor origins bypass walls.
6. **Input/terminal states:** one press means at most one action; all input observes
   enabled/modal/death/completion state; death and completion cannot be reversed by UI.
7. **UI:** event-driven, read-only refreshes, actual values, no threshold-inflating
   ceiling, and classification from the event/model that is the source of truth.
8. **Resources:** configuration only, validated, finite, bounded, stable-ID based.
9. **C#:** explicit access, sealed by default, nullable correctness, focused immutable
   types, no hot-path LINQ, broad catches, warning suppression, unsafe code, or magic
   collision numbers.
10. **Change discipline:** inspect authority/events/scenes/tests first; identify
    failure/terminal/FPS/transaction/sensor risks; define acceptance tests; then test
    normal, duplicate, invalid, boundary, terminal, modal, low-FPS, subscriber,
    rebinding, and prior-system regression cases.
