# 3D migration phase validation log

This log records only checks that were actually executed during the migration.
The final validation section supersedes interim counts after later phases add more
tests.

## Audit baseline

- The configured main scene remained `res://scenes/main/Main.tscn`.
- Shared authoritative models were found under `src/Gameplay` for inventory,
  health, stamina, flashlight, firearm state/reload, item use, power, and
  objective progress.
- Godot-specific 2D behavior remained under `src/World2D`; it was retained as the
  regression reference rather than edited into mixed 2D/3D controllers.
- Existing `LineZero.csproj.old*` files were byte-identical to the active project
  file and were removed as redundant generated backups.
- Baseline build: 0 warnings, 0 errors.
- Baseline `foundation-3d` suite: 8 passed, 0 failed.
- Baseline `Main3D.tscn` headless load: exit code 0.

## Phase 0–2: movement, aiming, camera, and occlusion

Completed behavior:

- XZ acceleration and deceleration now advance as one bounded vector, giving
  cardinal and diagonal movement equal acceleration magnitude.
- Walk, Crouch, Sprint, and Crawl use the shared `MovementMode` and
  `StaminaModel` domain types.
- Normal and Crawl movement shapes are distinct; Crawl exit uses a shape query
  that excludes the owning body.
- Death/completion are latched terminal states and cannot be cleared by closing UI
  or re-enabling input.
- The camera is fixed-yaw, fixed-pitch, orthographic, and screen-relative movement
  uses its flattened basis.
- Camera occlusion uses a dedicated collision layer, five bounded silhouette rays,
  a throttled query interval, clear-query hysteresis, and Compatibility-safe local
  material-alpha overrides that restore exact materials and shadow modes.

Stage 3D-02 later hardened this foundation with a third authored Crouch profile,
target-profile clearance checks for Crawl-to-Crouch and Crouch-to-Walk, Crawl Sprint
rejection without posture mutation, four identity-stable sensors, faded-wall count
events, designated colliding wall occluders, and an event-driven technical HUD.

Files created or modified in this phase:

- `src/World3D/CollisionLayers3D.cs`
- `src/World3D/GroundMovement3D.cs`
- `src/World3D/PlayerController3D.cs`
- `src/World3D/TopDownCamera3D.cs`
- `src/World3D/CameraOccluder3D.cs`
- `src/World3D/CameraOcclusionController3D.cs`
- `src/Core/Main3D.cs`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/Main3D.tscn`
- `src/Tests/Suites/Foundation3DFeatureTests.cs`
- `project.godot`
- removed `LineZero.csproj.old` and `LineZero.csproj.old.1`

Executed phase checks:

- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --path . --import`: passed.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=foundation-3d`:
  1 suite, 11 tests, 11 passed, 0 failed.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.

## Phase 3–4: interaction, inventory, health, hazards, and terminal input

Completed behavior:

- `PlayerInteractionSensor3D` is a constant-size dedicated `Area3D` with bounded
  candidates, throttled selection, aim-aware scoring, explicit world-only line of
  sight, owner exclusion, and safe completed-interaction events.
- Pickups and containers reuse `InventoryModel`, `InventoryComponent`, stable item
  definitions, and the existing transactional transfer panel.
- `Player3D` now owns the dimension-neutral health and inventory components. The
  existing health, stamina, inventory, interaction, loot, and item-use UI is reused
  without gameplay rules moving into controls.
- `PlayerHazardSensor3D` is constant-size and independent from normal/Crawl
  movement shapes. `DamageZone3D` uses a plain bounded periodic timer that retains
  whole-interval and fractional debt after long frames.
- Death is delivered through the health model's safe events and latched by both
  the player adapter and composition root. Closing inventory or loot UI cannot
  restore movement, aiming, interaction, or item use after a terminal state.

Files created or modified in this phase:

- `src/Gameplay/Interaction/InteractionCandidateScorer.cs`
- `src/Gameplay/Timing/PeriodicCatchUpTimer.cs`
- `src/Gameplay/Timing/PeriodicCatchUpResult.cs`
- `src/Core/Events/SafeEventPublisher.cs`
- `src/Core/Main3D.cs`
- `src/World3D/PlayerController3D.cs`
- `src/World3D/PlayerAimController3D.cs`
- `src/World3D/Interaction/Interactable3D.cs`
- `src/World3D/Interaction/PlayerInteractor3D.cs`
- `src/World3D/Interaction/InspectableObject3D.cs`
- `src/World3D/Interaction/LootContainer3D.cs`
- `src/World3D/Items/WorldItemPickup3D.cs`
- `src/World3D/Hazards/PlayerHazardSensor3D.cs`
- `src/World3D/Hazards/DamageZone3D.cs`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/items/WorldItemPickup3D.tscn`
- `scenes/3d/interactables/LootContainer3D.tscn`
- `scenes/3d/hazards/DamageZone3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/Main3D.tscn`
- `src/Tests/Suites/World3DGameplayFeatureTests.cs`
- `src/Tests/Framework/FeatureTestSuiteCatalog.cs`
- removed the dimension-neutral scorer from its old `World2D` folder location

