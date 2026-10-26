# Mobile solver adapter

These files are linked into `CombatSolver.dll` by `../CombatSolverIos/`; they are
not a separate Godot script assembly. `Entry.Mobile.cs` replaces the upstream
desktop initializer. The original solver engine still performs the search.

Apply `manifest.json` to the already mobile-patched **0.111.0** game assembly
using `CombatSolver.dll` as the hook assembly. All four hooks are required:

- `NGame._Ready`: adds the touch UI using built-in nodes and signal delegates.
- `CombatStateTracker.NotifyCombatStateChanged`: suppresses simulated UI state
  notifications and invalidates displayed results after live changes.
- `PowerModel.DynamicVars`: rejects lazy display-variable creation in simulation.
- `NBackgroundModeHandler.EnterBackgroundMode`: cancels search before the
  lifecycle patch pauses the scene tree.

The adapter never calls desktop `Entry.Initialize`, Ritsu initialization, a
runtime patcher, an auto-player, a child process, or a network uploader. It does
not enable general third-party mods. Ritsu APIs used by the core remain a build
dependency, with their AOT audit handled by the core/project integration.

## User flow

The combat overlay has a collapsible **Combat solver** button, **Search**,
**Cancel**, and an expandable **Options** panel. Search is available only during
an idle single-player Play phase. Root capture runs on the main thread; search
runs in one active session with a configurable number of expansion workers.
Progress is shown while searching. The final result lists current-turn actions,
targets and choices; executing an action is always the player's responsibility.
Potions default to disabled and can be set to Smart or Require at least one.
Automatic next-turn searches remain disabled.

The default Mobile preset uses 5 seconds, 3,000 expanded nodes, beam width 16,
12,000 transposition entries and one worker. Low, Medium, High and Very high read
their profiles directly from upstream `SolverSettings` (60/120/180/300 seconds,
60k/120k/250k/500k nodes). Custom permits 1–600 seconds, 100–1,000,000 nodes and
beam width 4–512. Workers can be set from 1 through min(logical CPU count, 16).
These are options, not device performance recommendations. Portfolio/novelty
passes remain disabled; matching preset profiles does not enable every desktop
feature. The immutable policy is captured per request, so changes affect the
next search. Options are saved separately in `user://combat_solver_mobile_options.json`
using generated JSON metadata; desktop settings are not read or written.
The engine gets an explicit fixed budget and normal GC; the adapter never enters
a no-GC region. A cancellation backstop is set ten seconds beyond the selected budget.

On iOS, admission requires at least 512 MiB reported by
`os_proc_available_memory()` from `/usr/lib/libSystem.B.dylib`. Admission fails
with an explicit status if the native memory measurement is unavailable. The
polling loop cancels if measurement becomes unavailable, below 256 MiB remaining,
or after managed heap growth exceeds 384 MiB. These are cooperative samples,
not a hard process memory cap; an allocation burst can outrun them. A native
symbol/link check and real-device memory test remain necessary.

## Isolation and stale results

Upstream's root capture verifies live and simulated state, including RNG counters,
before returning. Its before/after stamps remain authoritative during synchronous
capture; clone/materialization notification noise is suppressed separately from
the worker-only dynamic-variable guard. Search has a thread-local simulation
isolation scope and no route execution code.

The adapter checks both notification revision and full `LiveCombatStamp` before
publishing results. It cancels on combat/turn/action changes, a paused scene tree,
app backgrounding, focus loss, user
cancellation, memory pressure or timeout. No new search is admitted until the
previous session has completed. Results are cleared after live state changes. The
polling timer processes even while the scene tree is paused so cancellation can
drain. The implementation avoids Task-return postfixes for combat-end lifecycle.

Unsupported intents, uncompensated prediction gaps and unsupported-effect or
dynamic-resolution boundaries produce an explicit refusal, not recommendations.
Interim previews do not expose all final gap flags, so only non-actionable search
status is shown until final validation. A partial final route is labelled as
partial; a winning route is not guaranteed to be optimal.

## Validation required before claiming device support

Compile and weave checks establish the API contract only. Test root cloning and
search against fixed reference fixtures, including exact live RNG/state equality.
On the phone, check search, cancellation, changing a card/target mid-search,
combat exit, focus loss, repeat requests, unsupported effects and memory pressure.
Confirm the static power-variable guard and notification suppression are active.
The current UI needs actual-device layout and performance verification.

The isolated .NET harness passed 17 checks on 2026-10-26: original desktop and
adapted engines produced identical results for a fixed Ironclad starter-deck
combat at 40 nodes; cloning, root capture and searches preserved the full live
state/RNG stamp. An 80-node smoke test observed two concurrent expansion and
action-replay workers while leaving that fixture's live state and RNG unchanged. Four preset profiles,
custom policy propagation and generated JSON round-tripping were also checked.
These results do not establish NativeAOT or iPhone/iPad performance.
