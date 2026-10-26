using System.Runtime.InteropServices;

namespace CombatSolver.Mobile;

internal static class MobileSearchPolicy
{
    internal const long ManagedGrowthLimit = 384L * 1024 * 1024;
    internal const ulong AdmissionHeadroom = 512UL * 1024 * 1024;
    internal const ulong StopHeadroom = 256UL * 1024 * 1024;

    internal static SearchPolicySnapshot Create(SearchFramePressureSignal frames, MobileSolverOptions? options = null)
    {
        options = (options ?? new MobileSolverOptions()).Validated();
        SolverSearchProfile profile = options.Profile();
        int budget = profile.SoftTimeBudgetMilliseconds;
        frames.ResetPressure();
        return new SearchPolicySnapshot(
            Profile: profile,
            PotionPolicy: options.Potions,
            PotionStrategy: new PotionStrategySnapshot(options.Potions, []),
            DetailedDiagnostics: false,
            VerifyIncrementalSearch: false,
            FixedBudget: true,
            MeasurePhasePerformance: false,
            MaxDegreeOfParallelism: options.Workers,
            BudgetOverrideMilliseconds: budget,
            IncludeTurnSetup: false,
            TheftPolicy: null,
            ActTransitionBossHpStrategy: BossHpStrategy.MinimizeHpLoss,
            FinalBossHpStrategy: BossHpStrategy.MinimizeHpLoss,
            AcceptableBattleHpLoss: 0,
            Diagnostics: new SearchDiagnosticsSink(Entry.Logger.Info, _ => { }),
            FramePressureSignal: frames,
            MemoryPressureSignal: new SearchMemoryPressureSignal())
        {
            TranspositionEntryLimit = Math.Clamp(profile.MaxExpandedNodes * 4, 12_000,
                SearchPolicySnapshot.DefaultTranspositionEntryLimit),
            UseBeamWidthPortfolio = false,
            UseNoveltyPortfolio = false,
            PredictPotionReward = false,
        };
    }

    // Apple's per-process remaining memory is more useful here than physical RAM.
    // These are cooperative safeguards, not a hard allocation/jetsam guarantee.
    internal static ulong? AvailableMemory()
    {
        if (!OperatingSystem.IsIOS()) return null;
        try { return OsProcAvailableMemory(); }
        catch (EntryPointNotFoundException) { return null; }
        catch (DllNotFoundException) { return null; }
    }

    [DllImport("__Internal", EntryPoint = "os_proc_available_memory")]
    private static extern ulong OsProcAvailableMemory();
}