Executed phase checks:

- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --path . --import`: passed.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=world-3d-gameplay,foundation-3d,interactions,inventory,item-use,hazards`:
  6 suites, 33 tests, 33 passed, 0 failed. The item-use suite intentionally
  logged its two throwing-subscriber fixtures while confirming delivery continued.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.

## Phase 5–6: flashlight, visibility, and gameplay noise

Completed behavior:

- The 3D flashlight reuses `FlashlightModel` and
  `FlashlightBatteryService`, presents through a validated `SpotLight3D` on the
  aim pivot, drains by elapsed time, and obeys modal, death, and completion input
  gates.
- Deterministic visibility now shares the dimension-neutral visibility rules and
  combines posture, authored `Area3D` exposure, flashlight state, and life state.
  Its constant sensor remains independent of normal and Crawl movement shapes.
- `NoiseSystem3D` carries `Vector3` occurrences, uses bounded world-only raycasts,
  excludes acoustic owners, applies independent attenuation for multiple walls,
  deduplicates identical same-frame emissions, and isolates throwing listeners.
- Footsteps use actual XZ distance. The plain cadence model preserves fractional
  progress and all completed-step debt while the adapter emits at most four steps
  per physics update. Modal UI pauses rather than erases pending debt; terminal
  states explicitly clear it.
- The reused Noise HUD classifies the completed emitted kind and intensity. The
  first completed container search emits one interaction occurrence.

Files created or modified in this phase:

- `src/Gameplay/Flashlight/*` (reused without duplicated 3D rules)
- `src/Gameplay/Noise/INoiseEventSource.cs`
- `src/Gameplay/Noise/FootstepCadenceModel.cs`
- `src/Gameplay/Noise/FootstepCadenceAdvanceResult.cs`
- `src/Gameplay/Perception/IVisibilityStateSource.cs`
- `src/Gameplay/Perception/VisibilityRules.cs`
- `src/World3D/Flashlight/PlayerFlashlightController3D.cs`
- `src/World3D/Perception/PlayerVisibilityController3D.cs`
- `src/World3D/Perception/PlayerVisibilitySensor3D.cs`
- `src/World3D/Perception/LightExposureZone3D.cs`
- `src/World3D/Noise/NoiseSystem3D.cs`
- `src/World3D/Noise/NoiseOccurrence3D.cs`
- `src/World3D/Noise/PerceivedNoise3D.cs`
- `src/World3D/Noise/INoiseListener3D.cs`
- `src/World3D/Noise/PlayerFootstepNoiseEmitter3D.cs`
- `src/UI/FlashlightHudController.cs` (reused)
- `src/UI/VisibilityHudController.cs`
- `src/UI/NoiseHudController.cs`
- `src/World2D/Noise/NoiseSystem2D.cs`
- `src/World2D/Perception/PlayerVisibilityController2D.cs`
- `src/Core/Main.cs`
- `src/Core/Main3D.cs`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/levels/LightExposureZone3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/Main3D.tscn`
- `src/Tests/Fixtures/TestNoiseListener3D.cs`
- `src/Tests/Suites/World3DStealthFeatureTests.cs`
- `src/Tests/Suites/HudFeatureTests.cs`
- `src/Tests/Framework/FeatureTestSuiteCatalog.cs`

Executed phase checks:

- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --editor --path . --import --quit-after 600`: passed.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=world-3d-stealth,foundation-3d,world-3d-gameplay,flashlight,visibility,noise-hearing,footsteps,hud`:
  8 suites, 38 tests, 38 passed, 0 failed. Three fixtures intentionally logged
  subscriber/listener exceptions while verifying that healthy delivery continued.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.

## Phase 7–8: firearm combat and mutant AI

Completed behavior:

- Firearm discharge uses one plain transaction service: firearm state and target
  health are fully validated and committed without notification, then both models
  safely publish their completed state.
- The 3D weapon uses a camera-derived intent point but validates muzzle clearance
  and resolves the authoritative shot from the physical muzzle. World geometry is
  the first-hit blocker, and gunshot noise originates on the player's safe side.
- Reload timing, cancellation, inventory conservation, fire rate, tracers, and hit
  presentation are connected without moving rules into UI or shared resources.
- The Tunnel Mutant uses `CharacterBody3D`, `NavigationAgent3D`, and an authored
  `NavigationRegion3D`. Its one decision path enforces sight, Chase grace, target
  memory, noise, Search, and Patrol priority without Chase/Investigate thrashing.
- Perception and destination refresh use bounded periodic catch-up timers that
  preserve excess debt. Melee revalidates range, life, terminal state, and a
  world-only line of sight after its wind-up.
- Dead and terminal mutants unregister from gameplay noise, stop navigation and
  physics processing, and cannot finish pending attacks.

Files created or modified in these phases:

- `src/Gameplay/Combat/FirearmDischargeService.cs`
- `src/Gameplay/Combat/FirearmDischargeResult.cs`
- `src/Gameplay/Combat/FirearmState.cs`
- `src/Gameplay/Health/HealthModel.cs`
- `src/Gameplay/Enemies/MutantDecisionContext.cs`
- `src/Gameplay/Enemies/MutantDecisionRules.cs`
- `src/World3D/Combat/FirearmShotOccurrence3D.cs`
- `src/World3D/Combat/PlayerWeaponController3D.cs`
- `src/World3D/Enemies/MutantController3D.cs`
- `src/World3D/PlayerAimController3D.cs`
- `src/Core/Main3D.cs`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/enemies/TunnelMutant3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `data/navigation/TestLevel3DNavigation.tres`
- `src/Tests/Fixtures/TestDamageableTarget3D.cs`
- `src/Tests/Suites/World3DCombatFeatureTests.cs`
- `src/Tests/Suites/World3DMutantFeatureTests.cs`
- `src/Tests/Framework/FeatureTestSuiteCatalog.cs`

Executed phase checks:

- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --editor --path . --import --quit-after 600`: passed.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=world-3d-mutant,world-3d-combat,mutant-perception,noise-hearing,world-3d-stealth,foundation-3d,world-3d-gameplay`:
  7 suites, 48 tests, 48 passed, 0 failed. Throwing subscriber/listener
  fixtures logged their expected isolated errors while healthy delivery continued.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.

## Phase 9: fuse, power, emergency exit, and completion

Completed behavior:

- `Main3D` composes one shared `ObjectiveProgressModel` and the model owned by
  `PowerController3D`; UI and 3D adapters only observe their completed state.
- The replacement-fuse pickup advances FindFuse to RestorePower. `FuseBox3D`
  delegates the inventory/circuit commit to the existing transaction service, so
  the fuse is consumed exactly once and power is never partially restored.
- Powered presentation updates a primitive control indicator, exit-bay light, and
  deterministic visibility zone from circuit events.
- `EmergencyDoor3D` rejects unpowered or duplicate input. It advances progression
  only after its tween naturally completes, its blocking collision is disabled,
  and its one-shot Opened event has been safely published.
- `PlayerObjectiveSensor3D` is constant-size and independent from both movement
  profiles. `ObjectiveExitZone3D` detects only that layer, synchronizes an already
  overlapping living sensor when ReachExit begins, rejects early/dead entry, and
  publishes completion exactly once.
- Completion latches terminal state through `Main3D`, stops player input, combat,
  interaction, item use, hazards, footsteps, and mutant AI, then displays the
  reused event-driven completion panel.

Files created or modified in this phase:

- `src/Core/Main3D.cs`
- `src/World3D/Objectives/PlayerObjectiveSensor3D.cs`
- `src/World3D/Objectives/ObjectiveExitZone3D.cs`
- `src/World3D/Power/PowerController3D.cs`
- `src/World3D/Power/PowerControlledLight3D.cs`
- `src/World3D/Interaction/FuseBox3D.cs`
- `src/World3D/Interaction/EmergencyDoor3D.cs`
- `src/World3D/CameraOccluder3D.cs`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/interactables/FuseBox3D.tscn`
- `scenes/3d/interactables/EmergencyDoor3D.tscn`
- `scenes/3d/levels/ObjectiveExitZone3D.tscn`
- `scenes/3d/levels/PowerController3D.tscn`
- `scenes/3d/levels/PowerControlledLight3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/Main3D.tscn`
- `src/Tests/Suites/World3DObjectiveFeatureTests.cs`
- `src/Tests/Framework/FeatureTestSuiteCatalog.cs`

