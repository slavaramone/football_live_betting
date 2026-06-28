using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace LiveTotalsHelper.Tools;

public sealed class MonteCarloModelEvaluationOptions
{
    public string League { get; init; } = string.Empty;
    public IReadOnlyList<string> Seasons { get; init; } = [];
    public IReadOnlyList<double> StateMinutes { get; init; } = [45, 50, 55, 60, 65, 70, 75, 80, 85];
    public IReadOnlyList<double> Lines { get; init; } = [2.5, 3.5];
    public bool IncludeSettledLines { get; init; }
    public string CurvesPath { get; init; } = string.Empty;
    public string SideModelPath { get; init; } = string.Empty;
    public string OutputPath { get; init; } = "outputs/validation/monte-carlo-evaluation-summary.json";
    public int SimulationCount { get; init; } = 5_000;
    public double StepMinutes { get; init; } = 0.25;
    public int? RandomSeed { get; init; } = 12_345;
    public double EffectiveEndMinute { get; init; } = 96.0;
    public double AssumedOverOdds { get; init; } = 1.85;
    public double AssumedUnderOdds { get; init; } = 1.85;
    public double MinEdge { get; init; } = 0.05;
    public int MaxStates { get; init; }
    public int ProgressEvery { get; init; } = 100;
}

public sealed class MonteCarloModelEvaluationCommandResult
{
    public MonteCarloModelEvaluationSummary Summary { get; init; } = new();
    public string OutputPath { get; init; } = string.Empty;
}

public sealed class MonteCarloModelEvaluationSummary
{
    public string Version { get; init; } = "mc-evaluation-v1";
    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;
    public string League { get; init; } = string.Empty;
    public IReadOnlyList<string> Seasons { get; init; } = [];
    public IReadOnlyList<double> StateMinutes { get; init; } = [];
    public IReadOnlyList<double> Lines { get; init; } = [];
    public string CurvesPath { get; init; } = string.Empty;
    public string SideModelPath { get; init; } = string.Empty;
    public int SimulationCount { get; init; }
    public double StepMinutes { get; init; }
    public int? RandomSeed { get; init; }
    public double EffectiveEndMinute { get; init; }
    public double AssumedOverOdds { get; init; }
    public double AssumedUnderOdds { get; init; }
    public double MinEdge { get; init; }
    public MonteCarloDatasetSummary Dataset { get; init; } = new();
    public MonteCarloPredictionMetrics Overall { get; init; } = new();
    public MonteCarloStaticComparison StaticClockComparison { get; init; } = new();
    public MonteCarloBettingMetrics Betting { get; init; } = new();
    public IReadOnlyList<MonteCarloSliceSummary> Slices { get; init; } = [];
    public IReadOnlyList<MonteCarloWarningCount> TopWarnings { get; init; } = [];
}

public sealed class MonteCarloDatasetSummary
{
    public int MatchesLoaded { get; init; }
    public int MatchesUsed { get; init; }
    public int MatchesSkippedMissingScore { get; init; }
    public int MatchesSkippedInvalidTimeline { get; init; }
    public int LiveStatesBuilt { get; init; }
    public int EvaluationRowsBuilt { get; init; }
    public int RowsEvaluated { get; init; }
    public int RowsSkippedSettledLine { get; init; }
    public int RowsSkippedOutsideCurveHorizon { get; init; }
    public int RowsFailedSimulation { get; init; }
}

public sealed class MonteCarloPredictionMetrics
{
    public int Rows { get; init; }
    public double ActualRemainingAvg { get; init; }
    public double PredictedRemainingAvg { get; init; }
    public double Bias { get; init; }
    public double Mae { get; init; }
    public double Rmse { get; init; }
    public double MulticlassBrierRemaining { get; init; }
    public MonteCarloRemainingDistributionMetrics RemainingDistribution { get; init; } = new();
    public MonteCarloOverUnderMetrics OverUnder { get; init; } = new();
}

public sealed class MonteCarloRemainingDistributionMetrics
{
    public double PredictedP0 { get; init; }
    public double ActualP0 { get; init; }
    public double PredictedP1 { get; init; }
    public double ActualP1 { get; init; }
    public double PredictedP2 { get; init; }
    public double ActualP2 { get; init; }
    public double PredictedP3Plus { get; init; }
    public double ActualP3Plus { get; init; }
}

public sealed class MonteCarloOverUnderMetrics
{
    public int Rows { get; init; }
    public double ActualOverRate { get; init; }
    public double AveragePOver { get; init; }
    public double OverProbabilityBias { get; init; }
    public double BrierOver { get; init; }
    public double LogLossOver { get; init; }
}

