#if !DESKTOP_ORACLE
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CombatSolver;
using CombatSolver.Engine.InCombat.Simulation;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace OfflineSearchHarness;

// Reflection serializers exist only in this host validation executable. Compare
// bytes, not parsed values: order, escaping and enum form affect upstream keys.
internal static class JsonParityChecks
{
    internal static void Run(List<string> checks)
    {
        void Equal(string expected, string actual, string label)
        {
            if (expected != actual)
                throw new InvalidOperationException($"JSON parity failed: {label}\nEXPECTED {expected}\nACTUAL {actual}");
        }
        const string unicode = "卡牌 <>&\"' +\n\t 🚀\u2028";
        PlanCardChoice choice = new(PlanChoiceEffect.MoveToHand, (PileType)2,
            [new("CARD", 2, unicode, 3, 4, "标题")], unicode, "context", PlanChoiceTiming.PlayerTurnStart);
        PlanAction action = new(PlanActionKind.PlayCard, 2, CardId: "CARD", CardOccurrence: 1,
            TargetIndex: 2, TargetCombatId: 7, CardTitle: unicode, TargetName: "怪物", Choice: choice,
            NestedChoices: [choice], NestedChoicesBeforePrimary: 1, PotionSlot: 3,
            PotionId: "potion", PotionTitle: unicode, TurnStartChoices: [choice],
            RelicEffects: [new("relic", "遗物", unicode)], ReplayCount: 2, CardStateKey: "key",
            CardStateOccurrence: 1, EndsPlayerTurn: true, CardUpgradeLevel: 2, CardEnchantmentId: "enchanted");
        foreach (PlanAction item in new[] { action, new(PlanActionKind.EndTurn, 0),
            action with { Kind = PlanActionKind.UsePotion }, action with { Kind = (PlanActionKind)87 } })
            Equal(JsonSerializer.Serialize(item), MobileSolverJson.Serialize(item), "PlanAction default key");
        PlanAction[] prefix = [action, new(PlanActionKind.EndTurn, 2)];
        Equal(JsonSerializer.Serialize(prefix), MobileSolverJson.Serialize(prefix), "action prefix array key");
        foreach (IReadOnlyList<PlanCardChoice>? choices in new IReadOnlyList<PlanCardChoice>?[] { null, [], [choice] })
            Equal(JsonSerializer.Serialize(choices), MobileSolverJson.Serialize(choices), "turn-start choice list key");
        Equal(JsonSerializer.Serialize(unicode), MobileSolverJson.Serialize(unicode), "profile string escaping");
        Equal("null", MobileSolverJson.Serialize((string?)null), "nullable profile string");
        checks.Add("generated search JSON keys match upstream bytes including choices, nulls, Unicode and enum values");

        var cap = new TranspositionCapDiagnostics.Snapshot(10, 10, 11, true, 3, 40, 2, 18,
            new SortedDictionary<int, int> { [1] = 2, [3] = 4 });
        Equal(JsonSerializer.Serialize(cap), MobileSolverJson.Serialize(cap), "transposition diagnostics");
        var pending = new MobilePendingChoiceEvidence(unicode, "context", PlanChoiceTiming.EnemyTurn,
            2, PlanChoiceEffect.Exhaust, (PileType)1, 1, 2, 1.25, null, false, [unicode], [], [choice]);
        Equal(JsonSerializer.Serialize(new
        {
            pending.SourceId, pending.ContextId, pending.Timing, pending.Count, pending.Effect, pending.SourcePile,
            pending.MinCount, pending.MaxCount, pending.ReplacementValue, pending.MaxBranches, pending.IsImplicitAllSelection,
            pending.options, pending.sources, pending.choices,
        }), MobileSolverJson.Serialize(pending), "anonymous pending-choice evidence");
        checks.Add("generated always-evaluated search diagnostics match upstream bytes");

        var relaxed = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        var evidence = new JsonSerializerOptions(relaxed) { Converters = { new JsonStringEnumConverter() } };
        var entry = new CombatLogEntry(123456789, "INFO", unicode);
        Equal(JsonSerializer.Serialize(entry, relaxed), Encoding.UTF8.GetString(MobileSolverJson.SerializeLog(entry)),
            "journal UTF-8 log with relaxed escaping");
        var failed = new MobileFailedCandidateEvidence(1, unicode, 2, 3, action, prefix, [choice], 41, 5, 3, 2, 4, 15, true);
        Equal(JsonSerializer.Serialize(new
        {
            failed.schemaVersion, failed.stage, failed.Turn, failed.ActionCount, failed.attemptedAction,
            failed.prefix, failed.turnSetupChoices, failed.PlayerHp, failed.PlayerBlock, failed.Energy,
            failed.Stars, failed.HandCount, failed.EnemyHp, failed.hasPendingCycleExitObservation,
        }, evidence), MobileSolverJson.SerializeEvidence(failed), "failed candidate anonymous evidence");
        var replay = new MobileRouteReplayEvidence(1, "trace", unicode, null, unicode, null, "actual", "scope", 2, 1);
        Equal(JsonSerializer.Serialize(new
        {
            replay.schemaVersion, replay.traceId, replay.reason, replay.firstScalarDifference,
            replay.firstActualState, replay.expectedFinalState, replay.actualFinalState, replay.comparisonScope,
            replay.actionCount, replay.completedSteps,
        }, evidence), MobileSolverJson.SerializeEvidence(replay), "route replay anonymous evidence");
        var step = new MobileRouteActionEvidence("trace", 0, action, new ReplayStepValues(41, 5, 3, 0, 5), null);
        Equal(JsonSerializer.Serialize(new { step.traceId, step.index, step.action, step.expected, step.actual }, evidence),
            MobileSolverJson.SerializeEvidence(step), "route action anonymous evidence");
        var health = new MobileRouteHealthEvidence("trace", new RecordedHealthChange(1, "damage", 7,
            CombatDamageSource.For(CombatDamageSourceKind.Card, unicode), 10.25m, 7.5m, 41, 34));
        Equal(JsonSerializer.Serialize(new { health.traceId, health.change }, evidence),
            MobileSolverJson.SerializeEvidence(health), "health anonymous evidence");
        checks.Add("generated journal and all four replay evidence shapes match upstream UTF-8 and string enums");

        var settingsOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var custom = new SolverSettingsData
        {
            PerformancePreset = SolverPerformancePreset.Custom, SearchTimeLimitSeconds = 27.5,
            DeploymentFastMode = SolverDeploymentFastMode.Instant, OverlayTheme = SolverOverlayTheme.Light,
            SearchCompletionNotificationMode = SolverSearchCompletionNotificationMode.Always,
            ActTransitionBossHpStrategy = BossHpStrategy.MinimizeHpLoss, SearchBeamWidth = 37,
            ReporterContactQq = unicode, PotionDirectives = [new(2, unicode, SolverPotionDirective.Force)],
            RelicCounterRules = [new(RelicCounterId.PenNib, true, 1, 3, 2)],
        };
        foreach (SolverSettingsData data in new[] { new SolverSettingsData(), custom })
        {
            string expected = JsonSerializer.Serialize(data, settingsOptions);
            Equal(expected, MobileSolverJson.SerializeSettings(data), "settings schema/options");
            Equal(expected, Encoding.UTF8.GetString(MobileSolverJson.SerializeSettingsBytes(data)), "settings UTF-8");
            Equal(expected, MobileSolverJson.SerializeSettings(MobileSolverJson.DeserializeSettings(expected)!), "settings roundtrip");
        }
        foreach (string legacy in new[]
        {
            "{}",
            "{\"deepTimeLimitSeconds\":12.5,\"deepBeamWidth\":7,\"performancePreset\":\"High\",\"overlayTheme\":1}",
            "{\"deepTimeLimitSeconds\":12.5,\"searchTimeLimitSeconds\":27}",
            "{\"searchTimeLimitSeconds\":27,\"deepTimeLimitSeconds\":12.5}",
            "{\"searchTimeLimitSeconds\":null,\"deepTimeLimitSeconds\":12.5,\"unknown\":{\"ignored\":true}}",
        })
            Equal(JsonSerializer.Serialize(JsonSerializer.Deserialize<SolverSettingsData>(legacy, settingsOptions), settingsOptions),
                MobileSolverJson.SerializeSettings(MobileSolverJson.DeserializeSettings(legacy)!), "legacy/sparse settings and property order");
        checks.Add("generated settings preserve upstream camel-case, indentation, string enums, nulls and legacy migration reads");
    }
}
#endif