Executed phase checks:

- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --editor --path . --import --quit-after 600`: passed.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=world-3d-objectives,objective-power,emergency-exit,prototype-flow,scene-contract,foundation-3d,world-3d-gameplay,world-3d-stealth,world-3d-combat,world-3d-mutant,hud`:
  the nine matched suites ran 42 tests, all passed. (The requested selectors
  `objective-power` and `scene-contract` did not match their actual suite IDs.)
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn -- --suite=objectives-power,scene-contracts`:
  2 suites, 15 tests, 15 passed, 0 failed. Existing direct legacy-level contract
  fixtures logged unbound-mutant configuration errors; Phase 10 tracks their
  removal before final validation.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.

## Phase 10: complete technical slice, default scene, and final regression

Completed behavior:

- `TestLevel3D` now contains the complete technical loop in one authored 3D
  level: open movement space, a crawl-only passage, deterministic bright and
  dark areas, a dedicated hazard, battery/medkit/ammunition/fuse pickups, a
  firearm, a searchable container, occluding walls, a fuse box, powered door,
  dedicated exit sensor, navigation region, patrol route, and mutant.
- `Main3D` is the project main scene. The previous 2D composition remains
  unchanged and directly runnable at `res://scenes/main/Main.tscn` for regression
  comparison.
- Scene-contract tests cover the complete 3D loop and explicit dependencies.
  Legacy mutant fixtures are paused when loaded outside their composition root,
  so isolated scene contracts do not produce unrelated configuration failures.
- Mutant last-known-position Search now has an explicit maximum duration even
  when navigation cannot reach the destination. A regression test proves the
  bounded terminal transition.
- Interactables with world collision now explicitly expose that physical body to
  the interaction adapter. The ray excludes only the selected target's own body,
  while unrelated walls still block interaction and repeated key events remain
  single-action.
- The project remains on GL Compatibility. Both the 3D and legacy scenes also
  load successfully with the Mobile rendering override; the available runner has
  no graphics display or GPU device, so a hardware renderer switch was not made.

Files created or modified in this phase:

- `project.godot`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/Main3D.tscn`
- `src/World3D/Enemies/MutantController3D.cs`
- `src/World3D/Interaction/Interactable3D.cs`
- `src/World3D/Interaction/PlayerInteractor3D.cs`
- `src/World3D/Interaction/LootContainer3D.cs`
- `src/World3D/Interaction/FuseBox3D.cs`
- `src/World3D/Interaction/EmergencyDoor3D.cs`
- `src/Tests/Suites/Foundation3DFeatureTests.cs`
- `src/Tests/Suites/MutantPerceptionFeatureTests.cs`
- `src/Tests/Suites/SceneContractFeatureTests.cs`
- `src/Tests/Suites/World3DMutantFeatureTests.cs`
- `README.md`
- `docs/3d-migration.md`
- `docs/architecture.md`
- `docs/testing.md`
- `docs/3d-migration-validation.md`

Executed final checks:

- `dotnet restore LineZero.csproj`: passed.
- `dotnet build LineZero.csproj --no-restore`: passed, 0 warnings, 0 errors.
- `godot --headless --editor --path . --import --quit-after 600`: passed with
  exit code 0.
- `godot --headless --path . res://scenes/tests/FeatureTests.tscn`: 26 suites,
  129 tests, 129 passed, 0 failed. The logged exceptions are intentional test
  fixtures for subscriber/listener isolation; each associated test passed.
- `godot --headless --path . --quit-after 180`: the configured `Main3D` passed
  with exit code 0 and no runtime warning or error output.
- `godot --headless --path . --quit-after 180 res://scenes/3d/Main3D.tscn`:
  passed with exit code 0 and no runtime warning or error output.
- `godot --headless --path . --quit-after 180 res://scenes/main/Main.tscn`:
  the preserved legacy 2D reference passed with exit code 0 and no runtime
  warning or error output.
- Both scene-load commands above also passed with
  `--rendering-method mobile`.
- `godot --headless --path . --print-fps --quit-after 1200
  res://scenes/3d/Main3D.tscn`: passed with five reported samples of
  144–145 FPS (6.89–6.94 ms/frame). This is a dummy-headless throughput sample,
  not a hardware-GPU profile or a manual-play result.
- A non-headless Mobile-renderer attempt could not start because this runner has
  neither X11 nor Wayland. No hardware FPS, GPU profiler, or visual-play claim is
  made from that environment failure.

## Stage 3D-02: completed 3D player foundation

Completed behavior:

- Added a distinct authored Crouch capsule between the Walk and Crawl profiles.
- Taller transitions validate the target profile in place, exclude the player RID,
  and never expose an intermediate collider state.
- Crawl blocks Sprint without changing posture or stamina; blocked automatic
  Crouch-to-Walk Sprint also latches until Shift release.
- Four gameplay sensor resources, transforms, layers, and masks remain unchanged
  through every posture.
- TestLevel3D contains both Crouch-height and Crawl-only clearance geometry plus
  designated colliding wall occluders.
- Occlusion reports multiple active fades; the technical HUD consumes completed
  player, stamina, clearance, input, and occlusion events without polling.

Executed final checks on 2026-07-22:

- `/tmp/no-way-up-dotnet/dotnet restore LineZero.csproj --disable-parallel`:
  passed; dependencies were up to date.
- `/tmp/no-way-up-dotnet/dotnet build LineZero.csproj --no-restore
  -nodeReuse:false -p:UseSharedCompilation=false`: passed, 0 warnings, 0 errors.
- `/tmp/no-way-up-godot/Godot_v4.7.1-stable_mono_linux_x86_64/
  Godot_v4.7.1-stable_mono_linux.x86_64 --headless --path . --import`: passed
  with exit code 0.
- The same Godot executable with `--headless --path .
  res://scenes/tests/FeatureTests.tscn`: 27 suites, 140 tests, 140 passed,
  0 failed. Logged throwing-subscriber/listener exceptions were intentional
  isolation fixtures and their cases passed.
- The same Godot executable with `--headless --path . --quit-after 180
  res://scenes/3d/Main3D.tscn`: passed with exit code 0.
- The same Godot executable with `--headless --path . --quit-after 180
  res://scenes/main/Main.tscn`: passed with exit code 0.
- Manual hardware-rendered verification was unavailable: the runner exposed no X11
  or Wayland graphics session. No manual-play or hardware-GPU claim is made.

## 3D lighting and camera-occlusion correction

Completed behavior:

- Replaced Compatibility-incompatible geometry transparency with initialization-
  time, per-instance `StandardMaterial3D` alpha overrides. Shared source resources
  remain untouched; collision remains active; substantially faded visuals stop
  casting shadows; exact original material references and shadow modes return after
  the fade.
- Replaced the centre-only ray with five bounded silhouette samples for lower,
  centre, upper, left, and right coverage. The query remains dedicated-layer-only,
  excludes the player, retains multiple simultaneous blockers, and waits for two
  consecutive clear samples before restoring.
- Authored both internal obstacles, both passage walls, both exit partitions, the
  low ceiling, Crawl overhead, and camera-facing perimeter walls as World plus
  CameraOccluder bodies. Floors and gameplay areas remain outside the query mask.
- Hid exposure-zone markers by default behind an explicit development flag without
  changing collision or deterministic visibility. Named render layers exclude the
  player, pistol, shot markers, and aim marker from the flashlight while world walls
  remain lit and shadow-casting.
- Reduced directional, powered, bright-zone, and flashlight shadow opacity and
  energy while increasing bounded ambient fill. The scene remains dark, but its
  shadowed geometry is no longer configured for full black output.

Files created or modified for this correction:

- `src/World3D/CameraOccluder3D.cs`
- `src/World3D/CameraOcclusionController3D.cs`
- `src/World3D/RenderLayers3D.cs`
- `src/World3D/Flashlight/PlayerFlashlightController3D.cs`
- `src/World3D/Perception/LightExposureZone3D.cs`
- `scenes/3d/Main3D.tscn`
- `scenes/3d/player/Player3D.tscn`
- `scenes/3d/levels/TestLevel3D.tscn`
- `scenes/3d/levels/LightExposureZone3D.tscn`
- `scenes/3d/levels/PowerControlledLight3D.tscn`
- `src/Tests/Suites/LightingOcclusion3DFeatureTests.cs`
- `src/Tests/Suites/Foundation3DFeatureTests.cs`
- `src/Tests/Suites/PlayerFoundation3DFeatureTests.cs`
- `src/Tests/Framework/FeatureTestSuiteCatalog.cs`
- `project.godot`
- `README.md`
- `docs/3d-migration.md`
- `docs/architecture.md`
- `docs/testing.md`
- `docs/3d-migration-validation.md`

Executed final checks on 2026-07-22:

- `/tmp/no-way-up-dotnet/dotnet restore LineZero.csproj --disable-parallel
  -p:RestoreIgnoreFailedSources=true`: passed; all projects were up to date.
- `/tmp/no-way-up-dotnet/dotnet build LineZero.csproj --no-restore
  -nodeReuse:false -p:UseSharedCompilation=false`: passed, 0 warnings, 0 errors.
- The Godot executable with `--headless --path .
  res://scenes/tests/FeatureTests.tscn -- --suite=lighting-occlusion-3d,
  player-foundation-3d,foundation-3d`: 3 suites, 33 tests, all passed.
- The same executable with `--headless --path .
  res://scenes/tests/FeatureTests.tscn`: 28 suites, 150 tests, 150 passed,
  0 failed. Logged throwing-subscriber/listener exceptions were intentional
  isolation fixtures and their cases passed.
- The same executable with `--headless --path . --import`: passed with exit code 0.
- The same executable with `--headless --path . --quit-after 180
  res://scenes/3d/Main3D.tscn`: passed with exit code 0 and no runtime warning or
  error output.
- The same executable with `--headless --path . --quit-after 180
  res://scenes/main/Main.tscn`: passed with exit code 0 and no runtime warning or
  error output.
- The focused suite moved the player through `(4.7, 0.05, 7.1)`,
  `(7.9, 0.05, 8.6)`, the Crawl-only passage, and the low-ceiling area, then
  confirmed that every bounded silhouette obstruction at those locations was in
  active fade state and represented in the HUD count.
- Hardware-rendered manual verification was unavailable: the runner exposed no
  display server, Xvfb executable, or `/dev/dri` device. The position checks above
  are automated physics/material checks, not a visual-play claim.
