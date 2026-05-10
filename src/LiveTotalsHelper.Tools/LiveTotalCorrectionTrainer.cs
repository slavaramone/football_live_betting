using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class LiveTotalCorrectionTrainOptions
{
    public string InputPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; } = [];
    public List<int> TestSeasonIds { get; } = [];
    public string OutputModelPath { get; set; } = string.Empty;
    public string OutputPredictionsPath { get; set; } = string.Empty;
    public int MaxIterations { get; set; } = 2000;
    public double LearningRate { get; set; } = 0.05;
    public double L2 { get; set; } = 0.001;
}

public sealed class LiveTotalCorrectionTrainResult
{
    public int RowsRead { get; set; }
    public int DecisiveRowsUsed { get; set; }
    public int TrainingRows { get; set; }
    public int TestRows { get; set; }
    public string OutputModelPath { get; set; } = string.Empty;
    public string OutputPredictionsPath { get; set; } = string.Empty;
    public BinaryMetricSummary BaselineMetrics { get; set; } = new();
    public BinaryMetricSummary BaselineOnlyModelMetrics { get; set; } = new();
    public BinaryMetricSummary CorrectedModelMetrics { get; set; } = new();
    public List<string> Warnings { get; } = [];
}

public sealed class BinaryMetricSummary
{
    public int Count { get; set; }
    public double LogLoss { get; set; }
    public double BrierScore { get; set; }
    public double AveragePrediction { get; set; }
    public double ActualOverRate { get; set; }
}

public sealed class LiveTotalCorrectionModelFile
{
    public string ModelType { get; set; } = "live-total-correction-logistic";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<int> TrainingSeasonIds { get; set; } = [];
    public List<int> TestSeasonIds { get; set; } = [];
    public string[] BaselineOnlyFeatures { get; set; } = [];
    public double[] BaselineOnlyCoefficients { get; set; } = [];
    public string[] CorrectedFeatures { get; set; } = [];
    public double[] CorrectedCoefficients { get; set; } = [];
}

public sealed class LiveTotalCorrectionTrainer
{
    private readonly LiveTotalCorrectionTrainOptions _options;

    private static readonly string[] BaselineOnlyFeatureNames = ["Intercept", "BaselineLogit"];
    private static readonly string[] CorrectedFeatureNames = [
        "Intercept", "BaselineLogit", "MinuteScaled", "CurrentTotalGoals", "IsNilNil", "IsLevelWithGoals", "IsOneGoalMargin", "IsTwoGoalMargin", "IsThreePlusGoalMargin"
    ];

    public LiveTotalCorrectionTrainer(LiveTotalCorrectionTrainOptions options)
    {
        _options = options;
    }

    public async Task<LiveTotalCorrectionTrainResult> TrainAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        List<CorrectionRow> allRows = await CorrectionCsvReader.ReadAsync(_options.InputPath, cancellationToken);
        var result = new LiveTotalCorrectionTrainResult { RowsRead = allRows.Count };

        List<CorrectionRow> rows = allRows.Where(x => x.IsDecisiveBinaryOutcome).ToList();
        result.DecisiveRowsUsed = rows.Count;
        List<CorrectionRow> training = rows.Where(x => _options.TrainingSeasonIds.Contains(x.SeasonId)).ToList();
        List<CorrectionRow> test = rows.Where(x => _options.TestSeasonIds.Contains(x.SeasonId)).ToList();
        result.TrainingRows = training.Count;
        result.TestRows = test.Count;

        if (training.Count == 0)
            throw new ArgumentException("No decisive training rows were found for --training-season-ids.");
        if (test.Count == 0)
            throw new ArgumentException("No decisive test rows were found for --test-season-ids.");

        double[] baselineOnlyCoefficients = FitLogistic(training, BaselineOnlyFeatures, BaselineOnlyFeatureNames.Length);
        double[] correctedCoefficients = FitLogistic(training, CorrectedFeatures, CorrectedFeatureNames.Length);

