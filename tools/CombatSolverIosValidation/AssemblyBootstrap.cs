using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace OfflineSearchHarness;

internal static class AssemblyBootstrap
{
    internal static string InputRoot { get; private set; } = "";
    internal static string RuntimeGame { get; private set; } = "";
    internal static string Solver { get; private set; } = "";

    [ModuleInitializer]
    internal static void Initialize()
    {
        InputRoot = Path.GetFullPath((string?)AppContext.GetData("ValidationInputRoot")
            ?? throw new InvalidOperationException("Missing isolated input directory."));
        RuntimeGame = Path.GetFullPath((string?)AppContext.GetData("RuntimeGameDll")
            ?? throw new InvalidOperationException("Missing copied runtime game DLL."));
        Solver = Path.GetFullPath((string?)AppContext.GetData("SolverDll")
            ?? throw new InvalidOperationException("Missing adapted solver DLL."));
        if (!InputRoot.Contains("/ios-export/.work/", StringComparison.Ordinal)
            || !RuntimeGame.StartsWith(InputRoot + "/", StringComparison.Ordinal)
            || !File.Exists(Path.Combine(InputRoot, "prepared.json")))
            throw new InvalidOperationException("Validation only accepts prepared, isolated .work inputs.");
        AssemblyLoadContext.Default.Resolving += Resolve;
    }

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name == "sts2") return context.LoadFromAssemblyPath(RuntimeGame);
        if (name.Name == "CombatSolver") return context.LoadFromAssemblyPath(Solver);
        foreach (string relative in new[] { "ritsu/compat/0.111.0", "ritsu/shared", "game/data_sts2_macos_arm64" })
        {
            string path = Path.Combine(InputRoot, relative, name.Name + ".dll");
            if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
        }
        return null;
    }
}
