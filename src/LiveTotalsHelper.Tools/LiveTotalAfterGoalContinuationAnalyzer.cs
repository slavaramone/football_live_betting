using System.Globalization;
using System.Text;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalAfterGoalContinuationAnalysisOptions
{
    public string League { get; set; } = string.Empty;
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SummaryOutputPath { get; set; } = string.Empty;
    public List<int> TestSeasonIds { get; } = [];
    public List<double> TargetLines { get; } = [2.5, 3.5, 4.5];
    public List<int> Windows { get; } = [5, 10, 15, 20];
    public int MinSummaryRows { get; set; } = 5;
}

public sealed class LiveTotalAfterGoalContinuationAnalysisResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public string SummaryOutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int AfterGoalRows { get; set; }
    public int ContinuationRows { get; set; }
    public int SummaryRows { get; set; }
    public List<int> TestSeasonIds { get; } = [];
}

public sealed class LiveTotalAfterGoalContinuationAnalyzer
{
    private readonly LiveTotalAfterGoalContinuationAnalysisOptions _options;

    public LiveTotalAfterGoalContinuationAnalyzer(LiveTotalAfterGoalContinuationAnalysisOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalAfterGoalContinuationAnalysisResult> AnalyzeAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();

        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);
        List<InputRow> testRows = rows
            .Where(x => _options.TestSeasonIds.Count == 0 || _options.TestSeasonIds.Contains(x.SeasonId))
            .ToList();

        List<InputRow> afterGoalRows = testRows
            .Where(x => x.StateTrigger.Equals(LiveTotalStateTrigger.AfterGoal, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.MatchId)
            .ThenBy(x => x.CurrentTotalGoals)
            .ThenBy(x => x.Minute)
            .ToList();

        Dictionary<long, List<InputRow>> goalsByMatch = afterGoalRows
            .GroupBy(x => x.MatchId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderBy(y => y.CurrentTotalGoals).ThenBy(y => y.Minute).ToList());

        var continuationRows = new List<ContinuationRow>();
        foreach (InputRow row in afterGoalRows)
        {
            GoalContext goal = ResolveGoalContext(row);
            InputRow? nextGoal = FindNextGoal(goalsByMatch[row.MatchId], row);
            int? minutesToNextGoal = nextGoal is null ? null : Math.Max(0, nextGoal.Minute - row.Minute);

            var continuationRow = new ContinuationRow
            {
                League = _options.League,
                SeasonId = row.SeasonId,
                MatchId = row.MatchId,
                Minute = row.Minute,
                MinuteBand = MinuteBand(row.Minute),
                GoalNumber = row.CurrentTotalGoals,
                GoalSide = goal.GoalSide,
                ScoreBefore = goal.ScoreBefore,
                ScoreAfter = goal.ScoreAfter,
                ScoreStateAfter = LiveTotalStateCorrectionResolver.DetailedScoreState(row.HomeGoals, row.AwayGoals),
                GoalEffect = goal.Effect,
                CurrentTotalGoals = row.CurrentTotalGoals,
                FinalTotalGoals = row.ActualFinalTotalGoals,
                FinalGoalsAfterThis = Math.Max(0, row.ActualFinalTotalGoals - row.CurrentTotalGoals),
                NextGoalMinute = nextGoal?.Minute,
                MinutesToNextGoal = minutesToNextGoal,
                NextGoalSide = nextGoal is null ? string.Empty : ResolveGoalContext(nextGoal).GoalSide,
                NextGoalNumber = nextGoal?.CurrentTotalGoals
            };

            foreach (int window in _options.Windows.Distinct().OrderBy(x => x))
            {
                continuationRow.WindowResults[window] = minutesToNextGoal.HasValue && minutesToNextGoal.Value <= window;
            }

            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                continuationRow.LineResults[line] = new LineContinuationResult
                {
                    Line = line,
                    IsOpen = IsOverLineStillOpen(line, row.CurrentTotalGoals),
                    ActualOver = TryActualOver(line, row.ActualFinalTotalGoals)
                };
            }

            continuationRows.Add(continuationRow);
        }