public sealed class MonteCarloStaticComparison
{
    public int Rows { get; init; }
    public double StaticExpectedRemainingAvg { get; init; }
    public double StaticBias { get; init; }
    public double StaticMae { get; init; }
    public double StaticRmse { get; init; }
    public double McMaeMinusStaticMae { get; init; }
    public double McRmseMinusStaticRmse { get; init; }
}

public sealed class MonteCarloBettingMetrics
{
    public int Rows { get; init; }
    public int Bets { get; init; }
    public int OverBets { get; init; }
    public int UnderBets { get; init; }
    public int Wins { get; init; }
    public int Losses { get; init; }
    public int Pushes { get; init; }
    public double StrikeRate { get; init; }
    public double Profit { get; init; }
    public double Turnover { get; init; }
    public double Roi { get; init; }
    public double AverageEdge { get; init; }
    public double AverageBetProbability { get; init; }
    public double MinEdge { get; init; }
    public double AssumedOverOdds { get; init; }
    public double AssumedUnderOdds { get; init; }
}

public sealed class MonteCarloSliceSummary
{
    public string Name { get; init; } = string.Empty;
    public MonteCarloPredictionMetrics Prediction { get; init; } = new();
    public MonteCarloBettingMetrics Betting { get; init; } = new();
}

public sealed class MonteCarloWarningCount
{
    public string Warning { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class MonteCarloModelEvaluator
{
    private const double Epsilon = 0.000001;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly LiveTotalsDbContext _db;
    private readonly TextWriter _log;

    public MonteCarloModelEvaluator(LiveTotalsDbContext db, TextWriter? log = null)
    {
        _db = db;
        _log = log ?? TextWriter.Null;
    }

    public async Task<MonteCarloModelEvaluationCommandResult> EvaluateAsync(
        MonteCarloModelEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        ValidateOptions(options);

        StateWeibullCurveSet curves = await ReadJsonAsync<StateWeibullCurveSet>(options.CurvesPath, cancellationToken);
        NextGoalSideModelSet sideModel = await ReadJsonAsync<NextGoalSideModelSet>(options.SideModelPath, cancellationToken);

        HistoricalLiveDataset dataset = await BuildHistoricalDatasetAsync(options, cancellationToken);
        EvaluationAccumulator overall = new();
        EvaluationAccumulator staticComparisonRows = new();
        BettingAccumulator betting = new(options.MinEdge, options.AssumedOverOdds, options.AssumedUnderOdds);
        var sliceAccumulators = new Dictionary<string, EvaluationAccumulator>(StringComparer.OrdinalIgnoreCase);
        var sliceBetting = new Dictionary<string, BettingAccumulator>(StringComparer.OrdinalIgnoreCase);
        var warningCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int evaluated = 0;
        int skippedSettledLine = 0;
        int skippedOutsideCurveHorizon = 0;
        int failedSimulation = 0;
        var simulator = new LiveHazardMonteCarloSimulator();

        foreach (HistoricalEvaluationRow row in dataset.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int neededGoalsForOver = Math.Max(0, (int)Math.Floor(row.Line) + 1 - row.CurrentGoals);
            if (!options.IncludeSettledLines && neededGoalsForOver <= 0)
            {
                skippedSettledLine++;
                continue;
            }

            if (row.Minute >= curves.Curves.Max(x => x.BucketEndMinute) - Epsilon)
            {
                skippedOutsideCurveHorizon++;
                continue;
            }

            int rowIndex = evaluated + 1;
            var request = new LiveMonteCarloRequest
            {
                LeagueKey = string.IsNullOrWhiteSpace(options.League) ? curves.League : options.League,
                CurrentMinute = row.Minute,
                HomeGoals = row.HomeGoals,
                AwayGoals = row.AwayGoals,
                HomeRedCards = 0,
                AwayRedCards = 0,
                LastGoalMinute = row.LastGoalMinute,
                Line = row.Line,
                OverOdds = options.AssumedOverOdds,
                UnderOdds = options.AssumedUnderOdds,
                SimulationCount = options.SimulationCount,
                StepMinutes = options.StepMinutes,
                RandomSeed = options.RandomSeed.HasValue ? options.RandomSeed.Value + rowIndex * 7919 : null
            };

            LiveMonteCarloSimulationResult simulation;
            try
            {
                simulation = simulator.Run(new LiveMonteCarloSimulationOptions
                {
                    Request = request,
                    Curves = curves,
                    NextGoalSideModel = sideModel,
                    EffectiveEndMinute = options.EffectiveEndMinute,
                    TracePathCount = 0
                });
            }
            catch (ArgumentException)
            {
                skippedOutsideCurveHorizon++;
                continue;
            }
            catch (InvalidOperationException)
            {
                failedSimulation++;
                continue;
            }

            evaluated++;
            double staticExpected = CalculateStaticExpectedRemaining(curves, row.HomeGoals, row.AwayGoals, row.Minute, simulation.EffectiveEndMinute);
            EvaluationRecord record = EvaluationRecord.From(row, simulation, staticExpected);

            overall.Add(record);
            staticComparisonRows.Add(record);
            betting.Add(record, options.MinEdge, options.AssumedOverOdds, options.AssumedUnderOdds);

            foreach (string slice in BuildSliceNames(row))
            {
                if (!sliceAccumulators.TryGetValue(slice, out EvaluationAccumulator? accumulator))
                {
                    accumulator = new EvaluationAccumulator();
                    sliceAccumulators[slice] = accumulator;
                }

                accumulator.Add(record);

                if (!sliceBetting.TryGetValue(slice, out BettingAccumulator? bettingAccumulator))
                {
                    bettingAccumulator = new BettingAccumulator(options.MinEdge, options.AssumedOverOdds, options.AssumedUnderOdds);
                    sliceBetting[slice] = bettingAccumulator;
                }

                bettingAccumulator.Add(record, options.MinEdge, options.AssumedOverOdds, options.AssumedUnderOdds);
            }

            foreach (string warning in simulation.Warnings)
                warningCounts[warning] = warningCounts.TryGetValue(warning, out int current) ? current + 1 : 1;

            if (options.ProgressEvery > 0 && evaluated % options.ProgressEvery == 0)
                await _log.WriteLineAsync($"Evaluated {evaluated} rows...");

            if (options.MaxStates > 0 && evaluated >= options.MaxStates)
                break;
        }

        MonteCarloPredictionMetrics overallMetrics = overall.ToPredictionMetrics();
        MonteCarloModelEvaluationSummary summary = new()
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            League = options.League,
            Seasons = options.Seasons.ToList(),
            StateMinutes = options.StateMinutes.ToList(),
            Lines = options.Lines.ToList(),
            CurvesPath = Path.GetFullPath(options.CurvesPath),
            SideModelPath = Path.GetFullPath(options.SideModelPath),
            SimulationCount = options.SimulationCount,
            StepMinutes = options.StepMinutes,
            RandomSeed = options.RandomSeed,
            EffectiveEndMinute = options.EffectiveEndMinute,
            AssumedOverOdds = options.AssumedOverOdds,
            AssumedUnderOdds = options.AssumedUnderOdds,
            MinEdge = options.MinEdge,
            Dataset = new MonteCarloDatasetSummary
            {
                MatchesLoaded = dataset.MatchesLoaded,
                MatchesUsed = dataset.MatchesUsed,
                MatchesSkippedMissingScore = dataset.MatchesSkippedMissingScore,
                MatchesSkippedInvalidTimeline = dataset.MatchesSkippedInvalidTimeline,
                LiveStatesBuilt = dataset.LiveStatesBuilt,
                EvaluationRowsBuilt = dataset.Rows.Count,
                RowsEvaluated = evaluated,
                RowsSkippedSettledLine = skippedSettledLine,
                RowsSkippedOutsideCurveHorizon = skippedOutsideCurveHorizon,
                RowsFailedSimulation = failedSimulation
            },
            Overall = overallMetrics,
            StaticClockComparison = staticComparisonRows.ToStaticComparisonMetrics(overallMetrics),
            Betting = betting.ToMetrics(),
            Slices = sliceAccumulators
                .OrderBy(x => SliceSortKey(x.Key))
                .ThenBy(x => x.Key)
                .Select(x => new MonteCarloSliceSummary
                {
                    Name = x.Key,
                    Prediction = x.Value.ToPredictionMetrics(),
                    Betting = sliceBetting.TryGetValue(x.Key, out BettingAccumulator? bet) ? bet.ToMetrics() : new MonteCarloBettingMetrics()
                })
                .Where(x => x.Prediction.Rows > 0)
                .ToList(),
            TopWarnings = warningCounts
                .OrderByDescending(x => x.Value)
                .ThenBy(x => x.Key)
                .Take(50)
                .Select(x => new MonteCarloWarningCount { Warning = x.Key, Count = x.Value })
                .ToList()
        };

        string outputPath = await WriteSummaryAsync(options.OutputPath, summary, cancellationToken);
        return new MonteCarloModelEvaluationCommandResult
        {
            Summary = summary,
            OutputPath = outputPath
        };
    }

    private static void ValidateOptions(MonteCarloModelEvaluationOptions options)
    {
        if (options.SimulationCount <= 0)
            throw new ArgumentException("Simulation count must be positive.", nameof(options));
        if (options.StepMinutes <= 0)
            throw new ArgumentException("Step minutes must be positive.", nameof(options));
        if (options.StateMinutes.Count == 0)
            throw new ArgumentException("At least one state minute is required.", nameof(options));
        if (options.Lines.Count == 0)
            throw new ArgumentException("At least one total line is required.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.CurvesPath) || !File.Exists(options.CurvesPath))
            throw new FileNotFoundException($"State Weibull curves JSON was not found: {options.CurvesPath}", options.CurvesPath);
        if (string.IsNullOrWhiteSpace(options.SideModelPath) || !File.Exists(options.SideModelPath))
            throw new FileNotFoundException($"Next-goal-side model JSON was not found: {options.SideModelPath}", options.SideModelPath);
        if (options.EffectiveEndMinute <= 0)
            throw new ArgumentException("Effective end minute must be positive.", nameof(options));
        if (options.AssumedOverOdds <= 1.0 || options.AssumedUnderOdds <= 1.0)
            throw new ArgumentException("Assumed odds must be greater than 1.0.", nameof(options));
    }

    private async Task<HistoricalLiveDataset> BuildHistoricalDatasetAsync(
        MonteCarloModelEvaluationOptions options,
        CancellationToken cancellationToken)
    {
        IQueryable<MatchEntity> query = _db.Matches
            .AsNoTracking()
            .Include(x => x.Events);

        if (!string.IsNullOrWhiteSpace(options.League))
        {
            string league = options.League.Trim();
            query = query.Where(x => x.LeagueName == league || x.LeagueSlug == league || x.EventId == league);
        }

        if (options.Seasons.Count > 0)
        {
            string[] seasonStrings = options.Seasons.Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            int[] seasonIds = seasonStrings
                .Where(x => int.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                .Select(x => int.Parse(x, CultureInfo.InvariantCulture))
                .ToArray();

            query = query.Where(x => seasonStrings.Contains(x.SeasonYear)
                                     || seasonStrings.Contains(x.SeasonName)
                                     || seasonIds.Contains(x.SeasonId));
        }

        List<MatchEntity> matches = await query
            .OrderBy(x => x.SeasonYear)
            .ThenBy(x => x.RoundNumber)
            .ThenBy(x => x.StartTimeUtc)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);

        var dataset = new HistoricalLiveDataset { MatchesLoaded = matches.Count };
        foreach (MatchEntity match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!match.HomeScoreCurrent.HasValue || !match.AwayScoreCurrent.HasValue)
            {
                dataset.MatchesSkippedMissingScore++;
                continue;
            }

            int finalHomeGoals = match.HomeScoreCurrent.Value;
            int finalAwayGoals = match.AwayScoreCurrent.Value;
            if (finalHomeGoals < 0 || finalAwayGoals < 0)
            {
                dataset.MatchesSkippedMissingScore++;
                continue;
            }

            List<GoalSnapshot> rawGoals = match.Events
                .Where(IsScoringGoal)
                .Select(ToGoalSnapshot)
                .OrderBy(x => x.Minute)
                .ThenBy(x => x.HomeScore + x.AwayScore)
                .ThenBy(x => x.EventRowId)
                .ToList();

            if (!TryValidateGoalTimeline(rawGoals, finalHomeGoals, finalAwayGoals, out List<GoalSnapshot> goals))
            {
                dataset.MatchesSkippedInvalidTimeline++;
                continue;
            }

            dataset.MatchesUsed++;
            foreach (double minute in options.StateMinutes.OrderBy(x => x))
            {
                if (minute >= options.EffectiveEndMinute - Epsilon)
                    continue;

                (int homeAtMinute, int awayAtMinute, double? lastGoalMinute) = ScoreAtMinute(goals, minute);
                int currentGoals = homeAtMinute + awayAtMinute;
                int actualRemaining = Math.Max(0, finalHomeGoals + finalAwayGoals - currentGoals);
                double minutesSinceLastGoal = lastGoalMinute.HasValue ? Math.Max(0.0, minute - lastGoalMinute.Value) : minute;

                dataset.LiveStatesBuilt++;
                foreach (double line in options.Lines)
                {
                    dataset.Rows.Add(new HistoricalEvaluationRow
                    {
                        MatchId = match.Id,
                        EventId = match.EventId,
                        League = match.LeagueName,
                        LeagueSlug = match.LeagueSlug,
                        Season = !string.IsNullOrWhiteSpace(match.SeasonYear) ? match.SeasonYear : match.SeasonName,
                        SeasonId = match.SeasonId,
                        RoundNumber = match.RoundNumber,
                        HomeTeam = match.HomeTeamName,
                        AwayTeam = match.AwayTeamName,
                        Minute = minute,
                        Line = line,
                        HomeGoals = homeAtMinute,
                        AwayGoals = awayAtMinute,
                        FinalHomeGoals = finalHomeGoals,
                        FinalAwayGoals = finalAwayGoals,
                        LastGoalMinute = lastGoalMinute,
                        MinutesSinceLastGoal = minutesSinceLastGoal,
                        ActualRemainingGoals = actualRemaining
                    });
                }
            }
        }

        return dataset;
    }

    private static (int HomeGoals, int AwayGoals, double? LastGoalMinute) ScoreAtMinute(IReadOnlyList<GoalSnapshot> goals, double minute)
    {
        int homeGoals = 0;
        int awayGoals = 0;
        double? lastGoalMinute = null;

        foreach (GoalSnapshot goal in goals)
        {
            if (goal.Minute <= minute + Epsilon)
            {
                homeGoals = goal.HomeScore;
                awayGoals = goal.AwayScore;
                lastGoalMinute = goal.Minute;
            }
            else
            {
                break;
            }
        }

        return (homeGoals, awayGoals, lastGoalMinute);
    }

    private static double CalculateStaticExpectedRemaining(
        StateWeibullCurveSet curves,
        int homeGoals,
        int awayGoals,
        double minute,
        double effectiveEnd)
    {
        string scoreBucket = StateWeibullScoreBucketer.ResolveScoreBucket(homeGoals, awayGoals);
        double total = 0.0;
        double current = minute;
        double maxEnd = curves.Curves.Count == 0 ? effectiveEnd : curves.Curves.Max(x => x.BucketEndMinute);
        double end = Math.Min(effectiveEnd, maxEnd);

        while (current < end - Epsilon)
        {
            StateWeibullCurve? curve = curves.Curves
                .Where(x => x.ScoreBucket.Equals(scoreBucket, StringComparison.OrdinalIgnoreCase)
                            && current >= x.BucketStartMinute - Epsilon
                            && current < x.BucketEndMinute - Epsilon)
                .OrderBy(x => x.BucketStartMinute)
                .FirstOrDefault();
            if (curve is null)
                break;

            double segmentEnd = Math.Min(end, curve.BucketEndMinute);
            total += ExpectedGoalsBetween(curve, current, segmentEnd);
            current = segmentEnd;
        }

        return total;
    }

    private static double ExpectedGoalsBetween(StateWeibullCurve curve, double startMinute, double endMinute)
    {
        double start = Math.Clamp(startMinute - curve.BucketStartMinute, 0.0, curve.BucketLengthMinutes);
        double end = Math.Clamp(endMinute - curve.BucketStartMinute, 0.0, curve.BucketLengthMinutes);
        if (end <= start + Epsilon)
            return 0.0;

        double startCum = curve.ExpectedGoalsInBucket * Math.Pow(start / curve.BucketLengthMinutes, curve.ShapeK);
        double endCum = curve.ExpectedGoalsInBucket * Math.Pow(end / curve.BucketLengthMinutes, curve.ShapeK);
        return Math.Max(0.0, endCum - startCum);
    }

    private static IReadOnlyList<string> BuildSliceNames(HistoricalEvaluationRow row)
    {
        var slices = new List<string>
        {
            "all",
            $"line_{FormatLine(row.Line)}",
            $"minute_{FormatLine(row.Minute)}",
            $"score_{StateWeibullScoreBucketer.ResolveScoreBucket(row.HomeGoals, row.AwayGoals)}",
            $"needed_over_{Math.Max(0, (int)Math.Floor(row.Line) + 1 - row.CurrentGoals)}"
        };

        if (row.Minute >= 45.0)
            slices.Add("2h");
        if (row.Minute >= 75.0)
            slices.Add("late_75_plus");
        if (row.CurrentGoals >= 2)
            slices.Add("current_goals_2_plus");
        if (row.LastGoalMinute.HasValue && row.MinutesSinceLastGoal <= 5.0)
            slices.Add("after_goal_0_5");
        else if (row.LastGoalMinute.HasValue && row.MinutesSinceLastGoal <= 10.0)
            slices.Add("after_goal_5_10");

        return slices;
    }

    private static int SliceSortKey(string name)
    {
        if (name.Equals("all", StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith("line_", StringComparison.OrdinalIgnoreCase)) return 10;
        if (name.StartsWith("minute_", StringComparison.OrdinalIgnoreCase)) return 20;
        if (name.StartsWith("score_", StringComparison.OrdinalIgnoreCase)) return 30;
        if (name.StartsWith("needed_over_", StringComparison.OrdinalIgnoreCase)) return 40;
        return 50;
    }

    private static bool IsScoringGoal(MatchEventEntity item)
    {
        string type = item.IncidentType.Trim();
        if (!type.Contains("goal", StringComparison.OrdinalIgnoreCase))
            return false;
        if (type.Contains("cancel", StringComparison.OrdinalIgnoreCase) || type.Contains("var", StringComparison.OrdinalIgnoreCase))
            return false;
        if (item.HomeScore is null || item.AwayScore is null)
            return false;
        int total = item.HomeScore.Value + item.AwayScore.Value;
        return total > 0;
    }

    private static GoalSnapshot ToGoalSnapshot(MatchEventEntity item)
    {
        return new GoalSnapshot(
            item.Id,
            EffectiveMinute(item),
            item.IsHome,
            item.HomeScore ?? 0,
            item.AwayScore ?? 0);
    }

    private static double EffectiveMinute(MatchEventEntity item)
    {
        int minute = Math.Max(0, item.Minute);
        int added = Math.Max(0, item.AddedTime ?? 0);

        if (minute >= 90 && added > 0)
            return minute + added;

        if (minute == 45 && added > 0)
            return 45.0;

        return minute;
    }

    private static bool TryValidateGoalTimeline(
        IReadOnlyList<GoalSnapshot> rawGoals,
        int finalHomeGoals,
        int finalAwayGoals,
        out List<GoalSnapshot> goals)
    {
        goals = [];
        int previousHome = 0;
        int previousAway = 0;
        var usedTotals = new HashSet<int>();

        foreach (GoalSnapshot goal in rawGoals.OrderBy(x => x.HomeScore + x.AwayScore).ThenBy(x => x.Minute))
        {
            int total = goal.HomeScore + goal.AwayScore;
            if (usedTotals.Contains(total))
                continue;
            usedTotals.Add(total);

            int homeDelta = goal.HomeScore - previousHome;
            int awayDelta = goal.AwayScore - previousAway;
            if (homeDelta + awayDelta != 1 || homeDelta < 0 || awayDelta < 0)
            {
                goals = [];
                return false;
            }

            goals.Add(goal);
            previousHome = goal.HomeScore;
            previousAway = goal.AwayScore;
        }

        if (previousHome != finalHomeGoals || previousAway != finalAwayGoals)
        {
            goals = [];
            return false;
        }

        goals = goals.OrderBy(x => x.Minute).ThenBy(x => x.HomeScore + x.AwayScore).ToList();
        return true;
    }

    private static async Task<T> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        string json = await File.ReadAllTextAsync(path, cancellationToken);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new ArgumentException($"Could not read JSON file: {path}");
    }

    private static async Task<string> WriteSummaryAsync(
        string path,
        MonteCarloModelEvaluationSummary summary,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(summary, JsonOptions), Encoding.UTF8, cancellationToken);
        return fullPath;
    }

