using System.Reflection;
using System.Text.Json;
using CombatSolver;
using CombatSolver.Engine.Common;
#if !DESKTOP_ORACLE
using CombatSolver.Mobile;
#endif
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;

namespace OfflineSearchHarness;

internal static class Program
{
    private static int Main()
    {
        string output = Path.Combine(AssemblyBootstrap.InputRoot,
#if DESKTOP_ORACLE
            "validation-oracle");
#else
            "validation");
#endif
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        try
        {
            var loop = new MainLoopContext();
            SynchronizationContext.SetSynchronizationContext(loop);
            // Reviewed upstream bypasses: no Godot engine, native Steam API,
            // graphics/assets, or physical save store is initialized.
            GameBootstrap.ApplyGodotBypasses();
            GameBootstrap.SkipGodotNodeStaticConstructors();
            GameBootstrap.InitializeStaticState();
            typeof(Entry).GetProperty(nameof(Entry.Logger))!.GetSetMethod(true)!.Invoke(null,
                [Activator.CreateInstance(typeof(CombatSolverLog), BindingFlags.Instance | BindingFlags.NonPublic,
                    null, [Path.Combine(output, "logs")], null)!]);
            InstallMobileSemanticHooks();
            var scenario = new HarnessScenario("IRONCLAD", "FUZZY_WURM_CRAWLER_WEAK", "MOBILEVALIDATION1", 0, 0);
            loop.RunUntilCompleted(OfflineCombat.EnterCombatRoomAsync(scenario), TimeSpan.FromSeconds(30), "fixture setup");
            CombatState combat = OfflineCombat.WaitForPlayableCombat(loop);
#if !DESKTOP_ORACLE
            VanillaRuntimeContract.ValidateCombatRoot(combat);
#endif
            string before = ContinuationStamp.CaptureLive(combat).StateText;
            CardModel original = LocalContext.GetMe(combat)!.PlayerCombatState!.Hand.Cards.First(card => card.IsUpgradable);
            int originalUpgrade = original.CurrentUpgradeLevel;
            CardModel clone = PredictionUtils.CloneCardStateForSimulation(original);
            Require(!ReferenceEquals(original, clone), "clone has distinct identity", checks);
            PredictionUtils.UpgradeCard(clone);
            Require(clone.CurrentUpgradeLevel > originalUpgrade && original.CurrentUpgradeLevel == originalUpgrade,
                "clone upgrade does not upgrade source", checks);
            Require(ContinuationStamp.CaptureLive(combat).StateText == before,
                "clone/upgrade preserved complete live state and RNG stamp", checks);

            SolverDisplayNames names = SolverDisplayNames.Capture(combat);
            CombatRootSnapshot root = CombatRootSnapshot.Capture(combat);
            Require(ContinuationStamp.CaptureLive(combat).StateText == before,
                "root capture preserved complete live state and RNG stamp", checks);
            SearchPolicySnapshot policy = ValidationPolicy() with
            {
                Profile = new SolverSearchProfile(4, 40, 8, 4, 4, 30_000),
                BudgetOverrideMilliseconds = 30_000,
                StopAtAcceptableBattleHpLoss = false,
            };
            SolverResult Run(SearchPolicySnapshot? requestPolicy = null)
            {
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(35));
                var worker = Task.Run(() =>
                {
                    using IDisposable isolation = SimulationNotificationIsolation.Enter();
                    return CombatSearchCoordinator.Solve(root, names, new BattleDamageSnapshot(0, 0, 0, []),
                        requestPolicy ?? policy, cancel.Token, null);
                });
                loop.RunUntilCompleted(worker, TimeSpan.FromSeconds(40), "tiny fixed-node search");
                return worker.GetAwaiter().GetResult();
            }
            SolverResult first = Run();
            Require(first.TotalExpandedNodes > 0 || first.ExpandedNodes > 0, "real search expanded nodes", checks);
            Require(ContinuationStamp.CaptureLive(combat).StateText == before,
                "first search preserved complete live state and RNG stamp", checks);
            SolverResult second = Run();
            string Route(SolverResult result) => JsonSerializer.Serialize(result.BestNode.Actions);
            Require(Route(first) == Route(second) && first.ProjectedBattleHpLost == second.ProjectedBattleHpLost,
                "same fixed root and node budget reproduce route and HP prediction", checks);
            Require(ContinuationStamp.CaptureLive(combat).StateText == before,
                "repeated search preserved complete live state and RNG stamp", checks);
#if !DESKTOP_ORACLE
            SolverResult parallel = Run(policy with
            {
                MaxDegreeOfParallelism = 2,
                Profile = policy.Profile with { BeamWidth = 8, MaxExpandedNodes = 80 },
            });
            Require(parallel.MaxParallelExpansionConcurrency > 1
                || parallel.MaxParallelActionReplayConcurrency > 1
                || parallel.MaxParallelRoundChoiceReplayConcurrency > 1,
                "parallel smoke search actually used two workers", checks);
            Require(ContinuationStamp.CaptureLive(combat).StateText == before,
                "two-worker search preserved complete live state and RNG stamp", checks);
            foreach (var preset in new[] { MobilePreset.Low, MobilePreset.Medium, MobilePreset.High, MobilePreset.VeryHigh })
            {
                SolverPerformancePreset upstream = Enum.Parse<SolverPerformancePreset>(preset.ToString());
                Require(new MobileSolverOptions { Preset = preset }.Profile()
                    == SolverSettings.ResolvePerformanceValues(new SolverSettingsData { PerformancePreset = upstream }).Profile,
                    "mobile " + preset + " preset equals upstream profile", checks);
            }
            var custom = new MobileSolverOptions
            {
                Preset = MobilePreset.Custom, Seconds = 27, Nodes = 12_000, BeamWidth = 37,
                Workers = 2, Potions = SolverPotionPolicy.Smart,
            };
            string serialized = JsonSerializer.Serialize(custom, MobileOptionsJsonContext.Default.MobileSolverOptions);
            Require(JsonSerializer.Deserialize(serialized, MobileOptionsJsonContext.Default.MobileSolverOptions) == custom,
                "mobile options round-trip through generated JSON metadata", checks);
            SearchPolicySnapshot customPolicy = MobileSearchPolicy.Create(new SearchFramePressureSignal(), custom);
            Require(customPolicy.Profile.SoftTimeBudgetMilliseconds == 27_000
                && customPolicy.Profile.MaxExpandedNodes == 12_000 && customPolicy.Profile.BeamWidth == 37
                && customPolicy.MaxDegreeOfParallelism == 2 && customPolicy.PotionPolicy == SolverPotionPolicy.Smart,
                "custom mobile settings reach immutable search policy", checks);
#endif
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                bool observed = false;
                try
                {
                    using IDisposable isolation = SimulationNotificationIsolation.Enter();
                    CombatSearchCoordinator.Solve(root, names, new BattleDamageSnapshot(0, 0, 0, []),
                        policy, cancelled.Token, null);
                }
                catch (OperationCanceledException) { observed = true; }
                Require(observed, "already-cancelled search observes cancellation", checks);
            }
            var report = new
            {
                status = "passed", scope = "ordinary .NET isolated mock combat; not NativeAOT or device validation",
                artifacts = new
                {
                    game = AssemblyBootstrap.RuntimeGame,
                    gameSha256 = Hash(AssemblyBootstrap.RuntimeGame),
                    solver = AssemblyBootstrap.Solver,
                    solverSha256 = Hash(AssemblyBootstrap.Solver),
                },
                checks, scenario, root = OfflineCombat.DescribeRoot(combat),
                first.ExpandedNodes, first.TotalExpandedNodes, first.BoundaryReason,
                first.ProjectedBattleHpLost, actions = first.BestNode.Actions,
#if !DESKTOP_ORACLE
                mobileText = MobileRouteText.Format(first),
                parallel = new
                {
                    parallel.ExpandedNodes, parallel.TotalExpandedNodes,
                    parallel.MaxParallelExpansionConcurrency, parallel.MaxParallelActionReplayConcurrency,
                    parallel.MaxParallelRoundChoiceReplayConcurrency,
                },
#endif
                bypasses = GameBootstrap.Bypasses,
            };
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"PASS: {checks.Count} checks. Report: {output}/result.json");
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            { status = "failed", checks, error = error.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void InstallMobileSemanticHooks()
    {
        // This test host runs ordinary .NET. The iOS artifact receives these same
        // hooks by static IL weaving; no runtime patcher ships in the adapter.
#if DESKTOP_ORACLE
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(CombatStateTrackerIsolationPatch), "Prefix"));
        GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(typeof(PowerModel), "DynamicVars"),
            prefix: new HarmonyMethod(typeof(PowerDynamicVarMaterializationGuardPatch), "Prefix"));
