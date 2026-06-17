using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class SeasonVolumeFactorOptions
{
    public string League { get; set; } = string.Empty;
    public List<int> BaseSeasonIds { get; } = [];
    public int CurrentSeasonId { get; set; }
    public int BeforeRound { get; set; }
    public int PriorStrengthMatches { get; set; } = 100;
}

public sealed class SeasonVolumeFactorResult
{
    public double Factor { get; set; } = 1.0;
    public double RawFactor { get; set; } = 1.0;
    public double Weight { get; set; }
    public double BaseGoalsPerMatch { get; set; }
    public double CurrentGoalsPerMatch { get; set; }
    public int BaseMatches { get; set; }
    public int CurrentMatches { get; set; }
    public int BaseGoals { get; set; }
    public int CurrentGoals { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Warning { get; set; } = string.Empty;
}

public sealed class SeasonVolumeFactorCalculator
{
    private readonly LiveTotalsDbContext _db;

    public SeasonVolumeFactorCalculator(LiveTotalsDbContext db)
    {
        _db = db;
    }

    public async Task<SeasonVolumeFactorResult> CalculateAsync(SeasonVolumeFactorOptions options, CancellationToken cancellationToken)
    {
        Validate(options);

        List<MatchEntity> baseMatches = await BuildFinishedQuery(options.League)
            .Where(x => options.BaseSeasonIds.Contains(x.SeasonId))
            .ToListAsync(cancellationToken);

        List<MatchEntity> currentMatches = await BuildFinishedQuery(options.League)
            .Where(x => x.SeasonId == options.CurrentSeasonId && x.RoundNumber < options.BeforeRound)
            .ToListAsync(cancellationToken);

        var result = new SeasonVolumeFactorResult
        {
            BaseMatches = baseMatches.Count,
            CurrentMatches = currentMatches.Count,
            BaseGoals = baseMatches.Sum(TotalGoals),
            CurrentGoals = currentMatches.Sum(TotalGoals),
            Source = $"db-season-volume:base={string.Join(',', options.BaseSeasonIds.OrderBy(x => x))};current={options.CurrentSeasonId};before-round={options.BeforeRound};prior={options.PriorStrengthMatches}"
        };

        SeasonVolumeFactorMathResult math = SeasonVolumeFactorMath.Calculate(new SeasonVolumeFactorMathInput
        {
            BaseGoals = result.BaseGoals,
            BaseMatches = result.BaseMatches,
            CurrentGoals = result.CurrentGoals,
            CurrentMatches = result.CurrentMatches,
            PriorStrengthMatches = options.PriorStrengthMatches
        });

        result.BaseGoalsPerMatch = math.BaseGoalsPerMatch;
        result.CurrentGoalsPerMatch = math.CurrentGoalsPerMatch;
        result.RawFactor = math.RawFactor;
        result.Weight = math.Weight;
        result.Factor = math.Factor;
        result.Warning = math.Warning;
        return result;
    }

    private IQueryable<MatchEntity> BuildFinishedQuery(string league)
    {
        IQueryable<MatchEntity> query = _db.Matches.AsNoTracking()
            .Where(x => x.StatusType.ToLower() == "finished" && x.HomeScoreCurrent.HasValue && x.AwayScoreCurrent.HasValue);

        if (!string.IsNullOrWhiteSpace(league))
        {
            string normalizedLeague = league.Trim().ToLower();
            query = query.Where(x => x.LeagueName.ToLower() == normalizedLeague);
        }

        return query;
    }

    private static int TotalGoals(MatchEntity match) => (match.HomeScoreCurrent ?? 0) + (match.AwayScoreCurrent ?? 0);

    private static void Validate(SeasonVolumeFactorOptions options)
    {
        if (options.BaseSeasonIds.Count == 0)
            throw new ArgumentException("Provide --base-season-ids when --use-current-season-volume is true.");
        if (options.CurrentSeasonId <= 0)
            throw new ArgumentException("Provide --current-season-id when --use-current-season-volume is true.");
        if (options.BeforeRound <= 0)
            throw new ArgumentException("Provide --before-round greater than 0 when --use-current-season-volume is true.");
        if (options.PriorStrengthMatches < 0)
            throw new ArgumentException("--prior-strength-matches must be zero or greater.");
    }
}
