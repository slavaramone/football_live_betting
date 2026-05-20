using System.Globalization;

namespace LiveTotalsHelper.Tools;

public static class LiveTotalDecisionMode
{
    public const string FullModel = "FullModel";
    public const string AfterGoalOnly = "AfterGoalOnly";
    public const string SecondHalfAfterGoalOnly = "SecondHalfAfterGoalOnly";

    public static string Normalize(string value)
    {
        string normalized = (value ?? string.Empty).Trim().ToLowerInvariant().Replace("-", string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        return normalized switch
        {
            "aftergoalonly" or "aftergoal" => AfterGoalOnly,
            "2haftergoalonly" or "secondhalfaftergoalonly" or "secondhalfaftergoal" => SecondHalfAfterGoalOnly,
            _ => FullModel
        };
    }
}

public sealed class LiveTotalDecisionRuleOptions
{
    public string DecisionMode { get; set; } = LiveTotalDecisionMode.FullModel;
    public int? MinMinute { get; set; }
    public bool RequireGoalTrigger { get; set; }
    public double? MinLine { get; set; }
    public List<double> AllowedLines { get; set; } = [];
    public bool FallbackBettingEnabled { get; set; } = true;
    public string Notes { get; set; } = string.Empty;

    public string Summary()
    {
        var parts = new List<string> { LiveTotalDecisionMode.Normalize(DecisionMode) };
        if (MinMinute.HasValue)
            parts.Add($"minMinute={MinMinute.Value}");
        if (RequireGoalTrigger)
            parts.Add("requireGoalTrigger=true");
        if (MinLine.HasValue)
            parts.Add($"minLine={MinLine.Value.ToString("0.##", CultureInfo.InvariantCulture)}");
        if (AllowedLines.Count > 0)
            parts.Add($"allowedLines=[{string.Join(",", AllowedLines.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)))}]");
        if (!FallbackBettingEnabled)
            parts.Add("fallback=false");
        return string.Join("; ", parts);
    }
}

public sealed class LiveTotalSideDecision
{
    public string Decision { get; init; } = "NO BET";
    public string Explanation { get; init; } = string.Empty;

    public bool IsAction =>
        Decision.StartsWith("BET ", StringComparison.OrdinalIgnoreCase) ||
        Decision.StartsWith("LEAN ", StringComparison.OrdinalIgnoreCase) ||
        Decision.Equals("MANUAL REVIEW", StringComparison.OrdinalIgnoreCase);
}

public static class LiveTotalDecisionRulesHandler
{
    public static LiveTotalSideDecision BuildSideDecision(
        LiveTotalDecisionRuleOptions rules,
        IReadOnlyCollection<LiveTotalProfileBettingRule> profileRules,
        Func<double, string, string, LiveTotalProfileBettingRule?> findProfileRule,
        bool useProbabilityMoveFilter,
        bool underSignalsBettingAllowed,
        double defaultEdgeThreshold,
        double defaultMinOverProbabilityMove,
        double defaultMinUnderProbabilityMove,
        double line,
        double? edge,
        double probabilityMove,
        bool stateCorrectionSupported,
        string stateTrigger,
        int minute,
        bool hasRecentGoal,
        bool hasRedCard,
        string side)
    {
        string normalizedTrigger = LiveTotalStateTrigger.Normalize(stateTrigger);
        string mode = LiveTotalDecisionMode.Normalize(rules.DecisionMode);
        string sideUpper = side.ToUpperInvariant();

        if (!edge.HasValue)
            return No("NO ODDS", "No book odds were entered for this side/line.");

        string? scopeBlock = ScopeBlockReason(rules, mode, normalizedTrigger, minute, line);
        if (!string.IsNullOrWhiteSpace(scopeBlock))
            return No("NO BET - rules", scopeBlock);

        if (!stateCorrectionSupported)
            return No("NO BET - unsupported sparse state bucket", "State-correction bucket is not supported/usable, so betting is blocked.");

        if (hasRecentGoal && !normalizedTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase))
            return No("WAIT", "Recent goal detected but trigger is not AfterGoal; wait or rerun as after-goal.");

        LiveTotalProfileBettingRule? profileRule = findProfileRule(line, normalizedTrigger, sideUpper);
        bool fullModelMode = mode.Equals(LiveTotalDecisionMode.FullModel, StringComparison.OrdinalIgnoreCase);
        if (fullModelMode && profileRules.Count > 0 && profileRule is null && !rules.FallbackBettingEnabled)
            return No("NO BET - no profile rule", "FullModel fallback betting is disabled and no side/line/trigger profile rule matched.");

