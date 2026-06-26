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
        string gateReason = string.Empty;

        if (input.HomeRedCards + input.AwayRedCards > 0 || trigger == "after-red-card")
        {
            allowed = false;
            status = "NO BET - red card/manual review";
            gateReason = "Red-card states are manual review/no-bet in current paper-test rules.";
            warnings.Add(gateReason);
        }
        else if (trigger == "after-goal" && !profile.AllowAfterGoalBetting)
        {
            allowed = false;
            status = "LOG ONLY - after-goal not enabled for this profile";
            gateReason = "This profile is configured to log AfterGoal only.";
            warnings.Add(gateReason);
        }
        else if (trigger == "fixed-minute" && !profile.AllowFixedMinuteBetting)
        {
            allowed = false;
            status = "NO BET - fixed-minute disabled for this profile";
            gateReason = "Fixed-minute betting is disabled for this profile.";
            warnings.Add(gateReason);
        }
        else if (trigger == "fixed-minute" && input.LastGoalMinute >= 0 && input.Minute - input.LastGoalMinute <= input.RecentGoalMinutes)
        {
            allowed = false;
            status = "WAIT - recent goal";
            gateReason = $"Fixed-minute check is within {input.RecentGoalMinutes} minutes after a goal; rerun as AfterGoal or wait for the check window.";
            warnings.Add(gateReason);
        }
        else if (!IsCheckMinuteAllowed(trigger, input.Minute))
        {
            allowed = false;
            status = "NO BET - outside check window";
            gateReason = BuildCheckWindowReason(trigger, input.Minute);
            warnings.Add(gateReason);
        }

        Dictionary<double, double> overOdds = ParseOddsMap(input.LiveOverOddsText, warnings, "Over");
        Dictionary<double, double> underOdds = ParseOddsMap(input.LiveUnderOddsText, warnings, "Under");
        AddStructuredOdds(overOdds, input.LiveOddsLine, input.LiveOverOdds);
        AddStructuredOdds(underOdds, input.LiveOddsLine, input.LiveUnderOdds);
        AddStructuredOdds(overOdds, 2.5, input.LiveOverOdds25);
        AddStructuredOdds(underOdds, 2.5, input.LiveUnderOdds25);
        AddStructuredOdds(overOdds, 3.5, input.LiveOverOdds35);
        AddStructuredOdds(underOdds, 3.5, input.LiveUnderOdds35);
        foreach (string warning in ValidateMonotonicOdds(overOdds, underOdds))
            warnings.Add(warning);

        try
        {
            LiveTotalPriceOptions priceOptions = BuildPriceOptions(input, toolProfile, trigger, overOdds, underOdds);
            await EnsureEmpiricalSettlementAsync(priceOptions, toolProfile, warnings, cancellationToken);
            await ApplySeasonVolumeAsync(priceOptions, toolProfile, input, warnings, cancellationToken);

            var pricer = new LiveTotalPricer(priceOptions);
            LiveTotalPriceResult priced = await pricer.PriceAsync(cancellationToken);

            IReadOnlyList<LiveBettingDecisionRow> decisions = priced.Lines
                .SelectMany(line => ToDecisionRows(line, allowed, status, gateReason))
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
                DecisionRulesSummary = priced.DecisionRulesSummary,
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
            writer.WriteLine("CheckedAt,Profile,Match,Trigger,Minute,Score,StartingLine,StartingOver,StartingUnder,LiveOverOdds,LiveUnderOdds,Status,DecisionRules,Warnings,RemainingXg,StateCorrectionSupported,StateCorrectionSource,VolumeFactor,VolumeFactorSource,Line,Side,BookOdds,ModelProbability,FairOdds,Edge,BaseOverProbability,CorrectedOverProbability,ProbabilityMove,Decision,DecisionReason");
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
                Csv(result.DecisionRulesSummary),
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
                decision.BaselineOverProbability.HasValue ? D(decision.BaselineOverProbability.Value) : "",
                decision.CorrectedOverProbability.HasValue ? D(decision.CorrectedOverProbability.Value) : "",
                decision.ProbabilityMove.HasValue ? D(decision.ProbabilityMove.Value) : "",
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
            writer.WriteLine("BetLoggedAt,Mode,Profile,Match,Trigger,Minute,Score,Line,Side,BookOdds,Stake,ModelProbability,FairOdds,Edge,BaseOverProbability,CorrectedOverProbability,ProbabilityMove,Decision,DecisionReason,DecisionRules,RemainingXg,Notes");
        }

        double selectedLine = input.SelectedBetLine > 0
            ? input.SelectedBetLine
            : double.TryParse(NormalizeNumberText(input.SelectedBetLineText), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedLine)
                ? parsedLine
                : 0.0;
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
            row?.BaselineOverProbability.HasValue == true ? D(row.BaselineOverProbability.Value) : "",
            row?.CorrectedOverProbability.HasValue == true ? D(row.CorrectedOverProbability.Value) : "",
            row?.ProbabilityMove.HasValue == true ? D(row.ProbabilityMove.Value) : "",
            Csv(row?.Decision ?? "MANUAL"),
            Csv(row?.Reason ?? string.Empty),
            Csv(result.DecisionRulesSummary),
            D(result.RemainingXg),
            Csv(input.BetNotes)));

        return path;
    }

    private static LiveBettingProfile ToLiveProfile(LeagueProfile profile)
    {
        bool hasExplicitFixedMinuteRules = profile.LiveBettingRules.Any(x =>
            RuleTriggerMatches(x.StateTrigger, LiveTotalStateTrigger.FixedMinute));
        bool hasAllowedFixedMinuteRules = profile.LiveBettingRules.Any(x =>
            x.AllowBet && RuleTriggerMatches(x.StateTrigger, LiveTotalStateTrigger.FixedMinute));
        bool hasAllowedAfterGoalRules = profile.LiveBettingRules.Any(x =>
            x.AllowBet && RuleTriggerMatches(x.StateTrigger, LiveTotalStateTrigger.AfterGoal));

        return new LiveBettingProfile
        {
            Key = profile.Key,
            DisplayName = string.IsNullOrWhiteSpace(profile.Name) ? profile.League : profile.Name,
            RiskLevel = profile.RiskLevel,
            AllowFixedMinuteBetting = !hasExplicitFixedMinuteRules || hasAllowedFixedMinuteRules,
            AllowAfterGoalBetting = hasAllowedAfterGoalRules,
            AllowAfterRedCardBetting = false,
            UseCurrentSeasonVolume = profile.UseCurrentSeasonVolume,
            DefaultBeforeRound = profile.DefaultBeforeRound,
            EdgeThreshold = profile.EdgeThreshold,
            UseProbabilityMoveFilter = profile.UseProbabilityMoveFilter,
            DecisionMode = profile.DecisionMode,
            MinMinute = profile.MinMinute,
            RequireGoalTrigger = profile.RequireGoalTrigger,
            MinLine = profile.MinLine,
            TargetLines = profile.TargetLines,
            AllowedLines = profile.AllowedLines,
            FallbackBettingEnabled = profile.FallbackBettingEnabled,
            LiveBettingRulesCount = profile.LiveBettingRules.Count,
            Notes = BuildProfileNotes(profile)
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
            StateCorrectionScope = LiveTotalStateCorrectionScope.FixedMinute,
            StateCorrectionDirectionGuard = LiveTotalStateCorrectionDirectionGuard.UpOnly,
            LateGameCorrection = BuildProfileLateGameCorrection(profile),
            EmpiricalSettlementPath = profile.GetEmpiricalSettlementPath(),
            StateTrigger = trigger,
            StartingLine = input.StartingLine,
            StartingOverOdds = input.StartingOverOdds,
            StartingUnderOdds = input.StartingUnderOdds,
            Minute = input.Minute,
            HomeGoals = input.HomeGoals,
            AwayGoals = input.AwayGoals,
            EmpiricalWeight = profile.DefaultEmpiricalWeight,
            EdgeThreshold = profile.EdgeThreshold,
            UseProbabilityMoveFilter = profile.UseProbabilityMoveFilter,
            MinOverProbabilityMove = profile.MinOverProbabilityMove,
            MinUnderProbabilityMove = profile.MinUnderProbabilityMove,
            UnderSignalsBettingAllowed = profile.UnderSignalsBettingAllowed,
            HomeRedCards = input.HomeRedCards,
            AwayRedCards = input.AwayRedCards,
            LastGoalMinute = input.LastGoalMinute,
            RecentGoalMinutes = input.RecentGoalMinutes,
            VolumeFactor = 1.0,
            VolumeFactorSource = "none/default 1.0"
        };

        foreach (LiveTotalProfileBettingRule rule in profile.LiveBettingRules)
            options.LiveBettingRules.Add(rule);

        ApplyProfileDecisionRules(options.DecisionRules, profile);

        options.TargetLines.Clear();
        foreach (double line in ParseLines(input.TargetLinesText, profile.TargetLines.Count > 0 ? profile.TargetLines : overOdds.Keys.Concat(underOdds.Keys)))
            options.TargetLines.Add(line);

        foreach ((double line, double odds) in overOdds)
            options.LiveOverOddsByLine[line] = odds;
        foreach ((double line, double odds) in underOdds)
            options.LiveUnderOddsByLine[line] = odds;

        return options;
    }

    private static LiveTotalLateGameCorrectionOptions BuildProfileLateGameCorrection(LeagueProfile profile)
    {
        return new LiveTotalLateGameCorrectionOptions
        {
            Mode = profile.StateCorrectionLateGameMode,
            StartMinute = profile.StateCorrectionLateGameStartMinute,
            FactorMultiplier = profile.StateCorrectionLateGameFactorMultiplier,
            MaxFactor = profile.StateCorrectionLateGameMaxFactor,
            MaxLine = profile.StateCorrectionLateGameMaxLine
        }.Normalized();
    }

    private static bool RuleTriggerMatches(string ruleTrigger, string requestedTrigger)
    {
        if (string.IsNullOrWhiteSpace(ruleTrigger) ||
            ruleTrigger.Equals("All", StringComparison.OrdinalIgnoreCase) ||
            ruleTrigger.Equals("Any", StringComparison.OrdinalIgnoreCase))
            return true;

        return LiveTotalStateTrigger.Normalize(ruleTrigger).Equals(requestedTrigger, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task EnsureEmpiricalSettlementAsync(
        LiveTotalPriceOptions priceOptions,
        LeagueProfile profile,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(priceOptions.EmpiricalSettlementPath) ||
            File.Exists(priceOptions.EmpiricalSettlementPath))
            return;

        if (string.IsNullOrWhiteSpace(profile.CalibrationDatasetPath) ||
            !File.Exists(profile.CalibrationDatasetPath))
        {
            warnings.Add($"Empirical settlement table is missing and calibration CSV was not found: {profile.CalibrationDatasetPath}");
            return;
        }

        if (profile.TrainingSeasonIds.Count == 0)
        {
            warnings.Add("Empirical settlement table is missing and profile has no trainingSeasonIds.");
            return;
        }

        var fitOptions = new LiveTotalEmpiricalSettlementFitOptions
        {
            InputPath = profile.CalibrationDatasetPath,
            OutputPath = priceOptions.EmpiricalSettlementPath,
            MinBucketRows = 80,
            MinBucketMatches = 40,
            MaxRemainingGoals = 8,
            Smoothing = 0.25
        };

        foreach (int seasonId in profile.TrainingSeasonIds)
            fitOptions.TrainingSeasonIds.Add(seasonId);

        var fitter = new LiveTotalEmpiricalSettlementFitter(fitOptions);
        LiveTotalEmpiricalSettlementFitResult result = await fitter.FitAsync(cancellationToken);
        warnings.Add($"Built missing empirical settlement table: {result.OutputPath} ({result.Buckets.Count} buckets).");
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

    private static IReadOnlyList<LiveBettingDecisionRow> ToDecisionRows(
        LiveTotalLinePrice line,
        bool allowed,
        string blockedStatus,
        string gateReason)
    {
        var rows = new List<LiveBettingDecisionRow>();

        LiveTotalSideDecisionView over = ApplyAppGateToSide(
            allowed,
            blockedStatus,
            gateReason,
            line.OverDecision,
            line.OverDecisionExplanation,
            line.OverEdge,
            "OVER");

        rows.Add(new LiveBettingDecisionRow
        {
            Line = line.Line,
            Side = "OVER",
            BookOdds = line.BookOverOdds,
            ModelProbability = line.WinProbability,
            FairOdds = line.FairOdds,
            Edge = line.OverEdge,
            BaselineOverProbability = line.BaselineOverNoPushProbability,
            CorrectedOverProbability = line.CorrectedOverNoPushProbability,
            ProbabilityMove = line.OverProbabilityMove,
            Decision = over.Decision,
            Reason = over.Reason
        });

        LiveTotalSideDecisionView under = ApplyAppGateToSide(
            allowed,
            blockedStatus,
            gateReason,
            line.UnderDecision,
            line.UnderDecisionExplanation,
            line.UnderEdge,
            "UNDER");

        rows.Add(new LiveBettingDecisionRow
        {
            Line = line.Line,
            Side = "UNDER",
            BookOdds = line.BookUnderOdds,
            ModelProbability = line.UnderWinProbability,
            FairOdds = line.FairUnderOdds,
            Edge = line.UnderEdge,
            BaselineOverProbability = line.BaselineOverNoPushProbability,
            CorrectedOverProbability = line.CorrectedOverNoPushProbability,
            ProbabilityMove = line.OverProbabilityMove,
            Decision = under.Decision,
            Reason = under.Reason
        });

        return rows;
    }

    private static LiveTotalSideDecisionView ApplyAppGateToSide(
        bool allowed,
        string blockedStatus,
        string gateReason,
        string modelDecision,
        string modelReason,
        double? edge,
        string side)
    {
        if (allowed)
            return new LiveTotalSideDecisionView(modelDecision, modelReason);

        // Do not hide model-side no-bet reasons. A negative edge or edge-below-threshold row
        // should explain the price/value problem, not only the app check-window gate.
        if (IsModelNoBetReasonMoreImportant(modelDecision, edge))
            return new LiveTotalSideDecisionView(modelDecision, modelReason);

        string reason = string.IsNullOrWhiteSpace(gateReason)
            ? blockedStatus
            : $"{modelReason} App gate blocked the otherwise actionable {side} decision: {gateReason}";

        return new LiveTotalSideDecisionView(blockedStatus, reason);
    }

    private static bool IsModelNoBetReasonMoreImportant(string modelDecision, double? edge)
    {
        if (!edge.HasValue)
            return true;

        if (edge.Value <= 0)
            return true;

        // Keep explicit model/rules no-bet explanations such as edge below threshold,
        // probability-move filter, disabled profile rule, unsupported sparse bucket, etc.
        if (modelDecision.StartsWith("NO BET", StringComparison.OrdinalIgnoreCase) &&
            !modelDecision.Contains("outside check window", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private readonly record struct LiveTotalSideDecisionView(string Decision, string Reason);

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

    private static string BuildCheckWindowReason(string trigger, int minute)
    {
        if (trigger == "after-goal" || trigger == "after-red-card")
            return $"After-event checks are allowed only during minutes 1-90; current minute is {minute}.";

        return $"Fixed-minute checks are allowed only at 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, or 85. Current minute is {minute}.";
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
                !double.TryParse(NormalizeNumberText(parts[0]), NumberStyles.Float, CultureInfo.InvariantCulture, out double line) ||
                !double.TryParse(NormalizeNumberText(parts[1]), NumberStyles.Float, CultureInfo.InvariantCulture, out double odds))
            {
                warnings.Add($"Could not parse {side} odds item '{rawPart}'. Use format 2.5=1.90.");
                continue;
            }

            result[line] = odds;
        }

        return result;
    }

    private static void AddStructuredOdds(Dictionary<double, double> target, double line, double odds)
    {
        if (line <= 0 || odds <= 1.0)
            return;

        target[LiveTotalPricer.NormalizeLineKey(line)] = odds;
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

    private static string NormalizeNumberText(string value)
    {
        return (value ?? string.Empty).Trim().Replace(',', '.');
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

    private static string BuildProfileNotes(LeagueProfile profile)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(profile.Notes))
            parts.Add(profile.Notes);

        var decisionRules = new LiveTotalDecisionRuleOptions
        {
            DecisionMode = profile.DecisionMode,
            MinMinute = profile.MinMinute,
            RequireGoalTrigger = profile.RequireGoalTrigger,
            MinLine = profile.MinLine,
            AllowedLines = profile.AllowedLines,
            FallbackBettingEnabled = profile.FallbackBettingEnabled,
            Notes = profile.DecisionRulesNotes
        };
        parts.Add($"Decision rules: {decisionRules.Summary()}");
        if (!string.IsNullOrWhiteSpace(profile.DecisionRulesNotes))
            parts.Add(profile.DecisionRulesNotes);

        if (profile.UseProbabilityMoveFilter || profile.LiveBettingRules.Count > 0)
        {
            string rules = profile.LiveBettingRules.Count > 0
                ? $"{profile.LiveBettingRules.Count} profile rules"
                : $"global move filter O>={profile.MinOverProbabilityMove:P0}, U<={profile.MinUnderProbabilityMove:P0}";
            parts.Add($"Move filter active: {rules}");
        }

        return string.Join(" | ", parts);
    }

    private static void ApplyProfileDecisionRules(LiveTotalDecisionRuleOptions target, LeagueProfile profile)
    {
        target.DecisionMode = profile.DecisionMode;
        target.MinMinute = profile.MinMinute;
        target.RequireGoalTrigger = profile.RequireGoalTrigger;
        target.MinLine = profile.MinLine;
        target.AllowedLines.Clear();
        foreach (double line in profile.AllowedLines)
            target.AllowedLines.Add(line);
        target.FallbackBettingEnabled = profile.FallbackBettingEnabled;
        target.Notes = profile.DecisionRulesNotes;
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
