# Experimental CombatSolver iOS build

This opt-in build links CombatSolver 0.50.1 into the native port of the user's
0.111.0 beta game. It provides suggestions and adjustable search settings.
It is not a general mod loader. The normal `build-ios.sh` path stays separate.
The original feasibility study is in [COMBAT_SOLVER_IOS_STUDY.md](COMBAT_SOLVER_IOS_STUDY.md).

## Current validation

The standalone managed solver compiles. Static generation covers 603 card
records (99 strict and 303 general recipes), 283 concrete power types, and 394
private-member accesses through 155 bridges. Unknown behavior is rejected.
The runtime game retains its original member visibility.

An isolated ordinary .NET harness passed 17 checks, including a cloned card
upgrade, unchanged live state and RNG, deterministic search, cancellation,
actual two-worker expansion, upstream preset equality, generated JSON round-trip
and custom-option propagation.
For one fixed starter-deck combat, the adapted and installed desktop solvers
produced identical serialized actions, node counts, boundary and HP prediction.
That fixture is a smoke test, not broad prediction coverage or a speed benchmark.
The beta base game also compiled to an arm64 NativeAOT library independently.

**Integrated solver NativeAOT, signed packaging, device UI, memory behavior and
actual gameplay are still unverified. Do not treat this as a working device
release.** Validation reports and proprietary outputs stay in ignored `.work/`.

## Build without changing the running Mac game

Use a separate checkout/worktree and its own ignored `ios-export/config.sh`.
Follow the normal dependency instructions in `AGENTS.md`. This variant also
requires the exact beta game and installed Workshop versions pinned in
`src/CombatSolverIos/upstream.json`; an unexpected update fails validation.

```sh
bash ios-export/build-combat-solver.sh
```

The wrapper runs at low priority with one .NET processor by default. It copies
the user's game and Workshop inputs into `.work/solver-inputs`, verifies hashes,
and fetches only public mod source if the pinned source checkouts are absent.
Optional `STS2_COMBAT_SOLVER_SOURCE` and `STS2_RITSU_SOURCE` in local config select
existing clean pinned checkouts. `STS2_WORKSHOP_DIR` overrides the Workshop path.
It never imports upstream automatic-deployment targets, writes Steam files,
starts another game instance, or changes saves. Build work still consumes CPU,
memory and disk bandwidth.

The pipeline generates static clone/access bridges, card recipes and power
dispatch; compiles the isolated project; rewrites private accesses; applies the
37 mobile hooks and three solver hooks; then runs NativeAOT and Godot export.
It also handles the beta's renamed Sentry autoload and excludes its audited
Sentry module initializer. The DLL and asset pack must come from the same pin.
Generated recipes and proprietary game files are never committed.

## Settings

The touch overlay offers Search, Cancel and Options. The default Mobile preset
uses 5 seconds, 3,000 nodes, beam width 16 and one worker. The upstream Low,
Medium, High and Very high profiles remain selectable. Custom exposes seconds,
nodes and beam width; workers and potion policy are adjustable separately.
Settings persist in `user://combat_solver_mobile_options.json` and apply to the
next search. Desktop configuration is never read or written.

Search uses normal GC. Cooperative memory checks require 512 MiB available to
start and request cancellation below 256 MiB or after 384 MiB managed growth.
These are sampled safeguards, not a hard cap or a guarantee against termination.
No device-specific performance recommendation has been established.

See [runtime details](../src/CombatSolverIos.Runtime/README.md),
[static integration](../src/CombatSolverIos/README.md) and
[isolated validation](../tools/CombatSolverIosValidation/README.md).

## Packaging and eventual device testing

Use a separate Xcode DerivedData directory and set `STS2_BASE_APP` in the local
config to its exact `StS2.app`. This avoids accidentally selecting another
checkout's most recent build. `deploy-slim.sh` copies the base before replacing
its native library, retains upstream license notices inside the signed app,
and checks the bundle identifier. To prepare a signed app without installing:

```sh
bash ios-export/deploy-slim.sh --stage-only
```

After successful validation, normal `deploy-slim.sh` performs an overwrite
installation. Never uninstall. The matching beta PCK must also be transferred
with `push-pck.sh`; an old 0.107.1 pack cannot be reused for this build. Keep the
previous working build and content pack available while testing the beta.
Device validation must include search, cancellation, changing live state,
leaving combat, backgrounding, repeat requests and memory pressure.

CombatSolver and RitsuLib are MIT licensed. Preparation retains their licenses
and CombatSolver's additional third-party notices in local build outputs.
Game binaries and assets remain the user's own, local files.
