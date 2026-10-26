#!/usr/bin/env python3
"""Replace dynamic-only operations in copied, pinned solver source files.

Produces a compiler overlay. It never modifies the source checkout or Steam.
Exact substitutions fail on upstream drift instead of silently losing a guard.
"""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
work = root / "ios-export/.work/solver-inputs"
source_path = work / "source/CombatSolver/src/Search/SimulatedCombatState.cs"
text = source_path.read_text()
factories = {
    "GenericApplyMethod.MakeGenericMethod(type).CreateDelegate<ApplyPowerDelegate>()": "MissingAotPower<ApplyPowerDelegate>(type)",
    "GenericApplyClonedPowerMethod.MakeGenericMethod(type).CreateDelegate<ApplyClonedPowerDelegate>()": "MissingAotPower<ApplyClonedPowerDelegate>(type)",
    "GenericTemporaryStrengthLossMethod.MakeGenericMethod(type)\n                .CreateDelegate<ApplyTemporaryStrengthLossDelegate>()": "MissingAotPower<ApplyTemporaryStrengthLossDelegate>(type)",
    "GenericTemporaryDexterityMethod.MakeGenericMethod(type)\n                .CreateDelegate<ApplyTemporaryDexterityDelegate>()": "MissingAotPower<ApplyTemporaryDexterityDelegate>(type)",
}
for original, replacement in factories.items():
    if text.count(original) != 1:
        raise ValueError(f"Pinned source dispatch changed: {original}")
    text = text.replace(original, replacement)
for name in ["GenericApplyMethod", "GenericApplyClonedPowerMethod", "GenericTemporaryStrengthLossMethod", "GenericTemporaryDexterityMethod"]:
    pattern = rf"    private static readonly MethodInfo {name} = .*?;\n"
    text, count = re.subn(pattern, "", text, count=0, flags=re.DOTALL)
    if count != 1:
        raise ValueError(f"Pinned source method-info field changed: {name}")
if "MakeGenericMethod" in text:
    raise ValueError("Unconverted dynamic generic dispatch remains")
destination = work / "generated/SimulatedCombatState.Mobile.g.cs"
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text("// Generated mobile override; see original upstream LICENSE.\n#nullable enable\n" + text)
print("Prepared static-power-dispatch source overlay.")

# These private delegates are never read by any InvokeCalculate overload. Their
# unused static initialization nevertheless asks Harmony to find private Ritsu
# methods, which need not survive NativeAOT trimming. Preserve the complete
# calculation implementations and fail if upstream starts using either delegate.
dynamic_var_path = work / "source/CombatSolver/src/Engine/InCombat/Simulation/CombatPredictionDynamicVarExtensions.cs"
dynamic_var = dynamic_var_path.read_text()
unused_delegates = '''    private delegate DynamicVar GetDynamicVarDelegate(CalculatedVar calculatedVar);

    private static readonly GetDynamicVarDelegate GetBaseVar =
        AccessTools.Method(typeof(CalculatedVar), "GetBaseVar").CreateDelegate<GetDynamicVarDelegate>();

    private static readonly GetDynamicVarDelegate GetExtraVar =
        AccessTools.Method(typeof(CalculatedVar), "GetExtraVar").CreateDelegate<GetDynamicVarDelegate>();

'''
if dynamic_var.count(unused_delegates) != 1:
    raise ValueError("Pinned unused Ritsu delegate declarations changed")
dynamic_var = dynamic_var.replace(unused_delegates, "")
if re.search(r"\b(GetDynamicVarDelegate|GetBaseVar|GetExtraVar|AccessTools)\b", dynamic_var):
    raise ValueError("Ritsu delegates are now referenced; explicit NativeAOT adaptation required")
if dynamic_var.count("using HarmonyLib;\n") != 1:
    raise ValueError("Pinned Harmony import changed in dynamic-variable helper")
dynamic_var = dynamic_var.replace("using HarmonyLib;\n", "")
(work / "generated/CombatPredictionDynamicVarExtensions.Mobile.g.cs").write_text(
    "// Generated mobile override; see original upstream LICENSE.\n#nullable enable\n" + dynamic_var)
print("Prepared dynamic-variable overlay; removed only verified-unused reflection delegates.")
