using System.Reflection;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Models;

namespace CombatSolver;

/// <summary>
/// NativeAOT cannot install Harmony patches. The build's pinned game assembly
/// and static hook manifest replace the mutable desktop patch registry; a root
/// with external content is explicitly rejected rather than assumed compatible.
/// </summary>
internal static class PredictionModPatchAudit
{
    internal readonly record struct ForeignPatch(string ModId, string ModName, string Description);

    public static void RegisterAdaptedMonsterMachine(Type monsterType, string modId)
        => throw new PredictionUnsupportedException("Runtime monster adapters are unsupported by the iOS solver.");

    public static void ValidateCardOnPlay(IEnumerable<CardModel> cards) => CaptureCardOnPlay(cards);

    internal static AdaptedOnPlaySnapshot? CaptureCardOnPlay(IEnumerable<CardModel> cards)
    {
        VanillaRuntimeContract.ValidateLoadedMods();
        foreach (CardModel card in cards)
        {
            VanillaRuntimeContract.ValidateModel(card);
            AuditCardOnPlay(card.GetType(), adapted: false, out _);
        }
        // There is no registered runtime replacement composition in this build.
        return null;
    }

    internal static AdaptedCardOnPlayMirrors.Registration? AuditCardOnPlay(
        Type type, bool adapted, out ForeignPatch? firstForeign)
    {
        VanillaRuntimeContract.ValidateType(type);
        if (adapted)
            throw new PredictionUnsupportedException("Runtime card replacement compositions are unsupported on iOS.");
        VanillaRuntimeContract.ValidateMethod(AdaptedCardOnPlayMirrors.ResolveOnPlay(type));
        firstForeign = null;
        return null;
    }

    internal static void ValidateMonsterModels(IEnumerable<MonsterModel> monsters)
    {
        VanillaRuntimeContract.ValidateLoadedMods();
        foreach (MonsterModel monster in monsters) VanillaRuntimeContract.ValidateModel(monster);
    }

    internal static void RejectForeignPatches(IEnumerable<MethodBase> methods)
    {
        foreach (MethodBase method in methods) VanillaRuntimeContract.ValidateMethod(method);
    }
}
