# No Way Up

No Way Up is a top-down 2D survival prototype built with Godot 4 and C#/.NET 8.
The assembly and namespaces still use the historical `LineZero` root name.

The project favors explicit composition, plain gameplay models, event-driven UI,
and small Godot adapters. Gameplay rules should stay independent from scene-tree
lookups where practical.

## Current gameplay

- top-down Walk/Sprint movement with stamina;
- mouse-facing character and weapon presentation;
- AK-style automatic fire with detachable 30-round magazines;
- separate aimed and hip-fire spread;
- spread-aware crosshair and hidden system cursor during gameplay;
- flashlight mounted to the weapon with dynamic 2D wall occlusion;
- health, inventory, item use, loot transfer, and battery replacement;
- noise propagation and mutant perception;
- hazards, fuse/power progression, emergency exit, death, and completion states;
- camera zoom on the mouse wheel;
- compact production HUD for HP, stamina, ammunition, and flashlight state.

`Main.tscn` loads the metro gameplay level. `TestMain.tscn` keeps the compact
technical level and enables the debug HUD for regression work.

## Controls

| Action | Input |
| --- | --- |
| Move | `W`, `A`, `S`, `D` or arrow keys |
| Sprint | Hold `Left Shift` while moving |
| Aim | Mouse |
| Focus aim | Hold right mouse button |
| Fire | Left mouse button |
| Reload / change magazine | `R` |
| Toggle flashlight | `F` |
| Replace flashlight battery | `B` |
| Interact | `E` |
| Inventory | `Tab` |
| Camera zoom | Mouse wheel |

Crouch and crawl are intentionally not part of the current movement model.

## Character presentation

The runtime player art is intentionally small:

```text
assets/player/character/
  character.png
  weapon_ak.png
  source/rotations.aseprite
```

`character.png` contains the six-frame authored right-facing run cycle. The same
sheet is reused for all movement directions; the body is mirrored when facing left.
The first frame is the idle frame. The weapon is a separate sprite rotated by the
engine, with its Z order changed to preserve the right-handed presentation.

## Lighting

The flashlight is attached below the current weapon muzzle and uses
`flashlight_natural.png` as its light texture. Walls and doors generate 2D light
occluders from collision geometry. Rectangle obstacles use their far silhouette
edges so their visible surface can receive light while the shadow starts behind the
wall.

Lighting is presentation-only. Gameplay visibility is still derived from explicit
visibility-zone and flashlight-state models rather than rendered pixels.

## Project structure

```text
assets/                 runtime art and generated textures
data/                   authored Resource data
scenes/                 Godot scenes
src/Gameplay/           engine-light domain/gameplay models
src/World2D/            2D adapters and world controllers
src/UI/                 UI presentation controllers
src/Core/               composition root and shared infrastructure
src/Tests/              feature-test harness and suites
docs/                   architecture and testing notes
scripts/                local build/test helpers
```

The main scene is the composition root. It resolves the player, active level,
world systems, and UI once, then performs explicit bindings. Production status UI
is consolidated in `CompactPlayerStatusHudController`; legacy diagnostic panels are
not instantiated by gameplay scenes.

## Requirements

- Godot 4.7 .NET build;
- .NET 8 SDK.

Use the Godot .NET editor/runtime rather than the non-.NET build.

## Build

```bash
dotnet restore LineZero.csproj
dotnet build LineZero.csproj
```

Or build from the Godot editor.

## Run

Open `project.godot` in the Godot .NET editor and press `F5`, or run:

```bash
godot --path .
```

The executable may be named `godot4-mono`, `godot-mono`, `godot4`, or `godot`
depending on the local installation.

## Tests

The project includes a headless C# feature-test harness. See
[`docs/testing.md`](docs/testing.md) for details.

Run everything:

```bash
./scripts/test-all.sh
```

Run one suite:

```bash
./scripts/test-feature.sh weapon-integration
```

List available suites:

```bash
./scripts/list-tests.sh
```

## Current design constraints

- gameplay movement has only `Walk` and `Sprint`;
- player movement uses one collision profile;
- the AK's spare magazines keep their own remaining round counts;
- magazine objects are not yet separate general-inventory slots;
- the current flashlight uses Godot 2D lighting rather than a low-resolution
  pixel-lighting render pass;
- environment art is still largely greybox/prototype content.

When adding systems, prefer the smallest architecture that preserves explicit
ownership, deterministic state transitions, testability, and existing gameplay
behavior.
