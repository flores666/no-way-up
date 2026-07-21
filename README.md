# No Way Up

**No Way Up** is the public game name. The existing `LineZero` assembly, namespaces,
and internal project identifiers are intentionally retained in this stabilization stage.

No Way Up is a top-down survival-horror prototype set in a damaged underground
metro. The current cumulative build preserves the complete movement, crawl,
stamina, interaction, inventory, combat, mutant, noise, flashlight, visibility,
hazard, power, objective, death, and terminal-completion systems. The first real
playable greybox level now uses those systems in a longer exploration loop while
the original TestLevel remains available for focused regression work.

The codebase intentionally keeps 2D presentation and movement separate from data
that can be reused when a future 3D top-down version is introduced.


## MetroLevel01 gameplay greybox

`res://scenes/levels/MetroLevel01.tscn` is the default playable level loaded by
`res://scenes/main/Main.tscn`. It is designed as a 20–30 minute first-pass run,
depending on stealth, exploration, combat, and loot decisions.

The intended route is:

1. Start in the dim western concourse with the powerless emergency exit visible.
2. Enter Platform 01 through the ticket/access corridor.
3. Search the stopped train and recover the replacement fuse from its maintenance car.
4. Reach the northeastern service corridor and electrical room by either the exposed
   platform route or the crawl-only cable duct shortcut.
5. Install the fuse at the existing maintenance fuse box.
6. Follow the newly powered service and emergency lighting back to the western exit.
7. Open the powered emergency door and enter the existing objective exit zone.

Major authored spaces are the ticket hall/concourse, main platform, stopped train,
track bed, narrow service route, electrical room, crawl-only cable passage, powered
emergency corridor, and optional ticket-office/train loot areas. Three mutants,
three hazards, five containers, standalone ammunition, medkit, battery, and filter
pickups are placed to support stealth, combat, resource management, and alternate
routing. The train car is traversable and acts as both a landmark and objective-loot
space.

The blue floor marking connects the platform to the electrical room. Green markings
identify the emergency route. Dark platform/service exposure zones reward crouch and
crawl, while the powered exit, service junction, and electrical-room lights visibly
change after the single fuse circuit is restored.

`res://scenes/main/TestMain.tscn` runs the original
`res://scenes/levels/TestLevel.tscn` with the normal player and UI composition.
TestLevel remains the compact technical/regression level and is not replaced by the
metro greybox.

The gameplay HUD uses a compact 224-pixel left stack and disables the technical
FPS/position panel in `Main.tscn`; `TestMain.tscn` keeps that debug panel enabled.
Metro landmark labels are attached to explicit world-space `Node2D` anchors so their
Control offsets cannot collapse every sign onto one screen position. Mutant health
and `MUTANT DEAD` debug labels are disabled in the gameplay level while remaining
available to technical scenes that explicitly enable them.

Known greybox limitations:

- geometry and signage use primitive built-in visuals rather than final art;
- train interaction volumes use the existing proximity-based interaction system and
  do not add a new interaction line-of-sight framework;
- navigation is authored as explicit walkable polygons and should be rebaked when the
  layout changes materially;
- pacing is an authored target and still requires playtesting on the local Godot build.

## Prerequisites

