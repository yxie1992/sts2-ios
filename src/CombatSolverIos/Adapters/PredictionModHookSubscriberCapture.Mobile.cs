using CombatSolver.Engine.Common;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace CombatSolver;

/// <summary>Capture the unmodified game's subscriber and hand-limit contract.</summary>
internal sealed class PredictionModHookSubscriberCapture
{
    public AbstractModel[] RunSubscribers { get; }
    public AbstractModel[] CombatSubscribers { get; }
    public IReadOnlyDictionary<Player, int> MaxHandSizes { get; }
    public IReadOnlySet<Player> EveryCardFreePlayers { get; } = new HashSet<Player>();
    public bool HasBaseLibCardModifiers => false;
    public bool HasInactiveLoadoutSummonPowers => false;
    internal bool HasOnlyNonHealingLoadoutSubscribers => RunSubscribers.Length == 0 && CombatSubscribers.Length == 0;
    internal bool HasCertifiedNonHealingSubscribers => HasOnlyNonHealingLoadoutSubscribers;
    internal bool IsCertifiedNonHealingSubscriberSource(AbstractModel source) => false;
    public AdaptedOnPlaySnapshot? AdaptedOnPlay => null;
    public MirroredHookListenerFilter MirroredHookFilter { get; } = MirroredHookListenerFilter.Capture();

    private PredictionModHookSubscriberCapture(AbstractModel[] run, AbstractModel[] combat, Dictionary<Player, int> handSizes)
    {
        RunSubscribers = run;
        CombatSubscribers = combat;
        MaxHandSizes = handSizes;
    }

    public static PredictionModHookSubscriberCapture Capture(RunState runState, CombatState combat)
    {
        VanillaRuntimeContract.ValidateLoadedMods();
        AbstractModel[] runSubscribers = ModHelper.IterateAllRunStateSubscribers(runState).ToArray();
        AbstractModel[] combatSubscribers = ModHelper.IterateAllCombatStateSubscribers(combat).ToArray();
        VanillaRuntimeContract.RequireNoModSubscribers(runSubscribers, "run");
        VanillaRuntimeContract.RequireNoModSubscribers(combatSubscribers, "combat");
        PredictionModPatchAudit.ValidateCardOnPlay(
            combat.Players.SelectMany(player => player.PlayerCombatState?.AllCards ?? [])
                .Concat(runState.Players.SelectMany(player => player.Deck.Cards)));
        Dictionary<Player, int> limits = [];
        foreach (Player player in combat.Players)
            limits.Add(player, VanillaRuntimeContract.NativeMaxHandSize);
        return new(runSubscribers, combatSubscribers, limits);
    }

    public static string? CaptureLiveLoadoutSummonPowerState(CombatState combat)
    {
        VanillaRuntimeContract.RequireNoModSubscribers(ModHelper.IterateAllCombatStateSubscribers(combat), "combat");
        return null;
    }

    public void AppendCardAttachedListeners(IEnumerable<CardModel> cards, List<AbstractModel> listeners)
    {
        // This API appends BaseLib card modifiers, not native enchantments or
        // afflictions. They are rejected by the fixed build contract. Native
        // attached models are still validated and handled by upstream mirrors.
        foreach (CardModel card in cards) VanillaRuntimeContract.ValidateModel(card);
    }
}
