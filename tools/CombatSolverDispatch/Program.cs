using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;

if (args.Length != 2)
    throw new ArgumentException("Usage: CombatSolverDispatch <original copied sts2.dll> <generated directory>");
string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
const string expectedSha = "9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4";
string sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))).ToLowerInvariant();
if (sha != expectedSha) throw new InvalidOperationException("Game version mismatch; regenerate and review the port for this version.");
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadWrite = false });
IEnumerable<TypeDefinition> Walk(IEnumerable<TypeDefinition> types)
{
    foreach (var type in types)
    {
        yield return type;
        foreach (var nested in Walk(type.NestedTypes)) yield return nested;
    }
}
var all = Walk(assembly.MainModule.Types).ToDictionary(t => t.FullName);
bool IsPower(TypeDefinition type)
{
    for (TypeReference? parent = type.BaseType; parent != null;)
    {
        if (parent.FullName == "MegaCrit.Sts2.Core.Models.PowerModel") return true;
        if (!all.TryGetValue(parent.FullName, out var definition)) return false;
        parent = definition.BaseType;
    }
    return false;
}
var powers = all.Values.Where(t => !t.IsAbstract && IsPower(t)).OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
if (powers.Length < 100 || powers.Any(t => t.HasGenericParameters))
    throw new InvalidOperationException("Unexpected concrete power inventory; refusing incomplete dispatch.");
StringBuilder source = new("// Generated from pinned game SHA-256: " + sha + "\n");
source.AppendLine("namespace CombatSolver;");
source.AppendLine("internal sealed partial class SimulatedCombatState");
source.AppendLine("{");
source.AppendLine("    static SimulatedCombatState()");
source.AppendLine("    {");
foreach (var power in powers)
    source.AppendLine($"        RegisterAotPower<global::{power.FullName.Replace('/', '.')}>();");
source.AppendLine("    }");
source.AppendLine("    private static void RegisterAotPower<T>() where T : MegaCrit.Sts2.Core.Models.PowerModel");
source.AppendLine("    {");
source.AppendLine("        ApplyPowerDelegates[typeof(T)] = static (combat, target, amount, applier) => combat.Apply<T>(target, amount, applier);");
source.AppendLine("        ApplyClonedPowerDelegates[typeof(T)] = static (combat, prototype, target, amount, applier) => combat.ApplyClonedPowerCore<T>(prototype, target, amount, applier);");
source.AppendLine("        TemporaryStrengthLossDelegates[typeof(T)] = static (combat, target, amount, applier) => combat.ApplyTemporaryStrengthLoss<T>(target, amount, applier);");
source.AppendLine("        TemporaryDexterityDelegates[typeof(T)] = static (combat, target, amount, applier) => combat.ApplyTemporaryDexterity<T>(target, amount, applier);");
source.AppendLine("    }");
source.AppendLine("    private static TDelegate MissingAotPower<TDelegate>(System.Type type)");
source.AppendLine("        => throw new CombatSolver.Engine.Common.PredictionUnsupportedException(\"Power type is outside the compiled mobile dispatch: \" + type.FullName);");
source.AppendLine("}");
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "PowerDispatch.g.cs"), source.ToString());
Console.WriteLine($"Generated static dispatch for all {powers.Length} concrete vanilla powers.");
