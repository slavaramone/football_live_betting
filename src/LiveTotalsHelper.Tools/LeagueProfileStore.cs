using System.Text.Json;

namespace LiveTotalsHelper.Tools;

public sealed class LeagueProfilesConfig
{
    public List<LeagueProfile> Profiles { get; set; } = [];
}

public sealed class LeagueProfile
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;

    // Production/live artifacts and seasons.
    public string ModelPath { get; set; } = string.Empty;
    public string StateCorrectionPath { get; set; } = string.Empty;
    public string CalibrationDatasetPath { get; set; } = string.Empty;
    public List<int> TrainingSeasonIds { get; set; } = [];

    // Validation split and artifacts.
    public List<int> ValidationTrainingSeasonIds { get; set; } = [];
    public List<int> ValidationTestSeasonIds { get; set; } = [];
    public string ValidationModelPath { get; set; } = string.Empty;
    public string ValidationCalibrationDatasetPath { get; set; } = string.Empty;
    public string ValidationStateCorrectionPath { get; set; } = string.Empty;
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
    public int AfterGoalMinBucketMatches { get; set; } = 60;
    public double StateCorrectionMinFactor { get; set; } = 0.50;
    public double StateCorrectionMaxFactor { get; set; } = 2.50;

    // Live pricing/current-season volume defaults.
    public List<int> BaseSeasonIds { get; set; } = [];
    public int CurrentSeasonId { get; set; }
    public int? DefaultBeforeRound { get; set; }
    public bool UseCurrentSeasonVolume { get; set; } = true;
    public double DefaultEmpiricalWeight { get; set; } = 0.80;
    public double EdgeThreshold { get; set; } = 0.10;
    public int PriorStrengthMatches { get; set; } = 100;
    public List<double> TargetLines { get; set; } = [];
    public string RiskLevel { get; set; } = string.Empty;
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

        string baseDirPath = Path.Combine(AppContext.BaseDirectory, path);
        if (File.Exists(baseDirPath))
            return baseDirPath;

        string currentDirPath = Path.GetFullPath(path);
        return currentDirPath;
    }
}
