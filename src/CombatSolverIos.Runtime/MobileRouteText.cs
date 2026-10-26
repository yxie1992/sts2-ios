using System.Text;

namespace CombatSolver.Mobile;

internal static class MobileRouteText
{
    internal static string Format(SolverResult result, SolverPotionPolicy potionPolicy = SolverPotionPolicy.Disabled)
    {
        // A route that crossed an unmodelled effect is not presented as advice.
        PredictionGap[] gaps = result.Snapshot.PredictionGaps.Where(g => !g.Compensated).ToArray();
        if (result.Forecast.HasUnsupportedIntent || gaps.Length > 0
            || result.BoundaryReason is SearchBoundaryReason.UnsupportedEffect or SearchBoundaryReason.DynamicResolution)
        {
            string details = gaps.Length == 0 ? result.BoundaryReason.ToString()
                : string.Join("; ", gaps.Take(3).Select(g => $"{g.SourceId}: {g.Reason}"));
            return $"No recommendation: this combat includes unsupported behavior.\n{details}";
        }
        if (result.OnlyDeathRoutesFound)
            return "The search found only losing routes within this budget. No safe recommendation.";

        var text = new StringBuilder();
        text.Append(result.CombatEndedTurn.HasValue ? "Winning route found" : "Partial route — victory not established");
        text.Append($"\nTurn {result.StartTurnNumber}; predicted additional HP loss: {result.ProjectedBattleHpLost}");
        string potions = potionPolicy switch
        {
            SolverPotionPolicy.Disabled => "Potions: none",
            SolverPotionPolicy.Smart => "Potions: smart",
            _ => "Potions: require at least one",
        };
        text.Append($"\n{result.TotalExpandedNodes:N0} nodes; {result.TotalSearchElapsed.TotalSeconds:F1}s. {potions}.\n");
        AppendActions(text, result.BestNode.Actions.Where(a => a.Turn == result.StartTurnNumber));
        if (result.BoundaryReason != SearchBoundaryReason.None)
            text.Append($"\nSearch stopped: {result.BoundaryReason}.");
        text.Append("\nSuggestions only. Search again after any action.");
        return text.ToString();
    }

    internal static string FormatProgress(SolverProgress progress, int budgetMilliseconds)
    {
        var text = new StringBuilder($"Searching {progress.ElapsedMilliseconds / 1000d:F1}s / {budgetMilliseconds / 1000}s · {progress.ExpandedNodes:N0} nodes");
        // Interim previews do not expose all final prediction-gap flags. Show search
        // progress only; publish actionable advice after final validation.
        if (progress.CurrentBestResult is { Won: true } best)
            text.Append($"\nCandidate win found; predicted HP loss {best.ProjectedBattleHpLost}. Verifying…");
        return text.ToString();
    }

    private static void AppendActions(StringBuilder text, IEnumerable<PlanAction> actions)
    {
        int index = 0;
        foreach (PlanAction action in actions.Take(40))
        {
            string title = action.Kind switch
            {
                PlanActionKind.EndTurn => "End turn",
                PlanActionKind.UsePotion => "Use " + (string.IsNullOrEmpty(action.PotionTitle) ? action.PotionId : action.PotionTitle),
                _ => string.IsNullOrEmpty(action.CardTitle) ? action.CardId : action.CardTitle,
            };
            text.Append($"\n{++index}. {title}");
            if (!string.IsNullOrWhiteSpace(action.TargetName)) text.Append(" → " + action.TargetName);
            foreach (PlanCardChoice choice in action.GetActionChoicesInExecutionOrder())
                text.Append($"\n    {choice.Effect}: {string.Join(", ", choice.Cards.Select(c => c.Title))}");
            if (action.EndsPlayerTurn && action.Kind != PlanActionKind.EndTurn) text.Append(" (ends turn)");
        }
        if (index == 0) text.Append("\nNo card sequence found for the current turn.");
    }
}
