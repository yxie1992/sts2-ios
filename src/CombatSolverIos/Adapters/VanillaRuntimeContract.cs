using System.Reflection;
using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver;

/// <summary>
/// This executable has a fixed, build-time patch inventory. Its prediction
/// contract covers native game models only, with no runtime gameplay mods.
/// Validate mutable global configuration only on the capture thread; worker
/// validation below compares immutable type identities, never live game values.
/// </summary>
internal static class VanillaRuntimeContract
{
    // Pinned 0.111.0 native getter is ldc.i4.s 10; ret. Use the getter itself so
    // the adapter never introduces a separate hand-size rule.
    internal static int NativeMaxHandSize => CardPile.MaxCardsInHand;

    internal static void ValidateLoadedMods()
    {
        foreach (var mod in ModManager.GetLoadedMods())
            throw new PredictionUnsupportedException(
                $"The iOS solver supports the pinned original game only; loaded mod {mod.manifest?.id ?? "unknown"} is unsupported.");
        if (AdaptedCardOnPlayMirrors.Seal())
            throw new PredictionUnsupportedException("Runtime OnPlay adapters are unsupported by the fixed iOS prediction contract.");
    }

    internal static string? CaptureOnPlayConfiguration()
    {
        if (AdaptedCardOnPlayMirrors.Seal())
            throw new PredictionUnsupportedException("Runtime OnPlay adapters cannot be stamped by the iOS solver.");
        return null; // The validated fixed build has no runtime replacement configuration.
    }

    internal static void ValidateCombatRoot(CombatState combat)
    {
        ValidateLoadedMods();
        if (combat.RunState is not RunState run)
            throw new PredictionUnsupportedException("The iOS solver requires a native RunState.");
        RequireNoModSubscribers(ModHelper.IterateAllRunStateSubscribers(run), "run");
        RequireNoModSubscribers(ModHelper.IterateAllCombatStateSubscribers(combat), "combat");
        ValidateModels(combat.IterateHookListeners());
        foreach (var player in combat.Players)
        {
            ValidateModels(player.Relics);
            ValidateModels(player.PotionSlots.OfType<AbstractModel>());
            ValidateModels(player.Deck.Cards);
            ValidateModels(player.PlayerCombatState?.AllCards ?? []);
        }
        ValidateModels(combat.Enemies.Select(enemy => enemy.Monster).OfType<AbstractModel>());
    }

    internal static void ValidateType(Type type)
    {
        if (type.Assembly != typeof(AbstractModel).Assembly || type.Assembly.IsDynamic)
            throw PredictionUnsupportedException.ForContent(
                $"The iOS solver has no static prediction contract for external type {type.FullName}.", type);
    }

    internal static void ValidateModel(AbstractModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        ValidateType(model.GetType());
        if (model is CardModel card)
        {
            if (card.Enchantment is { } enchantment) ValidateType(enchantment.GetType());
            if (card.Affliction is { } affliction) ValidateType(affliction.GetType());
        }
    }

    internal static void ValidateModels(IEnumerable<AbstractModel> models)
    {
        foreach (AbstractModel model in models) ValidateModel(model);
    }

    internal static void ValidateMethod(MethodBase? method)
    {
        if (method?.DeclaringType is not { } declaringType)
            throw new PredictionUnsupportedException("A mirrored native method could not be resolved.");
        ValidateType(declaringType);
    }

    internal static void RequireNoModSubscribers(IEnumerable<AbstractModel> subscribers, string scope)
    {
        foreach (AbstractModel subscriber in subscribers)
            throw PredictionUnsupportedException.ForContent(
                $"The iOS solver does not support ModHelper {scope} subscriber {subscriber.GetType().FullName}.",
                subscriber.GetType());
    }
}
