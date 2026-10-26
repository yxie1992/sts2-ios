# CombatSolver NativeAOT warning audit

The first integrated beta build on 2026-10-26 emitted 50 warnings attributed to
CombatSolver: 22 JSON warnings and 28 other trimming warnings. This note covers
the non-JSON paths; JSON call sites are adapted separately with generated type
metadata. Warnings are not blanket-suppressed.

JSON adaptation lives in `src/CombatSolverIos/Adapters/MobileSolverJson.cs` and
seven exact SHA/count-checked source overlays. It covers action/choice keys,
diagnostic shapes, journal UTF-8, typed replay-evidence records, settings and enum
conversion. It does not root the entire solver to conceal missing metadata.
Settings reads use a generated DTO that reproduces the pinned upstream record's
62 defaults and legacy migration setters before constructing the immutable
record. Parity checks cover empty and sparse JSON, plus mixed legacy/current
property order; these caught source-generation behavior that would otherwise
replace omitted settings with zero values. The mobile options constructor also
declares explicit defaults for omitted JSON properties.

| Non-JSON warnings | Disposition |
| --- | --- |
| 1: `LiveCollectionGuard.ReadVersion` | Required fix. Private `List<T>._version` belongs to CoreLib, so rooting `sts2` does not retain its reflection metadata. The mobile override uses concrete `List<T>.GetEnumerator()` mutation guards and preserves rejection of same-count writes. It deliberately avoids the interface empty-enumerator optimization. |
| 14: `MirrorMethodSpec`, `MonsterValueReader`, `IntentForecaster`, `EnchantmentStateSupport`, `MirroredHookListenerFilter`, Dampen/PaleBlueDot internal state | The reflected methods, constructors and fields belong to native game types. The vanilla runtime contract rejects foreign models/subscribers; the Godot iOS SDK explicitly roots `$(TargetName)` (`sts2`) in `iOSNativeAOT.targets`. Existing member visibility remains unchanged after bridge rewriting. These warnings express missing annotations across generic reflection helpers, not missing game metadata in this fixed build. |
| 12: `PredictionModModelSupport` BaseLib adapter reflection | BaseLib is absent from the fixed dependency inventory, and runtime gameplay mods are rejected. The type-name scan returns null before the modifier adapter accesses any members or compiles an accessor. This branch is retained upstream code outside the supported mobile contract. |
| 1: `BaseLibCloneConcurrency` type-name scan | Same absent-BaseLib condition; it does not construct runtime code or mutate game state. |

`MonsterValueReader` expression trees use NativeAOT's interpreted execution. The
native fields/properties they access are covered by the game root. This is not a
claim of equivalent device performance.

The first build emitted no warnings attributed to `STS2RitsuLib`. Static
inspection found no module initializer in any of the five linked Ritsu
compatibility/shared DLLs. The desktop loader and full Ritsu initialization are
not called. Two unused private Ritsu delegate initializers were already removed
through an exact checked source overlay; all calculation method bodies remained
unchanged. Both card inference paths and all four dynamic power-dispatch
factories were replaced by build-time generation.

Harmony/MonoMod still appear in the game dependency closure. Compared with the
previous successful iOS build log, every existing warning text was unchanged;
the sole addition was `AccessTools.Property` metadata lookup for a rooted native
game property. The counts were 72→73 Harmony warnings and 168→168 MonoMod
warnings. Runtime IL readers, emitters and patchers in those warnings are not a
supported mobile execution path: mobile startup does not initialize the desktop
mod, and its fixed vanilla contract excludes runtime gameplay patches. Merely
having these dependency warnings does not establish that an emitter is executed.

The collection guard change requires both the ordinary root-preservation suite
and explicit unchanged/same-count/empty-list mutation tests. Native compilation,
linking, successful app launch and actual device solver checks remain distinct
validation gates; passing this warning audit does not establish those outcomes.
