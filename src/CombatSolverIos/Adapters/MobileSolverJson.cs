using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace CombatSolver;

// The upstream JSON shapes/options are part of search deduplication and replay
// evidence. Keep numeric keys separate from string-enum diagnostics/settings.
// Every call supplies generated metadata; NativeAOT needs no reflection fallback.
internal static class MobileSolverJson
{
    private static readonly MobileEvidenceJsonContext Evidence = new(
        new JsonSerializerOptions(MobileEvidenceJsonContext.Default.Options)
        { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    internal static string Serialize(PlanAction value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.PlanAction);
    internal static string Serialize(PlanAction[] value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.PlanActionArray);
    internal static string Serialize(IReadOnlyList<PlanCardChoice>? value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.IReadOnlyListPlanCardChoice);
    internal static string Serialize(TranspositionCapDiagnostics.Snapshot value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.TranspositionSnapshot);
    internal static string Serialize(MobilePendingChoiceEvidence value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.MobilePendingChoiceEvidence);
    internal static string Serialize(string? value)
        => JsonSerializer.Serialize(value, MobileKeyJsonContext.Default.String);
    internal static byte[] SerializeLog(CombatLogEntry value)
        => JsonSerializer.SerializeToUtf8Bytes(value, Evidence.CombatLogEntry);
    internal static string SerializeEvidence(MobileFailedCandidateEvidence value)
        => JsonSerializer.Serialize(value, Evidence.MobileFailedCandidateEvidence);
    internal static string SerializeEvidence(MobileRouteReplayEvidence value)
        => JsonSerializer.Serialize(value, Evidence.MobileRouteReplayEvidence);
    internal static string SerializeEvidence(MobileRouteActionEvidence value)
        => JsonSerializer.Serialize(value, Evidence.MobileRouteActionEvidence);
    internal static string SerializeEvidence(MobileRouteHealthEvidence value)
        => JsonSerializer.Serialize(value, Evidence.MobileRouteHealthEvidence);
    internal static string SerializeSettings(SolverSettingsData value)
        => JsonSerializer.Serialize(value, MobileSettingsJsonContext.Default.SolverSettingsData);
    internal static byte[] SerializeSettingsBytes(SolverSettingsData value)
        => JsonSerializer.SerializeToUtf8Bytes(value, MobileSettingsJsonContext.Default.SolverSettingsData);
    internal static SolverSettingsData? DeserializeSettings(string value)
        => JsonSerializer.Deserialize(value, MobileSettingsJsonContext.Default.MobileSolverSettingsJsonData)?.ToSettings();
}

// Names and declaration order deliberately match the upstream anonymous objects.
internal sealed record MobileFailedCandidateEvidence(
    int schemaVersion, string stage, int Turn, int ActionCount, PlanAction? attemptedAction,
    IReadOnlyList<PlanAction> prefix, IReadOnlyList<PlanCardChoice> turnSetupChoices,
    int PlayerHp, int PlayerBlock, int Energy, int Stars, int HandCount, int EnemyHp,
    bool hasPendingCycleExitObservation);
internal sealed record MobileRouteReplayEvidence(
    int schemaVersion, string traceId, string reason, int? firstScalarDifference,
    string? firstActualState, string? expectedFinalState, string? actualFinalState,
    string comparisonScope, int actionCount, int completedSteps);
internal sealed record MobileRouteActionEvidence(
    string traceId, int index, PlanAction action, ReplayStepValues? expected, ReplayStepValues? actual);
internal sealed record MobileRouteHealthEvidence(string traceId, RecordedHealthChange change);
internal sealed record MobilePendingChoiceEvidence(
    string SourceId, string ContextId, PlanChoiceTiming Timing, int Count, PlanChoiceEffect Effect,
    PileType SourcePile, int MinCount, int MaxCount, double ReplacementValue, int? MaxBranches,
    bool IsImplicitAllSelection, string[] options, string[] sources, IReadOnlyList<PlanCardChoice> choices);

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(PlanAction))]
[JsonSerializable(typeof(PlanAction[]))]
[JsonSerializable(typeof(IReadOnlyList<PlanCardChoice>))]
[JsonSerializable(typeof(TranspositionCapDiagnostics.Snapshot), TypeInfoPropertyName = "TranspositionSnapshot")]
[JsonSerializable(typeof(MobilePendingChoiceEvidence))]
[JsonSerializable(typeof(string))]
internal partial class MobileKeyJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, UseStringEnumConverter = true)]
[JsonSerializable(typeof(CombatLogEntry))]
[JsonSerializable(typeof(MobileFailedCandidateEvidence))]
[JsonSerializable(typeof(MobileRouteReplayEvidence))]
[JsonSerializable(typeof(MobileRouteActionEvidence))]
[JsonSerializable(typeof(MobileRouteHealthEvidence))]
internal partial class MobileEvidenceJsonContext : JsonSerializerContext { }

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SolverSettingsData))]
[JsonSerializable(typeof(MobileSolverSettingsJsonData))]
internal partial class MobileSettingsJsonContext : JsonSerializerContext { }
