using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class TeamVolumeFactorOptions
{
    public string League { get; set; } = string.Empty;
    public int SeasonId { get; set; }
    public int BeforeRound { get; set; }
    public long HomeTeamId { get; set; }
    public long AwayTeamId { get; set; }
    public int PriorStrengthMatches { get; set; } = 20;
}

public sealed class TeamVolumeFactorResult
{
    public double Factor { get; set; } = 1.0;
    public double HomeFactor { get; set; } = 1.0;
    public double AwayFactor { get; set; } = 1.0;
    public double LeagueGoalsPerMatch { get; set; }
    public double HomeGoalsPerMatch { get; set; }
    public double AwayGoalsPerMatch { get; set; }
    public int LeagueMatches { get; set; }
    public int HomeMatches { get; set; }
    public int AwayMatches { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}

public static class TeamVolumeFactorMath
{
    public static TeamVolumeFactorResult Calculate(
        int leagueGoals,
        int leagueMatches,
        int homeGoals,
        int homeMatches,
        int awayGoals,
        int awayMatches,
        int priorStrengthMatches)
    {
        if (priorStrengthMatches < 0)
            throw new ArgumentException("priorStrengthMatches must be >= 0.");

        var result = new TeamVolumeFactorResult
        {
            LeagueMatches = leagueMatches,
            HomeMatches = homeMatches,
            AwayMatches = awayMatches,
            LeagueGoalsPerMatch = leagueMatches > 0 ? leagueGoals / (double)leagueMatches : 0.0,
            HomeGoalsPerMatch = homeMatches > 0 ? homeGoals / (double)homeMatches : 0.0,
            AwayGoalsPerMatch = awayMatches > 0 ? awayGoals / (double)awayMatches : 0.0
        };

        if (leagueMatches <= 0 || result.LeagueGoalsPerMatch <= 0)
        {
            result.Warning = "No prior league matches; team volume factor defaults to 1.0.";
            return result;
        }

        result.HomeFactor = ShrunkTeamFactor(result.HomeGoalsPerMatch, homeMatches, result.LeagueGoalsPerMatch, priorStrengthMatches);
        result.AwayFactor = ShrunkTeamFactor(result.AwayGoalsPerMatch, awayMatches, result.LeagueGoalsPerMatch, priorStrengthMatches);
        result.Factor = Math.Sqrt(result.HomeFactor * result.AwayFactor);
        return result;
    }

    private static double ShrunkTeamFactor(double teamGoalsPerMatch, int teamMatches, double leagueGoalsPerMatch, int priorStrengthMatches)
    {
        if (teamMatches <= 0 || leagueGoalsPerMatch <= 0)
            return 1.0;

        double raw = teamGoalsPerMatch / leagueGoalsPerMatch;
        double denominator = priorStrengthMatches + teamMatches;
        if (denominator <= 0)
            return raw;

        return (priorStrengthMatches * 1.0 + teamMatches * raw) / denominator;
    }
}

public sealed class TeamVolumeFactorCalculator
{
    private readonly LiveTotalsDbContext _db;

    public TeamVolumeFactorCalculator(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public async Task<TeamVolumeFactorResult> CalculateAsync(TeamVolumeFactorOptions options, CancellationToken cancellationToken)
    {
        Validate(options);

        List<MatchEntity> matches = await BuildFinishedQuery(options.League)
            .Where(x => x.SofaScoreSeasonId == options.SeasonId && x.RoundNumber < options.BeforeRound)
            .ToListAsync(cancellationToken);

        int leagueGoals = matches.Sum(TotalGoals);
        int homeGoals = matches
            .Where(x => x.HomeTeamSofaScoreId == options.HomeTeamId || x.AwayTeamSofaScoreId == options.HomeTeamId)
            .Sum(TotalGoals);
        int awayGoals = matches
            .Where(x => x.HomeTeamSofaScoreId == options.AwayTeamId || x.AwayTeamSofaScoreId == options.AwayTeamId)
            .Sum(TotalGoals);
        int homeMatches = matches.Count(x => x.HomeTeamSofaScoreId == options.HomeTeamId || x.AwayTeamSofaScoreId == options.HomeTeamId);
        int awayMatches = matches.Count(x => x.HomeTeamSofaScoreId == options.AwayTeamId || x.AwayTeamSofaScoreId == options.AwayTeamId);

        TeamVolumeFactorResult result = TeamVolumeFactorMath.Calculate(
            leagueGoals,
            matches.Count,
            homeGoals,
            homeMatches,
            awayGoals,
            awayMatches,
            options.PriorStrengthMatches);
        result.Source = $"db-team-volume:season={options.SeasonId};before-round={options.BeforeRound};home={options.HomeTeamId};away={options.AwayTeamId};prior={options.PriorStrengthMatches}";
        return result;
    }

    private IQueryable<MatchEntity> BuildFinishedQuery(string league)
    {
        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking()
            .Where(x => x.StatusType.ToLower() == "finished" && x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue);

        if (!string.IsNullOrWhiteSpace(league))
        {
            string normalizedLeague = league.Trim().ToLower();
            query = query.Where(x => x.LeagueName.ToLower() == normalizedLeague || x.LeagueSlug.ToLower() == normalizedLeague);
        }

        return query;
    }

    private static int TotalGoals(MatchEntity match) => (match.HomeScoreCurrent ?? 0) + (match.AwayScoreCurrent ?? 0);

    private static void Validate(TeamVolumeFactorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.League))
            throw new ArgumentException("Team volume requires league.");
        if (options.SeasonId <= 0)
            throw new ArgumentException("Team volume requires season id.");
        if (options.BeforeRound <= 0)
            throw new ArgumentException("Team volume requires before round.");
        if (options.HomeTeamId <= 0 || options.AwayTeamId <= 0)
            throw new ArgumentException("Team volume requires positive home and away team ids.");
        if (options.PriorStrengthMatches < 0)
            throw new ArgumentException("Team volume prior strength matches must be >= 0.");
    }
}
