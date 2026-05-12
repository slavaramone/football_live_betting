using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.Infrastructure;

public sealed class SampleMatchRepository : IMatchRepository
{
    public IReadOnlyList<MatchSnapshot> GetLiveMatches(string league)
    {
        return
        [
            new() { League = league, HomeTeam = "Manual Home A", AwayTeam = "Manual Away A", Minute = 60, HomeGoals = 1, AwayGoals = 0, BestSignal = "Prepare check", BestEdgePercent = 0.0 },
            new() { League = league, HomeTeam = "Manual Home B", AwayTeam = "Manual Away B", Minute = 65, HomeGoals = 0, AwayGoals = 0, BestSignal = "Prepare check", BestEdgePercent = 0.0 },
            new() { League = league, HomeTeam = "Manual Home C", AwayTeam = "Manual Away C", Minute = 75, HomeGoals = 1, AwayGoals = 1, BestSignal = "Prepare check", BestEdgePercent = 0.0 },
            new() { League = league, HomeTeam = "Manual Home D", AwayTeam = "Manual Away D", Minute = 55, HomeGoals = 2, AwayGoals = 0, BestSignal = "Prepare check", BestEdgePercent = 0.0 }
        ];
    }
}