- Godot Engine 4.7.1 .NET build (the regular non-.NET editor cannot build C#)
- .NET 8 SDK

Godot 4.7.x keeps .NET 8 as its baseline. If a newer compatible Godot 4.x .NET
release is used, let the editor perform its normal project upgrade and update the
`Godot.NET.Sdk` version in `LineZero.csproj` to match that editor.

## Open the project

1. Start the Godot .NET editor.
2. Select **Import**.
3. Choose this repository's `project.godot` file.
4. Wait for the initial asset import and C# restore to finish.
5. Open `res://scenes/main/Main.tscn` if it is not already open.

From a terminal, the equivalent editor command is:

```bash
godot --editor --path .
```

The executable may be named `godot4` or may use a platform-specific Godot .NET
filename instead.

## Build

Restore and build the C# project from the repository root:

```bash
dotnet restore LineZero.csproj
dotnet build LineZero.csproj
```

The Godot editor can also build the project from its **Build** button.

## Run

Press **F6** in Godot to run the current scene, or **F5** to run the configured
main scene. From a terminal:

```bash
godot --path .
```

## Controls

| Action | Input |
| --- | --- |
| Move | `W`, `A`, `S`, `D` or arrow keys |
| Sprint while moving | Hold **Left Shift** |
| Toggle crouch | `C` |
| Toggle crawl | `Z` |
| Aim | Mouse cursor |
| Toggle flashlight | `F` |
| Replace flashlight battery | `B` |
| Interact | `E` |
| Fire equipped service pistol | Left mouse button |
| Reload service pistol | `R` |
| Toggle inventory | `Tab` |
| Select an inventory slot | Mouse or keyboard UI navigation |
| Use the selected usable item | **Use Selected Item** |
| Select a transfer slot or button | Mouse or keyboard UI navigation |
| Close the loot-transfer panel | `Escape` or **Close** |

When a nearby container shows a search prompt, press `E` to open it. Each container
tracks whether it has already been searched: the first successful opening emits the
configured interaction noise, while every later reopening is silent. Empty containers
follow the same rule, and closing the transfer panel never emits world noise. The
transfer panel provides four explicit operations:

- **Take One** moves one unit from the selected container stack to the player.
- **Take Stack** requests the complete selected container stack.
- **Store One** moves one unit from the selected player stack to the container.
- **Store Stack** requests the complete selected player stack.

If a complete stack cannot fit, the operation moves only the quantity that fits
and reports the remainder. Empty and full destinations leave both inventories
unchanged.

The normal inventory shows all 12 slots. Selecting an occupied slot displays its
name and description. The Use button is available only for an item with a use
effect while the player is alive. A Field Medkit restores up to 35 health and is
consumed only after healing succeeds; trying to use one at full health consumes
nothing.

The equipped Service Pistol is semi-automatic: each left-button press requests one
shot, and holding the button does not continue firing. Every accepted hit or miss
consumes one magazine round. Press `R` to reload from ordinary `9mm Ammunition`
stacks in the player inventory. Reloading takes 1.2 seconds and is canceled without
consumption if inventory, transfer UI, or death disables combat.

The reusable tunnel mutant has 75 health, moves through `NavigationAgent2D`, and
sees only a living target inside its 110-degree sight cone with an unobstructed
world ray. Its 320-unit base sight range is multiplied by player movement mode:
128 while crawling, 208 while crouched, 320 while walking, and 368 while sprinting.
It hears typed
noises through distance and wall attenuation and applies
exactly 15 melee damage after a short telegraph. Its health label appears after
damage. Test-level state labels expose `IDLE`, `PATROL`, `INVESTIGATE`, `CHASE`,
`SEARCH`, `ATTACK`, and `DEAD` for this development slice.

The event-driven noise HUD shows `SILENT`, `LOW`, `MEDIUM`, or `LOUD` for the
player's most recent world noise. Footsteps use accumulated actual distance and
preserve normalized cycle progress across mode changes. Crawl, crouch, walk, and
sprint use distance/intensity pairs of `220 / 0.20`, `132 / 0.45`, `110 / 1.00`,
and `90 / 1.80`. Crawl/crouch/walk footsteps display `LOW`; sprint footsteps and
world interactions display `MEDIUM`; accepted pistol shots display `LOUD`. Empty,
blocked, UI-consumed, teleport-like, and post-death movement produces no footstep.

The stamina HUD is also event-driven. Its progress bar uses the exact model value,
while numeric stamina uses deterministic one-decimal normal rounding. The HUD is
explicitly bound to the configured sprint restart threshold and clamps only a rounded
value that would cross that threshold or maximum early. Values such as `9.1`, `9.99`,
`10.0`, `99.1`, and `99.99` therefore display as `9.1`, `9.9`, `10.0`, `99.1`, and
`99.9`; the text cannot falsely imply that the 10-point sprint restart threshold or
maximum stamina has been reached. Stamina starts at 100, drains at 25
per second only when requested sprint produces meaningful post-`MoveAndSlide` displacement,
waits 0.75 seconds after the last actual drain, then recovers at 18 per second.
Empty stamina forces `WALK` and latches sprint until Left Shift is released, even if
stamina recovers above 10 while Shift remains held. Walking, crouching, crawling,
idling, blocked sprint, and an open UI permit recovery while the player is alive.

Crawl moves at 77 units per second and activates a separate smaller collision
footprint. `Z` requests a return to crouch; Left Shift may request a safe exit to
walk before sprinting. The controller tests the disabled normal shape at the
current transform against solid bodies, using the player's World collision mask
and excluding its own RID. If it does not fit, Crawl remains active and the existing
message presenter shows `Cannot stand here.` once for that input press.

The west wall of the starting room now opens into a narrow maintenance duct and an
open maintenance pocket containing one Battery. The normal player and mutant
profiles cannot enter the 24-unit opening; the crawl profile can. The static mutant
navigation polygon remains outside the duct, so no dynamic rebake is required.

### Stage 12 visibility and Stage 11 corrections

The flashlight HUD now uses deterministic one-decimal rounding instead of
`Math.Ceiling`. A model-level full-charge epsilon is aligned with that display, so
a value that renders as `100.0 / 100.0` is treated as full and `B` consumes nothing.
Low and critical states always retain power state: `ON · LOW`, `OFF · LOW`,
`ON · CRITICAL`, or `OFF · CRITICAL`; depletion and death remain `EMPTY` and
`DEAD`.

Battery replacement is one synchronous logical transaction. The service validates
input, useful capacity, and one stable-ID Battery before mutation. Inventory removal
and charge restoration then commit without notifications; only after both models
are consistent does the service publish exactly one inventory change and one
flashlight change. Rejected operations publish neither event and change neither
model.

Long physics frames emit at most three ordinary footstep events per physics update.
Completed steps beyond that budget are retained in a bounded FIFO debt and emitted
on later valid updates, while the fractional distance and weighted acoustic cycle
continue independently. Every queued intensity remains capped to the configured
normal Sprint footstep, so no delayed or current event can exceed that bound.
Initialization, invalid or severe-stall delta, teleport-like displacement, death,
and disabled movement clear unsafe debt instead of producing a delayed burst.
Walk, Crouch, Sprint, and Crawl segments still contribute their own normalized
acoustic weights, so rapid mode changes cannot downgrade or amplify completed
travel.

Player visibility is calculated by `PlayerVisibilityController2D`:

```text
final visibility = posture × ambient light × flashlight
```

Posture remains `Walk 1.00`, `Crouch 0.65`, `Sprint 1.15`, and `Crawl 0.40`.
Ambient light defaults to `1.00`; the test level adds a dark maintenance area at
`0.70` and a bright corridor at `1.25`. The flashlight contributes `1.45` only
while actually on. Charge percentage, flicker, and battery count do not affect
visibility.

`DarkMaintenanceArea` and `DarkMaintenanceOverlay` now share the gameplay
`RectangleShape2D` as their geometry source. The zone's base `160 × 120` rectangle
is positioned at `(-660, 0)` and scaled by `(2.25, 2.0)`, producing exact world-space
bounds `X = -840..-480`, `Y = -120..120`. The overlay derives its polygon corners
from that collision shape at startup, so no visually dark strip remains outside the
`0.70` ambient zone. The player's constant 10-unit light sensor enters and exits on
the same authored rectangle boundary; posture changes cannot alter that overlap.

`LightExposureZone2D` is an Area2D gameplay adapter rather than rendered-pixel
sampling. It detects only the player's dedicated `LightExposureSensor2D`, a fixed
10-unit circular Area2D on its own non-solid collision layer. Player-side sensor,
hazard, objective, and dark-overlay dependencies are authored as exported `NodePath`
values and resolved once through `RequiredNodePathResolver`; scene serialization
therefore matches the runtime property type instead of leaving typed fields null.
The sensor is separate
from both normal and Crawl movement shapes, so stationary C/Z posture changes cannot
produce false ambient-zone exits or entries. Overlapping zones still select the
highest priority; equal priority uses ordinal display name and scene path for
deterministic selection. Leaving all zones through actual sensor movement returns
ambient exposure to `1.00`. The player continues exposing the existing
`IVisibilityTarget`, now delegated to the visibility component, so mutants keep
FOV, wall ray, close-range, memory, hearing, and navigation behavior while their
effective sight range changes immediately with posture, zone, and flashlight.

`DamageZone2D` uses the same stable-sensor principle independently of movement
collision. The player owns a dedicated `PlayerHazardSensor2D`, a fixed 10-unit
circular `Area2D` on its own non-solid collision layer. Damage zones monitor that
sensor through `AreaEntered`/`AreaExited` rather than observing the player
`CharacterBody2D`. Walk, Crouch, Sprint, and Crawl therefore share one hazard
footprint, and stationary C/Z changes cannot stop, restart, or retrigger immediate
hazard damage. Physical movement collision remains owned exclusively by the normal
and Crawl shapes.

Immediate-entry damage remains a separate one-shot action for each real sensor
entry. Periodic damage keeps per-target accumulated overlap time. Every physics
update adds its valid positive `delta`, applies up to four due interval ticks, and
subtracts one full interval for each applied tick. The unspent whole intervals and
fractional remainder remain in the accumulator for later updates instead of being
reset or discarded. With a one-second interval, three seconds of uninterrupted
overlap therefore produces three periodic ticks in addition to any configured
entry hit. Exiting removes both the target and its accumulated time. Dead and
prototype-completed health models are rejected and removed from tracking.

The event-driven visibility HUD displays `HIDDEN`, `DIM`, `VISIBLE`, or `EXPOSED`
with the current multiplier and becomes `DEAD` on player death. It is explicitly
bound by `Main`, performs no per-frame polling, and reveals no individual mutant
awareness state.

### Earlier corrections retained through Stage 13

- Direct valid sight always selects `CHASE`/`ATTACK`; a stuck-navigation retry can
  postpone only path assignment and can no longer leave a seeing mutant on patrol
  or investigation.
- `LostTargetGraceSeconds` now has one meaning: remain in `CHASE`, move to the fixed
  last-seen point for two seconds, then enter `SEARCH` if sight has not returned.
  Unrelated noise does not reset that timer; valid reacquisition immediately wins.
- The noise HUD is bound to player health. Death synchronously cancels its silence
  timer, displays `SILENT`, and rejects later player-noise presentation.
- Stage 9 sprint now drains only after meaningful real displacement and shows
  `WALK` rather than retaining sprint visibility while blocked against a wall.
- Footsteps now accumulate traveled distance; C/Z/mode switching neither resets
  the cycle nor creates a duplicate occurrence.
- Reaching zero stamina now requires a real Left Shift release before sprint can
  start again. Recovery may continue while Shift remains held.


## Stage 13 objective and power loop

The current objective is held by the plain `ObjectiveProgressModel`. It starts at
`FindFuse` and accepts only the next forward stage: `RestorePower`, `OpenExit`,
`ReachExit`, then terminal `Completed`. Duplicate, backward, and out-of-order
requests return `false` and emit no event. `ObjectiveHudController` is explicitly
bound by `Main` and refreshes only from the model's `Changed` event.

The replacement fuse is the ordinary `replacement_fuse` item with a maximum stack
size of one and no use effect. It is stored in the Maintenance Fuse Crate beyond
the west crawl passage. It uses the existing container, transfer, inventory,
selection, and stacking code, so it may be moved between inventories before use.
Acquiring it advances the objective from `FindFuse` to `RestorePower`; transferring
it away does not move progression backward, but the fuse box still requires the
item to be present in the player inventory.

`PowerCircuitModel` is a plain C# state holder with only `HasInstalledFuse` and
`IsPowered`. `FuseInstallationService` prevalidates actor eligibility, the
`RestorePower` objective stage, circuit state, and one `replacement_fuse`. Inventory
removal and circuit activation are prepared as internal non-notifying plans, checked
again, and committed synchronously without callbacks between the two mutations.
Only after both models are consistent does the service publish one inventory change,
one circuit `Changed`, and one `PowerRestored` event. The shared `SafeEventPublisher`
invokes subscribers independently, reports subscriber exceptions through `Trace`,
and continues required notifications; therefore a failing HUD subscriber cannot
prevent the `PowerRestored` progression callback. Failed and repeated installation
attempts mutate nothing. Successful installation emits one medium interaction noise
and displays `Power restored.`.

Focused transaction validation is available through:

```bash
godot --headless --path . scenes/validation/FuseInstallationServiceValidation.tscn
```

It covers a missing fuse, duplicate installation, exact notification counts, and a
throwing inventory/circuit/objective subscriber while verifying that critical
`PowerRestored` progression still reaches `OpenExit`.

### Flashlight battery transaction hardening

`FlashlightBatteryService` now follows the same prepared-commit policy. It first
validates player eligibility, reentrancy, useful restorable capacity, near-full
state, and one available `battery`. `InventoryModel` and `FlashlightModel` then
prepare exact removal/restoration plans and revalidate them immediately before a
synchronous commit. The Battery is removed and the calculated charge is restored
without intermediate callbacks. Only after both states are complete are one
inventory `Changed` and one flashlight `Changed` published through the shared
`SafeEventPublisher`. A failing inventory HUD cannot stop flashlight delivery, and
a failing flashlight HUD cannot undo or repeat the already committed consumption.
All rejected paths leave both models unchanged.

Focused validation is available through:

```bash
godot --headless --path . scenes/validation/FlashlightBatteryServiceValidation.tscn
```

It covers missing Battery, near-full rejection, ineligible replacement, exact
one-Battery/one-restoration success, duplicate full-charge rejection, and throwing
inventory/flashlight subscribers while verifying that healthy subscribers still run.

The existing `SlidingDoor2D` now has an optional power requirement. The ordinary
corridor door remains independent of the circuit. Each door uses the terminal state
machine `Closed → Opening → Open`; only `Closed` accepts interaction. The emergency
exit keeps its collision closed while unpowered, reports `The emergency exit has no
power.`, and emits no opening noise. After restoration it uses the existing tween and
interaction-noise path. A naturally completed tween snaps the panel to its authored
target, disables the blocking collision, disables the interaction shape, enters
`Open`, and then publishes `Opened` once. The objective remains `OpenExit` while the
panel is moving and advances to `ReachExit` only from that completed event. Killed,
deleted, or otherwise interrupted tweens cannot publish completion.

`PowerControlledLight2D` subscribes to the same circuit. At startup its built-in
`PointLight2D`, visual overlay, and powered exposure zone are off. Power restoration
enables them immediately. The zone uses priority 30 and multiplier `1.35`, so the
existing posture × ambient × flashlight visibility formula updates without pixel
sampling or frame polling. Flashlight charge and the circuit remain independent.

The exit zone behind the emergency door completes only a living player at
`ReachExit`. A constant-size `PlayerObjectiveSensor2D` is explicitly attached to the
player on its own non-solid collision layer. `ObjectiveExitZone2D` keeps the normal
`BodyEntered` path and also observes that sensor. If the player enters early, the
zone remains subscribed to `ObjectiveProgressModel.Changed`; when the emergency door
finishes opening and the stage becomes `ReachExit`, the zone inspects its current
sensor overlaps and completes immediately without requiring an exit and re-entry.
There is no per-frame overlap polling, and the one-shot completion guard prevents
both body and sensor signals from publishing twice.

Completion closes inventory and transfer UI, permanently disables movement,
interaction, combat, posture, flashlight input, inventory reopening, and player
footstep emission, keeps the world visible, and displays `LINE ZERO — PROTOTYPE
ESCAPE COMPLETE` with a Quit button. Death and completion are both authoritative
terminal input blockers; closing UI cannot restore gameplay.

## Stable hazard-sensor and timing regression test

1. Stand with the fixed hazard sensor overlapping the edge of the exposed electrical
   floor and note the immediate damage plus the periodic tick cadence.
2. Without moving, repeatedly press `Z` and `C` through Crawl, Crouch, and Walk.
   Confirm the hazard does not emit a new immediate-entry hit, stop ticking, or alter
   its accumulated periodic time.
3. With `TickIntervalSeconds = 1`, remain continuously inside for approximately three
   seconds. Confirm three periodic ticks occur, in addition to the separate optional
   entry hit.
4. Simulate a long physics update of approximately `3.25` seconds. Confirm three
   periodic ticks are applied and approximately `0.25` seconds remains toward the
   next tick.
5. Simulate more than four elapsed intervals in one update. Confirm no more than four
   ticks occur in that update and the remaining overdue intervals are applied, at
   most four per update, on following physics updates.
6. Move fully outside the hazard. Confirm the target and all pending accumulated time
   are removed. Re-enter and confirm exactly one new immediate hit and a fresh
   periodic accumulator.
7. Die or reach `ESCAPE COMPLETE` while inside the hazard. Confirm no later tick can
   reduce health.

## Stage 13 manual test flow

1. Start `Main.tscn`; confirm the objective reads `FIND A REPLACEMENT FUSE`.
2. Visit the maintenance fuse box before obtaining the item. Press `E`; confirm
   `A replacement fuse is required.`, no inventory change, and no power change.
3. Visit the emergency exit before power. Confirm its prompt indicates no power,
   `E` reports `The emergency exit has no power.`, and the door stays closed.
4. Crawl through the west maintenance duct and open `Maintenance Fuse Crate`.
5. Transfer the Replacement Fuse to the player. Confirm it behaves as a normal
   one-slot item and the objective becomes `RESTORE POWER AT THE MAINTENANCE PANEL`.
6. Store the fuse in another container and verify the fuse box still rejects the
   attempt without changing progression; take it back.
7. Install the fuse. Confirm exactly one fuse disappears, one medium noise occurs,
   the message is `Power restored.`, and the objective becomes `OPEN THE EMERGENCY EXIT`.
8. Interact with the fuse box again. Confirm the prompt is `Power is online` and no
   second item or event is consumed.
9. Verify the exit light, green indicator, overlay, and powered visibility zone
   activate immediately; entering the bay changes the ambient multiplier to `1.35`.
10. Open the emergency exit. Confirm the original sliding animation and door noise
    remain, the objective stays `OPEN THE EMERGENCY EXIT` while the panel is moving,
    and it becomes `REACH THE EXIT` exactly once only after collision is disabled.
11. Enter the exit zone before `ReachExit` in a development setup and remain
    inside. Confirm it does not complete early. Open the emergency door and verify
    that, after the door finishes and progression reaches `ReachExit`, the zone
    completes immediately without requiring the player to leave and re-enter.
12. Confirm the objective reads `ESCAPE COMPLETE`, the completion panel appears,
    the scene remains visible, and movement, C/Z/Shift, E, fire, reload, F, B, Tab,
    transfer actions, and footsteps remain disabled.
13. Repeat from a fresh run, die before installing/opening/completing, and confirm
    no objective transition or world interaction can occur after death.
14. Re-run Stage 1–12 checks for inventory transfer, Tab release handling, crawl
    collision, flashlight drain/replacement, visibility zones, hearing, combat,
    mutant FOV/walls/memory, health, stamina, and bounded footsteps.

## Stage 13 known limitations

Progression is intentionally one fixed in-memory sequence. There is no save/load,
quest log, branching, reward, checkpoint, scene transition, removable fuse, second
circuit, electrical puzzle, generator, keycard, dialogue, cinematic, audio,
multiplayer, or 3D presentation. Powered exposure is authored zone data rather than
rendered-light analysis.

## Stage 12 manual test flow

1. Start `Main.tscn`. Confirm all previous HUDs, pickups, containers, hazards,
   targets, two mutants, crawl passage, and flashlight remain present with no
   startup error.
2. Collect one of the existing Battery pickups or take one from a container. At
   full charge press `B`; confirm `Flashlight battery is already full.` and no
   Battery count change.
3. Turn the flashlight on briefly until the HUD is just below full. When the
   one-decimal display still reads `100.0 / 100.0`, press `B`; confirm no Battery is
   consumed. Drain until the HUD reads below 100.0, press `B`, and confirm exactly
   one Battery is consumed and charge reaches full.
4. Temporarily set charge below 25 and below 10. Toggle `F` at each threshold and
   confirm `ON · LOW` ↔ `OFF · LOW` and `ON · CRITICAL` ↔ `OFF · CRITICAL`.
5. Cross the dark maintenance zone with the flashlight off. Its visible and gameplay
   bounds are exactly `X = -840..-480`, `Y = -120..120`; verify ambient changes to
   `0.70` when the constant-size sensor crosses the visible edge and returns to
   `1.00` at the same edge. Then compare Walk, Crouch, and Crawl categories. Crawl
   in darkness should be the least visible state.
6. Toggle the flashlight in darkness and confirm the multiplier changes immediately
   by `×1.45` without movement, battery-count, or flicker dependence.
7. Walk and sprint through the bright corridor. Confirm ambient `1.25`; Sprint with
   the flashlight on should report the highest exposure.
8. Move across zone boundaries and any overlap edge repeatedly. Confirm deterministic
   priority selection, return to `1.00` outside zones, and no duplicate HUD refresh
   or stale category. Stop exactly on a boundary and alternate `Z` and `C` without
   translating the player; the posture component of visibility may change, but the
   ambient multiplier and active zone name must remain unchanged until the fixed
   light-exposure sensor actually moves across the boundary.
9. Approach a mutant from the same clear direction under dark/off, normal/off,
   normal/on, and bright/on conditions. Detection distance should change while FOV
   and wall occlusion remain mandatory. Breaking sight must retain existing memory
   behavior rather than clearing it.
10. Rapidly switch Walk/Crouch/Sprint/Crawl while moving. Confirm accumulated
    footstep strength reflects each segment. Simulate a long frame or large valid
    displacement: at most three footstep events may occur, each no louder than a
    normal Sprint footstep, with fractional progress continuing afterward.
11. Open inventory and loot transfer. Confirm `F/B` remain blocked while visibility
    continues reflecting the current zone and already-selected flashlight state;
    closing UI must not leak the same input event.
12. Die with the flashlight on inside a zone. Confirm immediate flashlight off,
    visibility `DEAD`, disabled battery replacement, no post-death detection, and
    no input re-enable after closing modal UI.
13. Re-run the retained Stage 1–11 regression checks below for Tab, Crawl, Sprint,
    combat, reload, health, inventory, transfer, noise, mutant hearing, sight,
    navigation, and the blocked atomic crawl exit.

## Retained Stage 1–11 regression flow

1. Run the main scene. Confirm `HEALTH 100 / 100`, `SERVICE PISTOL 3 / 0`,
   `NOISE: SILENT`, `STAMINA 100.0 / 100.0`, `MODE: WALK`, two mutants, the west-wall
   maintenance opening, and no startup errors.
2. Walk horizontally and diagonally. Confirm terminal walk speed remains 220,
   diagonal movement is not faster, and stopping settles velocity to zero.
3. Press `C`, move, and confirm `MODE: CROUCH`, terminal speed 121, normal collision
   profile, and `LOW` footsteps. Press `C` again to return to Walk.
4. Hold Left Shift while moving in open space. Confirm `MODE: SPRINT`, terminal speed
   341, `MEDIUM` footsteps, falling stamina, and sight range 368.
5. Keep Shift and movement held against a solid wall. After actual displacement
   stops, confirm mode becomes `WALK`, stamina no longer falls, visibility returns to
   1.0, and no footstep is emitted. Move away from the wall without releasing Shift
   and confirm sprint resumes naturally.
6. Sprint to zero. Confirm same-tick Walk fallback and `0..100` clamping. Continue
   holding Shift through recovery above 10 and begin moving: sprint must not restart.
   Release and press Shift again to prove it can restart.
7. Confirm the existing 0.75-second recovery delay and approximately 18 stamina per
   second recovery while walking, crouching, crawling, idle, blocked, or UI-disabled.
8. In open space press `Z`. Confirm `MODE: CRAWL`, terminal speed 77, the normal
   shape disabled, the smaller crawl shape active, visibility multiplier 0.40, no
   stamina drain, and recovery continuing when applicable.
9. Press `Z` again in open space. Confirm the normal profile is restored and the
   posture becomes `CROUCH`; press `C` to return to Walk.
10. Rapidly alternate `C` and `Z`. After every deferred switch, inspect the player
    remote scene if needed and confirm exactly one of `NormalCollisionShape` and
    `CrawlCollisionShape` is active. The player must not jump or shift position.
11. Approach the narrow west maintenance opening while walking or crouching. Confirm
    the normal profile cannot cross its 24-unit inner gap.
12. Press `Z` and crawl through the complete duct. Confirm reliable traversal, no
    stamina cost, quiet `LOW` footsteps, and a Battery in the open pocket beyond.
13. While centered in the narrow duct, press `Z` once. Confirm the player remains in
    Crawl and `Cannot stand here.` appears once. Wait without more input and confirm
    the message is not repeatedly requested; each new Z press may request it once.
14. In the same blocked location, release and press Left Shift. Confirm the sprint
    request cannot force the normal shape or teleport the player and produces the
    same single rejection. In the open pocket, Z must successfully exit to Crouch.
15. Move continuously while switching among Crawl, Crouch, Walk, and Sprint faster
    than a step cycle. Confirm progress is preserved: movement eventually emits a
    step, while switches create neither silence nor duplicate pulses. Stand still
    and press C/Z repeatedly; no step may appear.
16. Compare footstep distance/intensity pairs: Crawl `220 / 0.20`, Crouch
    `132 / 0.45`, Walk `110 / 1.00`, and Sprint `90 / 1.80`. Approximate open-space
    hearing radii are 26, 58.5, 130, and 234 units before wall attenuation.
17. In clear forward sight compare Crawl/Crouch/Walk/Sprint effective mutant ranges
    of 128/208/320/368. Close distance must still detect Crawl; FOV and World walls
    must block all modes, and a posture change must not erase target memory.
18. Enter valid sight during `PATROL`, `INVESTIGATE`, or chase-route retry cooldown.
    Confirm immediate `CHASE`, or `ATTACK` in melee range, regardless of route retry.
19. Break established sight. Confirm two complete seconds of `CHASE` toward the fixed
    last-visible point, then `SEARCH`. Unrelated noise must not alter or extend grace;
    direct melee reacquisition must immediately select `ATTACK`.
20. Let `SEARCH` terminate by arrival, navigation failure, repeated lack of progress,
    or its five-second maximum. Only a strong gunshot may redirect an active search
    into anonymous `INVESTIGATE`.
21. Keep the corridor door closed during chase pressure. Confirm a route request
    after roughly 1.25 seconds without eight units of progress while state remains
    `CHASE` during retry delay. Mutants must never route into the maintenance duct.
22. Open the corridor door or search a metal container. Confirm exactly one `MEDIUM`
    pulse for the first accepted container search. Close with `Escape`, rapidly repeat
    `E` → `Escape`, and confirm the same container remains accessible but produces no
    further world-noise pulses; panel controls and inspection messages stay silent.
23. Fire a loaded shot near a hidden mutant. Confirm one `LOUD` pulse and independent
    investigation in range; hits and misses are equally loud and an empty click is
    silent.
24. Let a mutant attack. Confirm each completed telegraph deals 15 damage no faster
    than once per second and final wall/range validation prevents a through-wall hit.
25. Enter Crouch, then Crawl, while testing ordinary inventory with `Tab`. Both
    postures remain remembered, velocity stays zero, C/Z/Shift/movement cannot leak,
    stamina recovery and AI world time continue, and focused-control Tab still closes.
26. Open loot transfer and confirm movement, interaction, combat, and footsteps are
    disabled while transfer conservation, `Escape`, and Close continue to work.
27. Recheck every Stage 1–9 path: pistol, reload, pickups including the duct Battery,
    both containers, transfers, door, terminals, target dummies, hazard, medkit,
    mutant sight/hearing/search/attack/death, and inventory conservation.
28. Create a recent player noise and partially drain stamina, but keep the
    player alive for the flashlight checks below.
29. Confirm the flashlight starts on, follows mouse-facing aim in Walk, Crouch,
    Sprint, and Crawl, and the HUD initially shows `FLASHLIGHT`, `100.0 / 100.0`, `ON`,
    and the current spare-battery count.
30. Press `F` once and confirm exactly one transition to `OFF`; wait and verify
    charge does not fall. Press `F` once more and confirm exactly one transition to
    `ON` and approximately one charge unit drains per second.
31. Keep moving while rapidly changing Walk/Crouch/Sprint/Crawl. Confirm flashlight
    charge is unaffected by movement mode and the same visual follows aim without a
    duplicate crawl light.
32. Open normal inventory with `Tab` while the flashlight is on. Confirm F/B do
    nothing, charge continues draining because world simulation is not paused, and
    closing with the same held input cannot toggle or replace a battery. Repeat with
    loot transfer.
33. Pick up at least one existing Battery. Note the HUD reserve, allow charge to
    fall below full, press `B` once, and confirm one Battery is removed, charge is
    restored and clamped to 100, the reserve falls by one, and exactly one
    `Battery replaced.` message appears.
34. Press `B` at full charge and confirm `Flashlight battery is already full.`, no
    inventory change, and no flashlight change. With no Batteries remaining, drain
    below full and confirm `No spare batteries.` with no mutation.
35. For threshold testing, temporarily set `DrainPerSecond` in
    `StandardFlashlight.tres` to a higher positive value or lower the model's test
    starting charge locally. Confirm the HUD crosses to `ON · LOW` at 25,
    `ON · CRITICAL` at 10, and `EMPTY` at zero; toggling off preserves
    `OFF · LOW/CRITICAL`, and depletion turns the light off once. Restore production
    configuration afterward (`MaximumCharge=100`, `DrainPerSecond=1`, full start).
36. Recharge above each threshold and drain through it again. Confirm LOW and
    CRITICAL crossing notifications can occur again only after restoration above
    the corresponding threshold; ordinary drain still produces at most one
    flashlight `Changed` notification per physics update.
37. Exercise the mode-switch noise regression by sprinting almost to a step, then
    switching to Walk/Crouch/Crawl before crossing. Confirm the emitted strength
    retains the Sprint segment instead of becoming the final mode's quiet value;
    rapid switching creates neither missing nor duplicate steps.
38. Simulate a long physics frame or controlled large valid displacement in a debug
    build. Confirm no more than three ordinary footstep events are emitted, every
    intensity is at most the configured Sprint footstep, and the retained fractional
    cycle continues on the next update. Teleport-like movement, scene reload
    initialization, and invalid delta emit none.
39. In a debug setup, request Crawl exit in a clear location and place a solid body
    into the normal-shape volume before the deferred callback. Confirm Crawl is
    restored, normal collision is never enabled into the blocker, exactly one
    collider remains active, and one rejection is emitted.
40. Create a recent player noise, partially drain stamina and flashlight charge,
    enter Crawl, then die. Confirm immediate `NOISE: SILENT`, `STAMINA DISABLED`,
    flashlight `DEAD` and off, stopped drain, exactly one valid collision profile,
    closed panels, and no later movement, F/B, posture, interaction, combat, or
    player-footstep activity.

## Implemented scope

- reusable main, test-level, player, and debug-HUD scenes;
- a dark greybox starting room, corridor, side room, doorway, crawl-only maintenance
  duct/open pocket, and World-layer wall collision;
- smooth normalized movement using configurable acceleration and deceleration;
- validated walk/crouch/sprint/crawl speeds with action-based C, Z, and Left Shift
  controls;
- distinct normal/crawl shape resources, deferred collision-profile switching, and
  a self-excluding normal-shape clearance query that rejects blocked exits;
- a plain, clamped stamina model with immutable results, exact change/depletion/
  recovery events, delayed recovery, and a 10-stamina sprint restart threshold;
- event-driven stamina/progress and movement-mode HUD presentation;
- mouse-facing player visuals and flashlight without rotating the collision body;
- smoothed player-following camera;
- ambient darkness and a generated SVG flashlight cone;
- a validated typed flashlight definition resource and plain runtime charge model
  with clamping, threshold crossings, depletion, immutable results, and exact
  logical change events;
- a dedicated 2D flashlight controller that owns input/presentation/drain while the
  model remains independent of nodes, vectors, physics, and UI;
- atomic one-battery replacement through prepared inventory/charge plans, deferred
  safe model notifications, the stable `battery` item ID, near-full epsilon handling,
  eligibility validation, and duplicate guards;
- an explicitly bound, event-driven flashlight HUD with honest one-decimal charge,
  power-plus-warning status, reserve count, modal/death coordination, and no polling;
- a composed visibility controller using posture × ambient zone × flashlight state,
  with positive finite invariants and the existing `IVisibilityTarget` contract;
- reusable prioritized dark/bright `LightExposureZone2D` areas with deterministic
  overlap selection and safe removal;
- an explicitly bound event-driven visibility HUD showing HIDDEN/DIM/VISIBLE/EXPOSED
  and DEAD without revealing individual mutant awareness;
- FPS, player position, and flashlight state in a throttled debug HUD;
- dimension-independent interaction context, contract, and typed result;
- deterministic 2D target selection using distance, facing, priority, and hysteresis;
- a world line-of-sight check against solid level collision;
- a bottom-center interaction prompt and reusable timed message presenter;
- an animated sliding corridor door that stays open after use;
- one reusable terminal and one single-use emergency panel;
- reusable item-definition resources with stable IDs and per-item stack limits;
- a dimension-independent 12-slot player inventory with merge-first stacking;
- explicit partial-add results that never discard quantities that do not fit;
- interactable Scrap, Battery, and Gas Mask Filter pickups in the existing level;
- an observable Scrap sequence where `7 + 18` produces stacks of `20 + 5`;
- an event-updated 12-slot inventory panel with selection, item details, and a
  guarded Use request toggled with `Tab`;
- typed static seed entries for validated container starting contents;
- transactional inventory-to-inventory transfers with immutable results;
- merge-first destination filling, partial transfers, and quantity conservation;
- reusable solid loot-container scenes using the existing interaction system;
- a Maintenance Locker in the corridor and an independent Emergency Supply
  Cabinet in the side room;
- a two-inventory panel showing every player and container slot, including empties;
- Take One, Take Stack, Store One, and Store Stack operations;
- modal input coordination that stops movement and world interaction without
  pausing the scene tree;
- Close-button and `Escape` handling with prompt and gameplay restoration;
- a plain reusable health model with clamped damage/healing and strongly typed
  change results;
- a reusable `HealthComponent`, `IHealthOwner`, and 100-health player setup;
- ordered `Changed`, `Damaged`, `Healed`, and single-fire `Died` events;
- a reusable multi-target `DamageZone2D` with immediate and interval damage;
- a visible exposed-electrical-floor hazard in the starting room;
- an event-bound health HUD with both a bar and `HEALTH current / maximum` text;
- typed item-use effects, context/results, a centralized use service, and
  controlled one-event inventory consumption;
- a Field Medkit resource that stacks to 3 and restores up to 35 health;
- one medkit world pickup and medkit stock in the Emergency Supply Cabinet;
- centralized alive/modal input coordination and minimal no-respawn player death;
- a reusable firearm definition and plain magazine/reload state with typed results;
- an equipped Service Pistol with an initial three-round magazine, 25 damage,
  semi-automatic input, a 0.25-second fire interval, and 700-unit range;
- first-collision 2D hitscan from a stable player-side weapon origin, with a
  pre-fire World-geometry muzzle-clearance check, player exclusion, and a single
  reusable short-lived tracer;
- ordinary stackable 9mm ammunition, deterministic removal by item ID, two world
  pickups, and 16 rounds in the Maintenance Locker;
- a non-blocking 1.2-second reload transaction with current-reserve recalculation,
  partial loading, exact inventory consumption, and deterministic cancellation;
- an event-bound weapon HUD showing weapon, magazine, reserve, and reload/death state;
- two reusable 100-health target dummies with damage flash and destroyed state;
- combat input coordination for inventory, transfer UI, same-frame close input,
  and permanent current-run death locking;
- a validated, dimension-independent tunnel-mutant definition with 75 health and
  scalar movement, perception, chase, attack, and patrol tuning;
- an explicit deterministic mutant state machine with idle, ordered patrol, sight
  chase, last-known-position search, validated melee attack, terminal death, and
  typed state/attack events;
- a reusable `CharacterBody2D` mutant scene composed from `NavigationAgent2D`,
  `HealthComponent`, one solid/damageable collider, primitive visuals, attack and
  damage feedback, and event-driven world health/state labels;
- a baked test-level navigation polygon and controlled path refresh, with one
  two-point patrol mutant and one initially idle side-room guard;
- interval-based distance/FOV perception with a reusable exclusion list and
  World-layer ray checks that prevent sight and attacks through walls;
- a dimension-free `IVisibilityTarget` contract with deterministic crawl/crouch/
  walk/sprint sight-range multipliers of `0.40 / 0.65 / 1.0 / 1.15`;
- explicit one-time player binding from `Main` through `PlayableLevelController2D`,
  with target-death and target-lifetime cleanup and no per-frame scene searches;
- variable-count direct mutant binding, including valid zero-mutant test scenes,
  with clear rejection of incorrectly configured direct children;
- terminating last-known-position search with a five-second ceiling, explicit
  navigation-failure handling, progress monitoring, one forced repath, and bounded
  abandonment instead of permanent wall pushing;
- explicit navigation stop semantics that clear controller intent, reset the agent
  target to the current position, and remove stale velocity;
- static navigation cutouts around both solid containers and permanent target
  dummies, while retaining the sliding door as a documented dynamic limitation;
- immutable shared `NoiseEvent` metadata and typed 2D occurrences/perception values;
- one scene-owned `NoiseSystem2D` with explicit listener registration, immediate
  delivery, same-frame duplicate suppression, hearing sensitivity, deterministic
  range checks, and bounded multi-barrier World-layer attenuation per in-range listener;
- controlled player footstep, accepted-pistol-shot, sliding-door, and metal-
  container noise emitters, with no persistent sound nodes or per-frame hearing scan;
- deterministic `INVESTIGATE` behavior with kind/intensity/distance/recency priority,
  stronger-target replacement, alert waiting, sight override, and safe expiry;
- direct-sight state priority independent of route retry cooldown, plus a two-second
  `CHASE` grace before fixed-position `SEARCH`;
- weighted distance-cycle footsteps using actual displacement, per-segment mode
  contribution, a three-event long-frame cap with per-event Sprint bounds, retained
  fractional progress, teleport suppression, and the existing noise system;
- an event-driven `SILENT`/`LOW`/`MEDIUM`/`LOUD` player noise HUD;
- synchronous death reset and stale-timer protection for noise and stamina HUDs;
- architecture notes for replacing 2D presentation with 3D later.

### Multi-barrier noise attenuation

`NoiseSystem2D` tests the straight source-to-listener segment against authored
World collision shapes. Up to four distinct `(collider_id, shape_index)` barriers
are processed, so compound wall bodies still distinguish separate room walls while
the same barrier cannot be counted twice. Perceived intensity is
`originalIntensity × WallAttenuation^barrierCount`; with the current factor `0.5`,
one through four barriers retain `50%`, `25%`, `12.5%`, and `6.25%`. Source,
emission-origin, and listener colliders are excluded. Direct-distance rejection,
base radii, hearing sensitivity, minimum audible intensity, and mutant scoring are
unchanged. No reflections, materials, portals, frequency bands, or alternate-path
search were added.

## Intentionally excluded

This stage has no rendered-pixel light sampling, shadow-geometry analysis,
flashlight-beam intersection or direction reactions, light switches, electrical
networks, night vision, suppressors, alternative or weapon-mounted flashlight
types, automatic replacement, battery quality/partial battery items, charging
stations, generators, crafting, overheating, durability, advanced lighting,
flashlight audio, persistence, or multiplayer synchronization. It also has no drag
and drop, arbitrary quantity
entry, slot rearrangement,
sorting, filtering, equipment, item dropping, hotbar, crafting, random loot, loot
tables, weapon switching, additional firearms, weapon pickups, automatic weapons,
projectile or pellet simulation, player melee weapons, armor, resistances, status
effects, regeneration, advanced medical mechanics, item cooldowns, recoil
simulation, procedural spread, attachments, durability, shell physics, polished
animations, audio, respawning, game-over menus, saving, multiplayer, quests, or
dialogue trees. Crawl changes a top-down body footprint, speed, noise, and sight
range only: it does not simulate vertical body height and has no prone combat
animation, crawl-specific item restrictions, crawl stamina cost, vent loading
transition, ladder, climbing, crawl enemy, moving-object crawl, aim-based collider
rotation, jump, vault, dodge, roll, leaning, cover, sprint attack, exhaustion damage,
breathing, or heartbeat. Enemy scope is one tunnel-mutant type only: there is no suppressor,
light-level visibility meter, camouflage, material/frequency sound
model, echo/reflection system, ranged enemy, pack communication, shared blackboard,
door operation, dynamic navigation rebaking, spawning, waves, loot drop, knockback,
stagger, or advanced animation. The only firearm remains the equipped Service
Pistol; item use remains limited to the Field Medkit.

See [`docs/architecture.md`](docs/architecture.md) for the current boundaries and
the intended 3D migration path.

## Runtime hardening audit

The current cumulative build fixes the remaining wall-fire, exit, and scene-wiring
failures together rather than masking their symptoms:

- `LightExposureSensor2D`, `PlayerHazardSensor2D`, `PlayerObjectiveSensor2D`, and
  `LightExposureZoneOverlay2D` resolve explicit exported `NodePath` dependencies at
  startup. Overlap consumers use `TryGet...` accessors, so an invalid sensor cannot
  cascade into later `NullReferenceException` callbacks.
- A shot is prevalidated before ammunition mutation. If World geometry intersects
  the stable weapon-origin-to-muzzle corridor (including a one-unit authored
  clearance margin), the result is `MuzzleObstructed`: no round, tracer, damage,
  cooldown, or gunshot noise is produced. Valid hitscan, tracer, and gunshot noise
  all start from the player-side `WeaponOrigin`, so a muzzle beyond a thin wall
  cannot bypass that wall. Damage, fire interval, reload, and hearing values remain
  unchanged.
- `Main` subscribes to power, door, and exit terminal events before binding stateful
  level components. A player already inside the exit zone therefore completes as
  soon as the fully opened door advances the objective to `ReachExit`.

Regression flow: press the player against a thin wall and verify fire returns
`Muzzle obstructed` without consuming ammunition; restore power and open the
emergency exit, verify `OpenExit` remains during motion and the door cannot be
interacted with again after opening; enter or remain inside the exit zone and verify
`Completed` plus the terminal panel occur exactly once.

## Automated feature tests

The project now includes a built-in headless Godot C# regression harness with 20
independently selectable feature suites. It covers domain models, transactions,
HUD event binding, movement/Crawl collision profiles, constant sensors, hazards,
footstep debt, multi-wall hearing, mutant perception, wall-safe firearm hitscan,
container noise, powered emergency-door completion, early exit overlap, serialized
scene references, authored resources, and a full `Main.tscn` composition smoke test.

Run everything:

```bash
./scripts/test-all.sh
```

The test script uses Godot `--import` and waits for resource scanning to finish before starting the suites.
After a previously interrupted import, run once with `CLEAN_GODOT_CACHE=1` to rebuild the generated `.godot` cache cleanly.

Run one subsystem:

```bash
./scripts/test-feature.sh weapon-integration
```

List suite IDs:

```bash
./scripts/list-tests.sh
```

The scripts run `dotnet restore`, `dotnet build`, a Godot headless editor import,
and then the selected tests. Set `GODOT_BIN` or `DOTNET_BIN` when executables are
not on `PATH`. See [`docs/testing.md`](docs/testing.md) for the suite matrix,
direct runner commands, exit codes, isolation rules, and testing boundaries.

## Stage 14 stabilization

The public prototype name is **No Way Up**. The internal `LineZero` assembly and
namespaces remain unchanged for compatibility.

Stage 14 contains stabilization only:

- Noise HUD severity is derived from the emitted `NoiseOccurrence2D` kind and
  intensity. It never infers an already-emitted footstep from the player's current
  posture. Quiet footsteps are LOW, sprint-level footsteps are MEDIUM, gunshots are
  LOUD, and interaction severity follows its emitted intensity.
- The objective exit listens exclusively to `PlayerObjectiveSensor2D` on its
  dedicated collision layer. Movement-body and Crawl-profile changes cannot complete
  the objective; an already-overlapping sensor is checked when `ReachExit` begins.
- `HealthModel`, `StaminaModel`, and `FirearmState` publish subscribers independently
  through `SafeEventPublisher`. A failing UI subscriber cannot block later handlers
  or critical events such as `Died`.
- Reload completion is a two-model transaction implemented by
  `FirearmReloadService`: reserve and magazine outcomes are calculated first, both
  models mutate without notifications, and notifications publish only after a
  complete commit.
- Medkit use is a two-model transaction in `ItemUseService`: useful healing and exact
  source-slot consumption are prepared before either model changes.
- Mutant perception, damage, and hearing now feed one priority resolver. Confirmed
  sight, active Chase/Attack, and lost-target grace cannot be downgraded by footsteps,
  interactions, or gunshots. Player damage confirms target memory and enters or
  preserves Chase directly; same-frame noises are reduced to one strongest or most
  relevant stimulus before investigation is considered.

### Permanent implementation checklist

All future work must preserve these rules: plain C# owns gameplay state; Godot nodes
adapt input/physics/presentation; `Main` composes explicit dependencies; multi-model
operations validate, calculate, mutate silently, then publish immutable results;
events are safe and describe completed state; periodic systems preserve remainder
with bounded catch-up; gameplay triggers use fixed dedicated sensors rather than
movement colliders; death and completion are terminal; UI is event-driven and shows
honest model values; Resources contain configuration only; and every change receives
boundary, duplicate-input, terminal-state, low-FPS, subscriber-failure, scene-binding,
and regression tests before completion.
