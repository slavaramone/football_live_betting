using System.Globalization;
using System.Text;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Tools;

namespace LiveTotalsHelper.App.Services;

public sealed class LiveBettingSessionService : ILiveBettingSessionService
{
    private readonly LiveTotalsDbContext _db;
    private readonly string _logsFolder;
    private readonly List<LiveBettingProfile> _profiles;
    private readonly Dictionary<string, LeagueProfile> _toolProfiles;

    public LiveBettingSessionService(LiveTotalsDbContext db, IEnumerable<LeagueProfile> toolProfiles, string logsFolder)
    {
        _db = db;
        _logsFolder = string.IsNullOrWhiteSpace(logsFolder)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "LiveTotalsHelper")
            : logsFolder;

        _toolProfiles = toolProfiles
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);

        _profiles = _toolProfiles.Values
            .Select(ToLiveProfile)
            .OrderBy(x => x.DisplayName)
            .ToList();
    }

    public IReadOnlyList<LiveBettingProfile> GetProfiles() => _profiles;

    public LiveBettingProfile? FindProfileByLeague(string league)
    {
        string requestedKey = NormalizeLeagueKey(league);

        return _profiles.FirstOrDefault(x =>
            NormalizeLeagueKey(x.DisplayName) == requestedKey ||
            (_toolProfiles.TryGetValue(x.Key, out LeagueProfile? profile) &&
             (NormalizeLeagueKey(profile.League) == requestedKey ||
              requestedKey.EndsWith(NormalizeLeagueKey(profile.League), StringComparison.OrdinalIgnoreCase))));
    }

    public async Task<LiveBettingCheckResult> BuildCheckAsync(LiveBettingCheckInput input, CancellationToken cancellationToken = default)
    {
        LiveBettingProfile profile = _profiles.FirstOrDefault(x => x.Key.Equals(input.ProfileKey, StringComparison.OrdinalIgnoreCase))
            ?? _profiles.FirstOrDefault()
            ?? throw new InvalidOperationException("No live betting profiles are loaded.");

        LeagueProfile toolProfile = _toolProfiles.TryGetValue(profile.Key, out LeagueProfile? found)
            ? found
            : throw new InvalidOperationException($"Tool profile '{profile.Key}' was not found.");

        string trigger = NormalizeTrigger(input.StateTrigger);
        var warnings = new List<string>();
        bool allowed = true;
        string status = "READY";

        if (input.HomeRedCards + input.AwayRedCards > 0 || trigger == "after-red-card")
        {
            allowed = false;
            status = "NO BET - red card/manual review";
            warnings.Add("Red-card states are manual review/no-bet in current paper-test rules.");
        }
        else if (trigger == "after-goal" && !profile.AllowAfterGoalBetting)
        {
            allowed = false;
            status = "LOG ONLY - after-goal not enabled for this profile";
            warnings.Add("This profile is configured to log AfterGoal only.");
        }
        else if (trigger == "fixed-minute" && !profile.AllowFixedMinuteBetting)
        {
            allowed = false;
            status = "NO BET - fixed-minute disabled for this profile";
        }
        else if (trigger == "fixed-minute" && input.LastGoalMinute >= 0 && input.Minute - input.LastGoalMinute <= input.RecentGoalMinutes)
        {
            allowed = false;
            status = "WAIT - recent goal";
            warnings.Add($"Fixed-minute check is within {input.RecentGoalMinutes} minutes after a goal.");
        }
        else if (!IsCheckMinuteAllowed(trigger, input.Minute))
        {
            allowed = false;
            status = "NO BET - outside check window";
            warnings.Add("Use fixed minutes or immediate after-event checks only.");
        }

        Dictionary<double, double> overOdds = ParseOddsMap(input.LiveOverOddsText, warnings, "Over");
        Dictionary<double, double> underOdds = ParseOddsMap(input.LiveUnderOddsText, warnings, "Under");
        foreach (string warning in ValidateMonotonicOdds(overOdds, underOdds))
            warnings.Add(warning);

        try
        {
            LiveTotalPriceOptions priceOptions = BuildPriceOptions(input, toolProfile, trigger, overOdds, underOdds);
            await ApplySeasonVolumeAsync(priceOptions, toolProfile, input, warnings, cancellationToken);

            var pricer = new LiveTotalPricer(priceOptions);
            LiveTotalPriceResult priced = await pricer.PriceAsync(cancellationToken);

            IReadOnlyList<LiveBettingDecisionRow> decisions = priced.Lines
                .SelectMany(line => ToDecisionRows(line, allowed, status))
                .ToList();

            string finalStatus = allowed
                ? ResolveStatusFromPricedResult(priced, decisions)
                : status;

            foreach (string warning in priced.Warnings)
                warnings.Add(warning);

            return new LiveBettingCheckResult
            {
                IsBettingAllowed = allowed && priced.StateCorrectionSupported,
                Status = finalStatus,
                Warnings = string.Join(Environment.NewLine, warnings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()),
                ModelSummary = $"Remaining xG {priced.RemainingXg:0.###}; timing {priced.TimingRemainingShare:P1}; state {priced.StateCorrectionFactor:0.###}; volume {priced.VolumeFactor:0.###}",
                RemainingXg = priced.RemainingXg,
                StateCorrectionFactor = priced.StateCorrectionFactor,
                StateCorrectionSource = priced.StateCorrectionSource,
                StateCorrectionSupported = priced.StateCorrectionSupported,
                VolumeFactor = priced.VolumeFactor,
                VolumeFactorSource = priced.VolumeFactorSource,
                Decisions = decisions
            };
        }
        catch (Exception ex)
        {
            warnings.Add(ex.Message);
            return new LiveBettingCheckResult
            {
                IsBettingAllowed = false,
                Status = "ERROR",
                Warnings = string.Join(Environment.NewLine, warnings.Distinct()),
                Decisions = BuildGateOnlyRows(overOdds, underOdds, "ERROR", ex.Message)
            };
        }
    }

    public string AppendPaperLog(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        Directory.CreateDirectory(_logsFolder);
        string path = Path.Combine(_logsFolder, "paper-state-log.csv");

        bool writeHeader = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true, Encoding.UTF8);

        if (writeHeader)
        {
            writer.WriteLine("CheckedAt,Profile,Match,Trigger,Minute,Score,StartingLine,StartingOver,StartingUnder,LiveOverOdds,LiveUnderOdds,Status,Warnings,RemainingXg,StateCorrectionSupported,StateCorrectionSource,VolumeFactor,VolumeFactorSource,Line,Side,BookOdds,ModelProbability,FairOdds,Edge,Decision,Reason");
        }

        foreach (LiveBettingDecisionRow decision in result.Decisions.DefaultIfEmpty(new LiveBettingDecisionRow()))
        {
            writer.WriteLine(string.Join(',',
                Csv(result.CheckedAt.ToString("O", CultureInfo.InvariantCulture)),
                Csv(input.ProfileKey),
                Csv(input.MatchName),
                Csv(input.StateTrigger),
                input.Minute.ToString(CultureInfo.InvariantCulture),
                Csv($"{input.HomeGoals}-{input.AwayGoals}"),
                D(input.StartingLine),
                D(input.StartingOverOdds),
                D(input.StartingUnderOdds),
                Csv(input.LiveOverOddsText),
                Csv(input.LiveUnderOddsText),
                Csv(result.Status),
                Csv(result.Warnings),
                D(result.RemainingXg),
                result.StateCorrectionSupported ? "1" : "0",
                Csv(result.StateCorrectionSource),
                D(result.VolumeFactor),
                Csv(result.VolumeFactorSource),
                D(decision.Line),
                Csv(decision.Side),
                decision.BookOdds.HasValue ? D(decision.BookOdds.Value) : "",
                decision.ModelProbability.HasValue ? D(decision.ModelProbability.Value) : "",
                decision.FairOdds.HasValue ? D(decision.FairOdds.Value) : "",
                decision.Edge.HasValue ? D(decision.Edge.Value) : "",
                Csv(decision.Decision),
                Csv(decision.Reason)));
        }

        return path;
    }

    public string LogBet(LiveBettingCheckInput input, LiveBettingCheckResult result)
    {
        Directory.CreateDirectory(_logsFolder);
        string path = Path.Combine(_logsFolder, "bets-log.csv");

        bool writeHeader = !File.Exists(path);
        using var writer = new StreamWriter(path, append: true, Encoding.UTF8);

        if (writeHeader)
        {
            writer.WriteLine("BetLoggedAt,Mode,Profile,Match,Trigger,Minute,Score,Line,Side,BookOdds,Stake,ModelProbability,FairOdds,Edge,Decision,RemainingXg,Notes");
        }

        double.TryParse(input.SelectedBetLineText, NumberStyles.Float, CultureInfo.InvariantCulture, out double selectedLine);
        string selectedSide = (input.SelectedBetSide ?? string.Empty).Trim().ToUpperInvariant();

        LiveBettingDecisionRow? row = result.Decisions.FirstOrDefault(x =>
            Math.Abs(x.Line - selectedLine) < 0.0001 &&
            x.Side.Equals(selectedSide, StringComparison.OrdinalIgnoreCase));

        writer.WriteLine(string.Join(',',
            Csv(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture)),
            Csv(input.BetMode),
            Csv(input.ProfileKey),
            Csv(input.MatchName),
            Csv(input.StateTrigger),
            input.Minute.ToString(CultureInfo.InvariantCulture),
            Csv($"{input.HomeGoals}-{input.AwayGoals}"),
            D(selectedLine),
            Csv(selectedSide),
            D(input.SelectedBetOdds),
            D(input.Stake),
            row?.ModelProbability.HasValue == true ? D(row.ModelProbability.Value) : "",
            row?.FairOdds.HasValue == true ? D(row.FairOdds.Value) : "",
            row?.Edge.HasValue == true ? D(row.Edge.Value) : "",
            Csv(row?.Decision ?? "MANUAL"),
            D(result.RemainingXg),
            Csv(input.BetNotes)));

        return path;
    }

    private static LiveBettingProfile ToLiveProfile(LeagueProfile profile)
    {
        bool isLatvia = profile.Key.Contains("latvia", StringComparison.OrdinalIgnoreCase);
        return new LiveBettingProfile
        {
            Key = profile.Key,
            DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.League : profile.Name,
            RiskLevel = profile.RiskLevel,
            AllowFixedMinuteBetting = true,
            AllowAfterGoalBetting = !isLatvia,
            AllowAfterRedCardBetting = false,
            UseCurrentSeasonVolume = profile.UseCurrentSeasonVolume,
            DefaultBeforeRound = profile.DefaultBeforeRound,
            EdgeThreshold = profile.EdgeThreshold,
            Notes = profile.Notes
        };
    }

    private static LiveTotalPriceOptions BuildPriceOptions(
        LiveBettingCheckInput input,
        LeagueProfile profile,
        string trigger,
        IReadOnlyDictionary<double, double> overOdds,
        IReadOnlyDictionary<double, double> underOdds)
    {
        var options = new LiveTotalPriceOptions
        {
            ModelPath = profile.ModelPath,
            StateCorrectionPath = profile.StateCorrectionPath,
            StateTrigger = trigger,
            StartingLine = input.StartingLine,
            StartingOverOdds = input.StartingOverOdds,
            StartingUnderOdds = input.StartingUnderOdds,
            Minute = input.Minute,
            HomeGoals = input.HomeGoals,
            AwayGoals = input.AwayGoals,
            EmpiricalWeight = profile.DefaultEmpiricalWeight,
            EdgeThreshold = profile.EdgeThreshold,
            HomeRedCards = input.HomeRedCards,
            AwayRedCards = input.AwayRedCards,
            LastGoalMinute = input.LastGoalMinute,
            RecentGoalMinutes = input.RecentGoalMinutes,
            VolumeFactor = 1.0,
            VolumeFactorSource = "none/default 1.0"
        };

        options.TargetLines.Clear();
        foreach (double line in ParseLines(input.TargetLinesText, profile.TargetLines.Count > 0 ? profile.TargetLines : overOdds.Keys.Concat(underOdds.Keys)))
            options.TargetLines.Add(line);

        foreach ((double line, double odds) in overOdds)
            options.LiveOverOddsByLine[line] = odds;
        foreach ((double line, double odds) in underOdds)
            options.LiveUnderOddsByLine[line] = odds;

        return options;
    }

    private async Task ApplySeasonVolumeAsync(
        LiveTotalPriceOptions priceOptions,
        LeagueProfile profile,
        LiveBettingCheckInput input,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!profile.UseCurrentSeasonVolume)
            return;

        int beforeRound = input.BeforeRound ?? profile.DefaultBeforeRound ?? 0;
        if (profile.CurrentSeasonId <= 0 || profile.BaseSeasonIds.Count == 0 || beforeRound <= 0)
        {
            warnings.Add("Current-season volume skipped: profile needs currentSeasonId/baseSeasonIds and check needs beforeRound.");
            return;
        }

        try
        {
            var calculator = new SeasonVolumeFactorCalculator(_db);
            var options = new SeasonVolumeFactorOptions
            {
                League = profile.League,
                CurrentSeasonId = profile.CurrentSeasonId,
                BeforeRound = beforeRound,
                PriorStrengthMatches = profile.PriorStrengthMatches
            };
            foreach (int seasonId in profile.BaseSeasonIds)
                options.BaseSeasonIds.Add(seasonId);

            SeasonVolumeFactorResult volume = await calculator.CalculateAsync(options, cancellationToken);
            priceOptions.VolumeFactor = volume.Factor;
            priceOptions.VolumeFactorSource = volume.Source;
            if (!string.IsNullOrWhiteSpace(volume.Warning))
                warnings.Add(volume.Warning);
        }
        catch (Exception ex)
        {
            warnings.Add($"Current-season volume skipped: {ex.Message}");
        }
    }

    private static IReadOnlyList<LiveBettingDecisionRow> ToDecisionRows(LiveTotalLinePrice line, bool allowed, string blockedStatus)
    {
        var rows = new List<LiveBettingDecisionRow>();

        rows.Add(new LiveBettingDecisionRow
        {
            Line = line.Line,
            Side = "OVER",
            BookOdds = line.BookOverOdds,
            ModelProbability = line.WinProbability,
            FairOdds = line.FairOdds,
            Edge = line.OverEdge,
            Decision = allowed ? line.OverDecision : blockedStatus,
            Reason = allowed ? line.Decision : "Rule gate blocked betting before model edge decision."
        });

        rows.Add(new LiveBettingDecisionRow
        {
            Line = line.Line,
            Side = "UNDER",
            BookOdds = line.BookUnderOdds,
            ModelProbability = line.UnderWinProbability,
            FairOdds = line.FairUnderOdds,
            Edge = line.UnderEdge,
            Decision = allowed ? line.UnderDecision : blockedStatus,
            Reason = allowed ? line.Decision : "Rule gate blocked betting before model edge decision."
        });

        return rows;
    }

    private static IReadOnlyList<LiveBettingDecisionRow> BuildGateOnlyRows(
        IReadOnlyDictionary<double, double> overOdds,
        IReadOnlyDictionary<double, double> underOdds,
        string decision,
        string reason)
    {
        var rows = new List<LiveBettingDecisionRow>();
        foreach ((double line, double odds) in overOdds)
            rows.Add(new LiveBettingDecisionRow { Line = line, Side = "OVER", BookOdds = odds, Decision = decision, Reason = reason });
        foreach ((double line, double odds) in underOdds)
            rows.Add(new LiveBettingDecisionRow { Line = line, Side = "UNDER", BookOdds = odds, Decision = decision, Reason = reason });
        return rows.OrderBy(x => x.Line).ThenBy(x => x.Side).ToList();
    }

    private static string ResolveStatusFromPricedResult(LiveTotalPriceResult priced, IReadOnlyList<LiveBettingDecisionRow> decisions)
    {
        if (!priced.StateCorrectionSupported)
            return "NO BET - unsupported sparse state bucket";
        if (decisions.Any(x => x.Decision.StartsWith("BET", StringComparison.OrdinalIgnoreCase)))
            return "BET CANDIDATE";
        if (decisions.Any(x => x.Decision.StartsWith("LEAN", StringComparison.OrdinalIgnoreCase)))
            return "LEAN ONLY";
        return "NO BET";
    }

    private static bool IsCheckMinuteAllowed(string trigger, int minute)
    {
        if (trigger == "after-goal" || trigger == "after-red-card")
            return minute is >= 1 and <= 90;

        int[] fixedMinutes = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
        return fixedMinutes.Contains(minute);
    }

    private static Dictionary<double, double> ParseOddsMap(string text, List<string> warnings, string side)
    {
        var result = new Dictionary<double, double>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        foreach (string rawPart in text.Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = rawPart.Split('=', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 ||
                !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double line) ||
                !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double odds))
            {
                warnings.Add($"Could not parse {side} odds item '{rawPart}'. Use format 2.5=1.90.");
                continue;
            }

            result[line] = odds;
        }

        return result;
    }

    private static IEnumerable<double> ParseLines(string text, IEnumerable<double> fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback.Distinct().OrderBy(x => x);

        var values = new List<double>();
        foreach (string part in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                values.Add(value);
        }

        return values.Count > 0 ? values.Distinct().OrderBy(x => x) : fallback.Distinct().OrderBy(x => x);
    }

    private static IEnumerable<string> ValidateMonotonicOdds(IReadOnlyDictionary<double, double> overOdds, IReadOnlyDictionary<double, double> underOdds)
    {
        var overs = overOdds.OrderBy(x => x.Key).ToList();
        for (int i = 1; i < overs.Count; i++)
        {
            if (overs[i].Value < overs[i - 1].Value)
                yield return $"Over odds are not monotonic: Over {overs[i].Key:0.##} ({overs[i].Value:0.###}) < Over {overs[i - 1].Key:0.##} ({overs[i - 1].Value:0.###}). Check entered odds.";
        }

        var unders = underOdds.OrderBy(x => x.Key).ToList();
        for (int i = 1; i < unders.Count; i++)
        {
            if (unders[i].Value > unders[i - 1].Value)
                yield return $"Under odds are not monotonic: Under {unders[i].Key:0.##} ({unders[i].Value:0.###}) > Under {unders[i - 1].Key:0.##} ({unders[i - 1].Value:0.###}). Check entered odds.";
        }
    }

    private static string NormalizeLeagueKey(string value)
    {
        value = (value ?? string.Empty).Trim().ToLowerInvariant();
        value = value
            .Replace("latvia", string.Empty)
            .Replace("norwegian", string.Empty)
            .Replace("norway", string.Empty)
            .Replace("swedish", string.Empty)
            .Replace("sweden", string.Empty);
        return new string(value.Where(char.IsLetterOrDigit).ToArray());
    }

    private static string NormalizeTrigger(string value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "aftergoal" or "after-goal" or "goal" => "after-goal",
            "afterredcard" or "after-red-card" or "red" or "red-card" => "after-red-card",
            _ => "fixed-minute"
        };
    }

    private static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string Csv(string value)
    {
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
