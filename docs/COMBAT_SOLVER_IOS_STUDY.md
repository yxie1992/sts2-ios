# CombatSolver on iOS / iPadOS: feasibility study

Studied 2026-10-26 against local port commit `8832cbf`. This is a source and
static-assembly study, not a working solver port or a device benchmark.
Implementation progress after this study is tracked in [COMBAT_SOLVER_IOS.md](COMBAT_SOLVER_IOS.md).

## Verified versions and isolation

The user's running Steam installation already uses `public-beta`, build
`24724944`, game version **0.111.0**. Its startup log confirms **CombatSolver
0.50.1** and **RitsuLib 0.6.10**, using Ritsu's **0.111.0 compatibility variant**.
The previously validated iPhone build used **0.107.1**.

Sources were cloned into ignored `ios-export/.work/combat-solver-study/`:

| Source | Revision inspected |
| --- | --- |
| [CombatSolver](https://github.com/Torch1230/CombatSolver) | `5c773caaec742bc55588bc9ee21c361326a732f0` (0.50.1) |
| [STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) | `772f280964f2efc7c7e2f29fc59844e68616dd57` (0.6.10) |

Matching version labels do not establish binary/source reproducibility.
Workshop items are `3790899961` and `3747602295`, respectively.
The solver manifest requires game >= 0.111.0 and RitsuLib >= 0.6.0.

Only source, manifests, existing logs and copied assemblies were inspected.
The game was not attached to, restarted, reconfigured or launched a second time.
Steam content, Workshop files and saves were not written. The original process
remained running at the final process check. No full compilation, search stress
test, phone deployment or save synchronization was run.

Future isolated builds must explicitly disable upstream automatic deployment:
`CopyModOnBuild=false` for CombatSolver, `RitsuLibCopyToGame=false` for RitsuLib.
Both upstream projects default to copying build output into game directories.
Use copied references and isolated outputs; do not run their normal automation
against the live installation. User instructions to preserve the active game
take precedence over upstream agent instructions to build/deploy automatically.

## Beta compatibility probe

186 managed DLL files (86,563,968 bytes) were copied into the ignored study's
`beta-refs/`. A hash manifest is retained as `beta-refs-manifest.json`.
The copied `sts2.dll` is 9,756,160 bytes, SHA-256
`9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4`.

REA static inspection identified .NET 9 CIL, arm64, module MVID
`57785517-0b16-42b9-8b36-bad6fb28384b`, with complete metadata coverage and no
reported parsing issues. Retained parent evidence:
`ev_2a5b9753aa206596f15fb11b61de6839a096c37208df70117f282bb41cf9475b`.
This did not execute game code. Assembly version `0.1.0.0` is not the public game
version; the latter was obtained from the installed game's log.

The existing Weaver was run at low priority on that copy with the existing
mobile hook DLL and manifest. **All 37 hooks were woven successfully**; log:
`ios-export/.work/combat-solver-study/beta-weave-probe.log`.
Output is an isolated probe DLL, not a deployment artifact. This checks hook
lookup and current Weaver transformations only. It does not validate all hook
member references, IL type compatibility, NativeAOT, updated assets, gameplay or
save compatibility. A beta base rebuild and device test are still required.

## Why the desktop DLLs cannot be dropped into this build

The port uses NativeAOT and statically woven hooks. Microsoft documents no
dynamic assembly loading or Reflection.Emit, while expression trees can use an
interpreter; reflection is not categorically unavailable. See
[NativeAOT limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/#limitations-of-native-aot-deployment).

Paths below refer to the pinned upstream source trees unless qualified.

| Area | Source evidence | Required adaptation |
| --- | --- | --- |
| Simulation cloning | CombatSolver `src/Engine/Common/PredictionUtils.cs:19-153` creates DynamicMethod delegates used by CloneModelForSimulation | Generate static bridges into the user's game DLL, preserving clone stages and event detachment |
| Card behavior inference | `CardOnPlayInferrer.cs:28,100`; `MethodMirrorRegistry.cs:322-326` | Generate complete version-specific mirrors during the Mac build; remove both runtime inferrers; refuse unsupported behavior explicitly |
| Ritsu IL fallback | Ritsu `src/Utils/HarmonyIl/HarmonyIlMethodReader.cs:36-39,171-187` reads method IL and itself creates DynamicMethod labels | Keeping Ritsu DLLs or reflection metadata alone cannot fix runtime inference |
| Runtime patching | CombatSolver `src/Runtime/Entry.cs:57-109`; Ritsu `src/Patching/Core/ModPatcher.cs:531-551` | Replace required Harmony patches with static weaving; preserve simulation isolation |
| Private game members | CombatSolver project uses Krafs.Publicizer | Ensure final game assembly has valid static bridges/visibility, not only publicized compile references |
| Reflection and generic calls | `SimulatedCombatState.cs:713-774`, `MirrorMethodSpec.cs` | Preserve necessary metadata and generate known generic dispatch where needed; audit AOT warnings |
| Ritsu loader | `components/loader/ModuleAssemblyResolver.cs:18-33` uses AssemblyLoadContext.LoadFromAssemblyPath | Select 0.111.0 at build time and link required code statically |
| Framework startup | Ritsu `src/RitsuLibFramework.cs:436-484` installs patches and broad services | Avoid full Initialize just to obtain lifecycle events |
| Godot UI callbacks | Local `src/STS2MobileIos/Patches/ShaderWarmupPatch.cs:13-17` documents separate-assembly Node registration limitations | Use built-in Controls plus static handlers, or prove script registration under this exporter |

Setting `AllowInference=false` is insufficient: strict inference is still called.
`Expression.Compile()` is a compatibility/performance review item rather than an
automatic hard failure. Its interpreted form and reflection still need metadata.
The Ritsu script-registration implementation at `RitsuLibFramework.cs:1914-1955`
is a possible reference, not evidence that it works with this AOT exporter.

## Recommended implementation scope

Start with original game content plus CombatSolver, without general third-party
mod support. Reuse the search and simulation logic with an explicit iOS adapter.
Ritsu is a real desktop dependency, but porting its entire loader, settings UI,
Workshop integration and patching framework is unnecessary for this scope.

1. Rebuild and validate the **0.111.0 base game** separately. Match the DLL and
   PCK version; preserve the previous working artifacts. Use overwrite install.
2. Add build-time generation for clone/access bridges, card mirrors and known
   generic dispatch. Preserve unknown-behavior rejection, not silent skipping.
3. Connect combat/model lifecycle directly through static hooks. Combat-end
   handling must wait for the original Task to finish; a normal postfix runs
   when the Task is returned, not necessarily when it completes.
4. Retain live-state/simulation isolation. Only remove Ritsu extension-specific
   isolation when the corresponding framework feature is also excluded.
5. Provide a small touch UI: request/cancel search, display current best route,
   reject stale results after live state changes. Start with suggestions only.
6. Compare fixed combat snapshots against desktop results, verify no changes to
   live RNG/state, then benchmark memory, responsiveness and thermal behavior on
   each actual device before selecting presets.

Local Weaver currently supports a limited prefix/postfix contract, not general
Harmony transpilers/finalizers/state/priority semantics. It accepts one hook
assembly. The build stages a fixed dependency set and recreates `prebuilt/`, so
dependency inclusion/rooting must be implemented in build logic, not manually
copied into a directory that the next build deletes.

The upstream offline harness may help future comparisons without launching
Godot. It still uses ordinary .NET/Harmony and limited generated/fixture
scenarios; arbitrary saved-combat restoration is not supported. It is not an
existing remote solver service for an iPhone. A Mac helper would require a new
state protocol and isolation/correctness work.

## Search budgets and the user's 12 GB devices

No device has been benchmarked with the adapted solver. It is premature to claim
the iPhone cannot use the desktop node/time budgets, or that the iPad can.
More time may compensate for slower computation, but not for insufficient memory
to retain a search frontier. Equal node counts also do not guarantee equal memory
consumption or identical routes across different combats and stop conditions.

The inspected desktop presets range from 60 seconds / 60,000 nodes to 300 seconds
/ 500,000 nodes. The default NoGC region request is 16 GB, **not** a mandatory
allocation, minimum RAM requirement or hard process memory ceiling. Source
`SearchGcPolicy.cs:255-258` already disables that mechanism on iOS/Android.
An iOS adapter should deliberately use normal GC and measured memory admission.
Initially use one worker and conservative, adjustable search budgets; increase
only after measurement. Upstream source defaults to 4 workers on 4-15 logical
processors and 8 on >=16 (the README's simpler wording omits that last case).

The user's **iPhone 17 Pro and M5 iPad Pro both have 12 GB physical RAM**.
The iPhone RAM is identified in [iFixit's hardware inspection](https://www.ifixit.com/Device/iPhone_17_Pro).
[Apple's M5 iPad specifications](https://www.apple.com/ipad-pro/specs/) list
12 GB/9 CPU cores for 256/512 GB storage and 16 GB/10 cores for 1/2 TB.
[Apple's iPhone specifications](https://support.apple.com/en-us/125090) list
6 CPU cores for A19 Pro. The M5 may improve search throughput, but the user's
iPad does not have a physical-memory capacity advantage over their iPhone.
Core count alone is not a solver benchmark or a scaling guarantee.

Physical RAM is not the app's available budget. Game assets, GPU use, the OS,
other workloads and device/OS-specific app limits matter. Apple says the
[increased-memory entitlement](https://developer.apple.com/documentation/bundleresources/entitlements/com.apple.developer.kernel.increased-memory-limit)
is supported only on some models and recommends `os_proc_available_memory()`.
Measure the real signed build and retain a margin; do not promise a fixed number
of available GB from the device specification. Both devices still require the
same core AOT adaptations. A paid developer account does not perform these code
changes or make desktop Harmony/DynamicMethod work in this NativeAOT build.

## Status

Study and isolated 37-hook weaving probe complete. No solver implementation,
NativeAOT solver build, beta gameplay validation or device performance result is
claimed. The next useful milestone is a validated beta base plus a minimal
statically linked solver prototype. All game binaries remain local and ignored;
this document contains no game assets, credentials or personal signing values.
