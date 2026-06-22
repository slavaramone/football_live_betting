using System.Globalization;
using System.Text;
using System.Text.Json;
using LiveTotalsHelper.Modeling;

namespace LiveTotalsHelper.Tools;

public sealed class GoalModelComparisonOptions
{
    public string InputPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string EmpiricalSettlementPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public List<int> TestSeasonIds { get; } = [];
    public List<double> TargetLines { get; } = [2.5, 3.5];
    public double EdgeThreshold { get; set; } = 0.05;
    public double MarketMargin { get; set; } = 0.05;
}

public sealed class GoalModelComparisonResult
{
    public string InputPath { get; set; } = string.Empty;
    public string OutputPath { get; set; } = string.Empty;
    public int RowsRead { get; set; }
    public int TestRows { get; set; }
    public int SupportedRows { get; set; }
    public int UnsupportedEmpiricalRows { get; set; }
    public GoalModelComparisonSummary Overall { get; set; } = new();
    public List<GoalModelComparisonSummary> Summaries { get; } = [];
}

public sealed class GoalModelComparisonSummary
{
    public string StateTrigger { get; set; } = "All";
    public double? Line { get; set; }
    public int Rows { get; set; }
    public int Matches { get; set; }
    public double PoissonBrier { get; set; }
    public double EmpiricalBrier { get; set; }
    public double BrierImprovementPct { get; set; }
    public double PoissonLogLoss { get; set; }
    public double EmpiricalLogLoss { get; set; }
    public double LogLossImprovementPct { get; set; }
    public double EmpiricalBetterRate { get; set; }
    public int BenchmarkBets { get; set; }
    public double BenchmarkProfit { get; set; }
    public double BenchmarkRoi { get; set; }
    public bool ShowsImprovement => BrierImprovementPct > 0 && LogLossImprovementPct > 0;
}

public sealed class GoalModelComparisonEvaluator
{
    private readonly GoalModelComparisonOptions _options;

    public GoalModelComparisonEvaluator(GoalModelComparisonOptions options) => _options = options;

