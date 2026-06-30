using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class LeagueProfilesConfig
{
    public string ModelRoot { get; set; } = @"C:\Temp\football_data\models";
    public string ReportRoot { get; set; } = @"C:\Temp\football_data\reports";
    public List<int> DefaultCalibrationSeasonIds { get; set; } = [];
    public List<string> DefaultStateWeibullTimeBuckets { get; set; } = ["0-20", "20-35", "35-45", "45-60", "60-70", "70-80", "80-90", "90-96"];
    public List<double> DefaultTargetLines { get; set; } = [2.5, 3.5];
    public List<double> DefaultAllowedLines { get; set; } = [2.5, 3.5];
    public MonteCarloConfig MonteCarlo { get; set; } = new();
    public StateWeibullCurveFitProfileSettings StateWeibullCurveFit { get; set; } = new();
    public NextGoalSideFitProfileSettings NextGoalSideFit { get; set; } = new();
    public MarketBaselineProfileSettings MarketBaseline { get; set; } = new();
    public LiveStateCorrectionProfileSettings LiveStateCorrection { get; set; } = new();
    public List<LeagueProfile> Profiles { get; set; } = [];
}

public sealed class LiveTotalProfileBettingRule
{
    public string StateTrigger { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public double Line { get; set; }
    public double MinProbabilityMove { get; set; }
    public double MinEdge { get; set; }
    public bool AllowBet { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed class StateWeibullCurveFitProfileSettings
{
    public double MinMuFullBucketExposures { get; set; } = 75.0;
    public int MinMuGoals { get; set; } = 30;
    public double MinKFullBucketExposures { get; set; } = 150.0;
    public int MinKGoals { get; set; } = 50;
    public double MinK { get; set; } = 0.65;
    public double MaxK { get; set; } = 1.85;
    public double KStep { get; set; } = 0.05;
    public double DefaultK { get; set; } = 1.0;

    public StateWeibullCurveFitProfileSettings WithDefaultsFrom(StateWeibullCurveFitProfileSettings fallback)
    {
        return new StateWeibullCurveFitProfileSettings
        {
            MinMuFullBucketExposures = MinMuFullBucketExposures > 0 ? MinMuFullBucketExposures : fallback.MinMuFullBucketExposures,
            MinMuGoals = MinMuGoals > 0 ? MinMuGoals : fallback.MinMuGoals,
            MinKFullBucketExposures = MinKFullBucketExposures > 0 ? MinKFullBucketExposures : fallback.MinKFullBucketExposures,
            MinKGoals = MinKGoals > 0 ? MinKGoals : fallback.MinKGoals,
            MinK = MinK > 0 ? MinK : fallback.MinK,
            MaxK = MaxK > 0 ? MaxK : fallback.MaxK,
            KStep = KStep > 0 ? KStep : fallback.KStep,
            DefaultK = DefaultK > 0 ? DefaultK : fallback.DefaultK
        };
    }
}

public sealed class NextGoalSideFitProfileSettings
{
    public int MinExactGoals { get; set; } = 25;
    public int MinDirectionalOverallGoals { get; set; } = 50;
    public int MinPressureTimeGoals { get; set; } = 40;
    public int MinNeutralScoreTimeGoals { get; set; } = 25;
    public int MinTimeGoals { get; set; } = 50;
    public int MinLeagueGoals { get; set; } = 100;
    public double PriorWeightGoals { get; set; } = 6.0;

    public NextGoalSideFitProfileSettings WithDefaultsFrom(NextGoalSideFitProfileSettings fallback)
    {
        return new NextGoalSideFitProfileSettings
        {
            MinExactGoals = MinExactGoals > 0 ? MinExactGoals : fallback.MinExactGoals,
            MinDirectionalOverallGoals = MinDirectionalOverallGoals > 0 ? MinDirectionalOverallGoals : fallback.MinDirectionalOverallGoals,
            MinPressureTimeGoals = MinPressureTimeGoals > 0 ? MinPressureTimeGoals : fallback.MinPressureTimeGoals,
            MinNeutralScoreTimeGoals = MinNeutralScoreTimeGoals > 0 ? MinNeutralScoreTimeGoals : fallback.MinNeutralScoreTimeGoals,
            MinTimeGoals = MinTimeGoals > 0 ? MinTimeGoals : fallback.MinTimeGoals,
            MinLeagueGoals = MinLeagueGoals > 0 ? MinLeagueGoals : fallback.MinLeagueGoals,
            PriorWeightGoals = PriorWeightGoals > 0 ? PriorWeightGoals : fallback.PriorWeightGoals
        };
    }
}

public sealed class MarketBaselineProfileSettings
{
    public bool? Enabled { get; set; }
    public double? OddsSensitivityGoals { get; set; }
    public double? MultiplierShrink { get; set; }
    public double? LowTotalMultiplierShrink { get; set; }
    public double? HighTotalMultiplierShrink { get; set; }
    public double? MinMultiplier { get; set; }
    public double? MaxMultiplier { get; set; }
    public double? MinMarketExpectedTotalGoals { get; set; }
    public double? MaxMarketExpectedTotalGoals { get; set; }
    public double? ModelBaselineExpectedTotalGoals { get; set; }

    public MarketBaselineProfileSettings WithDefaultsFrom(MarketBaselineProfileSettings fallback)
    {
        return new MarketBaselineProfileSettings
        {
            Enabled = Enabled ?? fallback.Enabled ?? true,
            OddsSensitivityGoals = PositiveOrFallback(OddsSensitivityGoals, fallback.OddsSensitivityGoals, 1.25),
            MultiplierShrink = NonNegativeOrFallback(MultiplierShrink, fallback.MultiplierShrink, 0.65),
            LowTotalMultiplierShrink = NonNegativeOrFallback(LowTotalMultiplierShrink, fallback.LowTotalMultiplierShrink, null),
            HighTotalMultiplierShrink = NonNegativeOrFallback(HighTotalMultiplierShrink, fallback.HighTotalMultiplierShrink, null),
            MinMultiplier = PositiveOrFallback(MinMultiplier, fallback.MinMultiplier, 0.75),
            MaxMultiplier = PositiveOrFallback(MaxMultiplier, fallback.MaxMultiplier, 1.25),
            MinMarketExpectedTotalGoals = PositiveOrFallback(MinMarketExpectedTotalGoals, fallback.MinMarketExpectedTotalGoals, 1.0),
            MaxMarketExpectedTotalGoals = PositiveOrFallback(MaxMarketExpectedTotalGoals, fallback.MaxMarketExpectedTotalGoals, 6.0),
            ModelBaselineExpectedTotalGoals = PositiveOrFallback(ModelBaselineExpectedTotalGoals, fallback.ModelBaselineExpectedTotalGoals, null)
        };
    }

    private static double? PositiveOrFallback(double? value, double? fallback, double? defaultValue)
    {
        if (value.HasValue && value.Value > 0)
            return value.Value;
        if (fallback.HasValue && fallback.Value > 0)
            return fallback.Value;
        return defaultValue;
    }

    private static double? NonNegativeOrFallback(double? value, double? fallback, double? defaultValue)
    {
        if (value.HasValue && value.Value >= 0)
            return value.Value;
        if (fallback.HasValue && fallback.Value >= 0)
            return fallback.Value;
        return defaultValue;
    }
}


public sealed class LiveStateCorrectionProfileSettings
{
    public bool? Enabled { get; set; }
    public string Path { get; set; } = string.Empty;
    public int? MinRows { get; set; }
    public double? PriorRows { get; set; }
    public double? Shrink { get; set; }
    public double? MinMultiplier { get; set; }
    public double? MaxMultiplier { get; set; }

    public LiveStateCorrectionProfileSettings WithDefaultsFrom(LiveStateCorrectionProfileSettings fallback)
    {
        return new LiveStateCorrectionProfileSettings
        {
            Enabled = Enabled ?? fallback.Enabled ?? false,
            Path = !string.IsNullOrWhiteSpace(Path) ? Path : fallback.Path,
            MinRows = PositiveIntOrFallback(MinRows, fallback.MinRows, 80),
            PriorRows = NonNegativeOrFallback(PriorRows, fallback.PriorRows, 150.0),
            Shrink = NonNegativeOrFallback(Shrink, fallback.Shrink, 0.8),
            MinMultiplier = PositiveOrFallback(MinMultiplier, fallback.MinMultiplier, 0.75),
            MaxMultiplier = PositiveOrFallback(MaxMultiplier, fallback.MaxMultiplier, 1.35)
        };
    }

    private static int? PositiveIntOrFallback(int? value, int? fallback, int? defaultValue)
    {
        if (value.HasValue && value.Value > 0)
            return value.Value;
        if (fallback.HasValue && fallback.Value > 0)
            return fallback.Value;
        return defaultValue;
    }

    private static double? PositiveOrFallback(double? value, double? fallback, double? defaultValue)
    {
        if (value.HasValue && value.Value > 0)
            return value.Value;
        if (fallback.HasValue && fallback.Value > 0)
            return fallback.Value;
        return defaultValue;
    }

    private static double? NonNegativeOrFallback(double? value, double? fallback, double? defaultValue)
    {
        if (value.HasValue && value.Value >= 0)
            return value.Value;
        if (fallback.HasValue && fallback.Value >= 0)
            return fallback.Value;
        return defaultValue;
    }
}

public sealed class LeagueProfile
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;

    public string FlashscoreFixturesUrl { get; set; } = string.Empty;
    public int FlashscoreTournamentId { get; set; }
    public int FlashscoreSeasonId { get; set; }
    public string FlashscoreSeasonName { get; set; } = string.Empty;
    public string FlashscoreSeasonYear { get; set; } = string.Empty;
    public string FlashscoreCountry { get; set; } = string.Empty;
    public string FlashscoreCountryCode { get; set; } = string.Empty;

    public int CurrentSeasonId { get; set; }
    public List<int> CalibrationSeasonIds { get; set; } = [];
    public List<int> TrainingSeasonIds { get; set; } = [];
    public List<int> BaseSeasonIds { get; set; } = [];
    public int? DefaultBeforeRound { get; set; }
    public bool UseCurrentSeasonVolume { get; set; } = true;

    public string ModelFolder { get; set; } = string.Empty;
    public string ReportFolder { get; set; } = string.Empty;
    public string StateWeibullExposuresPath { get; set; } = string.Empty;
    public string StateWeibullCurvesPath { get; set; } = string.Empty;
    public string StateWeibullCurvesSummaryPath { get; set; } = string.Empty;
    public string NextGoalSideModelPath { get; set; } = string.Empty;
    public string NextGoalSideSummaryPath { get; set; } = string.Empty;
    public string CompetingHazardCurvesPath { get; set; } = string.Empty;
    public string CompetingHazardCurvesSummaryPath { get; set; } = string.Empty;
    public string LiveMonteCarloOutputPath { get; set; } = string.Empty;
    public string LiveMonteCarloPathsOutputPath { get; set; } = string.Empty;
    public string LiveMonteCarloEvaluationSummaryPath { get; set; } = string.Empty;
    public string LiveMonteCarloV3OutputPath { get; set; } = string.Empty;
    public string LiveMonteCarloV3PathsOutputPath { get; set; } = string.Empty;
    public string LiveMonteCarloV3EvaluationSummaryPath { get; set; } = string.Empty;
    public string LiveMonteCarloV3MarketBaselineTuningPath { get; set; } = string.Empty;
    public string LiveStateCorrectionPath { get; set; } = string.Empty;

    public List<string> StateWeibullTimeBuckets { get; set; } = [];
    public StateWeibullCurveFitProfileSettings StateWeibullCurveFit { get; set; } = new();
    public NextGoalSideFitProfileSettings NextGoalSideFit { get; set; } = new();
    public MonteCarloConfig MonteCarlo { get; set; } = new();
    public MarketBaselineProfileSettings MarketBaseline { get; set; } = new();
    public LiveStateCorrectionProfileSettings LiveStateCorrection { get; set; } = new();

    public double EdgeThreshold { get; set; } = 0.05;
    public bool UseProbabilityMoveFilter { get; set; }
    public bool UnderSignalsBettingAllowed { get; set; }
    public string DecisionMode { get; set; } = "StateWeibullMonteCarlo";
    public int? MinMinute { get; set; }
    public bool RequireGoalTrigger { get; set; }
    public double? MinLine { get; set; }
    public List<double> TargetLines { get; set; } = [];
    public List<double> AllowedLines { get; set; } = [];
    public bool FallbackBettingEnabled { get; set; } = true;
    public List<LiveTotalProfileBettingRule> LiveBettingRules { get; set; } = [];
    public string DecisionRulesNotes { get; set; } = string.Empty;
    public string RiskLevel { get; set; } = "MC paper test";
    public string Notes { get; set; } = string.Empty;
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
        LeagueProfilesConfig config = JsonSerializer.Deserialize<LeagueProfilesConfig>(json, JsonOptions()) ?? new LeagueProfilesConfig();

        ApplyProfileDefaults(config);
        return new LeagueProfileStore(config.Profiles);
    }

    public static async Task<LeagueProfileStore> LoadAsync(string path, CancellationToken cancellationToken)
    {
        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"League profiles file was not found: {resolvedPath}", resolvedPath);

        await using FileStream stream = File.OpenRead(resolvedPath);
        LeagueProfilesConfig config = await JsonSerializer.DeserializeAsync<LeagueProfilesConfig>(stream, JsonOptions(), cancellationToken)
            ?? new LeagueProfilesConfig();

        ApplyProfileDefaults(config);
        return new LeagueProfileStore(config.Profiles);
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

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static void ApplyProfileDefaults(LeagueProfilesConfig config)
    {
        foreach (LeagueProfile profile in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Key) && !string.IsNullOrWhiteSpace(profile.League))
                profile.Key = Slug(profile.League);
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = profile.League;
            if (string.IsNullOrWhiteSpace(profile.League))
                profile.League = profile.Name;
            if (profile.CurrentSeasonId <= 0 && profile.FlashscoreSeasonId > 0)
                profile.CurrentSeasonId = profile.FlashscoreSeasonId;
            if (profile.FlashscoreSeasonId <= 0 && profile.CurrentSeasonId > 0)
                profile.FlashscoreSeasonId = profile.CurrentSeasonId;
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear) && profile.FlashscoreSeasonId > 0)
                profile.FlashscoreSeasonYear = profile.FlashscoreSeasonId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonName) && !string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear))
                profile.FlashscoreSeasonName = profile.FlashscoreSeasonYear;

            if (profile.CalibrationSeasonIds.Count == 0)
                profile.CalibrationSeasonIds = profile.TrainingSeasonIds.Count > 0
                    ? profile.TrainingSeasonIds.ToList()
                    : profile.BaseSeasonIds.Count > 0
                        ? profile.BaseSeasonIds.ToList()
                        : config.DefaultCalibrationSeasonIds.ToList();
            if (profile.TrainingSeasonIds.Count == 0 && profile.CalibrationSeasonIds.Count > 0)
                profile.TrainingSeasonIds = profile.CalibrationSeasonIds.ToList();
            if (profile.BaseSeasonIds.Count == 0 && profile.CalibrationSeasonIds.Count > 0)
                profile.BaseSeasonIds = profile.CalibrationSeasonIds.ToList();

            if (profile.TargetLines.Count == 0)
                profile.TargetLines = config.DefaultTargetLines.Count > 0 ? config.DefaultTargetLines.ToList() : [2.5, 3.5];
            if (profile.AllowedLines.Count == 0)
                profile.AllowedLines = config.DefaultAllowedLines.Count > 0 ? config.DefaultAllowedLines.ToList() : profile.TargetLines.ToList();
            if (profile.StateWeibullTimeBuckets.Count == 0)
                profile.StateWeibullTimeBuckets = config.DefaultStateWeibullTimeBuckets.Count > 0
                    ? config.DefaultStateWeibullTimeBuckets.ToList()
                    : ["0-20", "20-35", "35-45", "45-60", "60-70", "70-80", "80-90", "90-96"];

            profile.MonteCarlo = profile.MonteCarlo.WithDefaultsFrom(config.MonteCarlo);
            profile.StateWeibullCurveFit = profile.StateWeibullCurveFit.WithDefaultsFrom(config.StateWeibullCurveFit);
            profile.NextGoalSideFit = profile.NextGoalSideFit.WithDefaultsFrom(config.NextGoalSideFit);
            profile.MarketBaseline = profile.MarketBaseline.WithDefaultsFrom(config.MarketBaseline);
            profile.LiveStateCorrection = profile.LiveStateCorrection.WithDefaultsFrom(config.LiveStateCorrection);

            string modelFolder = profile.ModelFolder;
            if (string.IsNullOrWhiteSpace(modelFolder) && !string.IsNullOrWhiteSpace(config.ModelRoot))
                modelFolder = Path.Combine(config.ModelRoot, profile.Key);
            string reportFolder = profile.ReportFolder;
            if (string.IsNullOrWhiteSpace(reportFolder) && !string.IsNullOrWhiteSpace(config.ReportRoot))
                reportFolder = config.ReportRoot;
            if (string.IsNullOrWhiteSpace(reportFolder))
                reportFolder = modelFolder;

            profile.ModelFolder = modelFolder;
            profile.ReportFolder = reportFolder;
            ApplyGeneratedPaths(profile, modelFolder, reportFolder);

            if (string.IsNullOrWhiteSpace(profile.DecisionRulesNotes))
                profile.DecisionRulesNotes = "State-Weibull Monte Carlo with next-goal-side fallback model.";
            if (string.IsNullOrWhiteSpace(profile.Notes))
                profile.Notes = "State-Weibull Monte Carlo profile. Calibration commands read/write paths and thresholds from this profile.";
        }
    }

    private static void ApplyGeneratedPaths(LeagueProfile profile, string modelFolder, string reportFolder)
    {
        string key = string.IsNullOrWhiteSpace(profile.Key) ? Slug(profile.League) : profile.Key;
        profile.StateWeibullExposuresPath = ValueOrDefault(profile.StateWeibullExposuresPath, modelFolder, $"{key}-state-weibull-exposures.csv");
        profile.StateWeibullCurvesPath = ValueOrDefault(profile.StateWeibullCurvesPath, modelFolder, $"{key}-state-weibull-curves.json");
        profile.StateWeibullCurvesSummaryPath = ValueOrDefault(profile.StateWeibullCurvesSummaryPath, reportFolder, $"{key}-state-weibull-curves-summary.csv");
        profile.NextGoalSideModelPath = ValueOrDefault(profile.NextGoalSideModelPath, modelFolder, $"{key}-next-goal-side-model.json");
        profile.NextGoalSideSummaryPath = ValueOrDefault(profile.NextGoalSideSummaryPath, reportFolder, $"{key}-next-goal-side-summary.csv");
        profile.CompetingHazardCurvesPath = ValueOrDefault(profile.CompetingHazardCurvesPath, modelFolder, $"{key}-competing-hazard-curves.json");
        profile.CompetingHazardCurvesSummaryPath = ValueOrDefault(profile.CompetingHazardCurvesSummaryPath, reportFolder, $"{key}-competing-hazard-curves-summary.csv");
        profile.LiveMonteCarloOutputPath = ValueOrDefault(profile.LiveMonteCarloOutputPath, reportFolder, $"{key}-live-total-mc.json");
        profile.LiveMonteCarloPathsOutputPath = ValueOrDefault(profile.LiveMonteCarloPathsOutputPath, reportFolder, $"{key}-live-total-mc-paths.csv");
        profile.LiveMonteCarloEvaluationSummaryPath = ValueOrDefault(profile.LiveMonteCarloEvaluationSummaryPath, reportFolder, $"{key}-mc-evaluation-summary.json");
        profile.LiveMonteCarloV3OutputPath = ValueOrDefault(profile.LiveMonteCarloV3OutputPath, reportFolder, $"{key}-live-total-mc-v3.json");
        profile.LiveMonteCarloV3PathsOutputPath = ValueOrDefault(profile.LiveMonteCarloV3PathsOutputPath, reportFolder, $"{key}-live-total-mc-v3-paths.csv");
        profile.LiveMonteCarloV3EvaluationSummaryPath = ValueOrDefault(profile.LiveMonteCarloV3EvaluationSummaryPath, reportFolder, $"{key}-mc-v3-evaluation-summary.json");
        profile.LiveMonteCarloV3MarketBaselineTuningPath = ValueOrDefault(profile.LiveMonteCarloV3MarketBaselineTuningPath, reportFolder, $"{key}-mc-v3-market-baseline-tuning-summary.json");
        if (string.IsNullOrWhiteSpace(profile.LiveStateCorrectionPath) && !string.IsNullOrWhiteSpace(profile.LiveStateCorrection.Path))
            profile.LiveStateCorrectionPath = profile.LiveStateCorrection.Path;
        profile.LiveStateCorrectionPath = ValueOrDefault(profile.LiveStateCorrectionPath, modelFolder, $"{key}-live-state-correction.json");
        if (string.IsNullOrWhiteSpace(profile.LiveStateCorrection.Path))
            profile.LiveStateCorrection.Path = profile.LiveStateCorrectionPath;
    }

    private static string ValueOrDefault(string value, string folder, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        if (string.IsNullOrWhiteSpace(folder))
            return fileName;
        return Path.Combine(folder, fileName);
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
}