    private static double SafeDivide(double numerator, double denominator)
        => denominator <= 0 ? 0.0 : numerator / denominator;

    private static double ClampProbability(double p)
        => Math.Clamp(p, 1e-12, 1.0 - 1e-12);

    private static string FormatLine(double value)
        => value.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_');

    private sealed record GoalSnapshot(int EventRowId, double Minute, bool IsHome, int HomeScore, int AwayScore);

    private sealed class HistoricalLiveDataset
    {
        public int MatchesLoaded { get; set; }
        public int MatchesUsed { get; set; }
        public int MatchesSkippedMissingScore { get; set; }
        public int MatchesSkippedInvalidTimeline { get; set; }
        public int LiveStatesBuilt { get; set; }
        public List<HistoricalEvaluationRow> Rows { get; } = [];
    }

    private sealed class HistoricalEvaluationRow
    {
        public int MatchId { get; init; }
        public string EventId { get; init; } = string.Empty;
        public string League { get; init; } = string.Empty;
        public string LeagueSlug { get; init; } = string.Empty;
        public string Season { get; init; } = string.Empty;
        public int SeasonId { get; init; }
        public int RoundNumber { get; init; }
        public string HomeTeam { get; init; } = string.Empty;
        public string AwayTeam { get; init; } = string.Empty;
        public double Minute { get; init; }
        public double Line { get; init; }
        public int HomeGoals { get; init; }
        public int AwayGoals { get; init; }
        public int CurrentGoals => HomeGoals + AwayGoals;
        public int FinalHomeGoals { get; init; }
        public int FinalAwayGoals { get; init; }
        public int FinalGoals => FinalHomeGoals + FinalAwayGoals;
        public double? LastGoalMinute { get; init; }
        public double MinutesSinceLastGoal { get; init; }
        public int ActualRemainingGoals { get; init; }
        public bool ActualOver => FinalGoals > Line;
        public bool ActualUnder => FinalGoals < Line;
        public bool ActualPush => Math.Abs(FinalGoals - Line) <= Epsilon;
    }

