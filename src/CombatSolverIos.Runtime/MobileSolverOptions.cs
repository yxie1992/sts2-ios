using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace CombatSolver.Mobile;

internal enum MobilePreset { Mobile, Low, Medium, High, VeryHigh, Custom }

// Explicit optional constructor values are preserved by source-generated JSON.
// Init-property initializers alone are lost for omitted JSON properties in .NET 9.
internal sealed record MobileSolverOptions(
    MobilePreset Preset = MobilePreset.Mobile,
    int Seconds = 5,
    int Nodes = 3_000,
    int BeamWidth = 16,
    int Workers = 1,
    SolverPotionPolicy Potions = SolverPotionPolicy.Disabled)
{
    internal static int MaximumWorkers => Math.Clamp(System.Environment.ProcessorCount, 1, 16);

    internal MobileSolverOptions Validated() => this with
    {
        Preset = Enum.IsDefined(Preset) ? Preset : MobilePreset.Mobile,
        Seconds = Math.Clamp(Seconds, 1, 600),
        Nodes = Math.Clamp(Nodes, 100, 1_000_000),
        BeamWidth = Math.Clamp(BeamWidth, 4, 512),
        Workers = Math.Clamp(Workers, 1, MaximumWorkers),
        Potions = Enum.IsDefined(Potions) ? Potions : SolverPotionPolicy.Disabled,
    };

    internal SolverSearchProfile Profile()
    {
        MobileSolverOptions options = Validated();
        if (options.Preset == MobilePreset.Mobile)
            return new SolverSearchProfile(16, 3_000, 24, 12, 12, 5_000);
        if (options.Preset == MobilePreset.Custom)
            return SolverSearchProfile.Default with
            {
                BeamWidth = options.BeamWidth,
                MaxExpandedNodes = options.Nodes,
                SoftTimeBudgetMilliseconds = checked(options.Seconds * 1_000),
            };
        SolverPerformancePreset desktop = options.Preset switch
        {
            MobilePreset.Low => SolverPerformancePreset.Low,
            MobilePreset.Medium => SolverPerformancePreset.Medium,
            MobilePreset.High => SolverPerformancePreset.High,
            MobilePreset.VeryHigh => SolverPerformancePreset.VeryHigh,
            _ => throw new InvalidOperationException("Invalid mobile search preset."),
        };
        // Query upstream preset definitions; do not duplicate beam/branch limits.
        return SolverSettings.ResolvePerformanceValues(new SolverSettingsData { PerformancePreset = desktop }).Profile;
    }

    private static string SettingsPath => Path.Combine(OS.GetUserDataDir(), "combat_solver_mobile_options.json");

    internal static MobileSolverOptions Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return (JsonSerializer.Deserialize(File.ReadAllText(SettingsPath), MobileOptionsJsonContext.Default.MobileSolverOptions)
                    ?? new MobileSolverOptions()).Validated();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Entry.Logger.Warn("Mobile options could not be read; using defaults: " + error.Message);
        }
        return new MobileSolverOptions();
    }

    internal void Save()
    {
        try
        {
            string path = SettingsPath;
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Validated(), MobileOptionsJsonContext.Default.MobileSolverOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Entry.Logger.Warn("Mobile options could not be saved: " + error.Message);
        }
    }
}

[JsonSerializable(typeof(MobileSolverOptions))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class MobileOptionsJsonContext : JsonSerializerContext { }
