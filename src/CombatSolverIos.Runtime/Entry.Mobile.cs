using Godot;

namespace CombatSolver;

// Linked instead of upstream Runtime/Entry.cs. No mod loader, patch registration,
// automatic search, process launch, network service, or desktop GC configuration.
public static class Entry
{
    public const string ModId = "CombatSolver";
    public static CombatSolverLog Logger { get; private set; } = null!;
    public static bool Enabled { get; private set; } = true;
    public static bool IsPreCombatWorker => false;

    internal static void InitializeMobile()
    {
        if (Logger != null) return;
        Logger = new CombatSolverLog(Path.Combine(OS.GetUserDataDir(), "logs", "CombatSolverMobile"));
        Logger.Info("Mobile solver adapter initialized; manual suggestions, bounded search, normal GC.");
    }
}
