# Automated feature tests

Line Zero uses a small built-in Godot C# test harness. It does not depend on a
third-party plugin, a global service locator, or a separate copy of gameplay
logic. Tests exercise plain domain models directly and instantiate the actual
Godot scenes for physics, signals, collision, HUD, and composition-root checks.

## Requirements

- .NET 8 SDK;
- the Godot 4.7.1 .NET editor/runtime;
- a completed Godot import for the project.

Set `GODOT_BIN` when the executable is not available under one of the usual
`godot4-mono`, `godot-mono`, `godot4`, or `godot` names. `DOTNET_BIN` may be used
the same way for the SDK executable.

## Commands

List all suites:

```bash
./scripts/list-tests.sh
```

Build, perform a blocking resource import, and run every suite:

```bash
./scripts/test-all.sh
```

Build, import, and run one feature suite:

```bash
./scripts/test-feature.sh weapon-integration
```

Direct runner invocation after an existing successful build/import:

```bash
godot4-mono --headless --path . \
  res://scenes/tests/FeatureTests.tscn -- --suite=hazards
```

Multiple suites may be selected in a direct invocation:

```bash
godot4-mono --headless --path . \
  res://scenes/tests/FeatureTests.tscn \
  -- --suite=flashlight,objectives-power,hud
```


The scripts use Godot's dedicated `--import` command. Do not replace it with
`--editor --quit-after 1`: that command can terminate the editor while its file
scan/import thread is still active and can crash or leave a partial `.godot`
cache. `--import` waits for resource import to finish before exiting.

After an interrupted editor import, force one clean generated-cache rebuild with:

```bash
env CLEAN_GODOT_CACHE=1 ./scripts/test-all.sh
```

The cache cleanup is opt-in and occurs before `dotnet restore`/`dotnet build`, so
the newly built C# assembly remains available to the subsequent Godot import.

The runner exits with:

- `0` when every selected case passes;
- `1` when at least one case fails;
- `2` for runner/bootstrap errors or an unknown suite selection.

## Final summaries

The Godot runner always finishes with a compact result block:

```text
[TEST][FINAL_SUMMARY]
  result: PASS
  suites: 20
  tests: 90
  passed: 90
  failed: 0
  duration: 1.23s
  failed cases: none
```

The shell entry points then print a pipeline summary after the runner exits:

```text
[TEST][PIPELINE_SUMMARY]
  result: PASS
  selection: all
  build/import: PASS
  tests: PASS
  exit code: 0
```

The pipeline block is also printed when build/import or tests fail. The original
non-zero exit code is preserved for local scripts and CI.

## Suites

| Suite ID | Coverage |
|---|---|
| `core-events` | Safe subscriber isolation and typed event arguments |
| `health` | Damage, healing, death, and permanent completion immunity |
| `stamina` | Bounds, depletion, recovery, and invalid inputs |
| `inventory` | Stacking, deterministic removal, transfer, and notifications |
| `item-use` | Medkit eligibility, healing, and exact consumption |
| `firearm-state` | Magazine accounting, rejection, reload, and cancellation |
| `flashlight` | Charge, thresholds, depletion, near-full handling, and battery transaction |
| `objectives-power` | Ordered objectives, fuse transaction, circuit, and subscriber failures |
| `visibility` | Posture, ambient zones, flashlight multiplier, priorities, and death |
| `mutant-perception` | Stimulus priority, Chase/grace memory, damage response, deterministic same-frame noise, FOV timing, and dead targets |
| `noise-hearing` | Distance, sensitivity, multi-wall attenuation, and deduplication |
| `hazards` | Stable sensor overlap, entry damage, periodic catch-up, exit, and completion |
| `footsteps` | Distance cycles, low-FPS debt, event cap, and terminal reset |
| `weapon-integration` | Muzzle obstruction, first hit, ammo, tracer, and gunshot noise |
| `interactions` | Container first-open noise and failed interaction behavior |
| `emergency-exit` | Power gating, completed door opening, one-shot event, and early exit overlap |
| `prototype-flow` | Full fuse, power, emergency-door, exit, and terminal-input loop through `Main` |
| `movement-crawl` | Collision-profile invariants, blocked exit, and constant sensors |
| `hud` | Honest stamina/flashlight values and event-driven objective status |
| `scene-contracts` | Exported paths, TestLevel preservation, MetroLevel01 content/crawl/power contracts, inputs, and both main-scene smoke loads |

## Structure

```text
src/Tests/Framework/       test runner, assertions, case isolation
src/Tests/Fixtures/        minimal typed test collaborators
src/Tests/Suites/          one independently selectable suite per feature group
scenes/tests/              aggregate headless runner scene
scripts/                   build/import/run entry points
```

Every asynchronous test case receives a temporary scene root. The root is freed
in `finally`, including after a failed assertion, so one case cannot leave bodies,
areas, timers, listeners, or signals behind for the next case. Suites run
sequentially and failures are aggregated instead of aborting the remaining tests.

## Testing boundaries

Plain C# models are tested without scene dependencies. Godot integration tests use
the real project scenes and real physics server for behavior that depends on
`Area2D`, `CharacterBody2D`, ray queries, tweens, signals, or UI nodes. The final
`scene-contracts` suite loads both `Main.tscn` and `TestMain.tscn`, validates the MetroLevel01 greybox contracts, and requires each composition root to reach
`Main.IsInitialized` without startup exceptions.

The harness is deterministic and intentionally avoids screenshots, rendered-pixel
sampling, random timing, arbitrary sleeps for domain tests, and duplicated gameplay
implementations. It is a regression suite, not a substitute for the documented
manual feel/balance checks.

Scene contracts also verify that Metro landmark Labels use distinct world-space
anchors, gameplay disables technical mutant/debug labels, and the persistent HUD
fits the compact layout bounds without panel overlap.

## Deterministic integration fixtures

Integration suites explicitly control mutable presentation state instead of relying on
headless-engine defaults. Weapon tests freeze the player's aim pivot before placing
wall geometry, Crawl tests use the actual `crawl` toggle and wait for both deferred
and physics updates, and HUD expectations use the process culture's decimal separator.
This keeps the tests portable across headless display backends and operating-system
locales without weakening gameplay assertions.

## HUD binding tests

HUD controllers are intentionally bound once for their scene lifetime. Tests that compare several model states must use a fresh HUD instance for each independent binding instead of rebinding one controller to another model. This mirrors production composition and prevents false failures from the controller's duplicate-binding guard.

Every failed case is written to standard output as `[TEST][FAIL]` and also reported through Godot's error channel, so failure details remain visible even when only stdout is captured.


## Stage 14 regression additions

The existing suites now additionally cover mixed-mode footstep HUD classification,
objective-sensor-only completion, stationary movement-profile switching at the exit,
already-overlapping objective sensors, safe health/stamina/firearm subscriber
publication, transactional partial/full/no-reserve/canceled/reentrant reload paths,
ammunition conservation, transactional medkit success/full/dead/final-stack paths,
and subscriber failures during reload and healing notifications.