        double minEdge = profileRule?.MinEdge > 0 ? profileRule.MinEdge : defaultEdgeThreshold;
        double minProbabilityMove = profileRule is not null
            ? profileRule.MinProbabilityMove
            : sideUpper.Equals("OVER", StringComparison.OrdinalIgnoreCase)
                ? defaultMinOverProbabilityMove
                : defaultMinUnderProbabilityMove;

        bool probabilityMoveAllowed = sideUpper.Equals("OVER", StringComparison.OrdinalIgnoreCase)
            ? probabilityMove >= minProbabilityMove
            : probabilityMove <= minProbabilityMove;

        if (profileRule is not null && !profileRule.AllowBet)
            return No("NO BET - profile rule disabled", $"Matched profile rule for {sideUpper} {line:0.##}, but allowBet=false. {profileRule.Notes}".Trim());

        if (profileRule is null && useProbabilityMoveFilter && sideUpper.Equals("UNDER", StringComparison.OrdinalIgnoreCase) && !underSignalsBettingAllowed)
            return No("NO BET - under disabled", "Global probability-move filter is active and under-side fallback is disabled.");

        if ((profileRule is not null || useProbabilityMoveFilter) && !probabilityMoveAllowed)
            return No(
                $"NO BET - move {probabilityMove:+0.0%;-0.0%;0.0%}",
                sideUpper.Equals("OVER", StringComparison.OrdinalIgnoreCase)
                    ? $"Over probability move {probabilityMove:+0.0%;-0.0%;0.0%} is below required {minProbabilityMove:+0.0%;-0.0%;0.0%}."
                    : $"Over probability move {probabilityMove:+0.0%;-0.0%;0.0%} is above required under threshold {minProbabilityMove:+0.0%;-0.0%;0.0%}.");

        string rulePart = profileRule is not null
            ? $"matched profile rule ({profileRule.StateTrigger}/{profileRule.Side} {profileRule.Line:0.##})"
            : $"scope {rules.Summary()}";
        string edgeText = edge.Value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture);
        string minEdgeText = minEdge.ToString("P0", CultureInfo.InvariantCulture);

        if (hasRedCard)
        {
            if (edge >= minEdge)
                return new LiveTotalSideDecision { Decision = "MANUAL REVIEW", Explanation = $"{rulePart}; edge {edgeText} >= {minEdgeText}, but red card requires manual review." };
            return No("NO BET", $"{rulePart}; edge {edgeText} below {minEdgeText}; red card also requires caution.");
        }

        if (edge >= minEdge)
            return new LiveTotalSideDecision { Decision = $"BET {sideUpper}", Explanation = $"{rulePart}; edge {edgeText} >= required {minEdgeText}." };

        if (edge >= minEdge / 2.0 && (profileRule is null || probabilityMoveAllowed))
            return new LiveTotalSideDecision { Decision = $"LEAN {sideUpper}", Explanation = $"{rulePart}; edge {edgeText} is below bet threshold {minEdgeText} but above lean threshold {(minEdge / 2.0):P0}." };

        return No("NO BET", $"{rulePart}; edge {edgeText} below required {minEdgeText}.");
    }

    private static string? ScopeBlockReason(LiveTotalDecisionRuleOptions rules, string mode, string normalizedTrigger, int minute, double line)
    {
        bool requiresAfterGoal = rules.RequireGoalTrigger ||
            mode.Equals(LiveTotalDecisionMode.AfterGoalOnly, StringComparison.OrdinalIgnoreCase) ||
            mode.Equals(LiveTotalDecisionMode.SecondHalfAfterGoalOnly, StringComparison.OrdinalIgnoreCase);

        if (requiresAfterGoal && !normalizedTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase))
            return $"Decision scope requires AfterGoal trigger; current trigger is {normalizedTrigger}.";

        int? minMinute = rules.MinMinute;
        if (!minMinute.HasValue && mode.Equals(LiveTotalDecisionMode.SecondHalfAfterGoalOnly, StringComparison.OrdinalIgnoreCase))
            minMinute = 46;
        if (minMinute.HasValue && minute < minMinute.Value)
            return $"Decision scope requires minute >= {minMinute.Value}; current minute is {minute}.";

        if (rules.MinLine.HasValue && line + 1e-9 < rules.MinLine.Value)
            return $"Line {line:0.##} is below rule minimum line {rules.MinLine.Value:0.##}.";

        if (rules.AllowedLines.Count > 0 && !rules.AllowedLines.Any(x => Math.Abs(x - line) < 0.0001))
            return $"Line {line:0.##} is not in allowed lines [{string.Join(", ", rules.AllowedLines.Select(x => x.ToString("0.##", CultureInfo.InvariantCulture)))}].";

        return null;
    }

    private static LiveTotalSideDecision No(string decision, string explanation)
    {
        return new LiveTotalSideDecision { Decision = decision, Explanation = explanation };
    }
}