#else
        GameBootstrap.Harmony.Patch(AccessTools.Method(typeof(CombatStateTracker), "NotifyCombatStateChanged"),
            prefix: new HarmonyMethod(typeof(MobileSolverRuntime), nameof(MobileSolverRuntime.NotificationPrefix)));
        GameBootstrap.Harmony.Patch(AccessTools.PropertyGetter(typeof(PowerModel), "DynamicVars"),
            prefix: new HarmonyMethod(typeof(MobileSolverRuntime), nameof(MobileSolverRuntime.PowerDynamicVarsPrefix)));
#endif
    }

    private static SearchPolicySnapshot ValidationPolicy()
    {
#if DESKTOP_ORACLE
        return new SearchPolicySnapshot(new SolverSearchProfile(16, 3_000, 24, 12, 12, 5_000),
            SolverPotionPolicy.Disabled, new PotionStrategySnapshot(SolverPotionPolicy.Disabled, []),
            false, false, true, false, 1, 5_000, false, null,
            BossHpStrategy.MinimizeHpLoss, BossHpStrategy.MinimizeHpLoss, 0,
            new SearchDiagnosticsSink(Entry.Logger.Info, _ => { }),
            new SearchFramePressureSignal(), new SearchMemoryPressureSignal())
        {
            TranspositionEntryLimit = 12_000,
            UseBeamWidthPortfolio = false,
            UseNoveltyPortfolio = false,
            PredictPotionReward = false,
        };
#else
        return MobileSearchPolicy.Create(new SearchFramePressureSignal());
#endif
    }

    private static string Hash(string path) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static void Require(bool condition, string check, List<string> checks)
    {
        if (!condition) throw new InvalidOperationException("FAILED: " + check);
        checks.Add(check);
        Console.WriteLine("PASS: " + check);
    }
}
