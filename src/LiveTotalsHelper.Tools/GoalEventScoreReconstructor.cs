using LiveTotalsHelper.Infrastructure.Persistence.Entities;

namespace LiveTotalsHelper.Tools;

internal sealed class GoalEventReconstruction
{
    public List<ReconstructedGoalEvent> Goals { get; } = [];
    public int FinalHomeFromEvents { get; set; }
    public int FinalAwayFromEvents { get; set; }
    public int RawGoalIncidentCount { get; set; }
    public int ExpandedGoalCount => Goals.Count;
    public int ScoreJumpCount { get; set; }
    public int MissingScoreSnapshotCount { get; set; }
    public int ImpossibleScoreSnapshotCount { get; set; }
    public bool FinalScoreMatchesMatch { get; set; }
    public bool HasScoreSnapshots { get; set; }

    public bool IsReliable => FinalScoreMatchesMatch && ImpossibleScoreSnapshotCount == 0;
}

internal sealed class ReconstructedGoalEvent
{
    public required MatchEventEntity Source { get; init; }
    public required int Minute { get; init; }
    public required int Sequence { get; init; }
    public required int HomeBefore { get; init; }
    public required int AwayBefore { get; init; }
    public required int HomeAfter { get; init; }
    public required int AwayAfter { get; init; }
    public required bool IsHomeGoal { get; init; }
    public bool IsExpandedFromScoreJump { get; init; }

    public string Side => IsHomeGoal ? "Home" : "Away";
}

internal static class GoalEventScoreReconstructor
{
    public static GoalEventReconstruction Reconstruct(MatchEntity match, IEnumerable<MatchEventEntity> rawGoals)
    {
        var result = new GoalEventReconstruction();
        int home = 0;
        int away = 0;
        int sequence = 0;

        List<MatchEventEntity> orderedGoals = rawGoals
            .OrderBy(EventSortKey)
            .ThenBy(x => x.Id)
            .ToList();

        result.RawGoalIncidentCount = orderedGoals.Count;
        result.HasScoreSnapshots = orderedGoals.Any(x => x.HomeScore.HasValue && x.AwayScore.HasValue);

        foreach (MatchEventEntity goal in orderedGoals)
        {
            int minute = GoalMinuteForModel(goal);
            if (goal.HomeScore.HasValue && goal.AwayScore.HasValue)
            {
                int targetHome = goal.HomeScore.Value;
                int targetAway = goal.AwayScore.Value;
                int deltaHome = targetHome - home;
                int deltaAway = targetAway - away;

                if (deltaHome < 0 || deltaAway < 0 || deltaHome + deltaAway <= 0)
                {
                    result.ImpossibleScoreSnapshotCount++;
                    AddFallbackGoal(result, goal, minute, ref home, ref away, ref sequence);
                    continue;
                }

                if (deltaHome + deltaAway > 1)
                    result.ScoreJumpCount++;

                AddExpandedGoals(result, goal, minute, deltaHome, deltaAway, ref home, ref away, ref sequence);
                continue;
            }

            // Goal-like incidents without score snapshots are not trusted. Flashscore can emit
            // duplicate/noisy goal rows with no HomeScore/AwayScore; counting them creates
            // false reconstructed scores. Keep a diagnostic count, but do not add a fallback goal.
            result.MissingScoreSnapshotCount++;
        }

        result.FinalHomeFromEvents = home;
        result.FinalAwayFromEvents = away;
        result.FinalScoreMatchesMatch = match.HomeScoreCurrent == home && match.AwayScoreCurrent == away;
        return result;
    }

    private static void AddExpandedGoals(
        GoalEventReconstruction result,
        MatchEventEntity source,
        int minute,
        int deltaHome,
        int deltaAway,
        ref int home,
        ref int away,
        ref int sequence)
    {
        int total = deltaHome + deltaAway;
        bool preferHomeFirst = source.IsHome;

        while (deltaHome > 0 || deltaAway > 0)
        {
            bool addHome;
            if (deltaHome > 0 && deltaAway > 0)
            {
                addHome = preferHomeFirst;
                preferHomeFirst = !preferHomeFirst;
            }
            else
            {
                addHome = deltaHome > 0;
            }

            AddGoal(result, source, minute, addHome, total > 1, ref home, ref away, ref sequence);
            if (addHome)
                deltaHome--;
            else
                deltaAway--;
        }
    }

    private static void AddFallbackGoal(
        GoalEventReconstruction result,
        MatchEventEntity source,
        int minute,
        ref int home,
        ref int away,
        ref int sequence)
    {
        AddGoal(result, source, minute, source.IsHome, isExpandedFromScoreJump: false, ref home, ref away, ref sequence);
    }

    private static void AddGoal(
        GoalEventReconstruction result,
        MatchEventEntity source,
        int minute,
        bool isHomeGoal,
        bool isExpandedFromScoreJump,
        ref int home,
        ref int away,
        ref int sequence)
    {
        int beforeHome = home;
        int beforeAway = away;
        if (isHomeGoal)
            home++;
        else
            away++;

        result.Goals.Add(new ReconstructedGoalEvent
        {
            Source = source,
            Minute = minute,
            Sequence = sequence++,
            HomeBefore = beforeHome,
            AwayBefore = beforeAway,
            HomeAfter = home,
            AwayAfter = away,
            IsHomeGoal = isHomeGoal,
            IsExpandedFromScoreJump = isExpandedFromScoreJump
        });
    }

    public static int GoalMinuteForModel(MatchEventEntity e)
    {
        int minute = Math.Max(0, e.Minute);
        if (minute >= 90) return 90;
        if (minute >= 45 && e.AddedTime is > 0) return 45;
        return Math.Min(90, minute);
    }

    public static int EventSortKey(MatchEventEntity matchEvent)
    {
        // Use display-minute order, not real elapsed seconds. Flashscore first-half added time
        // like 45+6 must be before 46. Older imported rows stored TimeSeconds as (45+6)*60,
        // which can incorrectly move 45+6 after 51 and create false score jumps.
        int minute = Math.Max(0, matchEvent.Minute);
        int added = Math.Max(0, matchEvent.AddedTime.GetValueOrDefault());
        return minute * 100 + added;
    }
}