    public async Task<GoalModelComparisonResult> EvaluateAsync(CancellationToken cancellationToken)
    {
        ValidateOptions();
        List<InputRow> rows = await ReadRowsAsync(_options.InputPath, cancellationToken);

        LiveTotalStateCorrectionFile correction = await ReadJsonAsync<LiveTotalStateCorrectionFile>(
            _options.StateCorrectionPath, "state correction", cancellationToken);
        LiveTotalEmpiricalSettlementFile empirical = await ReadJsonAsync<LiveTotalEmpiricalSettlementFile>(
            _options.EmpiricalSettlementPath, "empirical settlement", cancellationToken);

        List<InputRow> testRows = rows.Where(x => _options.TestSeasonIds.Contains(x.SeasonId)).ToList();
        var evaluated = new List<EvaluatedRow>();
        int unsupported = 0;

        foreach (InputRow row in testRows)
        {
            LiveTotalStateCorrectionResolution state = LiveTotalStateCorrectionResolver.Resolve(
                correction, row.StateTrigger, row.Minute, row.HomeGoals, row.AwayGoals);
            LiveTotalEmpiricalSettlementResolution distribution = LiveTotalEmpiricalSettlementResolver.Resolve(
                empirical, row.StateTrigger, row.Minute, row.HomeGoals, row.AwayGoals);

            if (!distribution.IsSupported || distribution.Probabilities.Count == 0)
            {
                unsupported++;
                continue;
            }

            double remainingMean = Math.Max(0.0, correction.LeagueAverageFinalGoals * row.TimingRemainingShare * state.Factor);
            foreach (double line in _options.TargetLines.Distinct().OrderBy(x => x))
            {
                OverSettlementProbabilities poisson = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(
                    line, row.CurrentTotalGoals, remainingMean);
                OverSettlementProbabilities empiricalPrice = TotalGoalsPricingCalculator.CalculateOverSettlementProbabilities(
                    line, row.CurrentTotalGoals, distribution.Probabilities, remainingMean);

                double poissonNoPush = NoPushProbability(poisson);
                double empiricalNoPush = NoPushProbability(empiricalPrice);
                bool? actualOver = TryActualOver(line, row.ActualFinalTotalGoals);
                if (!actualOver.HasValue)
                    continue;

                BenchmarkBet? bet = BuildBenchmarkBet(line, row.ActualFinalTotalGoals, poisson, empiricalPrice);
                evaluated.Add(new EvaluatedRow
                {
                    MatchId = row.MatchId,
                    StateTrigger = row.StateTrigger,
                    Line = line,
                    ActualOver = actualOver.Value,
                    PoissonProbability = poissonNoPush,
                    EmpiricalProbability = empiricalNoPush,
                    Bet = bet
                });
            }
        }

        if (evaluated.Count == 0)
            throw new InvalidOperationException("No supported test rows were available for the requested seasons and lines.");

        var result = new GoalModelComparisonResult
        {
            InputPath = _options.InputPath,
            OutputPath = ResolveOutputPath(),
            RowsRead = rows.Count,
            TestRows = testRows.Count,
            SupportedRows = testRows.Count - unsupported,
            UnsupportedEmpiricalRows = unsupported,
            Overall = BuildSummary("All", null, evaluated)
        };

        result.Summaries.AddRange(evaluated
            .SelectMany(x => new[]
            {
                new { Trigger = "All", Row = x },
                new { Trigger = x.StateTrigger, Row = x }
            })
            .GroupBy(x => new { x.Trigger, x.Row.Line })
            .OrderBy(x => TriggerOrder(x.Key.Trigger))
            .ThenBy(x => x.Key.Line)
            .Select(x => BuildSummary(x.Key.Trigger, x.Key.Line, x.Select(y => y.Row).ToList())));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(result.OutputPath));
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(result.OutputPath, ToCsv(result), Encoding.UTF8, cancellationToken);
        return result;
    }

    private BenchmarkBet? BuildBenchmarkBet(
        double line,
        int finalTotal,
        OverSettlementProbabilities poisson,
        OverSettlementProbabilities empirical)
    {
        double overOdds = ApplyMargin(TotalGoalsPricingCalculator.CalculateFairOdds(poisson));
        var poissonUnder = new OverSettlementProbabilities(poisson.LossProbability, poisson.PushProbability, poisson.WinProbability);
        double underOdds = ApplyMargin(TotalGoalsPricingCalculator.CalculateFairOdds(poissonUnder));

        double overEv = empirical.WinProbability * (overOdds - 1.0) - empirical.LossProbability;
        double underEv = empirical.LossProbability * (underOdds - 1.0) - empirical.WinProbability;
        if (Math.Max(overEv, underEv) < _options.EdgeThreshold)
            return null;

        bool over = overEv >= underEv;
        double odds = over ? overOdds : underOdds;
        return new BenchmarkBet
        {
            Profit = RealizedProfit(line, finalTotal, odds, over)
        };
    }

    private double ApplyMargin(double fairOdds)
    {
        if (double.IsInfinity(fairOdds) || fairOdds <= 1.0)
            return 1.000001;
        return 1.0 + (fairOdds - 1.0) / (1.0 + _options.MarketMargin);
    }

    private static double RealizedProfit(double line, int total, double odds, bool over)
    {
        double fraction = Math.Round(line - Math.Floor(line), 6);
        if (Math.Abs(fraction - 0.25) < 1e-6)
            return (RealizedProfit(Math.Floor(line), total, odds, over) + RealizedProfit(Math.Floor(line) + 0.5, total, odds, over)) / 2.0;
        if (Math.Abs(fraction - 0.75) < 1e-6)
            return (RealizedProfit(Math.Floor(line) + 0.5, total, odds, over) + RealizedProfit(Math.Floor(line) + 1.0, total, odds, over)) / 2.0;

        if (Math.Abs(total - line) < 1e-6)
            return 0.0;
        bool won = over ? total > line : total < line;
        return won ? odds - 1.0 : -1.0;
    }

    private static GoalModelComparisonSummary BuildSummary(string trigger, double? line, IReadOnlyCollection<EvaluatedRow> rows)
    {
        double poissonBrier = rows.Average(x => Squared(x.PoissonProbability - Bool(x.ActualOver)));
        double empiricalBrier = rows.Average(x => Squared(x.EmpiricalProbability - Bool(x.ActualOver)));
        double poissonLogLoss = rows.Average(x => LogLoss(x.PoissonProbability, x.ActualOver));
        double empiricalLogLoss = rows.Average(x => LogLoss(x.EmpiricalProbability, x.ActualOver));
        List<BenchmarkBet> bets = rows.Where(x => x.Bet is not null).Select(x => x.Bet!).ToList();
        double profit = bets.Sum(x => x.Profit);

        return new GoalModelComparisonSummary
        {
            StateTrigger = trigger,
            Line = line,
            Rows = rows.Count,
            Matches = rows.Select(x => x.MatchId).Distinct().Count(),
            PoissonBrier = poissonBrier,
            EmpiricalBrier = empiricalBrier,
            BrierImprovementPct = Improvement(poissonBrier, empiricalBrier),
            PoissonLogLoss = poissonLogLoss,
            EmpiricalLogLoss = empiricalLogLoss,
            LogLossImprovementPct = Improvement(poissonLogLoss, empiricalLogLoss),
            EmpiricalBetterRate = rows.Average(x =>
                Squared(x.EmpiricalProbability - Bool(x.ActualOver)) < Squared(x.PoissonProbability - Bool(x.ActualOver)) ? 1.0 : 0.0),
            BenchmarkBets = bets.Count,
            BenchmarkProfit = profit,
            BenchmarkRoi = bets.Count == 0 ? 0.0 : profit / bets.Count
        };
    }

    private static double NoPushProbability(OverSettlementProbabilities price)
    {
        double decisive = price.WinProbability + price.LossProbability;
        return decisive <= 1e-12 ? 0.5 : Math.Clamp(price.WinProbability / decisive, 0.0, 1.0);
    }

    private static bool? TryActualOver(double line, int total)
    {
        double fraction = Math.Round(line - Math.Floor(line), 6);
        if (Math.Abs(fraction) < 1e-6 && total == (int)line)
            return null;
        if (Math.Abs(fraction - 0.25) < 1e-6 && total == (int)Math.Floor(line))
            return null;
        if (Math.Abs(fraction - 0.75) < 1e-6 && total == (int)Math.Floor(line) + 1)
            return null;
        return fraction is >= 0 and < 1 ? total > line : null;
    }

    private static double Squared(double value) => value * value;
    private static double Bool(bool value) => value ? 1.0 : 0.0;
    private static double LogLoss(double probability, bool actual)
    {
        probability = Math.Clamp(probability, 1e-6, 1.0 - 1e-6);
        return actual ? -Math.Log(probability) : -Math.Log(1.0 - probability);
    }
    private static double Improvement(double baseline, double candidate) => baseline <= 0 ? 0 : (baseline - candidate) / baseline * 100.0;

    private string ResolveOutputPath()
    {
        if (!string.IsNullOrWhiteSpace(_options.OutputPath))
            return _options.OutputPath;
        string directory = Path.GetDirectoryName(_options.InputPath) ?? ".";
        return Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(_options.InputPath)}-poisson-vs-empirical.csv");
    }

    private void ValidateOptions()
    {
        RequireFile(_options.InputPath, "calibration dataset", "--input");
        RequireFile(_options.StateCorrectionPath, "state correction", "--state-correction");
        RequireFile(_options.EmpiricalSettlementPath, "empirical settlement", "--empirical-settlement");
        if (_options.TestSeasonIds.Count == 0)
            throw new ArgumentException("Missing --test-season-ids, or use --validation true with a profile validation split.");
        if (_options.TargetLines.Count == 0)
            throw new ArgumentException("At least one target line is required.");
        if (_options.EdgeThreshold < 0)
            throw new ArgumentException("--edge-threshold cannot be negative.");
        if (_options.MarketMargin < 0 || _options.MarketMargin >= 1)
            throw new ArgumentException("--market-margin must be between 0 and 1.");
    }

    private static void RequireFile(string path, string description, string argument)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"Missing {argument}.");
        if (!File.Exists(path))
            throw new FileNotFoundException($"{description} file was not found.", path);
    }

    private static async Task<T> ReadJsonAsync<T>(string path, string description, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
            ?? throw new InvalidOperationException($"Could not read {description} JSON.");
    }

    private static int TriggerOrder(string trigger) => trigger.Equals("All", StringComparison.OrdinalIgnoreCase) ? 0 :
        LiveTotalStateTrigger.Normalize(trigger) switch
        {
            LiveTotalStateTrigger.FixedMinute => 1,
            LiveTotalStateTrigger.AfterGoal => 2,
            LiveTotalStateTrigger.AfterRedCard => 3,
            _ => 99
        };

    private static string ToCsv(GoalModelComparisonResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("StateTrigger,Line,Rows,Matches,PoissonBrier,EmpiricalBrier,BrierImprovementPct,PoissonLogLoss,EmpiricalLogLoss,LogLossImprovementPct,EmpiricalBetterRate,BenchmarkBets,BenchmarkProfit,BenchmarkRoi,ShowsImprovement");
        foreach (GoalModelComparisonSummary row in new[] { result.Overall }.Concat(result.Summaries))
        {
            static string D(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
            sb.AppendLine(string.Join(',', row.StateTrigger, row.Line?.ToString("0.##", CultureInfo.InvariantCulture) ?? "All",
                row.Rows, row.Matches, D(row.PoissonBrier), D(row.EmpiricalBrier), D(row.BrierImprovementPct),
                D(row.PoissonLogLoss), D(row.EmpiricalLogLoss), D(row.LogLossImprovementPct), D(row.EmpiricalBetterRate),
                row.BenchmarkBets, D(row.BenchmarkProfit), D(row.BenchmarkRoi), row.ShowsImprovement));
        }
        return sb.ToString();
    }

    private static async Task<List<InputRow>> ReadRowsAsync(string path, CancellationToken cancellationToken)
    {
        string text = await File.ReadAllTextAsync(path, cancellationToken);
        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0) return [];
        var index = records[0].Select((name, position) => new { name, position })
            .ToDictionary(x => x.name.Trim(), x => x.position, StringComparer.OrdinalIgnoreCase);
        string[] required = ["SeasonId", "MatchId", "StateTrigger", "Minute", "HomeGoals", "AwayGoals", "CurrentTotalGoals", "TimingRemainingShare", "ActualFinalTotalGoals"];
        foreach (string column in required)
            if (!index.ContainsKey(column)) throw new ArgumentException($"Input CSV is missing required column '{column}'.");

        var rows = new List<InputRow>();
        foreach (List<string> record in records.Skip(1))
        {
            if (!TryInt(record, index, "SeasonId", out int seasonId) || !TryInt(record, index, "MatchId", out int matchId) ||
                !TryInt(record, index, "Minute", out int minute) || !TryInt(record, index, "HomeGoals", out int homeGoals) ||
                !TryInt(record, index, "AwayGoals", out int awayGoals) || !TryInt(record, index, "CurrentTotalGoals", out int currentTotal) ||
                !TryDouble(record, index, "TimingRemainingShare", out double remainingShare) || !TryInt(record, index, "ActualFinalTotalGoals", out int finalTotal)) continue;
            rows.Add(new InputRow { SeasonId = seasonId, MatchId = matchId, StateTrigger = LiveTotalStateTrigger.Normalize(Get(record, index, "StateTrigger")), Minute = minute,
                HomeGoals = homeGoals, AwayGoals = awayGoals, CurrentTotalGoals = currentTotal, TimingRemainingShare = remainingShare, ActualFinalTotalGoals = finalTotal });
        }
        return rows;
    }

    private static bool TryInt(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> index, string column, out int value)
        => int.TryParse(Get(row, index, column), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    private static bool TryDouble(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> index, string column, out double value)
        => double.TryParse(Get(row, index, column), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    private static string Get(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> index, string column)
        => index.TryGetValue(column, out int position) && position < row.Count ? row[position] : string.Empty;

    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>(); var record = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }
            if (ch == '"') quoted = true;
            else if (ch == ',') { record.Add(field.ToString()); field.Clear(); }
            else if (ch == '\n') { record.Add(field.ToString()); field.Clear(); records.Add(record); record = []; }
            else if (ch != '\r') field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }
        return records;
    }

    private sealed class InputRow
    {
        public int SeasonId { get; set; }
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = string.Empty;
        public int Minute { get; set; }
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }
        public int CurrentTotalGoals { get; set; }
        public double TimingRemainingShare { get; set; }
        public int ActualFinalTotalGoals { get; set; }
    }
    private sealed class EvaluatedRow
    {
        public int MatchId { get; set; }
        public string StateTrigger { get; set; } = string.Empty;
        public double Line { get; set; }
        public bool ActualOver { get; set; }
        public double PoissonProbability { get; set; }
        public double EmpiricalProbability { get; set; }
        public BenchmarkBet? Bet { get; set; }
    }
    private sealed class BenchmarkBet { public double Profit { get; set; } }
}
