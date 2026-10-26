#!/usr/bin/env python3
"""Generate exact, checked NativeAOT JSON overlays from verified upstream source.

Only ignored compiler inputs are written. The normal project target first runs
prepare-combat-solver.py --verify, so source commits and hashes are also checked.
"""
from pathlib import Path
import hashlib
import os
import re

ROOT = Path(__file__).resolve().parents[1]
WORK = ROOT / "ios-export/.work/solver-inputs"
SOURCE = WORK / "source/CombatSolver/src"
DESTINATION = WORK / "generated/Json"
SOURCE_SHA256 = {
    "Search/CombatBeamSolver.Phases.cs": "81138115a92be250fd67b389b53ef140c6866d6af246def629e9022c66c06eaa",
    "Search/CombatBeamSolver.Expansion.Opening.cs": "2b3036fbe2793f695625b70414be81fa56ecdb0376adea61888986fb04b1e0d4",
    "Search/FrontierContinuationScheduler.cs": "30a7193184c33459216324dfe9cbefcf285dbbc0e87dda5125c333ebf39a34d3",
    "Search/CombatBeamSolver.ExecutionChoiceContinuation.Testing.cs": "97972208f732efbea71d61d49768242687d1efd6ec0e900aa06bc3c4ce9c48dd",
    "Search/SearchReplayEvidence.cs": "6d7574c5bccc2dbe3d91e151a63f59b6eab97e02554ec8fe1d0bf4dd1f0441cc",
    "Runtime/CombatDiagnosticJournal.cs": "f050ef007eae31a71f2c7938adc51bff66d0865117d56745adfbb47872a78619",
    "Runtime/SolverSettings.cs": "017dc849422f96822593578e147c8f2d3b651ac676e6beeadfdf42dfed7f5d58",
}


def read_source(path: str) -> str:
    content = (SOURCE / path).read_bytes()
    if hashlib.sha256(content).hexdigest() != SOURCE_SHA256[path]:
        raise ValueError(f"Pinned source hash mismatch: {path}")
    return content.decode("utf-8")


def replace(text: str, old: str, new: str, count: int = 1) -> str:
    if text.count(old) != count:
        raise ValueError(f"Pinned JSON source changed ({text.count(old)} != {count}): {old}")
    return text.replace(old, new)