    private sealed class EvaluationRecord
    {
        public HistoricalEvaluationRow Row { get; init; } = new();
        public double PredictedRemaining { get; init; }
        public double StaticExpectedRemaining { get; init; }
        public double P0 { get; init; }
        public double P1 { get; init; }
        public double P2 { get; init; }
        public double P3Plus { get; init; }
        public double POver { get; init; }
        public double PUnder { get; init; }
        public double PPush { get; init; }

        public static EvaluationRecord From(HistoricalEvaluationRow row, LiveMonteCarloSimulationResult simulation, double staticExpected)
        {
            return new EvaluationRecord
            {
                Row = row,
                PredictedRemaining = simulation.ExpectedRemainingGoals,
                StaticExpectedRemaining = staticExpected,
                P0 = simulation.Distribution.P0,
                P1 = simulation.Distribution.P1,
                P2 = simulation.Distribution.P2,
                P3Plus = simulation.Distribution.P3Plus,
                POver = simulation.POver,
                PUnder = simulation.PUnder,
                PPush = simulation.PPush
            };
        }
    }

    private sealed class EvaluationAccumulator
    {
        private int _rows;
        private double _actualRemainingSum;
        private double _predictedRemainingSum;
        private double _absErrorSum;
        private double _sqErrorSum;
        private double _multiBrierSum;
        private double _predP0Sum;
        private double _predP1Sum;
        private double _predP2Sum;
        private double _predP3PlusSum;
        private int _actualP0;
        private int _actualP1;
        private int _actualP2;
        private int _actualP3Plus;
        private int _overRows;
        private int _actualOver;
        private double _pOverSum;
        private double _overBrierSum;
        private double _overLogLossSum;
        private double _staticExpectedSum;
        private double _staticAbsErrorSum;
        private double _staticSqErrorSum;

