# Isometric 3D migration

## Result

`res://scenes/3d/Main3D.tscn` is the configured main scene. It implements the
complete technical gameplay loop in top-down isometric 3D while preserving
`res://scenes/main/Main.tscn` and every legacy 2D adapter as regression references.
The migration adds no new weapon, item, enemy type, objective, level theme, audio,
save system, menu, NPC, or imported art.

The completed phases are:

1. audit and vector-based XZ movement fixes;
2. fixed orthographic camera, movement, postures, aim, and occlusion;
3. interaction, pickups, containers, inventory, health, hazards, and terminal input;
4. flashlight, deterministic visibility, distance noise, and event-driven HUDs;
5. muzzle-authoritative firearm combat and transactional damage;
6. NavigationAgent3D mutant patrol, perception, hearing, chase, search, melee, and death;
7. fuse, power, powered light, completed emergency door, objective sensor, and completion;
8. full primitive technical-level composition and final regression validation.

The phase-by-phase command record is in
[`3d-migration-validation.md`](3d-migration-validation.md).

## Run both paths

Run the configured 3D game:

```bash
godot --path .
```

Run the preserved 2D reference:

```bash
godot --path . res://scenes/main/Main.tscn
```

The compact legacy technical level remains available through
`res://scenes/main/TestMain.tscn`.

## Final scene hierarchy

```text
Main3D (composition root)
├── TestLevel3D
│   ├── WorldEnvironment / DirectionalLight3D
│   ├── NavigationRegion3D (authored NavigationMesh)
│   ├── Geometry
│   │   ├── floor, perimeter and sight/shot/noise blockers
│   │   ├── Crouch-height ceiling and Crawl-only passage
│   │   └── camera-occluder demonstrations
│   └── Gameplay
│       ├── TunnelMutant3D
│       ├── pickups, container, hazard, dark/bright zones
│       ├── PowerController3D / FuseBox3D / powered light
│       └── EmergencyDoor3D / ObjectiveExitZone3D
├── Player3D (CharacterBody3D)
│   ├── Walk, Crouch, and Crawl movement colliders
│   ├── interaction, visibility, hazard, and objective sensors
│   ├── inventory and health components
│   ├── aim, footsteps, and weapon gameplay adapters
│   └── VisualPivot3D
│       └── PlayerVisual3D (replaceable presentation wrapper)
│           ├── ModelYawRoot3D / ModelAlignmentRoot3D
│           │   ├── ImportedModelRoot3D
│           │   └── DevelopmentFallbackRoot3D
│           ├── RightHandSocket / WeaponSocket
│           │   ├── WeaponOrigin / MuzzleSocket
│           │   └── FlashlightSocket / flashlight adapter
│           ├── CameraTarget
│           └── AnimationPlayer / AnimationTree
├── NoiseSystem3D
├── TopDownCamera3D / CameraOcclusionController3D
└── CanvasLayer UI
    ├── health, stamina, weapon, noise, flashlight, visibility, objective
    └── interaction, inventory, transfer, and completion panels
```

`Main3D` resolves and binds every dependency once. No process, physics, interaction,
or mutation method searches the scene tree. UI receives completed model state and
does not own gameplay rules.

## Shared models and separate adapters

The 3D path reuses the plain `InventoryModel`, `HealthModel`, `StaminaModel`,
`FirearmState`, `FlashlightModel`, item definitions/use services,
`ObjectiveProgressModel`, `PowerCircuitModel`, reload, item-use, battery, fuse, and
firearm-discharge transactions. It also reuses movement/noise/visibility vocabulary,
mutant configuration, safe publication, immutable results, and the Canvas UI.

Spatial behavior stays deliberately separate:

| Responsibility | Legacy adapter | 3D adapter |
|---|---|---|
| Movement and posture | `PlayerController2D` | `PlayerController3D` |
| Interaction | `PlayerInteractor2D` | `PlayerInteractor3D` |
| Hazards / light / objective | dedicated `Area2D` sensors | dedicated `Area3D` sensors |
| Pickups and containers | `WorldItemPickup2D`, `LootContainer2D` | `WorldItemPickup3D`, `LootContainer3D` |
| Firearm | `PlayerWeaponController2D` | `PlayerWeaponController3D` |
| Noise | `NoiseSystem2D` and `Vector2` occurrences | `NoiseSystem3D` and `Vector3` occurrences |
| Mutant | `NavigationAgent2D` controller | `NavigationAgent3D` controller |
| Power and exit | 2D fuse/door/zone/light adapters | 3D fuse/door/zone/light adapters |

No universal Vector2/Vector3 layer or mixed 2D/3D controller was introduced.

## Camera, movement, and occlusion

The camera is orthographic with fixed 45-degree yaw and 55-degree downward pitch.
It follows smoothly without player rotation or mouse capture. Movement flattens the
camera forward/right basis onto XZ, normalizes input, and accelerates the horizontal
velocity as one vector, so cardinal and diagonal acceleration are equal.

