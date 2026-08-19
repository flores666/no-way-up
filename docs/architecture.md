# No Way Up architecture

This document describes the current runtime architecture. Historical implementation
notes are intentionally omitted so the document stays aligned with the codebase.

## Principles

- Keep gameplay state and rules in plain C# models when they do not need Godot APIs.
- Treat Godot nodes as adapters for input, physics, rendering, audio/noise, and UI.
- Use `Main` as the explicit composition root for scene-owned dependencies.
- Prefer typed events and explicit bindings over scene-tree searches from domain code.
- Validate authored resources and required nodes during initialization.
- Keep mutations transactional where inventory, power, ammunition, or item use spans
  more than one model.
- Avoid per-frame UI polling when the source model already exposes change events.

## High-level structure

```text
src/Core/               composition and shared infrastructure
src/Gameplay/           engine-light gameplay/domain models
src/World2D/            Godot 2D adapters and world controllers
src/UI/                 UI presentation controllers
src/Data/               typed Godot Resource definitions
data/                   authored resource instances
scenes/                 scene composition
src/Tests/              feature-test harness and suites
```

The codebase still uses the `LineZero` root namespace for compatibility with the
existing project and serialized Godot resources.

## Composition root

`src/Core/Main.cs` composes one active player, one active playable level, the shared
`NoiseSystem2D`, and the UI. It resolves required unique nodes once in `_Ready`,
binds dependencies, subscribes to cross-component events, and removes those
subscriptions in `_ExitTree`.

`Main` owns orchestration that crosses subsystem boundaries:

- inventory item-use requests;
- flashlight battery replacement;
- loot-transfer panel lifetime;
- fuse/power/objective progression;
- emergency-exit completion;
- terminal player input state after death/completion;
- gameplay versus UI mouse/crosshair ownership.

Gameplay models do not locate these UI or world nodes themselves.

## Player movement

`PlayerController2D` is a `CharacterBody2D` adapter with one movement collision
shape. The current movement model contains only:

- `MovementMode.Walk`;
- `MovementMode.Sprint`.

`PlayerMovementSettings` contains authored scalar tuning. The default resource uses
walk speed `198`, sprint speed `272.8`, acceleration/deceleration, stamina drain,
recovery, recovery delay, and the minimum stamina required to start a new sprint.

Movement input is sampled once per physics tick. `MoveAndSlide` determines actual
movement; sprint stamina is drained only when a sprint request produces meaningful
physical displacement. Reaching empty stamina ends the sprint session and requires a
real Shift release before another sprint may start.

Crouch/crawl input actions and alternate movement colliders are intentionally absent.

## Camera and pixel-art motion

`PlayerCameraZoom2D` follows physics samples but applies the camera transform during
render processing using Godot's physics interpolation fraction. This keeps the
camera and rendered player on the same temporal sample.

The project uses final 2D vertex pixel snapping rather than independent world-space
transform rounding. Character and weapon textures use nearest filtering at integer
art scale. Mouse wheel changes camera zoom within authored limits.

## Player presentation and aiming

`PlayerMiniDayzPresentation2D` owns only visual state:

- one six-frame body run sheet;
- frame index `0` as idle;
- left-facing body mirroring;
- weapon transform mirroring;
- weapon Z ordering relative to the body.

`PlayerController2D` rotates `AimPivot` from viewport-space mouse direction. The
character body and movement collision remain axis-aligned.

The weapon sprite is independent from the body sprite. `MuzzlePoint` is a child of
the weapon sprite so it inherits weapon rotation, scale, and left-side mirroring.

## Firearms

Static firearm tuning lives in `FirearmDefinition`; mutable ammunition/reload state
lives in `FirearmState`. `FirearmReloadService` performs reload transactions.

`PlayerWeaponController2D` is the Godot adapter for:

- fire/reload input;
- semi-automatic versus automatic fire mode;
- aimed versus hip-fire spread;
- muzzle-clearance validation;
- hitscan physics;
- delegation to the weapon FX presentation layer;
- damage application;
- gunshot noise emission.

The current AK resource uses automatic fire and detachable magazines. Each spare
magazine preserves its own remaining round count. Magazine objects are not yet
represented as separate general-inventory slots.

A shot computes its final spread direction once. Hitscan and the visual projectile
use the same validated start/end points so presentation cannot disagree with the
physical hit result. Trigger presses are consumed on the physics tick, after player
aim/movement sampling, so the first shot of a burst uses the same transform timeline
as subsequent automatic shots.

`WeaponFxController2D` owns transient firing presentation only. Its behavior is
configured by a `WeaponFxProfile2D` resource and currently covers the visual bullet,
procedural muzzle-flash variants, a short-lived dynamic muzzle light, heat-scaled
muzzle smoke, and obstacle impact pixels. Bullet, smoke, and impact nodes are
preallocated in fixed pools during `_Ready`; firing reuses those nodes instead of
allocating scene objects per shot. The top-level transient FX branch disables physics
interpolation because these render-time effects are explicitly positioned in world
space and pooled nodes can jump between unrelated shot positions. Reactivated pooled
sprites also reset their interpolation history before becoming visible.

