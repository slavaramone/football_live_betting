using System.Globalization;
using System.Text;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class WeibullDatasetOptions
{
    public string League { get; set; } = string.Empty;
    public int SeasonId { get; set; }
    public List<int> SeasonIds { get; } = [];
    public List<int> Rounds { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public int MaxModelMinute { get; set; } = 90;
    public bool IncludeUnreliableMatches { get; set; }
    public int MaxExamples { get; set; } = 20;
}

public sealed class WeibullDatasetResult
{
    public int MatchesChecked { get; set; }
    public int FinishedMatches { get; set; }
    public int ReliableFinishedMatches { get; set; }
    public int UnreliableFinishedMatches { get; set; }
    public int GoalRowsWritten { get; set; }
    public List<int> SeasonsIncluded { get; } = [];
    public string OutputPath { get; set; } = string.Empty;
    public List<string> Warnings { get; } = [];
}

public sealed class WeibullDatasetBuilder
{
    private readonly LiveTotalsDbContext _db;
    private readonly WeibullDatasetOptions _options;

    public WeibullDatasetBuilder(LiveTotalsDbContext db, WeibullDatasetOptions options)
    {
        _db = db;
        _options = options;
    }

    public async Task<WeibullDatasetResult> BuildAsync(CancellationToken cancellationToken)
    {
        var result = new WeibullDatasetResult { OutputPath = _options.OutputPath };

        IQueryable<MatchEntity> matchQuery = _db.Matches.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(_options.League))
            matchQuery = matchQuery.Where(x => x.LeagueName == _options.League || x.LeagueSlug == _options.League);

        List<int> seasonIds = GetSeasonIds(_options);
        if (seasonIds.Count > 0)
            matchQuery = matchQuery.Where(x => seasonIds.Contains(x.SofaScoreSeasonId));

        if (_options.Rounds.Count > 0)
            matchQuery = matchQuery.Where(x => _options.Rounds.Contains(x.RoundNumber));

        List<MatchEntity> matches = await matchQuery
            .OrderBy(x => x.RoundNumber)
            .ThenBy(x => x.StartTimeUtc)
            .ThenBy(x => x.SofaScoreEventId)
            .ToListAsync(cancellationToken);

        result.MatchesChecked = matches.Count;
        result.SeasonsIncluded.AddRange(matches
            .Select(x => x.SofaScoreSeasonId)
            .Distinct()
            .OrderBy(x => x));

        if (matches.Count == 0)
            result.Warnings.Add("No matches found for the provided filters.");

        HashSet<int> matchIds = matches.Select(x => x.Id).ToHashSet();

        List<MatchEventEntity> goals = await _db.MatchEvents.AsNoTracking()
            .Where(x => matchIds.Contains(x.MatchId) && x.IncidentType == "goal")
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.TimeSeconds ?? (x.Minute * 60))
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        Dictionary<int, List<MatchEventEntity>> goalsByMatch = goals
            .GroupBy(x => x.MatchId)
            .ToDictionary(x => x.Key, x => x.ToList());

        var rows = new List<WeibullGoalDatasetRow>();
        var unreliableExamples = new List<string>();

        foreach (MatchEntity match in matches)
        {
            if (!IsFinished(match))
                continue;

            result.FinishedMatches++;

            int finalHome = match.HomeScoreCurrent ?? 0;
            int finalAway = match.AwayScoreCurrent ?? 0;
            List<MatchEventEntity> matchGoals = goalsByMatch.GetValueOrDefault(match.Id) ?? [];
            int eventHomeGoals = matchGoals.Count(x => x.IsHome);
            int eventAwayGoals = matchGoals.Count(x => !x.IsHome);
            bool reliable = finalHome == eventHomeGoals && finalAway == eventAwayGoals;

            if (reliable)
            {
                result.ReliableFinishedMatches++;
            }
            else
            {
                result.UnreliableFinishedMatches++;
                if (unreliableExamples.Count < _options.MaxExamples)
                {
                    unreliableExamples.Add($"event {match.SofaScoreEventId} r{match.RoundNumber} {match.HomeTeamName} vs {match.AwayTeamName}: score {finalHome}-{finalAway}, goal events {eventHomeGoals}-{eventAwayGoals}");
                }

                if (!_options.IncludeUnreliableMatches)
                    continue;
            }

            int goalIndex = 0;
            foreach (MatchEventEntity goal in matchGoals)
            {
                goalIndex++;
                int rawMinute = goal.Minute;
                int modelMinute = ComputeModelMinute(goal, _options.MaxModelMinute);

                rows.Add(new WeibullGoalDatasetRow
                {
                    LeagueName = match.LeagueName,
                    LeagueSlug = match.LeagueSlug,
                    SofaScoreUniqueTournamentId = match.SofaScoreUniqueTournamentId,
                    SofaScoreSeasonId = match.SofaScoreSeasonId,
                    SeasonName = match.SeasonName,
                    SeasonYear = match.SeasonYear,
                    RoundNumber = match.RoundNumber,
                    MatchId = match.Id,
                    SofaScoreEventId = match.SofaScoreEventId,
                    StartTimeUtc = match.StartTimeUtc,
                    HomeTeamSofaScoreId = match.HomeTeamSofaScoreId,
                    HomeTeamName = match.HomeTeamName,
                    AwayTeamSofaScoreId = match.AwayTeamSofaScoreId,
                    AwayTeamName = match.AwayTeamName,
                    FinalHomeGoals = finalHome,
                    FinalAwayGoals = finalAway,
                    FinalTotalGoals = finalHome + finalAway,
                    GoalIndex = goalIndex,
                    GoalMinuteRaw = rawMinute,
                    GoalAddedTime = goal.AddedTime,
                    GoalTimeSeconds = goal.TimeSeconds,
                    GoalMinuteForModel = modelMinute,
                    IsHomeGoal = goal.IsHome,
                    HomeScoreAfterGoal = goal.HomeScore,
                    AwayScoreAfterGoal = goal.AwayScore,
                    IncidentClass = goal.IncidentClass,
                    PlayerName = goal.PlayerName,
                    AssistPlayerName = goal.AssistPlayerName,
                    IsReliableMatch = reliable
                });
            }
        }

        if (unreliableExamples.Count > 0)
        {
            result.Warnings.Add($"Excluded {result.UnreliableFinishedMatches} unreliable finished matches because score does not match goal events.");
            result.Warnings.AddRange(unreliableExamples);
        }

        string outputPath = ResolveOutputPath(_options.OutputPath, _options.League, GetSeasonIds(_options));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, ToCsv(rows), Encoding.UTF8, cancellationToken);

        result.GoalRowsWritten = rows.Count;
        result.OutputPath = outputPath;
        return result;
    }

    private static bool IsFinished(MatchEntity match)
    {
        return string.Equals(match.StatusType, "finished", StringComparison.OrdinalIgnoreCase)
            || string.Equals(match.StatusDescription, "Ended", StringComparison.OrdinalIgnoreCase)
            || string.Equals(match.StatusDescription, "Finished", StringComparison.OrdinalIgnoreCase);
    }

    private static int ComputeModelMinute(MatchEventEntity goal, int maxModelMinute)
    {
        int minute;
        if (goal.TimeSeconds is > 0)
            minute = Math.Max(1, (int)Math.Ceiling(goal.TimeSeconds.Value / 60.0));
        else
            minute = Math.Max(1, goal.Minute + Math.Max(0, goal.AddedTime ?? 0));

        if (maxModelMinute > 0)
            minute = Math.Min(minute, maxModelMinute);

        return minute;
    }

    private static List<int> GetSeasonIds(WeibullDatasetOptions options)
    {
        var seasonIds = options.SeasonIds
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();

        if (options.SeasonId > 0 && !seasonIds.Contains(options.SeasonId))
        {
            seasonIds.Add(options.SeasonId);
            seasonIds.Sort();
        }

        return seasonIds;
    }

    private static string ResolveOutputPath(string outputPath, string league, IReadOnlyCollection<int> seasonIds)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
            return outputPath;

        string leaguePart = string.IsNullOrWhiteSpace(league) ? "all-leagues" : SlugifySimple(league);
        string seasonPart = seasonIds.Count switch
        {
            0 => "all-seasons",
            1 => $"season-{seasonIds.First()}",
            _ => $"seasons-{seasonIds.Count}"
        };

        return Path.Combine("data", "weibull", $"{leaguePart}-{seasonPart}-goals.csv");
    }

    private static string SlugifySimple(string value)
    {
        var sb = new StringBuilder();
        foreach (char ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
                sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-');
        }

        return sb.ToString().Trim('-');
    }

    private static string ToCsv(List<WeibullGoalDatasetRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', Header));

        foreach (WeibullGoalDatasetRow row in rows)
        {
            string[] values =
            [
                row.LeagueName,
                row.LeagueSlug,
                row.SofaScoreUniqueTournamentId.ToString(CultureInfo.InvariantCulture),
                row.SofaScoreSeasonId.ToString(CultureInfo.InvariantCulture),
                row.SeasonName,
                row.SeasonYear,
                row.RoundNumber.ToString(CultureInfo.InvariantCulture),
                row.MatchId.ToString(CultureInfo.InvariantCulture),
                row.SofaScoreEventId.ToString(CultureInfo.InvariantCulture),
                row.StartTimeUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
                row.HomeTeamSofaScoreId.ToString(CultureInfo.InvariantCulture),
                row.HomeTeamName,
                row.AwayTeamSofaScoreId.ToString(CultureInfo.InvariantCulture),
                row.AwayTeamName,
                row.FinalHomeGoals.ToString(CultureInfo.InvariantCulture),
                row.FinalAwayGoals.ToString(CultureInfo.InvariantCulture),
                row.FinalTotalGoals.ToString(CultureInfo.InvariantCulture),
                row.GoalIndex.ToString(CultureInfo.InvariantCulture),
                row.GoalMinuteRaw.ToString(CultureInfo.InvariantCulture),
                row.GoalAddedTime?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.GoalTimeSeconds?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.GoalMinuteForModel.ToString(CultureInfo.InvariantCulture),
                row.IsHomeGoal ? "1" : "0",
                row.HomeScoreAfterGoal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.AwayScoreAfterGoal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.IncidentClass,
                row.PlayerName,
                row.AssistPlayerName,
                row.IsReliableMatch ? "1" : "0"
            ];

            sb.AppendLine(string.Join(',', values.Select(EscapeCsv)));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";

        return value;
    }

    private static readonly string[] Header =
    [
        "LeagueName",
        "LeagueSlug",
        "SofaScoreUniqueTournamentId",
        "SofaScoreSeasonId",
        "SeasonName",
        "SeasonYear",
        "RoundNumber",
        "MatchId",
        "SofaScoreEventId",
        "StartTimeUtc",
        "HomeTeamSofaScoreId",
        "HomeTeamName",
        "AwayTeamSofaScoreId",
        "AwayTeamName",
        "FinalHomeGoals",
        "FinalAwayGoals",
        "FinalTotalGoals",
        "GoalIndex",
        "GoalMinuteRaw",
        "GoalAddedTime",
        "GoalTimeSeconds",
        "GoalMinuteForModel",
        "IsHomeGoal",
        "HomeScoreAfterGoal",
        "AwayScoreAfterGoal",
        "IncidentClass",
        "PlayerName",
        "AssistPlayerName",
        "IsReliableMatch"
    ];
}

