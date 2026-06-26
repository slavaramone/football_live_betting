using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class LeagueProfilesConfig
{
    public string ModelRoot { get; set; } = @"C:\Temp\football_data\models";
    public List<int> DefaultSnapshotMinutes { get; set; } = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
    public List<double> DefaultTargetLines { get; set; } = [2.5, 3.5];
    public List<double> DefaultAllowedLines { get; set; } = [2.5, 3.5];
    public List<LeagueProfile> Profiles { get; set; } = [];
}

public sealed class LiveTotalProfileBettingRule
{
    public string StateTrigger { get; set; } = string.Empty; // FixedMinute, AfterGoal, AfterRedCard, All/Any
    public string Side { get; set; } = string.Empty; // Over / Under
    public double Line { get; set; }
    public double MinProbabilityMove { get; set; }
    public double MinEdge { get; set; }
    public bool AllowBet { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed class LeagueProfile
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;

    // Flashscore download/import defaults.
    public string FlashscoreFixturesUrl { get; set; } = string.Empty;
    public int FlashscoreTournamentId { get; set; }
    public int FlashscoreSeasonId { get; set; }
    public string FlashscoreSeasonName { get; set; } = string.Empty;
    public string FlashscoreSeasonYear { get; set; } = string.Empty;
    public string FlashscoreCountry { get; set; } = string.Empty;
    public string FlashscoreCountryCode { get; set; } = string.Empty;

    // Production/live artifacts and seasons.
    public string ModelPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string EmpiricalSettlementPath { get; set; } = string.Empty;
    public string CalibrationDatasetPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; set; } = [];

    // Validation split and artifacts.
    public List<int> ValidationTrainingSeasonIds { get; set; } = [];
    public List<int> ValidationTestSeasonIds { get; set; } = [];
    public string ValidationModelPath { get; set; } = string.Empty;
    public string ValidationCalibrationDatasetPath { get; set; } = string.Empty;
    public string ValidationStateCorrectionPath { get; set; } = string.Empty;
    public string ValidationEmpiricalSettlementPath { get; set; } = string.Empty;
    public string CalibrationAnalysisPath { get; set; } = string.Empty;
    public string ValidationCalibrationAnalysisPath { get; set; } = string.Empty;
    public string ModelEvaluationPath { get; set; } = string.Empty;
    public string ValidationModelEvaluationPath { get; set; } = string.Empty;

    // Weibull fit defaults.
    public int MaxMinute { get; set; } = 90;
    public string GroupByColumn { get; set; } = "ScoreStateBefore";
    public int MinGroupGoals { get; set; } = 30;
    public int MaxIterations { get; set; } = 100;
    public double Tolerance { get; set; } = 1e-9;
    public double BlendWeibullWeight { get; set; } = 0.30;

    // Calibration dataset defaults.
    public bool IncludeUnreliableMatches { get; set; }
    public bool IncludeEventTriggers { get; set; } = true;
    public List<int> SnapshotMinutes { get; set; } = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];

    // State correction fit defaults.
    public int StateCorrectionMinBucketMatches { get; set; } = 100;
    public double StateCorrectionMinFactor { get; set; } = 0.50;
    public double StateCorrectionMaxFactor { get; set; } = 2.50;
    public int StateCorrectionShrinkMatches { get; set; } = 25;
    public string StateCorrectionLateGameMode { get; set; } = LiveTotalLateGameCorrectionMode.BoostUp;
    public int StateCorrectionLateGameStartMinute { get; set; } = 70;
    public double StateCorrectionLateGameFactorMultiplier { get; set; } = 1.15;
    public double StateCorrectionLateGameMaxFactor { get; set; } = 2.50;
    public double StateCorrectionLateGameMaxLine { get; set; } = 2.50;

    // Live pricing/current-season volume defaults.
    public List<int> BaseSeasonIds { get; set; } = [];
    public int CurrentSeasonId { get; set; }
    public int? DefaultBeforeRound { get; set; }
    public bool UseCurrentSeasonVolume { get; set; } = true;
    public double DefaultEmpiricalWeight { get; set; } = 0.80;
    public double EdgeThreshold { get; set; } = 0.10;
    public bool UseProbabilityMoveFilter { get; set; }
    public double MinOverProbabilityMove { get; set; } = 0.10;
    public double MinUnderProbabilityMove { get; set; } = -0.12;
    public bool UnderSignalsBettingAllowed { get; set; }
    public List<LiveTotalProfileBettingRule> LiveBettingRules { get; set; } = [];
    // Decision/rules gate used by Avalonia live pricing and model evaluation.
    public string DecisionMode { get; set; } = LiveTotalDecisionMode.FullModel;
    public int? MinMinute { get; set; }
    public bool RequireGoalTrigger { get; set; }
    public double? MinLine { get; set; }
    public List<double> AllowedLines { get; set; } = [];
    public bool FallbackBettingEnabled { get; set; } = true;
    public string DecisionRulesNotes { get; set; } = string.Empty;

    public int PriorStrengthMatches { get; set; } = 100;
    public List<double> TargetLines { get; set; } = [];
    public string RiskLevel { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    public string GetEmpiricalSettlementPath(bool validationMode = false)
    {
        string configured = validationMode ? ValidationEmpiricalSettlementPath : EmpiricalSettlementPath;
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        string datasetPath = validationMode ? ValidationCalibrationDatasetPath : CalibrationDatasetPath;
        if (string.IsNullOrWhiteSpace(datasetPath))
            return string.Empty;

        string directory = Path.GetDirectoryName(datasetPath) ?? ".";
        string fileName = Path.GetFileNameWithoutExtension(datasetPath);
        return Path.Combine(directory, $"{fileName}-empirical-settlement.json");
    }
}