        List<CorrectionPredictionRow> predictions = test.Select(row => new CorrectionPredictionRow
        {
            SeasonId = row.SeasonId,
            SofaScoreEventId = row.SofaScoreEventId,
            Minute = row.Minute,
            Line = row.Line,
            DetailedScoreState = row.DetailedScoreState,
            CurrentTotalGoals = row.CurrentTotalGoals,
            ActualOverWin = row.ActualOverWin,
            BaselineProbability = ClampProbability(row.BaselineNoPushOverProbability),
            BaselineOnlyModelProbability = Predict(BaselineOnlyFeatures(row), baselineOnlyCoefficients),
            CorrectedModelProbability = Predict(CorrectedFeatures(row), correctedCoefficients)
        }).ToList();

        result.BaselineMetrics = Summarize(predictions, x => x.BaselineProbability);
        result.BaselineOnlyModelMetrics = Summarize(predictions, x => x.BaselineOnlyModelProbability);
        result.CorrectedModelMetrics = Summarize(predictions, x => x.CorrectedModelProbability);

        if (!string.IsNullOrWhiteSpace(_options.OutputModelPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.OutputModelPath)) ?? ".");
            var model = new LiveTotalCorrectionModelFile
            {
                TrainingSeasonIds = _options.TrainingSeasonIds.Distinct().OrderBy(x => x).ToList(),
                TestSeasonIds = _options.TestSeasonIds.Distinct().OrderBy(x => x).ToList(),
                BaselineOnlyFeatures = BaselineOnlyFeatureNames,
                BaselineOnlyCoefficients = baselineOnlyCoefficients,
                CorrectedFeatures = CorrectedFeatureNames,
                CorrectedCoefficients = correctedCoefficients
            };
            await File.WriteAllTextAsync(_options.OutputModelPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            result.OutputModelPath = _options.OutputModelPath;
        }

        if (!string.IsNullOrWhiteSpace(_options.OutputPredictionsPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_options.OutputPredictionsPath)) ?? ".");
            await File.WriteAllTextAsync(_options.OutputPredictionsPath, ToCsv(predictions), cancellationToken);
            result.OutputPredictionsPath = _options.OutputPredictionsPath;
        }

        return result;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.InputPath))
            throw new ArgumentException("Missing required argument --input.");
        if (!File.Exists(_options.InputPath))
            throw new FileNotFoundException("Calibration dataset CSV was not found.", _options.InputPath);
        if (_options.TrainingSeasonIds.Count == 0)
            throw new ArgumentException("Provide --training-season-ids.");
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Provide --test-season-ids.");
        if (_options.MaxIterations < 1)
            throw new ArgumentException("--max-iterations must be at least 1.");
        if (_options.LearningRate <= 0)
            throw new ArgumentException("--learning-rate must be greater than 0.");
        if (_options.L2 < 0)
            throw new ArgumentException("--l2 must be zero or greater.");
    }

    private double[] FitLogistic(IReadOnlyList<CorrectionRow> rows, Func<CorrectionRow, double[]> featureSelector, int featureCount)
    {
        var beta = new double[featureCount];
        for (int iter = 0; iter < _options.MaxIterations; iter++)
        {
            var gradient = new double[featureCount];
            foreach (CorrectionRow row in rows)
            {
                double[] x = featureSelector(row);
                double p = Predict(x, beta);
                double error = p - row.ActualOverWin;
                for (int j = 0; j < featureCount; j++)
                    gradient[j] += error * x[j];
            }

            for (int j = 0; j < featureCount; j++)
            {
                double penalty = j == 0 ? 0.0 : _options.L2 * beta[j];
                gradient[j] = (gradient[j] / rows.Count) + penalty;
                beta[j] -= _options.LearningRate * gradient[j];
            }
        }
        return beta;
    }

    private static double[] BaselineOnlyFeatures(CorrectionRow row) =>
        [1.0, Logit(row.BaselineNoPushOverProbability)];

    private static double[] CorrectedFeatures(CorrectionRow row) =>
        [
            1.0,
            Logit(row.BaselineNoPushOverProbability),
            row.Minute / 90.0,
            row.CurrentTotalGoals,
            row.DetailedScoreState == "NilNil" ? 1.0 : 0.0,
            row.DetailedScoreState == "LevelWithGoals" ? 1.0 : 0.0,
            row.DetailedScoreState == "OneGoalMargin" ? 1.0 : 0.0,
            row.DetailedScoreState == "TwoGoalMargin" ? 1.0 : 0.0,
            row.DetailedScoreState == "ThreePlusGoalMargin" ? 1.0 : 0.0
        ];

    private static double Predict(double[] x, double[] beta)
    {
        double z = 0.0;
        for (int i = 0; i < beta.Length; i++)
            z += x[i] * beta[i];
        if (z >= 0)
        {
            double ez = Math.Exp(-z);
            return 1.0 / (1.0 + ez);
        }
        else
        {
            double ez = Math.Exp(z);
            return ez / (1.0 + ez);
        }
    }

    private static double Logit(double p)
    {
        p = ClampProbability(p);
        return Math.Log(p / (1.0 - p));
    }

    private static double ClampProbability(double p) => Math.Clamp(p, 1e-6, 1.0 - 1e-6);

    private static BinaryMetricSummary Summarize(IReadOnlyCollection<CorrectionPredictionRow> rows, Func<CorrectionPredictionRow, double> selector)
    {
        double logLoss = 0.0;
        double brier = 0.0;
        double pred = 0.0;
        double actual = 0.0;
        foreach (CorrectionPredictionRow row in rows)
        {
            double p = ClampProbability(selector(row));
            double y = row.ActualOverWin;
            logLoss += -(y * Math.Log(p) + (1.0 - y) * Math.Log(1.0 - p));
            brier += Math.Pow(p - y, 2);
            pred += p;
            actual += y;
        }

        return new BinaryMetricSummary
        {
            Count = rows.Count,
            LogLoss = logLoss / rows.Count,
            BrierScore = brier / rows.Count,
            AveragePrediction = pred / rows.Count,
            ActualOverRate = actual / rows.Count
        };
    }

    private static string ToCsv(IEnumerable<CorrectionPredictionRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("SeasonId,SofaScoreEventId,Minute,Line,DetailedScoreState,CurrentTotalGoals,ActualOverWin,BaselineProbability,BaselineOnlyModelProbability,CorrectedModelProbability");
        foreach (CorrectionPredictionRow row in rows)
        {
            sb.AppendLine(string.Join(',',
                row.SeasonId.ToString(CultureInfo.InvariantCulture),
                row.SofaScoreEventId.ToString(CultureInfo.InvariantCulture),
                row.Minute.ToString(CultureInfo.InvariantCulture),
                row.Line.ToString("0.######", CultureInfo.InvariantCulture),
                row.DetailedScoreState,
                row.CurrentTotalGoals.ToString(CultureInfo.InvariantCulture),
                row.ActualOverWin.ToString(CultureInfo.InvariantCulture),
                row.BaselineProbability.ToString("0.######", CultureInfo.InvariantCulture),
                row.BaselineOnlyModelProbability.ToString("0.######", CultureInfo.InvariantCulture),
                row.CorrectedModelProbability.ToString("0.######", CultureInfo.InvariantCulture)));
        }
        return sb.ToString();
    }
}