The controller can stop and clear all transient FX without mutating firearm state,
ammunition, damage, spread, or hitscan results. Casing ejection is intentionally not
part of the weapon FX system.

## Crosshair and mouse ownership

`AimCrosshairController` is bound to the active weapon controller. During gameplay
the operating-system cursor is hidden and the custom crosshair is visible. Its gap
is calculated from the weapon's current aimed/hip-fire spread and the screen-space
aim distance.

`Main` supplies one interaction state to the controller:

- world gameplay active -> custom crosshair;
- inventory/loot UI active -> system cursor;
- terminal state -> no gameplay crosshair.

The crosshair does not own combat rules or modify weapon spread.

## Flashlight and lighting

`PlayerFlashlightController2D` owns the `FlashlightModel` adapter and flashlight
input. The controller is mounted below the current `MuzzlePoint`, so the light
follows weapon rotation and weapon length.

The flashlight uses a generated 2D light texture and Godot `PointLight2D` shadows.
Walls and doors use `CollisionLightOccluderGenerator2D` to derive occlusion geometry
from their collision shapes. Rectangle obstacles expose their far silhouette edges
to the light so the visible wall surface can receive light while shadowing starts
behind the wall.

Lighting presentation and stealth visibility are intentionally separate. Enemy
perception does not sample rendered pixels or light textures.

## Health and stamina

`HealthModel` and `StaminaModel` are plain mutable gameplay models with validated
bounds and typed change results. `HealthComponent` adapts authored Godot scenes to a
`HealthModel` instance.

Death is terminal for the current player run. `Main` and the player adapters disable
world/combat input and close modal UI without changing the underlying model ownership.

## Inventory and item use

`InventoryModel` owns fixed-capacity slots, stacking, removal, and deterministic
change notifications. `InventoryComponent` exposes one model instance to a scene.

`ItemUseService` performs item-use transactions through typed effect definitions.
Containers implement `IInventoryContainer`; player actors implement
`IInventoryOwner`. `LootTransferPanelController` receives both models explicitly and
does not search the world for inventories.

## Noise and perception

`NoiseSystem2D` is the scene-owned acoustic system. Emitters publish typed noise
occurrences with position, kind, and intensity. World collision can attenuate a
noise path before listeners receive it.

`PlayerFootstepNoiseEmitter2D` accumulates traveled physical distance rather than
using a timer. Walk and Sprint have separate step distance/intensity tuning and the
emitter bounds deferred event debt after severe stalls.

`PlayerVisibilityController2D` implements the dimension-light
`IVisibilityTarget` contract. Its current multiplier combines:

- movement mode (`Walk`/`Sprint`);
- authored ambient exposure zones;
- flashlight on/off state.

Mutant perception consumes this scalar while sight rays, FOV, memory, hearing,
attack range, and navigation remain separate concerns.

## Hazards and constant sensors

Light exposure, hazard detection, and objective exit detection use dedicated
player-root sensors. They are independent from the movement collider and do not
resize or rotate with weapon aim.

Hazards apply entry/periodic damage through health abstractions. Objective exit logic
publishes typed completion events rather than mutating UI directly.

## Power and objectives

`PowerCircuitModel`, `FuseInstallationService`, and `ObjectiveProgressModel` are
separate gameplay concerns. The main composition root binds level fuse boxes,
powered lights, the emergency door, and the exit zone to those models.

The objective sequence remains ordered. Completion disables further damage and
world/combat input through the normal terminal-state path.

## UI

Production status UI is consolidated in `CompactPlayerStatusHudController`. It
subscribes directly to:

- `HealthModel`;
- `StaminaModel`;
- `InventoryModel`;
- `FirearmState`;
- `FlashlightModel`.

It displays only HP, stamina, ammunition/magazines, and flashlight state. Noise,
visibility, and movement-mode diagnostics are not instantiated in gameplay scenes.

Inventory, loot transfer, interaction prompt/message, objective, completion, and
debug UI remain separate because they have different lifecycles and responsibilities.
The debug HUD is disabled in `Main.tscn` and enabled in `TestMain.tscn`.

## Resource validation

Authored resources validate finite ranges and invariants when loaded/initialized.
Scene adapters fail early when required nodes, masks, resources, or explicit
relationships are missing. This is preferred over silently continuing with partial
runtime state.

## Testing

The project uses a built-in Godot C# feature-test harness. Plain gameplay models are
tested directly; physics/UI/scene behavior instantiates real project scenes. The
scene-contract suite also smoke-loads both composition roots.

See `docs/testing.md` for commands and suite coverage.
