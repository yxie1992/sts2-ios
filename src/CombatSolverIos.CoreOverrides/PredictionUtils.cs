// Adapted from pinned CombatSolver 0.50.1; see its MIT license.
// Clone stages use build-time-injected game bridges; no Reflection.Emit.
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace CombatSolver.Engine.Common;

internal static class PredictionUtils
{
    public static TModel CloneModelForSimulation<TModel>(TModel source)
        where TModel : AbstractModel
    {
        // This helper does not call MutableClone's BaseLib postfix. Audited native card/Power stages
        // can run independently in isolation; nested MutableClone calls still take the
        // original gate through BaseLibCloneConcurrencyPatch.
        bool entered = BaseLibCloneConcurrency.IsRequired
            && !NativeModelCloneConcurrency.CanCloneIndependently(source)
            && BaseLibCloneConcurrency.Enter();
        try
        {
            TModel clone = (TModel)source.__CombatSolverMemberwiseClone();
            clone.IsMutable = true;
            if (clone is CardModel card)
                card.__CombatSolverDetachObservers();
            clone.__CombatSolverDeepCloneFields();
            clone.__CombatSolverAfterCloned();
            return clone;
        }
        finally
        {
            BaseLibCloneConcurrency.Exit(entered);
        }
    }

    public static CardModel CloneCardStateForSimulation(CardModel source)
    {
        CardModel clone = CloneModelForSimulation(source);
        PredictionModModelSupport.CloneCardAttachedModels(source, clone);
        clone.DeckVersion = source.DeckVersion;
        clone.HasBeenRemovedFromState = source.HasBeenRemovedFromState;
        return clone;
    }

    public static bool NeedsEndOfTurnCleanup(CardModel card)
    {
        if (card.ExhaustOnNextPlay
            || card.__CombatSolverGetSingleTurnRetain()
            || card.__CombatSolverGetSingleTurnSly())
        {
            return true;
        }

        foreach (TemporaryCardCost starCost in card._temporaryStarCosts)
        {
            if (starCost.ClearsWhenTurnEnds)
                return true;
        }

        CardEnergyCost cost = card.EnergyCost;
        List<LocalCostModifier> localModifiers = cost.__CombatSolverGetLocalCostModifiers();
        if (localModifiers.Count == 0)
            return false;
        foreach (LocalCostModifier modifier in localModifiers)
        {
            if (modifier.Expiration.HasFlag(LocalCostModifierExpiration.EndOfTurn))
                return true;
        }
        return false;
    }

    public static CardModel CreateCard(CardModel card, Player player)
    {
        card = CloneModelForSimulation(card);
        card.Owner = player;
        return card;
    }

    /// <summary>
    /// Mirrors <see cref="CardCmd.Upgrade(CardModel, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle)"/>.
    /// Does nothing if the card is not upgradable.
    /// </summary>
    public static void UpgradeCard(CardModel card)
    {
        if (!card.IsUpgradable)
        {
            return;
        }

        card.UpgradeInternal();
        card.FinalizeUpgradeInternal();
    }

    /// <summary>
    /// Same as <see cref="UpgradeCard"/>, but returns a new upgraded card instead of modifying the original card.
    /// Returns the original card if it is not upgradable.
    /// </summary>
    public static CardModel CreateUpgradedCard(CardModel card)
    {
        if (!card.IsUpgradable)
        {
            return card;
        }

        var previewCard = CloneModelForSimulation(card);
        UpgradeCard(previewCard);
        return previewCard;
    }

    /// <summary>
    /// Mirrors <see cref="CardCmd.Enchant(EnchantmentModel, CardModel, decimal)"/>.
    /// Does nothing if the card cannot be enchanted by the given enchantment.
    /// </summary>
    public static void EnchantCard(EnchantmentModel enchantment, CardModel card, decimal amount)
    {
        if (!enchantment.CanEnchant(card))
        {
            return;
        }

        if (card.Enchantment is null)
        {
            card.EnchantInternal(enchantment, amount);
            enchantment.ModifyCard();
        }
        else
        {
            // The CanEnchant check above ensures that the existing enchantment is the same type as the new enchantment.
            card.Enchantment.Amount += (int)amount;
        }

        card.FinalizeUpgradeInternal();
    }

    /// <summary>
    /// Same as <see cref="EnchantCard"/>, but returns a new enchanted card instead of modifying the original card.
    /// Returns the original card if it cannot be enchanted by the given enchantment.
    /// </summary>
    /// <returns></returns>
    public static CardModel CreateEnchantedCard(EnchantmentModel enchantment, CardModel card, decimal amount)
    {
        if (!enchantment.CanEnchant(card))
        {
            return card;
        }

        var previewCard = CloneModelForSimulation(card);
        EnchantCard(enchantment, previewCard, amount);
        return previewCard;
    }

    public static RelicModel CreateRelic(RelicModel relic, Player player)
    {
        relic = CloneModelForSimulation(relic);
        relic.Owner = player;
        return relic;
    }

    public static PotionModel CreatePotion(PotionModel potion, Player player)
    {
        potion = CloneModelForSimulation(potion);
        potion.Owner = player;
        return potion;
    }

    public static CardModel PredictTransformResult(CardModel original, Rng rng, bool isInCombat)
    {
        var options = CardFactory.GetDefaultTransformationOptions(original, isInCombat);
        var result = rng.NextItem(options)
            ?? throw new InvalidOperationException($"Could not predict a transform result for {original.Id}.");
        return result;
    }

    public static IReadOnlyList<PotionModel> PredictPotionRewards(Player player, int count, Rng rng)
    {
        return Enumerable.Range(0, count)
            .Select(_ => PotionFactory.CreateRandomPotionOutOfCombat(player, rng))
            .ToList();
    }
}
