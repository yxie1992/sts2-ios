# Isolated solver validation

This is an ordinary .NET 9 test executable, not the Godot game executable. It
tests the actual adapted solver against a newly constructed, deterministic
combat using copied game assemblies. It does not connect to the running game.

It links five reviewed files from the pinned upstream offline harness:
`GameBootstrap`, `OfflineCombat`, `MemorySaveStore`, `OfflineLocalization`, and
`MainLoopContext`. It deliberately excludes the upstream project, main program,
runtime initializer, generated-fixture tooling and automatic deployment targets.
The test-only Harmony patches bypass native Godot logging, resources, node static
constructors, localization, platform lookups and migration initialization. The
game's test mode is enabled and all game save reads/writes use an in-memory
`ISaveStore`. No Steam initialization, native game window, save synchronization,
mod copying or real save file access is performed.

Pass the **paired, rewritten** solver and game DLL outputs from the AOT bridge
tool. The compile-only publicized game reference is unsuitable as a runtime
substitute. The resolver rejects game inputs outside the prepared `.work` tree.

```sh
nice -n 15 "$HOME/.dotnet/dotnet" build tools/CombatSolverIosValidation -c Release \
  -p:SolverDll=/absolute/path/to/rewritten/CombatSolver.dll \
  -p:RuntimeGameDll=/absolute/path/to/rewritten/sts2.dll
DOTNET_PROCESSOR_COUNT=2 nice -n 15 "$HOME/.dotnet/dotnet" \
  tools/CombatSolverIosValidation/bin/Release/net9.0/OfflineSearchHarness.dll
```

Before execution, copy the paired rewritten inputs into a private validation
directory, then use `CombatSolverAot strip-analysis-initializer` to remove only
the audited Sentry module initializer from the test game's analysis copy. The
test host must never execute that telemetry initializer.

The assertions cover a real cloned-card upgrade without source mutation, complete
live-state/RNG equality after root capture and search, actual node expansion,
repeatability from the same root with a tiny fixed node budget, cancellation,
actual two-worker execution without live state/RNG changes, upstream preset
equality, generated JSON serialization and custom option propagation.
The list guard is checked for unchanged snapshots, same-count replacement,
add/remove on an initially empty list and mutation inside the capture-window
probe. Generated search keys, diagnostics, journal/replay evidence and settings
JSON are compared byte-for-byte with the upstream serialization options,
including Unicode, nulls, enum values and legacy settings migration fields.
The report and diagnostic log are written only under
`ios-export/.work/solver-inputs/validation/`. Serial searches use 40 nodes and
beam width 4; the parallel smoke test uses two workers, 80 nodes and beam width 8.
This is a correctness smoke test, not a performance benchmark.

Build with `-p:ValidationOracle=true` and copied original desktop solver/game
references to run the same serial scenario as an oracle. Output goes to
`validation-oracle/`. This flavor omits mobile-specific checks and uses the
desktop's corresponding notification and variable-materialization guards. The
mobile and oracle scenario root, action list, node counts, boundary and HP
prediction can then be compared; this is only one fixed starter-deck fixture.

Passing this harness does not establish iOS NativeAOT compatibility, device
memory safety, UI registration/layout, animation lifecycle behavior or complete
card/monster coverage. The test-only Godot bypasses and in-process Harmony must
never be included as an iOS runtime workaround.
