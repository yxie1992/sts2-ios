using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace CombatSolver.Engine.InCombat.Mirrors.Cards.OnPlay;

// Both inference paths use recipes generated on the Mac from the exact input
// game. Missing/unknown entries return null so normal unsupported-behavior
// handling remains active; no game method IL is examined on the device.
internal static class CardOnPlayInferrer
{
    public static Action<CardModel, CardOnPlayMirrorContext>? Infer(Type type, MethodInfo method)
        => CardOnPlayRecipes.Resolve(type, method, strict: false);

    public static Action<CardModel, CardOnPlayMirrorContext>? InferStrict(Type type, MethodInfo method)
        => CardOnPlayRecipes.Resolve(type, method, strict: true);

    internal static void ExecuteInferredActions(
        IReadOnlyList<Action<CardModel, CardOnPlayMirrorContext>> actions,
        CardModel card,
        CardOnPlayMirrorContext context)
    {
        foreach (var action in actions)
        {
            action(card, context);
            if (context.Simulator.HasPendingChoice) return;
        }
    }
}
