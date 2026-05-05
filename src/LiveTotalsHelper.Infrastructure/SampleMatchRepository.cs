using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.Infrastructure;

public sealed class SampleMatchRepository : IMatchRepository
{
    public IReadOnlyList<MatchSnapshot> GetLiveMatches(string league)
    {
        return
        [
            new() { League = league, HomeTeam = "Brisbane City U23", AwayTeam = "SWQ Thunder", Minute = 42, HomeGoals = 1, AwayGoals = 0, BestSignal = "Over 2.0", BestEdgePercent = 3.8 },
            new() { League = league, HomeTeam = "Moreton Bay Utd", AwayTeam = "Gold Coast Knights", Minute = 39, HomeGoals = 0, AwayGoals = 0, BestSignal = "Over 1.5", BestEdgePercent = 2.6 },
            new() { League = league, HomeTeam = "Rochedale Rovers", AwayTeam = "Eastern Suburbs", Minute = 45, HomeGoals = 1, AwayGoals = 1, BestSignal = "No bet", BestEdgePercent = 0.0 },
            new() { League = league, HomeTeam = "Sunshine Coast FC", AwayTeam = "Redlands Utd", Minute = 34, HomeGoals = 0, AwayGoals = 1, BestSignal = "Over 2.5", BestEdgePercent = 2.3 }
        ];
    }
}
