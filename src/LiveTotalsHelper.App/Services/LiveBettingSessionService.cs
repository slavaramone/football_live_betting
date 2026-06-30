using System.Globalization;
using System.Text.Json;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.MonteCarlo;
using LiveTotalsHelper.Core.Services;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Tools;

namespace LiveTotalsHelper.App.Services;

public sealed class LiveBettingSessionService : ILiveBettingSessionService
{
    private const string ModelRemovedMessage = "Old live-total model removed; Monte Carlo model files are not configured for this profile.";
    private const double DefaultMinimumEdge = 0.03;
    private readonly IReadOnlyList<LeagueProfile> _toolProfiles;
    private readonly string _logsFolder;
    private readonly Dictionary<string, LoadedMonteCarloModel> _modelCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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
                RiskLevel = profile.MonteCarlo.Enabled ? "MC paper test" : string.IsNullOrWhiteSpace(profile.RiskLevel) ? "Model disabled" : profile.RiskLevel,
                AllowFixedMinuteBetting = profile.MonteCarlo.Enabled,
                AllowAfterGoalBetting = profile.MonteCarlo.Enabled,
                AllowAfterRedCardBetting = profile.MonteCarlo.Enabled,
                UseCurrentSeasonVolume = profile.UseCurrentSeasonVolume,
                DefaultBeforeRound = profile.DefaultBeforeRound,
                EdgeThreshold = profile.EdgeThreshold,
                UseProbabilityMoveFilter = false,
                DecisionMode = profile.MonteCarlo.Enabled ? "StateWeibullMonteCarlo" : "ModelDisabled",
                MinMinute = profile.MinMinute,
                RequireGoalTrigger = profile.RequireGoalTrigger,
                MinLine = profile.MinLine,
                TargetLines = profile.TargetLines,
                AllowedLines = profile.AllowedLines,
                FallbackBettingEnabled = false,
                LiveBettingRulesCount = profile.LiveBettingRules.Count,
                Notes = BuildProfileNotes(profile)
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
        return Task.Run(() => BuildCheck(input, cancellationToken), cancellationToken);
    }

    private LiveBettingCheckResult BuildCheck(LiveBettingCheckInput input, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        LeagueProfile? profile = FindToolProfile(input.ProfileKey);
        List<double> targetLines = ParseLines(input.TargetLinesText);
        if (targetLines.Count == 0)
            targetLines = [input.LiveOddsLine > 0 ? input.LiveOddsLine : input.StartingLine];

        if (profile is null)
            return BuildDisabledResult(input, targetLines, $"League profile '{input.ProfileKey}' was not found.");

        if (!profile.MonteCarlo.Enabled)
            return BuildDisabledResult(input, targetLines, ModelRemovedMessage);

        LoadedMonteCarloModel model;
        try
        {
            model = LoadMonteCarloModel(profile);
        }
        catch (Exception ex)
        {
            return BuildDisabledResult(input, targetLines, ex.Message, "MC MODEL LOAD ERROR");
        }

        var estimator = new EffectiveEndMinuteEstimator();
        var decisions = new List<LiveBettingDecisionRow>();
        var summaries = new List<string>();
        var allWarnings = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        double? firstRemainingXg = null;
        double? firstEffectiveEnd = null;
        double? firstP0 = null;
        double? firstP1 = null;
        double? firstP2 = null;
        double? firstP3Plus = null;
        double? firstLiveStateCorrectionMultiplier = null;
        string firstLiveStateCorrectionSource = "disabled";

        foreach (double line in targetLines.Distinct().OrderBy(x => x))
        {
            cancellationToken.ThrowIfCancellationRequested();

            double? overOdds = ResolveOddsForLine(line, input.LiveOverOddsText, input.LiveOverOdds, input.LiveOddsLine);
            double? underOdds = ResolveOddsForLine(line, input.LiveUnderOddsText, input.LiveUnderOdds, input.LiveOddsLine);

            var request = new LiveMonteCarloRequest
            {
                LeagueKey = profile.Key,
                CurrentMinute = input.Minute,
                HomeGoals = input.HomeGoals,
                AwayGoals = input.AwayGoals,
                HomeRedCards = input.HomeRedCards,
                AwayRedCards = input.AwayRedCards,
                LastGoalMinute = input.LastGoalMinute >= 0 ? input.LastGoalMinute : null,
                Line = line,
                OverOdds = overOdds,
                UnderOdds = underOdds,
                PregameTotalLine = input.StartingLine > 0 ? input.StartingLine : null,
                PregameOverOdds = input.StartingOverOdds > 1 ? input.StartingOverOdds : null,
                PregameUnderOdds = input.StartingUnderOdds > 1 ? input.StartingUnderOdds : null,
                UseMarketBaseline = profile.MarketBaseline.Enabled ?? true,
                MarketBaselineLowTotalShrink = profile.MarketBaseline.LowTotalMultiplierShrink,
                MarketBaselineHighTotalShrink = profile.MarketBaseline.HighTotalMultiplierShrink,
                MarketBaselineMinMultiplier = profile.MarketBaseline.MinMultiplier,
                MarketBaselineMaxMultiplier = profile.MarketBaseline.MaxMultiplier,
                MarketBaselineOddsSensitivityGoals = profile.MarketBaseline.OddsSensitivityGoals,
                UseLiveStateCorrection = profile.LiveStateCorrection.Enabled ?? false,
                SimulationCount = profile.MonteCarlo.SimulationCount,
                StepMinutes = profile.MonteCarlo.StepMinutes,
                RandomSeed = profile.MonteCarlo.RandomSeed
            };

            EffectiveEndMinuteEstimate endEstimate = estimator.Estimate(request, profile.MonteCarlo);
            var simulator = new LiveCompetingHazardMonteCarloSimulator();
            LiveMonteCarloSimulationResult simulation;
            try
            {
                simulation = simulator.Run(new LiveCompetingHazardMonteCarloSimulationOptions
                {
                    Request = request,
                    Curves = model.CompetingCurves,
                    LiveStateCorrection = model.LiveStateCorrection,
                    EffectiveEndMinute = endEstimate.EffectiveEndMinute,
                    TracePathCount = 0
                });
            }
            catch (Exception ex)
            {
                decisions.Add(new LiveBettingDecisionRow
                {
                    Line = line,
                    Side = "MC",
                    Decision = "MC ERROR",
                    Reason = ex.Message
                });
                allWarnings.Add(ex.Message);
                continue;
            }

            firstRemainingXg ??= simulation.ExpectedRemainingGoals;
            firstEffectiveEnd ??= simulation.EffectiveEndMinute;
            firstP0 ??= simulation.Distribution.P0;
            firstP1 ??= simulation.Distribution.P1;
            firstP2 ??= simulation.Distribution.P2;
            firstP3Plus ??= simulation.Distribution.P3Plus;
            firstLiveStateCorrectionMultiplier ??= simulation.LiveStateCorrection.Multiplier;
            if (firstLiveStateCorrectionSource.Equals("disabled", StringComparison.OrdinalIgnoreCase) && simulation.LiveStateCorrection.Enabled)
                firstLiveStateCorrectionSource = string.IsNullOrWhiteSpace(simulation.LiveStateCorrection.FactorKey) ? simulation.LiveStateCorrection.Status : simulation.LiveStateCorrection.FactorKey;

            foreach (string warning in simulation.Warnings)
                allWarnings.Add(warning);

            summaries.Add($"{FormatLine(line)}: rem {simulation.ExpectedRemainingGoals:0.00}, Over {simulation.POver:0.0%}, Under {simulation.PUnder:0.0%}");
            decisions.Add(BuildDecisionRow(line, "OVER", overOdds, simulation.POver, simulation.FairOverOdds, simulation.OverEdge, simulation, profile));
            decisions.Add(BuildDecisionRow(line, "UNDER", underOdds, simulation.PUnder, simulation.FairUnderOdds, simulation.UnderEdge, simulation, profile));
        }

        bool anyBet = decisions.Any(x => x.Decision.Equals("MC VALUE", StringComparison.OrdinalIgnoreCase));
        string distributionText = firstP0.HasValue
            ? $"P0 {firstP0:0.0%}, P1 {firstP1:0.0%}, P2 {firstP2:0.0%}, P3+ {firstP3Plus:0.0%}"
            : "no distribution";
        string modelSummary = firstRemainingXg.HasValue
            ? $"MC rem xG {firstRemainingXg:0.00}; eff end {firstEffectiveEnd:0.#}; {distributionText}; {string.Join(" | ", summaries)}"
            : string.Join(" | ", summaries);

        string warningText = BuildWarningSummary(allWarnings);
        return new LiveBettingCheckResult
        {
            CheckedAt = DateTimeOffset.Now,
            IsBettingAllowed = anyBet,
            Status = anyBet ? "MC VALUE FOUND" : "MC PRICED",
            Warnings = warningText,
            ModelSummary = modelSummary,
            DecisionRulesSummary = $"V3 competing-hazard MC. Sims={profile.MonteCarlo.SimulationCount}, step={profile.MonteCarlo.StepMinutes:0.###}, min edge={GetMinimumEdge(profile):0.0%}. Curves: {Path.GetFileName(model.CompetingCurvesPath)}; live-state correction: {(model.LiveStateCorrection.Settings.Enabled ? Path.GetFileName(model.LiveStateCorrectionPath) : "disabled") }.",
            RemainingXg = firstRemainingXg ?? 0,
            StateCorrectionFactor = firstLiveStateCorrectionMultiplier ?? 1,
            StateCorrectionSource = firstLiveStateCorrectionSource,
            StateCorrectionSupported = profile.LiveStateCorrection.Enabled ?? false,
            VolumeFactor = profile.MonteCarlo.SimulationCount,
            VolumeFactorSource = "MC simulations",
            Decisions = decisions
        };
    }

    private LiveBettingDecisionRow BuildDecisionRow(
        double line,
        string side,
        double? bookOdds,
        double modelProbability,
        double? fairOdds,
        double? edge,
        LiveMonteCarloSimulationResult simulation,
        LeagueProfile profile)
    {
        double minEdge = GetMinimumEdge(profile);
        string decision;
        if (!bookOdds.HasValue)
            decision = "NO ODDS";
        else if (edge.HasValue && edge.Value >= minEdge)
            decision = "MC VALUE";
        else if (edge.HasValue && edge.Value > 0)
            decision = "SMALL EDGE";
        else
            decision = "NO BET";

        string reason = $"{side} prob {modelProbability:0.0%}, fair {FormatNullableOdds(fairOdds)}, book {FormatNullableOdds(bookOdds)}, edge {FormatNullablePercent(edge)}. " +
                        $"Dist: 0={simulation.Distribution.P0:0.0%}, 1={simulation.Distribution.P1:0.0%}, 2={simulation.Distribution.P2:0.0%}, 3+={simulation.Distribution.P3Plus:0.0%}. " +
                        $"{simulation.Explanation}";

        return new LiveBettingDecisionRow
        {
            Line = line,
            Side = side,
            BookOdds = bookOdds,
            ModelProbability = modelProbability,
            FairOdds = fairOdds,
            Edge = edge,
            BaselineOverProbability = simulation.POver,
            CorrectedOverProbability = modelProbability,
            ProbabilityMove = simulation.PPush > 0 ? simulation.PPush : null,
            Decision = decision,
            Reason = reason
        };
    }

    private LiveBettingCheckResult BuildDisabledResult(
        LiveBettingCheckInput input,
        IReadOnlyList<double> targetLines,
        string message,
        string status = "MODEL DISABLED")
    {
        var decisions = new List<LiveBettingDecisionRow>();
        foreach (double line in targetLines.Distinct().OrderBy(x => x))
        {
            double? overOdds = ResolveOddsForLine(line, input.LiveOverOddsText, input.LiveOverOdds, input.LiveOddsLine);
            double? underOdds = ResolveOddsForLine(line, input.LiveUnderOddsText, input.LiveUnderOdds, input.LiveOddsLine);
            decisions.Add(new LiveBettingDecisionRow { Line = line, Side = "OVER", BookOdds = overOdds, Decision = status, Reason = message });
            decisions.Add(new LiveBettingDecisionRow { Line = line, Side = "UNDER", BookOdds = underOdds, Decision = status, Reason = message });
        }

        return new LiveBettingCheckResult
        {
            CheckedAt = DateTimeOffset.Now,
            IsBettingAllowed = false,
            Status = status,
            Warnings = message,
            ModelSummary = message,
            DecisionRulesSummary = "Configure Monte Carlo curve and next-goal-side model files for this profile.",
            RemainingXg = 0,
            StateCorrectionFactor = 1,
            StateCorrectionSource = "disabled",
            StateCorrectionSupported = false,
            VolumeFactor = 1,
            VolumeFactorSource = "disabled",
            Decisions = decisions
        };
    }

    private LoadedMonteCarloModel LoadMonteCarloModel(LeagueProfile profile)
    {
        string key = string.IsNullOrWhiteSpace(profile.Key) ? profile.Name : profile.Key;
        if (_modelCache.TryGetValue(key, out LoadedMonteCarloModel? cached))
            return cached;

        if (string.IsNullOrWhiteSpace(profile.CompetingHazardCurvesPath))
            throw new InvalidOperationException($"CompetingHazardCurvesPath is not configured for profile '{profile.Key}'.");

        string competingPath = LeagueProfileStore.ResolvePath(profile.CompetingHazardCurvesPath);
        if (!File.Exists(competingPath))
            throw new FileNotFoundException($"Competing-hazard curves file was not found: {competingPath}", competingPath);

        CompetingHazardCurveSet competingCurves = JsonSerializer.Deserialize<CompetingHazardCurveSet>(File.ReadAllText(competingPath), _jsonOptions)
            ?? throw new InvalidOperationException($"Could not read competing-hazard curves: {competingPath}");

        LiveStateCorrectionSet liveStateCorrection = LiveStateCorrectionSet.Disabled;
        string liveStateCorrectionPath = string.Empty;
        if (profile.LiveStateCorrection.Enabled ?? false)
        {
            liveStateCorrectionPath = LeagueProfileStore.ResolvePath(profile.LiveStateCorrectionPath);
            if (File.Exists(liveStateCorrectionPath))
            {
                liveStateCorrection = JsonSerializer.Deserialize<LiveStateCorrectionSet>(File.ReadAllText(liveStateCorrectionPath), _jsonOptions)
                    ?? LiveStateCorrectionSet.EnabledWithoutFactors(profile.League);
            }
            else
            {
                liveStateCorrection = LiveStateCorrectionSet.EnabledWithoutFactors(profile.League);
            }
        }

        var loaded = new LoadedMonteCarloModel(competingCurves, liveStateCorrection, competingPath, liveStateCorrectionPath);
        _modelCache[key] = loaded;
        return loaded;
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
            Csv(row?.Decision ?? "NO MODEL ROW"),
            Csv(row?.Reason ?? "Selected bet row was not found in latest MC output."),
            Csv(input.BetNotes)));

        return path;
    }

    private LeagueProfile? FindToolProfile(string profileKeyOrName)
    {
        return _toolProfiles.FirstOrDefault(profile =>
            profile.Key.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            profile.Name.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            profile.League.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildProfileNotes(LeagueProfile profile)
    {
        string baseNotes = string.IsNullOrWhiteSpace(profile.Notes) ? ModelRemovedMessage : profile.Notes;
        if (!profile.MonteCarlo.Enabled)
            return baseNotes;

        string marketBaseline = profile.MarketBaseline.Enabled ?? true
            ? $" Market baseline: low shrink {FormatNullable(profile.MarketBaseline.LowTotalMultiplierShrink)}, high shrink {FormatNullable(profile.MarketBaseline.HighTotalMultiplierShrink)}."
            : " Market baseline disabled.";

        string liveStateCorrection = profile.LiveStateCorrection.Enabled ?? false
            ? $" Live-state correction enabled: {Path.GetFileName(profile.LiveStateCorrectionPath)}."
            : " Live-state correction disabled.";

        return $"MC enabled.{marketBaseline}{liveStateCorrection} {baseNotes}";
    }

    private static double GetMinimumEdge(LeagueProfile profile)
    {
        return profile.EdgeThreshold > 0 ? profile.EdgeThreshold : DefaultMinimumEdge;
    }

    private static string BuildWarningSummary(SortedSet<string> warnings)
    {
        if (warnings.Count == 0)
            return string.Empty;

        return $"{warnings.Count} MC warning(s). Sample: " + string.Join(" | ", warnings.Take(4));
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

    private static string FormatLine(double line)
        => line.ToString("0.##", CultureInfo.InvariantCulture);

    private static string FormatNullable(double? value)
        => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "profile/default";

    private static string FormatNullableOdds(double? value)
        => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";

    private static string FormatNullablePercent(double? value)
        => value.HasValue ? value.Value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture) : "-";

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

    private sealed record LoadedMonteCarloModel(
        CompetingHazardCurveSet CompetingCurves,
        LiveStateCorrectionSet LiveStateCorrection,
        string CompetingCurvesPath,
        string LiveStateCorrectionPath);
}