        public void Add(EvaluationRecord record)
        {
            _rows++;
            double actual = record.Row.ActualRemainingGoals;
            double predicted = record.PredictedRemaining;
            double error = predicted - actual;
            _actualRemainingSum += actual;
            _predictedRemainingSum += predicted;
            _absErrorSum += Math.Abs(error);
            _sqErrorSum += error * error;

            double y0 = actual == 0 ? 1.0 : 0.0;
            double y1 = actual == 1 ? 1.0 : 0.0;
            double y2 = actual == 2 ? 1.0 : 0.0;
            double y3 = actual >= 3 ? 1.0 : 0.0;
            _multiBrierSum += Math.Pow(record.P0 - y0, 2)
                              + Math.Pow(record.P1 - y1, 2)
                              + Math.Pow(record.P2 - y2, 2)
                              + Math.Pow(record.P3Plus - y3, 2);

            _predP0Sum += record.P0;
            _predP1Sum += record.P1;
            _predP2Sum += record.P2;
            _predP3PlusSum += record.P3Plus;
            if (actual == 0) _actualP0++;
            else if (actual == 1) _actualP1++;
            else if (actual == 2) _actualP2++;
            else _actualP3Plus++;

            if (!record.Row.ActualPush)
            {
                _overRows++;
                double yOver = record.Row.ActualOver ? 1.0 : 0.0;
                if (record.Row.ActualOver)
                    _actualOver++;
                _pOverSum += record.POver;
                _overBrierSum += Math.Pow(record.POver - yOver, 2);
                double p = ClampProbability(record.POver);
                _overLogLossSum += -(yOver * Math.Log(p) + (1.0 - yOver) * Math.Log(1.0 - p));
            }

            double staticError = record.StaticExpectedRemaining - actual;
            _staticExpectedSum += record.StaticExpectedRemaining;
            _staticAbsErrorSum += Math.Abs(staticError);
            _staticSqErrorSum += staticError * staticError;
        }