Directional shadows use `SHADOW_ORTHOGONAL` explicitly. The former default
four-split PSSM placed split 3 at 50% of the old 55-unit distance, or 27.5 camera
units. The visible ground depth spans approximately 17.46–32.86 camera units, so that
unblended split crossed the gameplay viewport and created a camera-relative cutoff.
The orthogonal map removes cascade transitions. Its 44-unit maximum distance covers
the 32.86-unit far visible receiver depth, approximately 3.25 units of maximum
off-screen caster reach from the 7.25-unit-high technical geometry, and a four-unit
safety margin without unnecessarily diluting resolution. Shadow fading begins only
at the map limit, the pancake is bounded to 10 units, and the camera far clip is 48
units. The flashlight remains at 18 units and both important Omni lights remain at 8
units; all three explicitly disable camera-distance fading. The positional shadow
atlas is unchanged because the repository contains only these three bounded
shadow-casting positional lights and no source evidence identified atlas eviction as
the screen-fixed boundary.

The aim pivot rotates only around Y toward a validated cursor projection. The last
valid direction survives a near-zero projection. The model-facing root converges on
that authored aim yaw with frame-rate-independent exponential smoothing while the
CharacterBody3D and fixed camera remain unrotated. Walk, Crouch, and Crawl shapes are
distinct authored resources. Leaving Crawl for Crouch and leaving Crouch for Walk
query the disabled target profile at its final transform, exclude the player RID,
and switch exactly one collider only after clearance succeeds. Shift and Space can
request a direct return to Walk from Crouch or Crawl, but the request is rejected when
the standing profile does not fit. Successful posture events select predefined
presentation profiles; rejected clearance requests publish no posture change and
cannot advance the visual state. The primitive fallback changes only its isolated
figure root, never an imported skeleton or physical collider. Authored sockets lower
with the accepted profile, while their local transforms and the aim pivot remain
stable. Camera rays affect only `CameraOccluder3D` visuals.
The GL Compatibility path never uses `GeometryInstance3D.Transparency`: every
occluder prepares local `StandardMaterial3D` alpha overrides during initialization
and keeps collision enabled. When fading begins, the transparent camera visual stops
owning the shadow and a generated `ShadowsOnly` mesh with the exact original mesh and
materials takes over atomically. Restore disables the proxy and reinstates the exact
original material references and shadow mode, preserving shadows without duplicate
casters or a missing-shadow frame. Five cached,
bounded rays sample lower, centre, upper, and horizontal silhouette points. Queries
use only the named camera-occluder physics layer, exclude the player, retain a
two-clear-query release hysteresis, and support multiple concurrent blockers.

World, player, and development visuals use named render layers. The flashlight cull
mask includes world geometry but excludes player and aim presentation, preventing
self-shadow while retaining wall shadows. Exposure-zone markers are hidden by
default and can be revealed only through an explicit development flag; this does not
change their collision or deterministic visibility multiplier.

The four interaction, hazard, visibility, and objective areas remain constant in
size, transform, layer, mask, and resource identity across all posture changes.
`DebugHud3D` subscribes symmetrically to completed player/stamina/input/clearance and
occlusion changes. `PlayerVisual3D` publishes a bounded, changed-only debug snapshot
at five hertz. The HUD reports movement mode, exact stamina, posture, clearance,
input/terminal state, active faded-wall count, animation state, local blend, action,
visual source, yaw, clip availability, and socket validation without per-frame text
formatting or gameplay polling.

## Player model and animation replacement contract

The repository currently contains no `.glb`, `.gltf`, `.fbx`, `.blend`, skeletal
player, or animation set. `PlayerVisual3D` consequently activates its clearly named
development humanoid and pistol primitives and leaves the empty imported root and
`AnimationTree` inactive. Missing clips freeze or hold presentation safely: missing
one-shots never map to an unrelated locomotion loop, and missing locomotion may use a
configured Idle only. Gameplay remains fully runnable when optional art is absent.

The presentation state machine is a plain C# type. Its strict priority is:

1. Death (latched).
2. Terminal or currently disabled presentation.
3. Hit Reaction.
4. Reload.
5. Fire.
6. Walk, Sprint, Crouch Walk, or Crawl Move.
7. The posture-appropriate Idle state.

Reload starts only for `ReloadStatus.Started` and clears only after Completed or
Canceled. Fire observes `ShotResolved`, not attempted or rejected input. Damage
observes one completed health change; lethal damage immediately latches Death and
the later death notification is idempotent. Neither animation tracks nor visual
events mutate gameplay.

Directional locomotion samples `CharacterBody3D.Velocity` during idle processing,
after physics has produced the latest movement result. Horizontal velocity is dotted
against aim-right and aim-forward, divided by the authoritative movement-mode speed,
and length-limited for a stable two-dimensional blend. Separate 0.05 m/s stop and
0.12 m/s start thresholds provide hysteresis. Optional AnimationTree blend and speed
parameter paths are cached once; there are no process-time path searches, string
construction, resource creation, or collection allocation.

To replace the fallback safely:

