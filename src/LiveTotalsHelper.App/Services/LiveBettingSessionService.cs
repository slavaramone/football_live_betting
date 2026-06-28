using System.Globalization;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Tools;

namespace LiveTotalsHelper.App.Services;

public sealed class LiveBettingSessionService : ILiveBettingSessionService
{
    private const string ModelRemovedMessage = "Old live-total model removed; Avalonia shell is waiting for the redesigned model.";
    private readonly IReadOnlyList<LeagueProfile> _toolProfiles;
    private readonly string _logsFolder;

    public LiveBettingSessionService(LiveTotalsDbContext db, IEnumerable<LeagueProfile> toolProfiles, string logsFolder)
    {
        _ = db;
        _toolProfiles = toolProfiles.ToList();
        _logsFolder = string.IsNullOrWhiteSpace(logsFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LiveTotalsHelper")
            : logsFolder;
    }

    public IReadOnlyList<LiveBettingProfile> GetProfiles()
    {
        return _toolProfiles
            .Select(profile => new LiveBettingProfile
            {
                Key = profile.Key,
                DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.League : profile.Name,
                RiskLevel = string.IsNullOrWhiteSpace(profile.RiskLevel) ? "Model disabled" : profile.RiskLevel,
                AllowFixedMinuteBetting = false,
                AllowAfterGoalBetting = false,
                AllowAfterRedCardBetting = false,
                UseCurrentSeasonVolume = profile.UseCurrentSeasonVolume,
                DefaultBeforeRound = profile.DefaultBeforeRound,
                EdgeThreshold = profile.EdgeThreshold,
                UseProbabilityMoveFilter = profile.UseProbabilityMoveFilter,
                DecisionMode = "ModelDisabled",
                MinMinute = profile.MinMinute,
                RequireGoalTrigger = profile.RequireGoalTrigger,
                MinLine = profile.MinLine,
                TargetLines = profile.TargetLines,
                AllowedLines = profile.AllowedLines,
                FallbackBettingEnabled = false,
                LiveBettingRulesCount = profile.LiveBettingRules.Count,
                Notes = string.IsNullOrWhiteSpace(profile.Notes) ? ModelRemovedMessage : profile.Notes
            })
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    public LiveBettingProfile? FindProfileByLeague(string league)
    {
        return GetProfiles().FirstOrDefault(profile =>
            profile.Key.Equals(league, StringComparison.OrdinalIgnoreCase) ||
            profile.DisplayName.Equals(league, StringComparison.OrdinalIgnoreCase));
    }

    public Task<LiveBettingCheckResult> BuildCheckAsync(LiveBettingCheckInput input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        List<double> targetLines = ParseLines(input.TargetLinesText);
        if (targetLines.Count == 0)
            targetLines = [input.LiveOddsLine > 0 ? input.LiveOddsLine : input.StartingLine];

        var decisions = new List<LiveBettingDecisionRow>();
        foreach (double line in targetLines.Distinct().OrderBy(x => x))
        {
            double? overOdds = ResolveOddsForLine(line, input.LiveOverOddsText, input.LiveOverOdds, input.LiveOddsLine);
            double? underOdds = ResolveOddsForLine(line, input.LiveUnderOddsText, input.LiveUnderOdds, input.LiveOddsLine);

            if (overOdds.HasValue)
            {
                decisions.Add(new LiveBettingDecisionRow
                {
                    Line = line,
                    Side = "OVER",
                    BookOdds = overOdds.Value,
                    Decision = "MODEL DISABLED",
                    Reason = ModelRemovedMessage
                });
            }

            if (underOdds.HasValue)
            {
                decisions.Add(new LiveBettingDecisionRow
                {
                    Line = line,
                    Side = "UNDER",
                    BookOdds = underOdds.Value,
                    Decision = "MODEL DISABLED",
                    Reason = ModelRemovedMessage
                });
            }
        }

        if (decisions.Count == 0)
        {
            decisions.Add(new LiveBettingDecisionRow
            {
                Line = input.LiveOddsLine > 0 ? input.LiveOddsLine : input.StartingLine,
                Side = "OVER",
                Decision = "NO ODDS",
                Reason = "Enter live odds after the redesigned model is connected."
            });
        }

        var result = new LiveBettingCheckResult
        {
            CheckedAt = DateTimeOffset.Now,
            IsBettingAllowed = false,
            Status = "MODEL DISABLED",
            Warnings = ModelRemovedMessage,
            ModelSummary = ModelRemovedMessage,
            DecisionRulesSummary = "No betting decisions are produced until the redesigned model is implemented.",
            RemainingXg = 0,
            StateCorrectionFactor = 1,
            StateCorrectionSource = "disabled",
            StateCorrectionSupported = false,
            VolumeFactor = 1,
            VolumeFactorSource = "disabled",
            Decisions = decisions
        };

        return Task.FromResult(result);
    }

    public string AppendPaperLog(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        string path = Path.Combine(_logsFolder, "paper-log.csv");
        EnsureLogHeader(path, "CheckedAt,Profile,Match,Trigger,Minute,Score,Status,Warnings,Line,Side,BookOdds,Decision,DecisionReason");

        using var writer = new StreamWriter(path, append: true);
        foreach (LiveBettingDecisionRow decision in result.Decisions.DefaultIfEmpty(new LiveBettingDecisionRow()))
        {
            writer.WriteLine(string.Join(",",
                Csv(result.CheckedAt.ToString("O", CultureInfo.InvariantCulture)),
                Csv(input.ProfileKey),
                Csv(input.MatchName),
                Csv(input.StateTrigger),
                input.Minute.ToString(CultureInfo.InvariantCulture),
                Csv($"{input.HomeGoals}-{input.AwayGoals}"),
                Csv(result.Status),
                Csv(result.Warnings),
                decision.Line.ToString("0.##", CultureInfo.InvariantCulture),
                Csv(decision.Side),
                decision.BookOdds?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty,
                Csv(decision.Decision),
                Csv(decision.Reason)));
        }

        return path;
    }

    public string LogBet(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        string path = Path.Combine(_logsFolder, "bets-log.csv");
        EnsureLogHeader(path, "BetLoggedAt,Mode,Profile,Match,Trigger,Minute,Score,Line,Side,BookOdds,Stake,Decision,DecisionReason,Notes");

        LiveBettingDecisionRow? row = result.Decisions.FirstOrDefault(x =>
            Math.Abs(x.Line - input.SelectedBetLine) < 0.0001 &&
            x.Side.Equals(input.SelectedBetSide, StringComparison.OrdinalIgnoreCase));

        using var writer = new StreamWriter(path, append: true);
        writer.WriteLine(string.Join(",",
            Csv(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)),
            Csv(input.BetMode),
            Csv(input.ProfileKey),
            Csv(input.MatchName),
            Csv(input.StateTrigger),
            input.Minute.ToString(CultureInfo.InvariantCulture),
            Csv($"{input.HomeGoals}-{input.AwayGoals}"),
            input.SelectedBetLine.ToString("0.##", CultureInfo.InvariantCulture),
            Csv(input.SelectedBetSide),
            input.SelectedBetOdds.ToString("0.###", CultureInfo.InvariantCulture),
            input.Stake.ToString("0.##", CultureInfo.InvariantCulture),
            Csv(row?.Decision ?? "MODEL DISABLED"),
            Csv(row?.Reason ?? ModelRemovedMessage),
            Csv(input.BetNotes)));

        return path;
    }

    private static List<double> ParseLines(string value)
    {
        var lines = new List<double>();
        foreach (string token in (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string normalized = token;
            int equals = normalized.IndexOf('=');
            if (equals >= 0)
                normalized = normalized[..equals];

            if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double line) && line > 0)
                lines.Add(line);
        }

        return lines;
    }

    private static double? ResolveOddsForLine(double line, string oddsText, double fallbackOdds, double fallbackLine)
    {
        foreach (string token in (oddsText ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = token.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2)
                continue;

            if (double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLine) &&
                Math.Abs(parsedLine - line) < 0.0001 &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedOdds) &&
                parsedOdds > 1)
                return parsedOdds;
        }

        if (Math.Abs(fallbackLine - line) < 0.0001 && fallbackOdds > 1)
            return fallbackOdds;

        return null;
    }

    private static void EnsureLogHeader(string path, string header)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        if (!File.Exists(path))
            File.WriteAllText(path, header + Environment.NewLine);
    }

    private static string Csv(string value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