public sealed class LeagueProfileStore
{
    private readonly IReadOnlyList<LeagueProfile> _profiles;

    private LeagueProfileStore(IReadOnlyList<LeagueProfile> profiles)
    {
        _profiles = profiles;
    }

    public IReadOnlyList<LeagueProfile> Profiles => _profiles;


    public static LeagueProfileStore Load(string path)
    {
        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"League profiles file was not found: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);
        LeagueProfilesConfig config = JsonSerializer.Deserialize<LeagueProfilesConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new LeagueProfilesConfig();

        ApplyProfileDefaults(config);
        return new LeagueProfileStore(config.Profiles);
    }

    public static async Task<LeagueProfileStore> LoadAsync(string path, CancellationToken cancellationToken)
    {
        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"League profiles file was not found: {resolvedPath}", resolvedPath);

        await using FileStream stream = File.OpenRead(resolvedPath);
        LeagueProfilesConfig config = await JsonSerializer.DeserializeAsync<LeagueProfilesConfig>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }, cancellationToken) ?? new LeagueProfilesConfig();

        ApplyProfileDefaults(config);
        return new LeagueProfileStore(config.Profiles);
    }


    private static void ApplyProfileDefaults(LeagueProfilesConfig config)
    {
        string modelRoot = string.IsNullOrWhiteSpace(config.ModelRoot)
            ? @"C:\Temp\football_data\models"
            : config.ModelRoot;

        foreach (LeagueProfile profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Key) && !string.IsNullOrWhiteSpace(profile.League))
                profile.Key = Slug(profile.League);
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = profile.League;
            if (string.IsNullOrWhiteSpace(profile.League))
                profile.League = profile.Name;

            if (profile.ValidationTrainingSeasonIds.Count == 0 && profile.TrainingSeasonIds.Count > 0)
                profile.ValidationTrainingSeasonIds = profile.TrainingSeasonIds.ToList();
            if (profile.ValidationTestSeasonIds.Count == 0 && profile.CurrentSeasonId > 0)
                profile.ValidationTestSeasonIds = [profile.CurrentSeasonId];
            if (profile.BaseSeasonIds.Count == 0 && profile.TrainingSeasonIds.Count > 0)
                profile.BaseSeasonIds = profile.TrainingSeasonIds.ToList();
            if (profile.TargetLines.Count == 0)
                profile.TargetLines = config.DefaultTargetLines.Count > 0 ? config.DefaultTargetLines.ToList() : [2.5, 3.5];
            if (profile.AllowedLines.Count == 0)
                profile.AllowedLines = config.DefaultAllowedLines.Count > 0 ? config.DefaultAllowedLines.ToList() : profile.TargetLines.ToList();
            if (profile.SnapshotMinutes.Count == 0)
                profile.SnapshotMinutes = config.DefaultSnapshotMinutes.Count > 0
                    ? config.DefaultSnapshotMinutes.ToList()
                    : [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
            if (profile.CurrentSeasonId <= 0 && profile.ValidationTestSeasonIds.Count > 0)
                profile.CurrentSeasonId = profile.ValidationTestSeasonIds.Max();
            if (profile.FlashscoreSeasonId <= 0 && profile.CurrentSeasonId > 0)
                profile.FlashscoreSeasonId = profile.CurrentSeasonId;
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear) && profile.FlashscoreSeasonId > 0)
                profile.FlashscoreSeasonYear = profile.FlashscoreSeasonId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonName) && !string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear))
                profile.FlashscoreSeasonName = profile.FlashscoreSeasonYear;

            string key = string.IsNullOrWhiteSpace(profile.Key) ? Slug(profile.League) : profile.Key;
            string root = Path.Combine(modelRoot, key);
            string trainingRange = SeasonRange(profile.TrainingSeasonIds);
            string validationTrainingRange = SeasonRange(profile.ValidationTrainingSeasonIds);
            string validationDatasetRange = ValidationDatasetRange(profile.ValidationTrainingSeasonIds, profile.ValidationTestSeasonIds);
            string validationTestRange = SeasonRange(profile.ValidationTestSeasonIds);

            profile.ModelPath = SetPathIfMissing(profile.ModelPath, root, $"weibull-{trainingRange}.json");
            profile.StateCorrectionPath = SetPathIfMissing(profile.StateCorrectionPath, root, $"state-correction-{trainingRange}.json");
            profile.EmpiricalSettlementPath = SetPathIfMissing(profile.EmpiricalSettlementPath, root, $"empirical-settlement-{trainingRange}.json");
            profile.CalibrationDatasetPath = SetPathIfMissing(profile.CalibrationDatasetPath, root, $"calibration-{trainingRange}.csv");
            profile.CalibrationAnalysisPath = SetPathIfMissing(profile.CalibrationAnalysisPath, root, $"calibration-analysis-{trainingRange}.csv");
            profile.ModelEvaluationPath = SetPathIfMissing(profile.ModelEvaluationPath, root, $"model-evaluation-{trainingRange}.csv");

            profile.ValidationModelPath = SetPathIfMissing(profile.ValidationModelPath, root, $"validation-weibull-{validationTrainingRange}.json");
            profile.ValidationCalibrationDatasetPath = SetPathIfMissing(profile.ValidationCalibrationDatasetPath, root, $"validation-calibration-{validationDatasetRange}.csv");
            profile.ValidationStateCorrectionPath = SetPathIfMissing(profile.ValidationStateCorrectionPath, root, $"validation-state-correction-{validationTrainingRange}.json");
            profile.ValidationEmpiricalSettlementPath = SetPathIfMissing(profile.ValidationEmpiricalSettlementPath, root, $"validation-empirical-settlement-{validationTrainingRange}.json");
            profile.ValidationCalibrationAnalysisPath = SetPathIfMissing(profile.ValidationCalibrationAnalysisPath, root, $"validation-calibration-analysis-{validationDatasetRange}.csv");
            profile.ValidationModelEvaluationPath = SetPathIfMissing(profile.ValidationModelEvaluationPath, root, $"validation-model-evaluation-{validationTestRange}.csv");
        }
    }

    private static string SetPathIfMissing(string value, string directory, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(fileName) || fileName.Contains("--", StringComparison.Ordinal))
            return value;

        return Path.Combine(directory, fileName);
    }

    private static string SeasonRange(IReadOnlyCollection<int> seasons)
    {
        if (seasons.Count == 0)
            return string.Empty;

        int min = seasons.Min();
        int max = seasons.Max();
        return min == max
            ? min.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{min.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string ValidationDatasetRange(IReadOnlyCollection<int> trainingSeasons, IReadOnlyCollection<int> testSeasons)
    {
        if (trainingSeasons.Count == 0)
            return SeasonRange(testSeasons);
        if (testSeasons.Count == 0)
            return SeasonRange(trainingSeasons);

        int min = trainingSeasons.Min();
        int max = testSeasons.Max();
        return min == max
            ? min.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{min.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{max.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var chars = value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        string slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        return slug.Trim('-');
    }

    public LeagueProfile FindRequired(string profileKeyOrName)
    {
        LeagueProfile? profile = _profiles.FirstOrDefault(p =>
            p.Key.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.League.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase));

        if (profile is not null)
            return profile;

        string available = _profiles.Count == 0
            ? "none"
            : string.Join(", ", _profiles.Select(p => string.IsNullOrWhiteSpace(p.Key) ? p.Name : p.Key));
        throw new ArgumentException($"League profile '{profileKeyOrName}' was not found. Available profiles: {available}.");
    }

    public static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        foreach (string root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? directory = new DirectoryInfo(root);
            while (directory is not null)
            {
                string candidate = Path.GetFullPath(Path.Combine(directory.FullName, path));
                if (File.Exists(candidate))
                    return candidate;

                directory = directory.Parent;
            }
        }

        return Path.GetFullPath(path);
    }
}