1. Import a licensed `.glb` authored at 1 metre per Godot unit, Y-up, facing `-Z`,
   with its origin on the ground. Preserve its `Skeleton3D` and required clips;
   disable imported cameras and lights.
2. Instance or inherit the generated import beneath `ImportedModelRoot3D`. Never edit
   the generated import directly. Normalize scale and orientation exactly once at
   `ModelAlignmentRoot3D`.
3. Create a `PlayerAnimationSet3D` configuration resource that records each exact
   imported clip name. Author the stable Idle/Walk/Sprint/Crouch/Crawl/Fire/Reload/
   HitReaction/Death states in the wrapper `AnimationTree` and assign the resource.
4. Adjust `RightHandSocket`, `WeaponSocket`, `MuzzleSocket`, and `FlashlightSocket`,
   or replace their presentation attachment with validated bone attachments. Keep
   the gameplay-safe `WeaponOrigin` to muzzle segment and wall query intact.
5. Confirm root motion does not write to `CharacterBody3D.GlobalPosition`, all player
   meshes use the player visual render layer, and the flashlight still illuminates
   only the World layer.

Final art still required: one licensed player `.glb` with a compatible skeleton; the
eleven mapped clips (or a documented subset with safe gaps); production pistol and
flashlight meshes; and final socket/bone transforms. None is claimed by this stage.

Interaction rays exclude the player's RID and the selected interactable's own
declared physical body. This keeps solid containers, the fuse box, and the emergency
door usable without allowing an unrelated wall to be bypassed.

## Collision layers

All numeric values are centralized in `CollisionLayers3D` and named in
`project.godot`.

| Layer | Value | Purpose |
|---|---:|---|
| World | 1 | movement, LOS, muzzle, interaction, and acoustic blockers |
| Player movement | 2 | physical player body |
| Mutant movement | 4 | physical mutant body |
| Firearm target | 8 | damageable shot target |
| Interaction area | 16 | interactable detection |
| Aim surface | 32 | reserved explicit aim surfaces |
| Visibility zone | 64 | deterministic exposure zones |
| Hazard zone | 128 | damage zones |
| Objective zone | 256 | exit-zone identity |
| Player interaction sensor | 512 | fixed interaction query area |
| Player visibility sensor | 1024 | fixed exposure sensor |
| Player hazard sensor | 2048 | fixed damage sensor |
| Player objective sensor | 4096 | fixed exit sensor |
| Camera occluder | 8192 | camera-to-player visual blockers |

Movement colliders never activate visibility, hazards, objectives, or interaction.
World queries use explicit masks and exclude their owner where applicable.

## Transactions, events, timing, and terminal state

Multi-model operations validate and prepare every result before mutation, commit all
models without public notifications, then safely publish completed state. This is
used for transfers, reload, medkits, battery replacement, fuse installation, and
firearm/target damage. Rejected or reentrant calls mutate nothing.

`SafeEventPublisher` invokes subscribers independently and logs event/subscriber
context. A presentation exception cannot block a later critical handler, does not
roll back state, and never retries the mutation.

Hazard ticks, perception, chase refresh, interaction selection, footsteps, and
other periodic work preserve elapsed remainder. Gameplay-critical debt is retained
behind bounded catch-up limits; presentation-only selection debt is explicitly
clamped. Death and completion latch one-way terminal state and stop all player
input, hazard tracking, pending combat, footsteps, mutant navigation/perception, and
modal UI restoration.

## Technical level coverage

The primitive level contains an open movement area, standing and Crawl-only
passages, dark and bright exposure zones, a damage hazard, battery/medkit/ammunition/
fuse pickups, an equipped pistol, a loot container, world blockers, a power room,
powered exit lighting, an emergency door, an objective zone, an authored navigation
region, a mutant route, and camera occluders. It is a functional parity level, not
the final metro art pass.

## Renderer and performance result

The project remains on GL Compatibility. Mobile successfully loaded both the 3D and
legacy 2D scenes through the headless command-line override, but the environment had
no X11/Wayland display or exposed GPU, so Mobile Vulkan could not be measured on
hardware. Permanently switching without that comparison would violate the renderer
gate.

A clean 1,200-frame dummy-headless smoke printed five samples of 144–145 FPS
(6.89–6.94 ms per frame). These numbers validate bounded runtime behavior only;
they are not RTX 4060 rendering results and provide no hardware minimum-FPS claim.
The Godot GPU profiler and manual playthrough remain target-machine checks.

## Validation boundary

The current automated catalog contains 28 suites and 154 cases. It covers the full 3D
objective loop, movement/posture constraints, scene contracts, transactions,
throwing subscribers, low-FPS debt, muzzle walls, navigation/AI priority, sensors,
death/completion, repeated input, and all legacy regressions. Throwing-subscriber
fixtures intentionally write contextual error lines while still passing the
delivery assertions.

Headless import, default-main load, explicit 3D load, legacy 2D load, Mobile override
loads, and the runtime smoke all complete successfully. A hardware-rendered launch,
GPU profile, and human feel/readability playthrough could not be performed in the
display-less workspace and must be run on the target machine.
