# CombatSolver NativeAOT preparation

This tool operates on copied, locally owned game binaries. It never deploys,
starts the game, installs Harmony patches, initializes the mod entry point, or
accesses saves. The standalone solver project must be used; the upstream project
defaults to copying its build output into the desktop game's mods directory.

Build the tool with `.NET 9`:

```sh
dotnet build tools/CombatSolverAot -c Release
```

The commands below are arguments to the resulting `CombatSolverAot.dll`.

1. `bridge ORIGINAL_GAME COMPILE_REFERENCE REPORT` injects explicit clone bridges
   and writes a publicized **compile reference**. The sibling `runtime/sts2.dll`
   contains the same bridges but preserves every original member's visibility.
   Both copies neutralize the exact audited `SentryAutoInit.Init(); ret` module
   initializer, matching the iOS port's disabled telemetry behavior. Other module
   initializer shapes fail the build and require review.
   Do not deploy or AOT-compile the publicized compile reference.
2. `recipes DESKTOP_SOLVER ORIGINAL_GAME_DIR RITSU_DIR OUTPUT_CS REPORT` runs only
   the pinned desktop solver's IL inference routines in a separate host assembly
   context. Inputs must match CombatSolver 0.50.1 and Ritsu's 0.111.0 compatibility
   branch. It writes static card recipes and an inventory containing both
   successful and rejected analyses. Unknown cards remain unsupported. The
   output checks the original game SHA-256 injected by the bridge step.
3. Compile the standalone mobile solver project against the compile reference.
4. `rewrite SOLVER RUNTIME_GAME OUTPUT_SOLVER OUTPUT_GAME REPORT ORIGINAL_GAME_DIR`
   rewrites direct private accesses in the compiled solver to newly injected
   public access bridges. Use **both outputs together** for weaving and AOT.
   Original game fields/methods retain their visibility, preserving existing
   reflection behavior. A structural comparison verifies that their attributes
   and method bodies did not change. Unsupported access shapes stop the build.

The clone bridge preserves the upstream sequence: shallow clone, set mutable,
detach card observer fields, virtual `DeepCloneFields`, virtual `AfterCloned`.
The override source replaces `Reflection.Emit` with these bridges; other helper
behavior is retained from the pinned upstream MIT-licensed source.

The IL oracle never invokes card effects, model constructors, `ModelDb`, or
Ritsu startup. Its analysis-only game copy removes the exact audited
`SentryAutoInit.Init(); ret` module initializer. It similarly omits Harmony's
audited module version logger and rejects other dependency module initializers.
These analysis copies are never deployment inputs. The runtime bridge step also
disables the game's Sentry initializer intentionally; it leaves other original
method bodies intact. For isolated host tests,
`strip-analysis-initializer INPUT OUTPUT ORIGINAL_GAME_DIR` exposes the same
Sentry-only transformation without changing any other game method.

Reports and all generated game binaries belong under ignored `.work/` paths.
Successful generation establishes static compatibility, not device correctness;
the paired rewritten binaries still require the offline isolation checks,
NativeAOT compilation, and actual device validation.

The first pinned-beta generation inspected 603 concrete card types (99 strict
recipes and 303 general recipes); the retained general inference has exactly the
desktop solver's incomplete-risk semantics. The first complete mobile assembly
needed 394 rewritten accesses through 155 access bridges. The separate offline
validation harness passed nine checks, including clone/upgrade isolation, live
state and RNG preservation, 40 expanded search nodes, two deterministic results,
and cancellation. These host checks do not substitute for an iOS device test.