        public MonteCarloPredictionMetrics ToPredictionMetrics()
        {
            return new MonteCarloPredictionMetrics
            {
                Rows = _rows,
                ActualRemainingAvg = SafeDivide(_actualRemainingSum, _rows),
                PredictedRemainingAvg = SafeDivide(_predictedRemainingSum, _rows),
                Bias = SafeDivide(_predictedRemainingSum - _actualRemainingSum, _rows),
                Mae = SafeDivide(_absErrorSum, _rows),
                Rmse = Math.Sqrt(SafeDivide(_sqErrorSum, _rows)),
                MulticlassBrierRemaining = SafeDivide(_multiBrierSum, _rows),
                RemainingDistribution = new MonteCarloRemainingDistributionMetrics
                {
                    PredictedP0 = SafeDivide(_predP0Sum, _rows),
                    ActualP0 = SafeDivide(_actualP0, _rows),
                    PredictedP1 = SafeDivide(_predP1Sum, _rows),
                    ActualP1 = SafeDivide(_actualP1, _rows),
                    PredictedP2 = SafeDivide(_predP2Sum, _rows),
                    ActualP2 = SafeDivide(_actualP2, _rows),
                    PredictedP3Plus = SafeDivide(_predP3PlusSum, _rows),
                    ActualP3Plus = SafeDivide(_actualP3Plus, _rows)
                },
                OverUnder = new MonteCarloOverUnderMetrics
                {
                    Rows = _overRows,
                    ActualOverRate = SafeDivide(_actualOver, _overRows),
                    AveragePOver = SafeDivide(_pOverSum, _overRows),
                    OverProbabilityBias = SafeDivide(_pOverSum - _actualOver, _overRows),
                    BrierOver = SafeDivide(_overBrierSum, _overRows),
                    LogLossOver = SafeDivide(_overLogLossSum, _overRows)
                }
            };
        }

