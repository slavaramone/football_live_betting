using System.Globalization;
using System.Text;
using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class AfterGoalTeamProfileOptions
{
    public string AnglesDirectory { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public int MinTrainSample { get; set; } = 50;
    public int MinTestSample { get; set; } = 15;
    public double MinTrainAbsResidual { get; set; } = 0.10;
    public double MinTestAbsResidual { get; set; } = 0.05;
    public double StrongTestAbsResidual { get; set; } = 0.15;
    public bool RequireTestConfirmation { get; set; } = true;
    public bool WatchlistEnabled { get; set; } = true;
    public int WatchlistTrainSampleTolerance { get; set; } = 10;
    public int WatchlistTestSampleTolerance { get; set; } = 5;
    public double WatchlistResidualTolerance { get; set; } = 0.03;
}

public sealed class AfterGoalTeamProfileResult
{
    public List<AfterGoalTeamProfileRow> Profiles { get; } = [];
    public List<AfterGoalUsableSignalRow> UsableSignals { get; } = [];
    public List<AfterGoalWatchlistSignalRow> WatchlistSignals { get; } = [];
    public List<string> Warnings { get; } = [];
    public string SourceTrainSeasons { get; set; } = string.Empty;
    public string SourceTestSeason { get; set; } = string.Empty;
    public int TeamsAnalyzed => Profiles.Count;
    public int UsableScoringSignalsCount => UsableSignals.Count(x => x.TriggerType == "AfterScoring");
    public int UsableConcedingSignalsCount => UsableSignals.Count(x => x.TriggerType == "AfterConceding");
    public int WatchlistAfterScoringCount => WatchlistSignals.Count(x => x.TriggerType == "AfterScoring");
    public int WatchlistAfterConcedingCount => WatchlistSignals.Count(x => x.TriggerType == "AfterConceding");
    public int UnstableSignalsCount { get; set; }
    public int NoSignalCount => Profiles.Count(x => !x.CombinedUsable);
}

public sealed class AfterGoalTeamProfileRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TrainSeasons { get; set; } = string.Empty;
    public string TestSeason { get; set; } = string.Empty;
    public string AfterScoringProfile { get; set; } = string.Empty;
    public bool AfterScoringUsable { get; set; }
    public int AfterScoringTrainSampleSize { get; set; }
    public int AfterScoringTestSampleSize { get; set; }
    public double? AfterScoringTrainShrunkResidual { get; set; }
    public double? AfterScoringTestResidualVsBaseline { get; set; }
    public string AfterScoringStability { get; set; } = string.Empty;
    public string AfterScoringReason { get; set; } = string.Empty;
    public bool AfterScoringWatchlist { get; set; }
    public string AfterScoringWatchlistReason { get; set; } = string.Empty;
    public string AfterConcedingProfile { get; set; } = string.Empty;
    public bool AfterConcedingUsable { get; set; }
    public int AfterConcedingTrainSampleSize { get; set; }
    public int AfterConcedingTestSampleSize { get; set; }
    public double? AfterConcedingTrainShrunkResidual { get; set; }
    public double? AfterConcedingTestResidualVsBaseline { get; set; }
    public string AfterConcedingStability { get; set; } = string.Empty;
    public string AfterConcedingReason { get; set; } = string.Empty;
    public bool AfterConcedingWatchlist { get; set; }
    public string AfterConcedingWatchlistReason { get; set; } = string.Empty;
    public string CombinedProfile { get; set; } = string.Empty;
    public bool CombinedUsable { get; set; }
    public string CombinedDirection { get; set; } = string.Empty;
    public string CombinedConfidence { get; set; } = string.Empty;
    public string CombinedReason { get; set; } = string.Empty;
    public double StrongestUsableTestAbsResidual { get; set; }
}

public sealed class AfterGoalUsableSignalRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public int TrainSampleSize { get; set; }
    public int TestSampleSize { get; set; }
    public double TrainShrunkResidual { get; set; }
    public double TestResidualVsBaseline { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class AfterGoalWatchlistSignalRow
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Team { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string WatchlistReason { get; set; } = string.Empty;
    public string FailedRule { get; set; } = string.Empty;
    public int TrainSampleSize { get; set; }
    public int TestSampleSize { get; set; }
    public double TrainShrunkResidual { get; set; }
    public double TestResidualVsBaseline { get; set; }
    public string TrainDirection { get; set; } = string.Empty;
    public string TestDirection { get; set; } = string.Empty;
    public int TrainSampleShortBy { get; set; }
    public int TestSampleShortBy { get; set; }
    public double AbsTrainResidualShortBy { get; set; }
    public double AbsTestResidualShortBy { get; set; }
    public string Confidence { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

internal sealed class AfterGoalTeamAngleRow
{
    public string LeagueKey { get; init; } = string.Empty;
    public string LeagueName { get; init; } = string.Empty;
    public string Team { get; init; } = string.Empty;
    public string TrainSeasons { get; init; } = string.Empty;
    public string TestSeason { get; init; } = string.Empty;
    public int TrainSampleSize { get; init; }
    public double TrainShrunkResidual { get; init; }
    public string Direction { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public int TestSampleSize { get; init; }
    public double? TestAvgResidualVsBaseline { get; init; }
}

internal sealed class ClassifiedTeamSignal
{
    public string TriggerType { get; init; } = string.Empty;
    public string Profile { get; init; } = string.Empty;
    public bool Usable { get; init; }
    public string Direction { get; init; } = "NONE";
    public int TrainSampleSize { get; init; }
    public int TestSampleSize { get; init; }
    public double? TrainShrunkResidual { get; init; }
    public double? TestResidualVsBaseline { get; init; }
    public string Stability { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed class AfterGoalTeamProfileBuilder
{
    public async Task<AfterGoalTeamProfileResult> BuildAsync(AfterGoalTeamProfileOptions options, CancellationToken cancellationToken)
    {
        string anglesDirectory = Path.GetFullPath(options.AnglesDirectory);
        string scoringPath = Path.Combine(anglesDirectory, "team-after-scoring-angles.csv");
        string concedingPath = Path.Combine(anglesDirectory, "team-after-conceding-angles.csv");
        string summaryPath = Path.Combine(anglesDirectory, "after-goal-angle-analysis-summary.json");

        RequireFile(scoringPath);
        RequireFile(concedingPath);
        RequireFile(summaryPath);

        List<AfterGoalTeamAngleRow> scoringRows = await ReadAngleRowsAsync(scoringPath, cancellationToken);
        List<AfterGoalTeamAngleRow> concedingRows = await ReadAngleRowsAsync(concedingPath, cancellationToken);
        AfterGoalSourceMetadata metadata = await ReadMetadataAsync(summaryPath, cancellationToken);

        var result = new AfterGoalTeamProfileResult
        {
            SourceTrainSeasons = metadata.TrainSeasons,
            SourceTestSeason = metadata.TestSeason
        };

        AddMetadataWarnings(result, scoringRows, concedingRows);
        if (scoringRows.Count > 0 && scoringRows.All(x => x.TestSampleSize == 0))
            result.Warnings.Add("All scoring angle rows have TestSampleSize = 0; Patch 2 split may be wrong.");
        if (concedingRows.Count > 0 && concedingRows.All(x => x.TestSampleSize == 0))
            result.Warnings.Add("All conceding angle rows have TestSampleSize = 0; Patch 2 split may be wrong.");

        Dictionary<string, AfterGoalTeamAngleRow> scoringByTeam = scoringRows.ToDictionary(TeamKey, x => x, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, AfterGoalTeamAngleRow> concedingByTeam = concedingRows.ToDictionary(TeamKey, x => x, StringComparer.OrdinalIgnoreCase);
        List<string> teamKeys = scoringByTeam.Keys.Union(concedingByTeam.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();

        foreach (string key in teamKeys)
        {
            scoringByTeam.TryGetValue(key, out AfterGoalTeamAngleRow? scoring);
            concedingByTeam.TryGetValue(key, out AfterGoalTeamAngleRow? conceding);
            if (scoring is null)
                result.Warnings.Add($"Team appears in conceding report but not scoring report: {conceding?.Team}");
            if (conceding is null)
                result.Warnings.Add($"Team appears in scoring report but not conceding report: {scoring?.Team}");

            ClassifiedTeamSignal scoringSignal = AfterGoalTeamSignalClassifier.Classify(scoring, "AfterScoring", options);
            ClassifiedTeamSignal concedingSignal = AfterGoalTeamSignalClassifier.Classify(conceding, "AfterConceding", options);
            AfterGoalWatchlistSignalRow? scoringWatchlist = AfterGoalTeamWatchlistClassifier.Classify(scoring, scoringSignal, "AfterScoring", options);
            AfterGoalWatchlistSignalRow? concedingWatchlist = AfterGoalTeamWatchlistClassifier.Classify(conceding, concedingSignal, "AfterConceding", options);

            if (scoringSignal.Profile == "Unstable")
                result.UnstableSignalsCount++;
            if (concedingSignal.Profile == "Unstable")
                result.UnstableSignalsCount++;

            AfterGoalTeamAngleRow source = scoring ?? conceding ?? new AfterGoalTeamAngleRow();
            AfterGoalTeamProfileRow profile = CreateProfileRow(source, scoringSignal, concedingSignal, scoringWatchlist, concedingWatchlist, result.SourceTrainSeasons, result.SourceTestSeason);
            result.Profiles.Add(profile);

            AddUsableSignal(result, profile, scoringSignal);
            AddUsableSignal(result, profile, concedingSignal);
            AddWatchlistSignal(result, profile, scoringWatchlist);
            AddWatchlistSignal(result, profile, concedingWatchlist);
        }

        SortResult(result);
        return result;
    }

    private static AfterGoalTeamProfileRow CreateProfileRow(
        AfterGoalTeamAngleRow source,
        ClassifiedTeamSignal scoring,
        ClassifiedTeamSignal conceding,
        AfterGoalWatchlistSignalRow? scoringWatchlist,
        AfterGoalWatchlistSignalRow? concedingWatchlist,
        string trainSeasons,
        string testSeason)
    {
        List<ClassifiedTeamSignal> usable = [scoring, conceding];
        usable = usable.Where(x => x.Usable).ToList();
        string combinedDirection = CombinedDirection(usable);
        string combinedProfile = combinedDirection switch
        {
            "OVER" => "ContinuationTeam",
            "UNDER" => "SuppressionTeam",
            "MIXED" => "MixedTriggerTeam",
            _ => "NoStableSignal"
        };

        return new AfterGoalTeamProfileRow
        {
            LeagueKey = source.LeagueKey,
            LeagueName = source.LeagueName,
            Team = source.Team,
            TrainSeasons = trainSeasons,
            TestSeason = testSeason,
            AfterScoringProfile = scoring.Profile,
            AfterScoringUsable = scoring.Usable,
            AfterScoringTrainSampleSize = scoring.TrainSampleSize,
            AfterScoringTestSampleSize = scoring.TestSampleSize,
            AfterScoringTrainShrunkResidual = scoring.TrainShrunkResidual,
            AfterScoringTestResidualVsBaseline = scoring.TestResidualVsBaseline,
            AfterScoringStability = scoring.Stability,
            AfterScoringReason = scoring.Reason,
            AfterScoringWatchlist = scoringWatchlist is not null,
            AfterScoringWatchlistReason = scoringWatchlist?.Reason ?? string.Empty,
            AfterConcedingProfile = conceding.Profile,
            AfterConcedingUsable = conceding.Usable,
            AfterConcedingTrainSampleSize = conceding.TrainSampleSize,
            AfterConcedingTestSampleSize = conceding.TestSampleSize,
            AfterConcedingTrainShrunkResidual = conceding.TrainShrunkResidual,
            AfterConcedingTestResidualVsBaseline = conceding.TestResidualVsBaseline,
            AfterConcedingStability = conceding.Stability,
            AfterConcedingReason = conceding.Reason,
            AfterConcedingWatchlist = concedingWatchlist is not null,
            AfterConcedingWatchlistReason = concedingWatchlist?.Reason ?? string.Empty,
            CombinedProfile = combinedProfile,
            CombinedUsable = usable.Count > 0,
            CombinedDirection = combinedDirection,
            CombinedConfidence = CombinedConfidence(usable),
            CombinedReason = CombinedReason(usable),
            StrongestUsableTestAbsResidual = usable.Count == 0 ? 0 : usable.Max(x => Math.Abs(x.TestResidualVsBaseline.GetValueOrDefault()))
        };
    }

    private static void AddUsableSignal(AfterGoalTeamProfileResult result, AfterGoalTeamProfileRow profile, ClassifiedTeamSignal signal)
    {
        if (!signal.Usable)
            return;

        result.UsableSignals.Add(new AfterGoalUsableSignalRow
        {
            LeagueKey = profile.LeagueKey,
            LeagueName = profile.LeagueName,
            Team = profile.Team,
            TriggerType = signal.TriggerType,
            Direction = signal.Direction,
            TrainSampleSize = signal.TrainSampleSize,
            TestSampleSize = signal.TestSampleSize,
            TrainShrunkResidual = signal.TrainShrunkResidual.GetValueOrDefault(),
            TestResidualVsBaseline = signal.TestResidualVsBaseline.GetValueOrDefault(),
            Confidence = SignalConfidence(signal),
            Reason = signal.Reason
        });
    }

    private static void AddWatchlistSignal(AfterGoalTeamProfileResult result, AfterGoalTeamProfileRow profile, AfterGoalWatchlistSignalRow? signal)
    {
        if (signal is null)
            return;

        signal.LeagueKey = profile.LeagueKey;
        signal.LeagueName = profile.LeagueName;
        signal.Team = profile.Team;
        result.WatchlistSignals.Add(signal);
    }

    private static string CombinedDirection(IReadOnlyList<ClassifiedTeamSignal> usable)
    {
        if (usable.Count == 0)
            return "NONE";

        bool anyOver = usable.Any(x => x.Direction == "OVER");
        bool anyUnder = usable.Any(x => x.Direction == "UNDER");
        return anyOver && anyUnder ? "MIXED" : anyOver ? "OVER" : "UNDER";
    }

    private static string CombinedConfidence(IReadOnlyList<ClassifiedTeamSignal> usable)
    {
        if (usable.Count == 0)
            return "NONE";

        string direction = CombinedDirection(usable);
        if (usable.Count >= 2 && direction is "OVER" or "UNDER" && usable.All(x => x.TrainSampleSize >= 80 && x.TestSampleSize >= 20))
            return "HIGH";
        if (usable.Any(x => x.TrainSampleSize >= 50 && x.TestSampleSize >= 15))
            return "MEDIUM";
        return "LOW";
    }

    private static string SignalConfidence(ClassifiedTeamSignal signal)
        => signal.TrainSampleSize >= 80 && signal.TestSampleSize >= 20 ? "HIGH" :
            signal.TrainSampleSize >= 50 && signal.TestSampleSize >= 15 ? "MEDIUM" : "LOW";

    private static string CombinedReason(IReadOnlyList<ClassifiedTeamSignal> usable)
    {
        if (usable.Count == 0)
            return "No usable after-goal signal passed stability thresholds.";

        return string.Join(" ", usable.Select(x => $"{x.TriggerType} {x.Direction}: test residual {Signed(x.TestResidualVsBaseline.GetValueOrDefault())}."));
    }

    private static void SortResult(AfterGoalTeamProfileResult result)
    {
        result.Profiles.Sort((left, right) =>
        {
            int usable = right.CombinedUsable.CompareTo(left.CombinedUsable);
            if (usable != 0) return usable;
            int confidence = ConfidenceRank(right.CombinedConfidence).CompareTo(ConfidenceRank(left.CombinedConfidence));
            if (confidence != 0) return confidence;
            int residual = right.StrongestUsableTestAbsResidual.CompareTo(left.StrongestUsableTestAbsResidual);
            if (residual != 0) return residual;
            return string.Compare(left.Team, right.Team, StringComparison.OrdinalIgnoreCase);
        });

        result.UsableSignals.Sort((left, right) =>
        {
            int confidence = ConfidenceRank(right.Confidence).CompareTo(ConfidenceRank(left.Confidence));
            if (confidence != 0) return confidence;
            int residual = Math.Abs(right.TestResidualVsBaseline).CompareTo(Math.Abs(left.TestResidualVsBaseline));
            if (residual != 0) return residual;
            int test = right.TestSampleSize.CompareTo(left.TestSampleSize);
            if (test != 0) return test;
            return right.TrainSampleSize.CompareTo(left.TrainSampleSize);
        });

        result.WatchlistSignals.Sort((left, right) =>
        {
            int testResidual = Math.Abs(right.TestResidualVsBaseline).CompareTo(Math.Abs(left.TestResidualVsBaseline));
            if (testResidual != 0) return testResidual;
            int testSample = right.TestSampleSize.CompareTo(left.TestSampleSize);
            if (testSample != 0) return testSample;
            int trainResidual = Math.Abs(right.TrainShrunkResidual).CompareTo(Math.Abs(left.TrainShrunkResidual));
            if (trainResidual != 0) return trainResidual;
            return right.TrainSampleSize.CompareTo(left.TrainSampleSize);
        });
    }

    private static int ConfidenceRank(string confidence)
        => confidence switch
        {
            "HIGH" => 3,
            "MEDIUM" => 2,
            "LOW" => 1,
            _ => 0
        };

    private static void AddMetadataWarnings(AfterGoalTeamProfileResult result, IReadOnlyList<AfterGoalTeamAngleRow> scoringRows, IReadOnlyList<AfterGoalTeamAngleRow> concedingRows)
    {
        string scoringMeta = string.Join("|", scoringRows.Select(x => $"{x.LeagueKey}|{x.TrainSeasons}|{x.TestSeason}").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        string concedingMeta = string.Join("|", concedingRows.Select(x => $"{x.LeagueKey}|{x.TrainSeasons}|{x.TestSeason}").Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x));
        if (!string.Equals(scoringMeta, concedingMeta, StringComparison.OrdinalIgnoreCase))
            result.Warnings.Add("Scoring and conceding reports have different LeagueKey / TrainSeasons / TestSeason metadata.");
    }

    private static async Task<AfterGoalSourceMetadata> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path, cancellationToken));
        JsonElement root = document.RootElement;
        string trainSeasons = ReadStringArray(root, "ResolvedTrainSeasons");
        if (string.IsNullOrWhiteSpace(trainSeasons))
            trainSeasons = ReadStringArray(root, "TrainSeasons");

        string testSeason = ReadString(root, "ResolvedTestSeason");
        if (string.IsNullOrWhiteSpace(testSeason))
            testSeason = ReadString(root, "TestSeason");

        return new AfterGoalSourceMetadata(trainSeasons, testSeason);
    }

    private static async Task<List<AfterGoalTeamAngleRow>> ReadAngleRowsAsync(string path, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? headerLine = await reader.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new ArgumentException($"Angle report is empty: {path}");

        List<string> headers = CsvUtility.ParseLine(headerLine);
        var headerIndex = headers.Select((name, index) => new { name, index }).ToDictionary(x => x.name, x => x.index, StringComparer.OrdinalIgnoreCase);
        string[] required =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TrainSeasons",
            "TestSeason",
            "TrainSampleSize",
            "TrainShrunkResidual",
            "Direction",
            "Confidence",
            "TestSampleSize",
            "TestAvgResidualVsBaseline"
        ];
        List<string> missing = required.Where(x => !headerIndex.ContainsKey(x)).ToList();
        if (missing.Count > 0)
            throw new ArgumentException($"Angle report {path} is missing required columns: {string.Join(", ", missing)}");

        var rows = new List<AfterGoalTeamAngleRow>();
        while (!reader.EndOfStream)
        {
            string? line = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(line))
                continue;

            List<string> values = CsvUtility.ParseLine(line);
            rows.Add(new AfterGoalTeamAngleRow
            {
                LeagueKey = Get(values, headerIndex, "LeagueKey"),
                LeagueName = Get(values, headerIndex, "LeagueName"),
                Team = Get(values, headerIndex, "Team"),
                TrainSeasons = Get(values, headerIndex, "TrainSeasons"),
                TestSeason = Get(values, headerIndex, "TestSeason"),
                TrainSampleSize = GetInt(values, headerIndex, "TrainSampleSize"),
                TrainShrunkResidual = GetDouble(values, headerIndex, "TrainShrunkResidual"),
                Direction = Get(values, headerIndex, "Direction"),
                Confidence = Get(values, headerIndex, "Confidence"),
                TestSampleSize = GetInt(values, headerIndex, "TestSampleSize"),
                TestAvgResidualVsBaseline = GetNullableDouble(values, headerIndex, "TestAvgResidualVsBaseline")
            });
        }

        return rows;
    }

    private static string TeamKey(AfterGoalTeamAngleRow row)
        => $"{row.LeagueKey}|{row.Team}";

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Required input file was not found: {path}", path);
    }

    private static string Get(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
    {
        int index = headerIndex[name];
        return index < values.Count ? values[index] : string.Empty;
    }

    private static int GetInt(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => int.TryParse(Get(values, headerIndex, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;

    private static double GetDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => double.TryParse(Get(values, headerIndex, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0;

    private static double? GetNullableDouble(IReadOnlyList<string> values, IReadOnlyDictionary<string, int> headerIndex, string name)
        => double.TryParse(Get(values, headerIndex, name), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : null;

    private static string ReadString(JsonElement root, string propertyName)
        => root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static string ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return string.Empty;

        return string.Join(";", value.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private sealed record AfterGoalSourceMetadata(string TrainSeasons, string TestSeason);

    internal static string Signed(double value)
        => value >= 0 ? "+" + value.ToString("0.0000", CultureInfo.InvariantCulture) : value.ToString("0.0000", CultureInfo.InvariantCulture);
}

internal static class AfterGoalTeamSignalClassifier
{
    public static ClassifiedTeamSignal Classify(AfterGoalTeamAngleRow? row, string triggerType, AfterGoalTeamProfileOptions options)
    {
        if (row is null)
            return new ClassifiedTeamSignal
            {
                TriggerType = triggerType,
                Profile = "NoSignal",
                Stability = "TrainNeutral",
                Reason = "No angle row exists for this trigger."
            };

        string triggerPrefix = triggerType == "AfterScoring" ? "AfterScoring" : "AfterConceding";
        string trainDirection = NormalizeDirection(row.Direction);
        string testDirection = TestDirection(row.TestAvgResidualVsBaseline);
        double testResidual = row.TestAvgResidualVsBaseline.GetValueOrDefault();

        if (row.TestSampleSize <= 0 || row.TestAvgResidualVsBaseline is null)
            return Create(row, triggerType, "NoTestSample", false, "NONE", "NoTest", "No test sample is available.");

        if (row.TrainSampleSize < options.MinTrainSample || row.TestSampleSize < options.MinTestSample)
        {
            return Create(row, triggerType, "LowSample", false, trainDirection, "InsufficientSample",
                $"Low sample: train {row.TrainSampleSize} / test {row.TestSampleSize} below thresholds {options.MinTrainSample} / {options.MinTestSample}.");
        }

        if (trainDirection == "NEUTRAL" || Math.Abs(row.TrainShrunkResidual) < options.MinTrainAbsResidual)
        {
            return Create(row, triggerType, "NoSignal", false, trainDirection, "TrainNeutral",
                "No signal: train direction NEUTRAL or residual below threshold.");
        }

        if (testDirection is "OVER" or "UNDER" && testDirection != trainDirection)
        {
            return Create(row, triggerType, "Unstable", false, trainDirection, "FailedOpposite",
                $"Unstable: train {trainDirection} residual {AfterGoalTeamProfileBuilder.Signed(row.TrainShrunkResidual)}, but test residual {AfterGoalTeamProfileBuilder.Signed(testResidual)}.");
        }

        if (Math.Abs(testResidual) < options.MinTestAbsResidual)
        {
            return Create(row, triggerType, "Weak", false, trainDirection, "TestWeak",
                $"Weak: train {trainDirection} confirmed direction, but test residual {AfterGoalTeamProfileBuilder.Signed(testResidual)} is below {options.MinTestAbsResidual.ToString("0.####", CultureInfo.InvariantCulture)}.");
        }

        if (options.RequireTestConfirmation && testDirection != trainDirection)
        {
            return Create(row, triggerType, "Unstable", false, trainDirection, "FailedOpposite",
                $"Unstable: train {trainDirection} residual {AfterGoalTeamProfileBuilder.Signed(row.TrainShrunkResidual)}, but test direction is {testDirection}.");
        }

        string profile = $"{triggerPrefix}{ToTitle(trainDirection)}";
        string stability = Math.Abs(testResidual) >= options.StrongTestAbsResidual ? "ConfirmedStrong" : "ConfirmedWeak";
        return Create(row, triggerType, profile, true, trainDirection, stability,
            $"Usable {trainDirection}: train residual {AfterGoalTeamProfileBuilder.Signed(row.TrainShrunkResidual)} over {row.TrainSampleSize} events; test residual {AfterGoalTeamProfileBuilder.Signed(testResidual)} over {row.TestSampleSize} events.");
    }

    private static ClassifiedTeamSignal Create(AfterGoalTeamAngleRow row, string triggerType, string profile, bool usable, string direction, string stability, string reason)
        => new()
        {
            TriggerType = triggerType,
            Profile = profile,
            Usable = usable,
            Direction = direction,
            TrainSampleSize = row.TrainSampleSize,
            TestSampleSize = row.TestSampleSize,
            TrainShrunkResidual = row.TrainShrunkResidual,
            TestResidualVsBaseline = row.TestAvgResidualVsBaseline,
            Stability = stability,
            Reason = reason
        };

    private static string NormalizeDirection(string value)
        => value.Equals("OVER", StringComparison.OrdinalIgnoreCase) ? "OVER" :
            value.Equals("UNDER", StringComparison.OrdinalIgnoreCase) ? "UNDER" : "NEUTRAL";

    private static string TestDirection(double? value)
        => !value.HasValue || Math.Abs(value.Value) < 0.0000001 ? "NEUTRAL" : value.Value > 0 ? "OVER" : "UNDER";

    private static string ToTitle(string direction)
        => direction.Equals("OVER", StringComparison.OrdinalIgnoreCase) ? "Over" :
            direction.Equals("UNDER", StringComparison.OrdinalIgnoreCase) ? "Under" : "Neutral";
}

internal static class AfterGoalTeamWatchlistClassifier
{
    public static AfterGoalWatchlistSignalRow? Classify(AfterGoalTeamAngleRow? row, ClassifiedTeamSignal strictSignal, string triggerType, AfterGoalTeamProfileOptions options)
    {
        if (!options.WatchlistEnabled || row is null || strictSignal.Usable)
            return null;

        string trainDirection = NormalizeDirection(row.Direction);
        string testDirection = TestDirection(row.TestAvgResidualVsBaseline);
        if (trainDirection is not ("OVER" or "UNDER"))
            return null;
        if (row.TestSampleSize <= 0 || row.TestAvgResidualVsBaseline is null)
            return null;
        if (options.RequireTestConfirmation && testDirection != trainDirection)
            return null;
        if (testDirection is "OVER" or "UNDER" && testDirection != trainDirection)
            return null;

        double absTrainResidual = Math.Abs(row.TrainShrunkResidual);
        double absTestResidual = Math.Abs(row.TestAvgResidualVsBaseline.Value);
        int trainSampleShortBy = Math.Max(0, options.MinTrainSample - row.TrainSampleSize);
        int testSampleShortBy = Math.Max(0, options.MinTestSample - row.TestSampleSize);
        double trainResidualShortBy = Math.Max(0, options.MinTrainAbsResidual - absTrainResidual);
        double testResidualShortBy = Math.Max(0, options.MinTestAbsResidual - absTestResidual);

        bool trainSampleWithin = trainSampleShortBy <= options.WatchlistTrainSampleTolerance;
        bool testSampleWithin = testSampleShortBy <= options.WatchlistTestSampleTolerance;
        bool trainResidualWithin = trainResidualShortBy <= options.WatchlistResidualTolerance;
        bool testResidualWithin = testResidualShortBy <= options.WatchlistResidualTolerance;
        bool samplesMeaningful = trainSampleWithin && testSampleWithin;
        bool residualsMeaningful = trainResidualWithin && testResidualWithin && (trainResidualShortBy == 0 || testResidualShortBy == 0);

        if (!samplesMeaningful || !residualsMeaningful)
            return null;

        List<string> failed = [];
        if (trainSampleShortBy > 0) failed.Add("TrainSample");
        if (testSampleShortBy > 0) failed.Add("TestSample");
        if (trainResidualShortBy > 0) failed.Add("TrainResidual");
        if (testResidualShortBy > 0) failed.Add("TestResidual");
        if (failed.Count == 0)
            return null;

        string watchlistReason = WatchlistReason(trainSampleShortBy, testSampleShortBy, trainResidualShortBy, testResidualShortBy);
        return new AfterGoalWatchlistSignalRow
        {
            TriggerType = triggerType,
            Direction = trainDirection,
            WatchlistReason = watchlistReason,
            FailedRule = string.Join(";", failed),
            TrainSampleSize = row.TrainSampleSize,
            TestSampleSize = row.TestSampleSize,
            TrainShrunkResidual = row.TrainShrunkResidual,
            TestResidualVsBaseline = row.TestAvgResidualVsBaseline.Value,
            TrainDirection = trainDirection,
            TestDirection = testDirection,
            TrainSampleShortBy = trainSampleShortBy,
            TestSampleShortBy = testSampleShortBy,
            AbsTrainResidualShortBy = trainResidualShortBy,
            AbsTestResidualShortBy = testResidualShortBy,
            Confidence = Confidence(row, options),
            Reason = Reason(trainDirection, row, options, trainSampleShortBy, testSampleShortBy, trainResidualShortBy, testResidualShortBy)
        };
    }

    private static string WatchlistReason(int trainSampleShortBy, int testSampleShortBy, double trainResidualShortBy, double testResidualShortBy)
    {
        if (trainSampleShortBy > 0 && testSampleShortBy == 0 && trainResidualShortBy <= 0 && testResidualShortBy <= 0)
            return "NearTrainSample";
        if (testSampleShortBy > 0 && trainSampleShortBy == 0 && trainResidualShortBy <= 0 && testResidualShortBy <= 0)
            return "NearTestSample";
        if (trainResidualShortBy > 0 || testResidualShortBy > 0)
            return trainSampleShortBy > 0 || testSampleShortBy > 0 ? "ConfirmedButMarginal" : "NearResidualThreshold";
        return "ManualReview";
    }

    private static string Reason(string direction, AfterGoalTeamAngleRow row, AfterGoalTeamProfileOptions options, int trainSampleShortBy, int testSampleShortBy, double trainResidualShortBy, double testResidualShortBy)
    {
        var details = new List<string>();
        if (trainSampleShortBy > 0)
            details.Add($"train sample {row.TrainSampleSize} is {trainSampleShortBy} below threshold");
        if (testSampleShortBy > 0)
            details.Add($"test sample {row.TestSampleSize} is {testSampleShortBy} below threshold");
        if (trainResidualShortBy > 0)
            details.Add($"train residual is {trainResidualShortBy.ToString("0.0000", CultureInfo.InvariantCulture)} below threshold");
        if (testResidualShortBy > 0)
            details.Add($"test residual is {testResidualShortBy.ToString("0.0000", CultureInfo.InvariantCulture)} below threshold");

        return $"Watchlist {direction}: {string.Join(", ", details)}, but train residual {AfterGoalTeamProfileBuilder.Signed(row.TrainShrunkResidual)} and test residual {AfterGoalTeamProfileBuilder.Signed(row.TestAvgResidualVsBaseline.GetValueOrDefault())} confirm direction.";
    }

    private static string Confidence(AfterGoalTeamAngleRow row, AfterGoalTeamProfileOptions options)
        => row.TrainSampleSize >= options.MinTrainSample && row.TestSampleSize >= options.MinTestSample ? "MEDIUM" : "LOW";

    private static string NormalizeDirection(string value)
        => value.Equals("OVER", StringComparison.OrdinalIgnoreCase) ? "OVER" :
            value.Equals("UNDER", StringComparison.OrdinalIgnoreCase) ? "UNDER" : "NEUTRAL";

    private static string TestDirection(double? value)
        => !value.HasValue || Math.Abs(value.Value) < 0.0000001 ? "NEUTRAL" : value.Value > 0 ? "OVER" : "UNDER";
}

public static class AfterGoalTeamProfileReportWriter
{
    public static async Task WriteAsync(string outputDirectory, AfterGoalTeamProfileOptions options, AfterGoalTeamProfileResult result, CancellationToken cancellationToken)
    {
        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);

        await WriteProfilesAsync(Path.Combine(fullDirectory, "after-goal-team-profiles.csv"), result.Profiles, cancellationToken);
        await WriteSignalsAsync(Path.Combine(fullDirectory, "after-goal-usable-signals.csv"), result.UsableSignals, cancellationToken);
        await WriteWatchlistAsync(Path.Combine(fullDirectory, "after-goal-watchlist-signals.csv"), result.WatchlistSignals, cancellationToken);

        var summary = new
        {
            InputAnglesDirectory = Path.GetFullPath(options.AnglesDirectory),
            OutputDirectory = fullDirectory,
            SourceTrainSeasons = result.SourceTrainSeasons,
            SourceTestSeason = result.SourceTestSeason,
            options.MinTrainSample,
            options.MinTestSample,
            options.MinTrainAbsResidual,
            options.MinTestAbsResidual,
            options.StrongTestAbsResidual,
            options.RequireTestConfirmation,
            options.WatchlistEnabled,
            options.WatchlistTrainSampleTolerance,
            options.WatchlistTestSampleTolerance,
            options.WatchlistResidualTolerance,
            TeamsAnalyzed = result.TeamsAnalyzed,
            UsableScoringSignalsCount = result.UsableScoringSignalsCount,
            UsableConcedingSignalsCount = result.UsableConcedingSignalsCount,
            WatchlistSignalsCount = result.WatchlistSignals.Count,
            WatchlistAfterScoringCount = result.WatchlistAfterScoringCount,
            WatchlistAfterConcedingCount = result.WatchlistAfterConcedingCount,
            result.UnstableSignalsCount,
            NoSignalCount = result.NoSignalCount,
            Warnings = result.Warnings,
            Timestamp = DateTimeOffset.UtcNow
        };

        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(fullDirectory, "after-goal-team-profiles-summary.json"), json, Encoding.UTF8, cancellationToken);
    }

    private static async Task WriteProfilesAsync(string path, IReadOnlyList<AfterGoalTeamProfileRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TrainSeasons",
            "TestSeason",
            "AfterScoringProfile",
            "AfterScoringUsable",
            "AfterScoringTrainSampleSize",
            "AfterScoringTestSampleSize",
            "AfterScoringTrainShrunkResidual",
            "AfterScoringTestResidualVsBaseline",
            "AfterScoringStability",
            "AfterScoringReason",
            "AfterScoringWatchlist",
            "AfterScoringWatchlistReason",
            "AfterConcedingProfile",
            "AfterConcedingUsable",
            "AfterConcedingTrainSampleSize",
            "AfterConcedingTestSampleSize",
            "AfterConcedingTrainShrunkResidual",
            "AfterConcedingTestResidualVsBaseline",
            "AfterConcedingStability",
            "AfterConcedingReason",
            "AfterConcedingWatchlist",
            "AfterConcedingWatchlistReason",
            "CombinedProfile",
            "CombinedUsable",
            "CombinedDirection",
            "CombinedConfidence",
            "CombinedReason"
        ];

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (AfterGoalTeamProfileRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(ProfileValues(row)));
    }

    private static async Task WriteSignalsAsync(string path, IReadOnlyList<AfterGoalUsableSignalRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TriggerType",
            "Direction",
            "TrainSampleSize",
            "TestSampleSize",
            "TrainShrunkResidual",
            "TestResidualVsBaseline",
            "Confidence",
            "Reason"
        ];

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (AfterGoalUsableSignalRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(SignalValues(row)));
    }

    private static async Task WriteWatchlistAsync(string path, IReadOnlyList<AfterGoalWatchlistSignalRow> rows, CancellationToken cancellationToken)
    {
        string[] headers =
        [
            "LeagueKey",
            "LeagueName",
            "Team",
            "TriggerType",
            "Direction",
            "WatchlistReason",
            "FailedRule",
            "TrainSampleSize",
            "TestSampleSize",
            "TrainShrunkResidual",
            "TestResidualVsBaseline",
            "TrainDirection",
            "TestDirection",
            "TrainSampleShortBy",
            "TestSampleShortBy",
            "AbsTrainResidualShortBy",
            "AbsTestResidualShortBy",
            "Confidence",
            "Reason"
        ];

        await using var writer = new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteLineAsync(string.Join(",", headers));
        foreach (AfterGoalWatchlistSignalRow row in rows)
            await writer.WriteLineAsync(CsvUtility.ToLine(WatchlistValues(row)));
    }

    private static IEnumerable<string> ProfileValues(AfterGoalTeamProfileRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Team;
        yield return row.TrainSeasons;
        yield return row.TestSeason;
        yield return row.AfterScoringProfile;
        yield return Bool(row.AfterScoringUsable);
        yield return row.AfterScoringTrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return row.AfterScoringTestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.AfterScoringTrainShrunkResidual);
        yield return Format(row.AfterScoringTestResidualVsBaseline);
        yield return row.AfterScoringStability;
        yield return row.AfterScoringReason;
        yield return Bool(row.AfterScoringWatchlist);
        yield return row.AfterScoringWatchlistReason;
        yield return row.AfterConcedingProfile;
        yield return Bool(row.AfterConcedingUsable);
        yield return row.AfterConcedingTrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return row.AfterConcedingTestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.AfterConcedingTrainShrunkResidual);
        yield return Format(row.AfterConcedingTestResidualVsBaseline);
        yield return row.AfterConcedingStability;
        yield return row.AfterConcedingReason;
        yield return Bool(row.AfterConcedingWatchlist);
        yield return row.AfterConcedingWatchlistReason;
        yield return row.CombinedProfile;
        yield return Bool(row.CombinedUsable);
        yield return row.CombinedDirection;
        yield return row.CombinedConfidence;
        yield return row.CombinedReason;
    }

    private static IEnumerable<string> SignalValues(AfterGoalUsableSignalRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Team;
        yield return row.TriggerType;
        yield return row.Direction;
        yield return row.TrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return row.TestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TrainShrunkResidual);
        yield return Format(row.TestResidualVsBaseline);
        yield return row.Confidence;
        yield return row.Reason;
    }

    private static IEnumerable<string> WatchlistValues(AfterGoalWatchlistSignalRow row)
    {
        yield return row.LeagueKey;
        yield return row.LeagueName;
        yield return row.Team;
        yield return row.TriggerType;
        yield return row.Direction;
        yield return row.WatchlistReason;
        yield return row.FailedRule;
        yield return row.TrainSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return row.TestSampleSize.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.TrainShrunkResidual);
        yield return Format(row.TestResidualVsBaseline);
        yield return row.TrainDirection;
        yield return row.TestDirection;
        yield return row.TrainSampleShortBy.ToString(CultureInfo.InvariantCulture);
        yield return row.TestSampleShortBy.ToString(CultureInfo.InvariantCulture);
        yield return Format(row.AbsTrainResidualShortBy);
        yield return Format(row.AbsTestResidualShortBy);
        yield return row.Confidence;
        yield return row.Reason;
    }

    private static string Bool(bool value) => value.ToString(CultureInfo.InvariantCulture).ToLowerInvariant();

    private static string Format(double? value)
        => value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
}

internal static class CsvUtility
{
    public static List<string> ParseLine(string line)
    {
        var values = new List<string>();
        var builder = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    quoted = false;
                }
                else
                {
                    builder.Append(ch);
                }
            }
            else if (ch == ',')
            {
                values.Add(builder.ToString());
                builder.Clear();
            }
            else if (ch == '"')
            {
                quoted = true;
            }
            else
            {
                builder.Append(ch);
            }
        }

        values.Add(builder.ToString());
        return values;
    }

    public static string ToLine(IEnumerable<string> values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        string text = value ?? string.Empty;
        return text.Contains('"') || text.Contains(',') || text.Contains('\r') || text.Contains('\n')
            ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : text;
    }
}