internal sealed class WeibullGoalDatasetRow
{
    public string LeagueName { get; set; } = string.Empty;
    public string LeagueSlug { get; set; } = string.Empty;
    public int SofaScoreUniqueTournamentId { get; set; }
    public int SofaScoreSeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public int RoundNumber { get; set; }
    public int MatchId { get; set; }
    public long SofaScoreEventId { get; set; }
    public DateTimeOffset? StartTimeUtc { get; set; }
    public long HomeTeamSofaScoreId { get; set; }
    public string HomeTeamName { get; set; } = string.Empty;
    public long AwayTeamSofaScoreId { get; set; }
    public string AwayTeamName { get; set; } = string.Empty;
    public int FinalHomeGoals { get; set; }
    public int FinalAwayGoals { get; set; }
    public int FinalTotalGoals { get; set; }
    public int GoalIndex { get; set; }
    public int GoalMinuteRaw { get; set; }
    public int? GoalAddedTime { get; set; }
    public int? GoalTimeSeconds { get; set; }
    public int GoalMinuteForModel { get; set; }
    public bool IsHomeGoal { get; set; }
    public int? HomeScoreAfterGoal { get; set; }
    public int? AwayScoreAfterGoal { get; set; }
    public string IncidentClass { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string AssistPlayerName { get; set; } = string.Empty;
    public bool IsReliableMatch { get; set; }
}