        public MonteCarloStaticComparison ToStaticComparisonMetrics(MonteCarloPredictionMetrics mc)
        {
            return new MonteCarloStaticComparison
            {
                Rows = _rows,
                StaticExpectedRemainingAvg = SafeDivide(_staticExpectedSum, _rows),
                StaticBias = SafeDivide(_staticExpectedSum - _actualRemainingSum, _rows),
                StaticMae = SafeDivide(_staticAbsErrorSum, _rows),
                StaticRmse = Math.Sqrt(SafeDivide(_staticSqErrorSum, _rows)),
                McMaeMinusStaticMae = mc.Mae - SafeDivide(_staticAbsErrorSum, _rows),
                McRmseMinusStaticRmse = mc.Rmse - Math.Sqrt(SafeDivide(_staticSqErrorSum, _rows))
            };
        }
    }

    private sealed class BettingAccumulator
    {
        private int _rows;
        private int _bets;
        private int _overBets;
        private int _underBets;
        private int _wins;
        private int _losses;
        private int _pushes;
        private double _profit;
        private double _edgeSum;
        private double _probabilitySum;
        private readonly double _minEdge;
        private readonly double _assumedOverOdds;
        private readonly double _assumedUnderOdds;

        public BettingAccumulator(double minEdge, double assumedOverOdds, double assumedUnderOdds)
        {
            _minEdge = minEdge;
            _assumedOverOdds = assumedOverOdds;
            _assumedUnderOdds = assumedUnderOdds;
        }