def overlays() -> dict[str, str]:
    result = {}
    direct = {
        "Search/CombatBeamSolver.Phases.cs": [
            ("System.Text.Json.JsonSerializer.Serialize(transpositions)", "MobileSolverJson.Serialize(transpositions)")],
        "Search/CombatBeamSolver.Expansion.Opening.cs": [
            ("JsonSerializer.Serialize(probeAction)", "MobileSolverJson.Serialize(probeAction)"),
            ("JsonSerializer.Serialize(action.TurnStartChoices)", "MobileSolverJson.Serialize(action.TurnStartChoices)")],
        "Search/FrontierContinuationScheduler.cs": [
            ("JsonSerializer.Serialize(prefix)", "MobileSolverJson.Serialize(prefix)")],
        "Runtime/CombatDiagnosticJournal.cs": [
            ('''    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
''', ""),
            ("JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions)", "MobileSolverJson.SerializeLog(entry)")],
        "Runtime/SolverSettings.cs": [
            ('''    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
''', ""),
            ("JsonSerializer.SerializeToUtf8Bytes(_current, JsonOptions)", "MobileSolverJson.SerializeSettingsBytes(_current)"),
            ("JsonSerializer.Deserialize<SolverSettingsData>(File.ReadAllText(path), JsonOptions)", "MobileSolverJson.DeserializeSettings(File.ReadAllText(path))"),
            ("JsonSerializer.Serialize(runtimeProfile.RequestedProfile)", "MobileSolverJson.Serialize(runtimeProfile.RequestedProfile)"),
            ('''JsonSerializer.Deserialize<SolverSettingsData>(
               JsonSerializer.Serialize(data, JsonOptions),
               JsonOptions)''', "MobileSolverJson.DeserializeSettings(MobileSolverJson.SerializeSettings(data))"),
            ("JsonSerializer.Deserialize<SolverSettingsData>(json, JsonOptions)", "MobileSolverJson.DeserializeSettings(json)"),
            ("JsonSerializer.Serialize(data, JsonOptions)", "MobileSolverJson.SerializeSettings(data)")],
    }
    for path, substitutions in direct.items():
        text = read_source(path)
        for old, new in substitutions:
            text = replace(text, old, new)
        result[path] = text

    path = "Search/CombatBeamSolver.ExecutionChoiceContinuation.Testing.cs"
    result[path] = replace(read_source(path), '''return System.Text.Json.JsonSerializer.Serialize(new
                {
                    request.SourceId, request.ContextId, request.Timing, request.Count, request.Effect, request.SourcePile,
                    spec.MinCount, spec.MaxCount, spec.ReplacementValue, spec.MaxBranches, spec.IsImplicitAllSelection,
                    options = spec.Options.Select(CardChoiceSupport.ChoiceCardKey).ToArray(),
                    sources = spec.SourceCards.Select(CardChoiceSupport.ChoiceCardKey).ToArray(),
                    choices = CardChoiceSupport.BuildChoices(spec, displayNames,
                        _profile.MaxPileChoiceBranchesPerAction, _profile.MaxHandChoiceBranchesPerAction),
                });''', '''return MobileSolverJson.Serialize(new MobilePendingChoiceEvidence(
                    request.SourceId, request.ContextId, request.Timing, request.Count, request.Effect, request.SourcePile,
                    spec.MinCount, spec.MaxCount, spec.ReplacementValue, spec.MaxBranches, spec.IsImplicitAllSelection,
                    spec.Options.Select(CardChoiceSupport.ChoiceCardKey).ToArray(),
                    spec.SourceCards.Select(CardChoiceSupport.ChoiceCardKey).ToArray(),
                    CardChoiceSupport.BuildChoices(spec, displayNames,
                        _profile.MaxPileChoiceBranchesPerAction, _profile.MaxHandChoiceBranchesPerAction)));''')

    path = "Search/SearchReplayEvidence.cs"
    text = read_source(path)
    text = replace(text, '''    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
''', "")
    text = replace(text, '''JsonSerializer.Serialize(new
        {
            schemaVersion = 1, stage, node.Turn, node.ActionCount, attemptedAction,
            prefix = node.Actions, turnSetupChoices = node.GetTurnSetupChoices(),
            node.Snapshot.PlayerHp, node.Snapshot.PlayerBlock, node.Snapshot.Energy, node.Snapshot.Stars,
            node.Snapshot.HandCount, node.Snapshot.EnemyHp,
            hasPendingCycleExitObservation = node.PendingCycleExitObservation != null,
        }, JsonOptions)''', '''MobileSolverJson.SerializeEvidence(new MobileFailedCandidateEvidence(
            1, stage, node.Turn, node.ActionCount, attemptedAction,
            node.Actions, node.GetTurnSetupChoices(),
            node.Snapshot.PlayerHp, node.Snapshot.PlayerBlock, node.Snapshot.Energy, node.Snapshot.Stars,
            node.Snapshot.HandCount, node.Snapshot.EnemyHp,
            node.PendingCycleExitObservation != null))''')
    text = replace(text, '''JsonSerializer.Serialize(new
        {
            schemaVersion = 1, traceId, reason, firstScalarDifference = FirstScalarDifference,
            firstActualState = FirstActualState, expectedFinalState, actualFinalState,
            comparisonScope = "hp_block_energy_stars_hand_count_per_action; full_state_at_final_failure",
            actionCount = _actions.Count, completedSteps = _steps.Count,
        }, JsonOptions)''', '''MobileSolverJson.SerializeEvidence(new MobileRouteReplayEvidence(
            1, traceId, reason, FirstScalarDifference,
            FirstActualState, expectedFinalState, actualFinalState,
            "hp_block_energy_stars_hand_count_per_action; full_state_at_final_failure",
            _actions.Count, _steps.Count))''')
    text = replace(text, '''JsonSerializer.Serialize(new
            {
                traceId, index, action = _actions[index], expected = _expected[index],
                actual = index < _steps.Count ? _steps[index].Actual : (ReplayStepValues?)null,
            }, JsonOptions)''', '''MobileSolverJson.SerializeEvidence(new MobileRouteActionEvidence(
                traceId, index, _actions[index], _expected[index],
                index < _steps.Count ? _steps[index].Actual : (ReplayStepValues?)null))''')
    text = replace(text, "JsonSerializer.Serialize(new { traceId, change }, JsonOptions)",
                   "MobileSolverJson.SerializeEvidence(new MobileRouteHealthEvidence(traceId, change))")
    result[path] = text

    # .NET 9 source generation treats init-only record properties as synthetic
    # constructor arguments, overwriting omitted values and migration effects.
    # Deserialize into a private mutable DTO with the exact upstream defaults and
    # setters, then copy into the original immutable settings record. This keeps
    # callers' immutable data contract and preserves property-order semantics.
    settings = read_source("Runtime/SolverSettings.cs")
    declaration = "internal sealed record SolverSettingsData\n{"
    end = "\ninternal sealed record SolverSettingsSnapshot("
    if settings.count(declaration) != 1 or settings.count(end) != 1:
        raise ValueError("Pinned settings record boundaries changed")
    block = declaration + settings.split(declaration, 1)[1].split(end, 1)[0]
    names = re.findall(r"public\s+[\w?\[\]]+\s+(\w+)\s*\{\s*get", block)
    if len(names) != 62 or len(set(names)) != 62 or len(re.findall(r"\binit\b", block)) != 62:
        raise ValueError("Pinned settings property layout changed")
    block = block.replace(declaration, "internal sealed class MobileSolverSettingsJsonData\n{")
    block = re.sub(r"\binit\b", "set", block)
    block = block.rstrip()
    if not block.endswith("\n}"):
        raise ValueError("Pinned settings closing brace changed")
    copy = "\n    internal SolverSettingsData ToSettings() => new()\n    {\n"
    copy += "".join(f"        {name} = this.{name},\n" for name in names)
    copy += "    };\n}\n"
    result["Runtime/MobileSolverSettingsJsonData.cs"] = (
        "using System.Text.Json.Serialization;\nnamespace CombatSolver;\n\n" + block[:-2] + copy)

    # New reflection JSON calls in these reached files need an explicit audit.
    for path, text in result.items():
        if "JsonSerializer." in text or "JsonStringEnumConverter" in text:
            raise ValueError(f"Unconverted reflection JSON in {path}")
    return result


def main() -> None:
    if DESTINATION.resolve() != DESTINATION:
        raise ValueError("Refusing symlinked compiler-output directory")
    generated = overlays()  # Validate every replacement before writing anything.
    DESTINATION.mkdir(parents=True, exist_ok=True)
    expected = {Path(path).stem + ".Json.g.cs" for path in generated}
    if {p.name for p in DESTINATION.iterdir()} - expected:
        raise ValueError("Unexpected files in owned JSON overlay directory")
    for path, text in generated.items():
        output = DESTINATION / (Path(path).stem + ".Json.g.cs")
        if output.is_symlink():
            raise ValueError(f"Refusing symlinked output: {output}")
        contents = "// Generated mobile JSON override; see original upstream LICENSE.\n#nullable enable\n" + text
        if output.exists() and output.read_text() == contents:
            continue
        temporary = output.with_suffix(".tmp")
        with temporary.open("x") as stream:
            stream.write(contents)
        os.replace(temporary, output)
    print(f"Prepared {len(generated)} checked NativeAOT JSON source overlays.")


if __name__ == "__main__":
    main()