internal sealed class CorrectionRow
{
    public int SeasonId { get; set; }
    public long SofaScoreEventId { get; set; }
    public int Minute { get; set; }
    public double Line { get; set; }
    public string DetailedScoreState { get; set; } = string.Empty;
    public int CurrentTotalGoals { get; set; }
    public double BaselineNoPushOverProbability { get; set; }
    public bool IsDecisiveBinaryOutcome { get; set; }
    public int ActualOverWin { get; set; }
}

internal sealed class CorrectionPredictionRow
{
    public int SeasonId { get; set; }
    public long SofaScoreEventId { get; set; }
    public int Minute { get; set; }
    public double Line { get; set; }
    public string DetailedScoreState { get; set; } = string.Empty;
    public int CurrentTotalGoals { get; set; }
    public int ActualOverWin { get; set; }
    public double BaselineProbability { get; set; }
    public double BaselineOnlyModelProbability { get; set; }
    public double CorrectedModelProbability { get; set; }
}

internal static class CorrectionCsvReader
{
    public static async Task<List<CorrectionRow>> ReadAsync(string path, CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(path, cancellationToken);
        if (lines.Length == 0)
            throw new ArgumentException("Calibration dataset CSV is empty.");

        string[] header = ParseCsvLine(lines[0]).ToArray();
        int season = FindRequiredColumn(header, "SofaScoreSeasonId");
        int eventId = FindRequiredColumn(header, "SofaScoreEventId");
        int minute = FindRequiredColumn(header, "Minute");
        int line = FindRequiredColumn(header, "Line");
        int detailedState = FindRequiredColumn(header, "DetailedScoreState");
        int currentTotal = FindRequiredColumn(header, "CurrentTotalGoals");
        int baseline = FindRequiredColumn(header, "BaselineNoPushOverProbability");
        int decisive = FindRequiredColumn(header, "IsDecisiveBinaryOutcome");
        int actual = FindRequiredColumn(header, "ActualOverWinFraction");

        int maxIndex = new[] { season, eventId, minute, line, detailedState, currentTotal, baseline, decisive, actual }.Max();
        var result = new List<CorrectionRow>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            string[] cells = ParseCsvLine(lines[i]).ToArray();
            if (cells.Length <= maxIndex)
                throw new ArgumentException($"Calibration dataset row {i + 1} has too few columns.");

            double actualFraction = ParseDouble(cells[actual], "ActualOverWinFraction", i + 1);
            result.Add(new CorrectionRow
            {
                SeasonId = ParseInt(cells[season], "SofaScoreSeasonId", i + 1),
                SofaScoreEventId = ParseLong(cells[eventId], "SofaScoreEventId", i + 1),
                Minute = ParseInt(cells[minute], "Minute", i + 1),
                Line = ParseDouble(cells[line], "Line", i + 1),
                DetailedScoreState = cells[detailedState],
                CurrentTotalGoals = ParseInt(cells[currentTotal], "CurrentTotalGoals", i + 1),
                BaselineNoPushOverProbability = ParseDouble(cells[baseline], "BaselineNoPushOverProbability", i + 1),
                IsDecisiveBinaryOutcome = ParseBool(cells[decisive], "IsDecisiveBinaryOutcome", i + 1),
                ActualOverWin = actualFraction >= 0.5 ? 1 : 0
            });
        }
        return result;
    }

    private static int FindRequiredColumn(string[] header, string name)
    {
        int index = Array.FindIndex(header, x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            return index;
        throw new ArgumentException($"Calibration dataset CSV must contain column {name}.");
    }

    private static int ParseInt(string value, string column, int row) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : throw new ArgumentException($"Calibration dataset row {row}: {column} must be an integer.");

    private static long ParseLong(string value, string column, int row) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new ArgumentException($"Calibration dataset row {row}: {column} must be an integer.");

    private static double ParseDouble(string value, string column, int row) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : throw new ArgumentException($"Calibration dataset row {row}: {column} must be a number.");

    private static bool ParseBool(string value, string column, int row)
    {
        if (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        throw new ArgumentException($"Calibration dataset row {row}: {column} must be 0/1 or true/false.");
    }

    private static IEnumerable<string> ParseCsvLine(string line)
    {
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                yield return current.ToString();
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        yield return current.ToString();
    }
}