        List<ContinuationSummaryRow> summaryRows = BuildSummaries(continuationRows);

        string outputPath = ResolveOutputPath();
        string summaryOutputPath = ResolveSummaryOutputPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".");
        await File.WriteAllTextAsync(outputPath, ToRowsCsv(continuationRows), Encoding.UTF8, cancellationToken);
        await File.WriteAllTextAsync(summaryOutputPath, ToSummaryCsv(summaryRows), Encoding.UTF8, cancellationToken);

        var result = new LiveTotalAfterGoalContinuationAnalysisResult
        {
            InputPath = _options.InputPath,
            OutputPath = outputPath,
            SummaryOutputPath = summaryOutputPath,
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            AfterGoalRows = afterGoalRows.Count,
            ContinuationRows = continuationRows.Count,
            SummaryRows = summaryRows.Count
        };
        result.TestSeasonIds.AddRange(_options.TestSeasonIds.OrderBy(x => x));
        return result;
    }

    private List<ContinuationSummaryRow> BuildSummaries(IReadOnlyCollection<ContinuationRow> rows)
    {
        var result = new List<ContinuationSummaryRow>();

        AddSummary(result, "All", rows, string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => x.GoalEffect))
            AddSummary(result, "GoalEffect", group, string.Empty, group.Key, string.Empty, 0, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => new { x.MinuteBand, x.GoalEffect }))
            AddSummary(result, "MinuteBand+GoalEffect", group, group.Key.MinuteBand, group.Key.GoalEffect, string.Empty, 0, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => new { x.GoalEffect, x.GoalSide }))
            AddSummary(result, "GoalEffect+GoalSide", group, string.Empty, group.Key.GoalEffect, group.Key.GoalSide, 0, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => new { x.MinuteBand, x.GoalEffect, x.GoalSide }))
            AddSummary(result, "MinuteBand+GoalEffect+GoalSide", group, group.Key.MinuteBand, group.Key.GoalEffect, group.Key.GoalSide, 0, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => new { x.MinuteBand, x.GoalNumber, x.GoalEffect }))
            AddSummary(result, "MinuteBand+GoalNumber+GoalEffect", group, group.Key.MinuteBand, group.Key.GoalEffect, string.Empty, group.Key.GoalNumber, string.Empty, string.Empty, string.Empty);

        foreach (var group in rows.GroupBy(x => new { x.MinuteBand, x.GoalEffect, x.ScoreStateAfter }))
            AddSummary(result, "MinuteBand+GoalEffect+ScoreStateAfter", group, group.Key.MinuteBand, group.Key.GoalEffect, string.Empty, 0, string.Empty, string.Empty, group.Key.ScoreStateAfter);

        foreach (var group in rows.GroupBy(x => new { x.MinuteBand, x.GoalEffect, x.ScoreBefore, x.ScoreAfter, x.ScoreStateAfter }))
            AddSummary(result, "ExactScore", group, group.Key.MinuteBand, group.Key.GoalEffect, string.Empty, 0, group.Key.ScoreBefore, group.Key.ScoreAfter, group.Key.ScoreStateAfter);

        return result
            .Where(x => x.Rows >= _options.MinSummaryRows)
            .OrderBy(x => SummaryBucketOrder(x.BucketType))
            .ThenBy(x => MinuteBandOrder(x.MinuteBand))
            .ThenBy(x => GoalEffectOrder(x.GoalEffect))
            .ThenBy(x => x.GoalNumber)
            .ThenBy(x => x.GoalSide, StringComparer.Ordinal)
            .ThenByDescending(x => x.Rows)
            .ToList();
    }

    private void AddSummary(
        List<ContinuationSummaryRow> result,
        string bucketType,
        IEnumerable<ContinuationRow> sourceRows,
        string minuteBand,
        string goalEffect,
        string goalSide,
        int goalNumber,
        string scoreBefore,
        string scoreAfter,
        string scoreStateAfter)
    {
        List<ContinuationRow> bucketRows = sourceRows.ToList();
        if (bucketRows.Count == 0)
            return;

        var row = new ContinuationSummaryRow
        {
            BucketType = bucketType,
            MinuteBand = minuteBand,
            GoalEffect = goalEffect,
            GoalSide = goalSide,
            GoalNumber = goalNumber,
            ScoreBefore = scoreBefore,
            ScoreAfter = scoreAfter,
            ScoreStateAfter = scoreStateAfter,
            Rows = bucketRows.Count,
            Matches = bucketRows.Select(x => x.MatchId).Distinct().Count(),
            AverageMinute = bucketRows.Average(x => x.Minute),
            AverageCurrentTotalGoals = bucketRows.Average(x => x.CurrentTotalGoals),
            AverageFinalTotalGoals = bucketRows.Average(x => x.FinalTotalGoals),
            AverageFinalGoalsAfterThis = bucketRows.Average(x => x.FinalGoalsAfterThis),
            NextGoalRows = bucketRows.Count(x => x.MinutesToNextGoal.HasValue),
            AverageMinutesToNextGoal = bucketRows.Where(x => x.MinutesToNextGoal.HasValue).Select(x => (double)x.MinutesToNextGoal!.Value).DefaultIfEmpty(double.NaN).Average(),
            NoNextGoalRate = bucketRows.Average(x => x.MinutesToNextGoal.HasValue ? 0.0 : 1.0)
        };

        foreach (int window in _options.Windows.Distinct().OrderBy(x => x))
        {
            row.WindowRates[window] = bucketRows.Average(x => x.WindowResults.TryGetValue(window, out bool hit) && hit ? 1.0 : 0.0);
        }

        foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
        {
            List<ContinuationRow> openRows = bucketRows
                .Where(x => x.LineResults.TryGetValue(line, out LineContinuationResult? lineResult) && lineResult.IsOpen)
                .ToList();
            int openMatches = openRows.Select(x => x.MatchId).Distinct().Count();
            double overRate = openRows
                .Select(x => x.LineResults[line].ActualOver)
                .Where(x => x.HasValue)
                .Select(x => x!.Value ? 1.0 : 0.0)
                .DefaultIfEmpty(double.NaN)
                .Average();

            var lineSummary = new LineSummaryRow
            {
                Line = line,
                OpenRows = openRows.Count,
                OpenMatches = openMatches,
                OpenOverRate = overRate
            };

            foreach (int window in _options.Windows.Distinct().OrderBy(x => x))
            {
                lineSummary.OpenWindowRates[window] = openRows.Count == 0
                    ? double.NaN
                    : openRows.Average(x => x.WindowResults.TryGetValue(window, out bool hit) && hit ? 1.0 : 0.0);
            }

            row.LineSummaries[line] = lineSummary;
        }

        result.Add(row);
    }

    private static InputRow? FindNextGoal(IReadOnlyList<InputRow> matchGoals, InputRow row)
    {
        return matchGoals
            .Where(x => x.CurrentTotalGoals > row.CurrentTotalGoals)
            .OrderBy(x => x.CurrentTotalGoals)
            .ThenBy(x => x.Minute)
            .FirstOrDefault();
    }

    private static GoalContext ResolveGoalContext(InputRow row)
    {
        string side = NormalizeSide(row.TriggerEventSide);
        int beforeHome = row.HomeGoals;
        int beforeAway = row.AwayGoals;
        if (side == "Home")
            beforeHome = Math.Max(0, beforeHome - 1);
        else if (side == "Away")
            beforeAway = Math.Max(0, beforeAway - 1);
        else
        {
            if (row.HomeGoals >= row.AwayGoals && row.HomeGoals > 0)
            {
                side = "Home";
                beforeHome--;
            }
            else if (row.AwayGoals > 0)
            {
                side = "Away";
                beforeAway--;
            }
            else
            {
                side = "Unknown";
            }
        }

        beforeHome = Math.Max(0, beforeHome);
        beforeAway = Math.Max(0, beforeAway);
        int beforeTotal = beforeHome + beforeAway;
        int beforeAbs = Math.Abs(beforeHome - beforeAway);
        int afterAbs = Math.Abs(row.HomeGoals - row.AwayGoals);
        string effect;

        if (beforeTotal == 0)
            effect = "FirstGoal";
        else if (afterAbs == 0 && beforeAbs == 1)
            effect = "Equalizer";
        else if (beforeAbs == 0 && afterAbs == 1)
            effect = "CreatesOneGoalLead";
        else if (afterAbs > beforeAbs && afterAbs == 2)
            effect = "ExtendsToTwoGoalLead";
        else if (afterAbs > beforeAbs && afterAbs >= 3)
            effect = "ExtendsToThreePlusLead";
        else if (afterAbs < beforeAbs)
            effect = afterAbs == 0 ? "Equalizer" : "CutsDeficit";
        else
            effect = "Other";

        return new GoalContext
        {
            GoalSide = side,
            ScoreBefore = $"{beforeHome}-{beforeAway}",
            ScoreAfter = $"{row.HomeGoals}-{row.AwayGoals}",
            Effect = effect
        };
    }

    private static string NormalizeSide(string side)
    {
        if (side.Equals("home", StringComparison.OrdinalIgnoreCase) || side.Equals("h", StringComparison.OrdinalIgnoreCase))
            return "Home";
        if (side.Equals("away", StringComparison.OrdinalIgnoreCase) || side.Equals("a", StringComparison.OrdinalIgnoreCase))
            return "Away";
        return string.Empty;
    }

    private static bool IsOverLineStillOpen(double line, int currentGoals)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);
        if (Math.Abs(frac - 0.5) < 1e-6)
            return currentGoals <= floor;
        if (Math.Abs(frac) < 1e-6)
            return currentGoals < floor;
        if (Math.Abs(frac - 0.25) < 1e-6)
            return currentGoals <= floor;
        if (Math.Abs(frac - 0.75) < 1e-6)
            return currentGoals <= floor;
        return currentGoals <= floor;
    }

    private static bool? TryActualOver(double line, int finalTotal)
    {
        double frac = Math.Round(line - Math.Floor(line), 6);
        int floor = (int)Math.Floor(line);

        if (Math.Abs(frac - 0.5) < 1e-6)
            return finalTotal > line;
        if (Math.Abs(frac) < 1e-6)
            return finalTotal == floor ? null : finalTotal > floor;
        if (Math.Abs(frac - 0.25) < 1e-6)
            return finalTotal == floor ? null : finalTotal > floor;
        if (Math.Abs(frac - 0.75) < 1e-6)
            return finalTotal == floor + 1 ? null : finalTotal > floor + 1;
        return null;
    }

    private string ToRowsCsv(IReadOnlyCollection<ContinuationRow> rows)
    {
        var sb = new StringBuilder();
        List<int> windows = _options.Windows.Distinct().OrderBy(x => x).ToList();
        List<double> lines = _options.TargetLines.Distinct().OrderBy(x => x).ToList();

        sb.Append("League,SeasonId,MatchId,Minute,MinuteBand,GoalNumber,GoalSide,ScoreBefore,ScoreAfter,ScoreStateAfter,GoalEffect,CurrentTotalGoals,FinalTotalGoals,FinalGoalsAfterThis,NextGoalMinute,MinutesToNextGoal,NextGoalNumber,NextGoalSide");
        foreach (int window in windows)
            sb.Append($",NextGoalWithin{window}");
        foreach (double line in lines)
        {
            string prefix = LinePrefix(line);
            sb.Append($",{prefix}_Open,{prefix}_ActualOver");
        }
        sb.AppendLine();

        foreach (ContinuationRow row in rows.OrderBy(x => x.SeasonId).ThenBy(x => x.MatchId).ThenBy(x => x.GoalNumber).ThenBy(x => x.Minute))
        {
            sb.Append(string.Join(',',
                EscapeCsv(row.League),
                row.SeasonId.ToString(CultureInfo.InvariantCulture),
                row.MatchId.ToString(CultureInfo.InvariantCulture),
                row.Minute.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.MinuteBand),
                row.GoalNumber.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.GoalSide),
                EscapeCsv(row.ScoreBefore),
                EscapeCsv(row.ScoreAfter),
                EscapeCsv(row.ScoreStateAfter),
                EscapeCsv(row.GoalEffect),
                row.CurrentTotalGoals.ToString(CultureInfo.InvariantCulture),
                row.FinalTotalGoals.ToString(CultureInfo.InvariantCulture),
                row.FinalGoalsAfterThis.ToString(CultureInfo.InvariantCulture),
                row.NextGoalMinute?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.MinutesToNextGoal?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                row.NextGoalNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                EscapeCsv(row.NextGoalSide)));

            foreach (int window in windows)
            {
                sb.Append(',');
                sb.Append(row.WindowResults.TryGetValue(window, out bool hit) && hit ? "1" : "0");
            }

            foreach (double line in lines)
            {
                row.LineResults.TryGetValue(line, out LineContinuationResult? lineResult);
                sb.Append(',');
                sb.Append(lineResult?.IsOpen == true ? "1" : "0");
                sb.Append(',');
                sb.Append(lineResult?.ActualOver.HasValue == true ? (lineResult.ActualOver.Value ? "1" : "0") : string.Empty);
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private string ToSummaryCsv(IReadOnlyCollection<ContinuationSummaryRow> rows)
    {
        var sb = new StringBuilder();
        List<int> windows = _options.Windows.Distinct().OrderBy(x => x).ToList();
        List<double> lines = _options.TargetLines.Distinct().OrderBy(x => x).ToList();

        sb.Append("BucketType,MinuteBand,GoalEffect,GoalSide,GoalNumber,ScoreBefore,ScoreAfter,ScoreStateAfter,Rows,Matches,AvgMinute,AvgCurrentTotalGoals,AvgFinalTotalGoals,AvgFinalGoalsAfterThis,NextGoalRows,AvgMinutesToNextGoal,NoNextGoalRate");
        foreach (int window in windows)
            sb.Append($",NextGoalWithin{window}Rate");
        foreach (double line in lines)
        {
            string prefix = LinePrefix(line);
            sb.Append($",{prefix}_OpenRows,{prefix}_OpenMatches,{prefix}_OpenOverRate");
            foreach (int window in windows)
                sb.Append($",{prefix}_OpenNextGoalWithin{window}Rate");
        }
        sb.AppendLine();

        foreach (ContinuationSummaryRow row in rows)
        {
            sb.Append(string.Join(',',
                EscapeCsv(row.BucketType),
                EscapeCsv(row.MinuteBand),
                EscapeCsv(row.GoalEffect),
                EscapeCsv(row.GoalSide),
                row.GoalNumber == 0 ? string.Empty : row.GoalNumber.ToString(CultureInfo.InvariantCulture),
                EscapeCsv(row.ScoreBefore),
                EscapeCsv(row.ScoreAfter),
                EscapeCsv(row.ScoreStateAfter),
                row.Rows.ToString(CultureInfo.InvariantCulture),
                row.Matches.ToString(CultureInfo.InvariantCulture),
                D(row.AverageMinute),
                D(row.AverageCurrentTotalGoals),
                D(row.AverageFinalTotalGoals),
                D(row.AverageFinalGoalsAfterThis),
                row.NextGoalRows.ToString(CultureInfo.InvariantCulture),
                D(row.AverageMinutesToNextGoal),
                D(row.NoNextGoalRate)));

            foreach (int window in windows)
            {
                sb.Append(',');
                sb.Append(D(row.WindowRates.TryGetValue(window, out double rate) ? rate : double.NaN));
            }

            foreach (double line in lines)
            {
                row.LineSummaries.TryGetValue(line, out LineSummaryRow? lineRow);
                sb.Append(',');
                sb.Append(lineRow?.OpenRows.ToString(CultureInfo.InvariantCulture) ?? "0");
                sb.Append(',');
                sb.Append(lineRow?.OpenMatches.ToString(CultureInfo.InvariantCulture) ?? "0");
                sb.Append(',');
                sb.Append(D(lineRow?.OpenOverRate ?? double.NaN));
                foreach (int window in windows)
                {
                    sb.Append(',');
                    double rate = double.NaN;
                    if (lineRow is not null && !lineRow.OpenWindowRates.TryGetValue(window, out rate))
                        rate = double.NaN;
                    sb.Append(D(rate));
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string MinuteBand(int minute) => minute switch
    {
        <= 20 => "1-20",
        <= 35 => "21-35",
        <= 50 => "36-50",
        <= 65 => "51-65",
        _ => "66-90"
    };

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;

        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(_options.InputPath);
        return Path.Combine(directory, $"{fileName}-after-goal-continuation.csv");
    }

    private string ResolveSummaryOutputPath(string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(_options.SummaryOutputPath))
            return _options.SummaryOutputPath;

        string directory = Path.GetDirectoryName(outputPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(outputPath);
        return Path.Combine(directory, $"{fileName}-summary.csv");
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Live total calibration dataset CSV was not found.", _options.InputPath);
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        if (_options.Windows.Count == 0)
            throw new ArgumentException("At least one continuation window is required.");
        if (_options.Windows.Any(x => x <= 0))
            throw new ArgumentException("Continuation windows must be positive minute counts.");
        if (_options.MinSummaryRows < 1)
            throw new ArgumentException("--min-summary-rows must be >= 1.");
    }

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
            return [];

        string[] headers = records[0].Select(x => x.Trim()).ToArray();
        var index = headers.Select((name, position) => new { name, position })
            .ToDictionary(x => x.name, x => x.position, StringComparer.OrdinalIgnoreCase);

        foreach (string required in new[]
        {
            "SeasonId", "MatchId", "StateTrigger", "TriggerEventSide", "Minute", "HomeGoals", "AwayGoals",
            "CurrentTotalGoals", "ActualFinalTotalGoals"
        })
        {
            if (!index.ContainsKey(required))
                throw new ArgumentException($"Input CSV is missing required column '{required}'. Rebuild the calibration dataset with the latest builder.");
        }

        var rows = new List<InputRow>();
        foreach (List<string> record in records.Skip(1))
        {
            if (record.Count == 1 && string.IsNullOrWhiteSpace(record[0]))
                continue;

            if (!TryGetInt(record, index, "SeasonId", out int seasonId) ||
                !TryGetLong(record, index, "MatchId", out long matchId) ||
                !TryGetInt(record, index, "Minute", out int minute) ||
                !TryGetInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryGetInt(record, index, "AwayGoals", out int awayGoals) ||
                !TryGetInt(record, index, "CurrentTotalGoals", out int currentTotalGoals) ||
                !TryGetInt(record, index, "ActualFinalTotalGoals", out int actualFinalTotalGoals))
                continue;

            rows.Add(new InputRow
            {
                SeasonId = seasonId,
                MatchId = matchId,
                StateTrigger = LiveTotalStateTrigger.Normalize(GetString(record, index, "StateTrigger")),
                TriggerEventSide = GetString(record, index, "TriggerEventSide"),
                Minute = minute,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                CurrentTotalGoals = currentTotalGoals,
                ActualFinalTotalGoals = actualFinalTotalGoals
            });
        }

        return rows;
    }

    private static bool TryGetInt(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out int value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               int.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetLong(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column, out long value)
    {
        value = 0;
        return index.TryGetValue(column, out int position) &&
               position < record.Count &&
               long.TryParse(record[position], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string GetString(IReadOnlyList<string> record, IReadOnlyDictionary<string, int> index, string column) =>
        index.TryGetValue(column, out int position) && position < record.Count ? record[position] : string.Empty;

    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }

    private static string LinePrefix(double line) => $"Over{line.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', '_')}";

    private static string D(double value) => double.IsNaN(value) || double.IsInfinity(value)
        ? string.Empty
        : value.ToString("0.######", CultureInfo.InvariantCulture);

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static int SummaryBucketOrder(string bucketType) => bucketType switch
    {
        "All" => 1,
        "GoalEffect" => 2,
        "MinuteBand+GoalEffect" => 3,
        "GoalEffect+GoalSide" => 4,
        "MinuteBand+GoalEffect+GoalSide" => 5,
        "MinuteBand+GoalNumber+GoalEffect" => 6,
        "MinuteBand+GoalEffect+ScoreStateAfter" => 7,
        "ExactScore" => 8,
        _ => 99
    };

    private static int MinuteBandOrder(string band) => band switch
    {
        "1-20" => 1,
        "21-35" => 2,
        "36-50" => 3,
        "51-65" => 4,
        "66-90" => 5,
        _ => 99
    };

    private static int GoalEffectOrder(string effect) => effect switch
    {
        "FirstGoal" => 1,
        "Equalizer" => 2,
        "CreatesOneGoalLead" => 3,
        "CutsDeficit" => 4,
        "ExtendsToTwoGoalLead" => 5,
        "ExtendsToThreePlusLead" => 6,
        _ => 99
    };

    private sealed class InputRow
    {
        public int SeasonId { get; set; }
        public long MatchId { get; set; }
        public string StateTrigger { get; set; } = LiveTotalStateTrigger.FixedMinute;
        public string TriggerEventSide { get; set; } = string.Empty;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public int ActualFinalTotalGoals { get; set; }
    }

    private sealed class GoalContext
    {
        public string GoalSide { get; set; } = string.Empty;
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string Effect { get; set; } = string.Empty;
    }

    private sealed class ContinuationRow
    {
        public string League { get; set; } = string.Empty;
        public int SeasonId { get; set; }
        public long MatchId { get; set; }
        public int Minute { get; set; }
        public string MinuteBand { get; set; } = string.Empty;
        public int GoalNumber { get; set; }
        public string GoalSide { get; set; } = string.Empty;
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string ScoreStateAfter { get; set; } = string.Empty;
        public string GoalEffect { get; set; } = string.Empty;
        public int CurrentTotalGoals { get; set; }
        public int FinalTotalGoals { get; set; }
        public int FinalGoalsAfterThis { get; set; }
        public int? NextGoalMinute { get; set; }
        public int? MinutesToNextGoal { get; set; }
        public int? NextGoalNumber { get; set; }
        public string NextGoalSide { get; set; } = string.Empty;
        public Dictionary<int, bool> WindowResults { get; } = [];
        public Dictionary<double, LineContinuationResult> LineResults { get; } = [];
    }

    private sealed class LineContinuationResult
    {
        public double Line { get; set; }
        public bool IsOpen { get; set; }
        public bool? ActualOver { get; set; }
    }

    private sealed class ContinuationSummaryRow
    {
        public string BucketType { get; set; } = string.Empty;
        public string MinuteBand { get; set; } = string.Empty;
        public string GoalEffect { get; set; } = string.Empty;
        public string GoalSide { get; set; } = string.Empty;
        public int GoalNumber { get; set; }
        public string ScoreBefore { get; set; } = string.Empty;
        public string ScoreAfter { get; set; } = string.Empty;
        public string ScoreStateAfter { get; set; } = string.Empty;
        public int Rows { get; set; }
        public int Matches { get; set; }
        public double AverageMinute { get; set; }
        public double AverageCurrentTotalGoals { get; set; }
        public double AverageFinalTotalGoals { get; set; }
        public double AverageFinalGoalsAfterThis { get; set; }
        public int NextGoalRows { get; set; }
        public double AverageMinutesToNextGoal { get; set; }
        public double NoNextGoalRate { get; set; }
        public Dictionary<int, double> WindowRates { get; } = [];
        public Dictionary<double, LineSummaryRow> LineSummaries { get; } = [];
    }

    private sealed class LineSummaryRow
    {
        public double Line { get; set; }
        public int OpenRows { get; set; }
        public int OpenMatches { get; set; }
        public double OpenOverRate { get; set; }
        public Dictionary<int, double> OpenWindowRates { get; } = [];
    }
}