        public void Add(EvaluationRecord record, double minEdge, double overOdds, double underOdds)
        {
            _rows++;
            double overEdge = record.POver - 1.0 / overOdds;
            double underEdge = record.PUnder - 1.0 / underOdds;
            bool takeOver = overEdge >= minEdge && overEdge >= underEdge;
            bool takeUnder = underEdge >= minEdge && underEdge > overEdge;
            if (!takeOver && !takeUnder)
                return;

            _bets++;
            if (takeOver)
            {
                _overBets++;
                _edgeSum += overEdge;
                _probabilitySum += record.POver;
                if (record.Row.ActualOver)
                {
                    _wins++;
                    _profit += overOdds - 1.0;
                }
                else if (record.Row.ActualPush)
                {
                    _pushes++;
                }
                else
                {
                    _losses++;
                    _profit -= 1.0;
                }
            }
            else
            {
                _underBets++;
                _edgeSum += underEdge;
                _probabilitySum += record.PUnder;
                if (record.Row.ActualUnder)
                {
                    _wins++;
                    _profit += underOdds - 1.0;
                }
                else if (record.Row.ActualPush)
                {
                    _pushes++;
                }
                else
                {
                    _losses++;
                    _profit -= 1.0;
                }
            }
        }

        public MonteCarloBettingMetrics ToMetrics()
        {
            return new MonteCarloBettingMetrics
            {
                Rows = _rows,
                Bets = _bets,
                OverBets = _overBets,
                UnderBets = _underBets,
                Wins = _wins,
                Losses = _losses,
                Pushes = _pushes,
                StrikeRate = SafeDivide(_wins, _wins + _losses),
                Profit = _profit,
                Turnover = _bets,
                Roi = SafeDivide(_profit, _bets),
                AverageEdge = SafeDivide(_edgeSum, _bets),
                AverageBetProbability = SafeDivide(_probabilitySum, _bets),
                MinEdge = _minEdge,
                AssumedOverOdds = _assumedOverOdds,
                AssumedUnderOdds = _assumedUnderOdds
            };
        }
    }
}
