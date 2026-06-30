using System.Text.Json;
using System.Text.Json.Serialization;

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
    public string StateTrigger { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public double Line { get; set; }
    public double MinProbabilityMove { get; set; }
    public double MinEdge { get; set; }
    public bool AllowBet { get; set; } = true;
    public string Notes { get; set; } = string.Empty;
}

public sealed class LeagueProfile
{
    public const string V4ModelVersion = "v4-after-goal-angles";

    public string ModelVersion { get; set; } = string.Empty;
    public LeagueProfileIdentity Identity { get; set; } = new();
    public LeagueProfileDataSources DataSources { get; set; } = new();
    public LeagueProfileSeasons Seasons { get; set; } = new();
    public LeagueProfileArtifacts Artifacts { get; set; } = new();
    public AfterGoalEventsProfileSettings AfterGoalEvents { get; set; } = new();
    public AfterGoalAnglesProfileSettings AfterGoalAngles { get; set; } = new();
    public AfterGoalTeamProfilesSettings AfterGoalTeamProfiles { get; set; } = new();
    public AfterGoalEntryGatesSettings AfterGoalEntryGates { get; set; } = new();
    public LeagueProfileSafetySettings Safety { get; set; } = new();
    public LeagueProfileLegacySettings Legacy { get; set; } = new();

    // Backward-compatible flattened properties used by download/import/app shell code.
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string League { get; set; } = string.Empty;
    public string FlashscoreFixturesUrl { get; set; } = string.Empty;
    public string FlashscoreResultsUrl { get; set; } = string.Empty;
    public int FlashscoreTournamentId { get; set; }
    public int FlashscoreSeasonId { get; set; }
    public string FlashscoreSeasonName { get; set; } = string.Empty;
    public string FlashscoreSeasonYear { get; set; } = string.Empty;
    public string FlashscoreCountry { get; set; } = string.Empty;
    public string FlashscoreCountryCode { get; set; } = string.Empty;
    public int CurrentSeasonId { get; set; }
    public List<int> BaseSeasonIds { get; set; } = [];
    public int? DefaultBeforeRound { get; set; }
    public bool UseCurrentSeasonVolume { get; set; } = true;
    public double EdgeThreshold { get; set; } = 0.10;
    public bool UseProbabilityMoveFilter { get; set; }
    public bool UnderSignalsBettingAllowed { get; set; }
    public string DecisionMode { get; set; } = "ModelDisabled";
    public int? MinMinute { get; set; }
    public bool RequireGoalTrigger { get; set; }
    public double? MinLine { get; set; }
    public List<double> TargetLines { get; set; } = [];
    public List<double> AllowedLines { get; set; } = [];
    public bool FallbackBettingEnabled { get; set; } = false;
    public List<LiveTotalProfileBettingRule> LiveBettingRules { get; set; } = [];
    public string DecisionRulesNotes { get; set; } = "Model V4 profiles are directional filters only; market gates are required later.";
    public string RiskLevel { get; set; } = "Model V4 research";
    public string Notes { get; set; } = string.Empty;

    [JsonIgnore]
    public string ModelRoot { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeagueProfileIdentity
{
    public string LeagueKey { get; set; } = string.Empty;
    public string LeagueName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string CompetitionType { get; set; } = "league";
}

public sealed class LeagueProfileDataSources
{
    public FlashscoreSourceSettings Flashscore { get; set; } = new();
    public SofaScoreSourceSettings SofaScore { get; set; } = new();
}

public sealed class FlashscoreSourceSettings
{
    public bool Enabled { get; set; } = true;
    public string FixturesUrl { get; set; } = string.Empty;
    public string ResultsUrl { get; set; } = string.Empty;
    public int TournamentId { get; set; }
    public int SeasonId { get; set; }
    public string SeasonName { get; set; } = string.Empty;
    public string SeasonYear { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
}

public sealed class SofaScoreSourceSettings
{
    public bool Enabled { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class LeagueProfileSeasons
{
    public List<int> Available { get; set; } = [];
    public int DefaultTrainFrom { get; set; }
    public int DefaultTrainTo { get; set; }
    public int DefaultTestSeason { get; set; }
    public bool AllowDefaultLatestSeasonTestSplit { get; set; } = true;
}

public sealed class LeagueProfileArtifacts
{
    public string AfterGoalEventsFile { get; set; } = "after-goal-events.csv";
    public string AfterGoalAnglesDir { get; set; } = "after-goal-angles";
    public string AfterGoalProfilesDir { get; set; } = "after-goal-profiles";
}

public sealed class AfterGoalEventsProfileSettings
{
    public bool Enabled { get; set; } = true;
    public bool IncludePenaltyShootouts { get; set; }
    public bool RequireGoalScoreSnapshot { get; set; } = true;
    public bool SkipMatchesWhenFinalScoreMismatch { get; set; } = true;
    public string StoppageTimeMode { get; set; } = "period-aware";
    public string OrderBy { get; set; } = "MatchId+GoalIndex";
}

public sealed class AfterGoalAnglesProfileSettings
{
    public bool Enabled { get; set; } = true;
    public int MinSample { get; set; } = 30;
    public int StrongSample { get; set; } = 80;
    public double ShrinkK { get; set; } = 50;
    public bool IncludeOpponentPairsDefault { get; set; }
    public AfterGoalBaselineSettings Baseline { get; set; } = new();
    public AfterGoalStateBucketSettings StateBuckets { get; set; } = new();
}

public sealed class AfterGoalBaselineSettings
{
    public List<string> PrimaryKey { get; set; } = ["LeagueKey", "MinuteBand", "TotalGoalsAfterBand", "ScoreGapAfterBand", "Half"];
    public List<List<string>> Fallbacks { get; set; } =
    [
        ["LeagueKey", "MinuteBand", "TotalGoalsAfterBand", "Half"],
        ["LeagueKey", "MinuteBand", "Half"],
        ["LeagueKey", "Half"],
        ["LeagueKey"]
    ];
}

public sealed class AfterGoalStateBucketSettings
{
    public List<string> MinuteBands { get; set; } = ["00-15", "16-30", "31-45+", "46-60", "61-75", "76-90+"];
    public List<string> TotalGoalsAfterBands { get; set; } = ["1", "2", "3", "4", "5+"];
    public List<string> ScoreGapAfterBands { get; set; } = ["Draw", "Lead1", "Lead2", "Lead3Plus"];
}

public sealed class AfterGoalTeamProfilesSettings
{
    public bool Enabled { get; set; } = true;
    public int MinTrainSample { get; set; } = 50;
    public int MinTestSample { get; set; } = 15;
    public double MinTrainAbsResidual { get; set; } = 0.10;
    public double MinTestAbsResidual { get; set; } = 0.05;
    public double StrongTestAbsResidual { get; set; } = 0.15;
    public bool RequireTestConfirmation { get; set; } = true;
    public AfterGoalWatchlistSettings Watchlist { get; set; } = new();
}

public sealed class AfterGoalWatchlistSettings
{
    public bool Enabled { get; set; } = true;
    public int TrainSampleTolerance { get; set; } = 10;
    public int TestSampleTolerance { get; set; } = 5;
    public double ResidualTolerance { get; set; } = 0.03;
}

public sealed class AfterGoalEntryGatesSettings
{
    public bool Enabled { get; set; }
    public string Description { get; set; } = "Reserved for Patch 4. Profiles are not automatic bet triggers.";
    public List<string> DefaultAllowedMinuteBands { get; set; } = ["00-15", "16-30", "31-45+", "46-60", "61-75"];
    public List<string> DefaultAvoidMinuteBands { get; set; } = ["76-90+"];
    public List<string> DefaultAllowedScoreGapBands { get; set; } = ["Draw", "Lead1", "Lead2"];
    public List<string> DefaultAvoidScoreGapBands { get; set; } = ["Lead3Plus"];
    public List<string> DefaultAllowedTotalGoalsAfterBands { get; set; } = ["1", "2", "3", "4"];
    public List<string> DefaultAvoidTotalGoalsAfterBands { get; set; } = ["5+"];
    public bool RequireTriggerAgreement { get; set; }
    public string ConflictPolicy { get; set; } = "NoBet";
    public bool MarketGateRequired { get; set; } = true;
}

public sealed class LeagueProfileSafetySettings
{
    public bool FailOnLeagueKeyMismatch { get; set; } = true;
    public bool FailOnEmptyTestSeason { get; set; } = true;
    public bool FailOnTrainTestOverlap { get; set; } = true;
    public bool FailOnMissingRequiredColumns { get; set; } = true;
    public bool DoNotUseProfilesAsAutomaticBetTriggers { get; set; } = true;
}

public sealed class LeagueProfileLegacySettings
{
    public bool Enabled { get; set; }
    public string Reason { get; set; } = "Disabled after migration to Model V4 after-goal angle workflow.";

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LeagueProfileValidationResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];
    public bool IsValid => Errors.Count == 0;
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
        LeagueProfilesConfig config = LoadConfig(path);
        ApplyProfileDefaults(config);
        ValidateOrThrow(config.Profiles);
        return new LeagueProfileStore(config.Profiles);
    }

    public static async Task<LeagueProfileStore> LoadAsync(string path, CancellationToken cancellationToken)
    {
        LeagueProfilesConfig config = await LoadConfigAsync(path, cancellationToken);
        ApplyProfileDefaults(config);
        ValidateOrThrow(config.Profiles);
        return new LeagueProfileStore(config.Profiles);
    }

    public static LeagueProfileValidationResult ValidateFile(string path)
    {
        LeagueProfilesConfig config = LoadConfig(path);
        ApplyProfileDefaults(config);
        return Validate(config.Profiles);
    }

    public static LeagueProfileValidationResult Validate(IEnumerable<LeagueProfile> profiles)
    {
        var result = new LeagueProfileValidationResult();
        foreach (LeagueProfile profile in profiles)
            ValidateProfile(profile, result);
        return result;
    }

    public LeagueProfile FindRequired(string profileKeyOrName)
    {
        LeagueProfile? profile = _profiles.FirstOrDefault(p =>
            p.Key.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Name.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.League.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Identity.LeagueKey.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase) ||
            p.Identity.LeagueName.Equals(profileKeyOrName, StringComparison.OrdinalIgnoreCase));

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

    public string ResolveArtifactPath(LeagueProfile profile, string artifactPath)
        => ResolveProfileArtifactPath(profile, artifactPath);

    public static string ResolveProfileArtifactPath(LeagueProfile profile, string artifactPath)
    {
        if (Path.IsPathRooted(artifactPath))
            return artifactPath;

        string root = string.IsNullOrWhiteSpace(profile.ModelRoot)
            ? @"C:\Temp\football_data\models"
            : profile.ModelRoot;
        string key = string.IsNullOrWhiteSpace(profile.Key) ? Slug(profile.League) : profile.Key;
        return Path.Combine(root, key, artifactPath);
    }

    private static LeagueProfilesConfig LoadConfig(string path)
    {
        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"League profiles file was not found: {resolvedPath}", resolvedPath);

        string json = File.ReadAllText(resolvedPath);
        return JsonSerializer.Deserialize<LeagueProfilesConfig>(json, JsonOptions()) ?? new LeagueProfilesConfig();
    }

    private static async Task<LeagueProfilesConfig> LoadConfigAsync(string path, CancellationToken cancellationToken)
    {
        string resolvedPath = ResolvePath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"League profiles file was not found: {resolvedPath}", resolvedPath);

        await using FileStream stream = File.OpenRead(resolvedPath);
        return await JsonSerializer.DeserializeAsync<LeagueProfilesConfig>(stream, JsonOptions(), cancellationToken)
            ?? new LeagueProfilesConfig();
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static void ApplyProfileDefaults(LeagueProfilesConfig config)
    {
        string modelRoot = string.IsNullOrWhiteSpace(config.ModelRoot)
            ? @"C:\Temp\football_data\models"
            : config.ModelRoot;

        foreach (LeagueProfile profile in config.Profiles)
        {
            profile.ModelRoot = modelRoot;

            if (string.IsNullOrWhiteSpace(profile.ModelVersion))
                profile.ModelVersion = string.Empty;

            if (string.IsNullOrWhiteSpace(profile.Key))
                profile.Key = Coalesce(profile.Identity.LeagueKey, Slug(profile.Identity.LeagueName), Slug(profile.League));
            if (string.IsNullOrWhiteSpace(profile.Name))
                profile.Name = Coalesce(profile.Identity.LeagueName, profile.League);
            if (string.IsNullOrWhiteSpace(profile.League))
                profile.League = Coalesce(profile.Identity.LeagueName, profile.Name);

            if (string.IsNullOrWhiteSpace(profile.Identity.LeagueKey))
                profile.Identity.LeagueKey = profile.Key;
            if (string.IsNullOrWhiteSpace(profile.Identity.LeagueName))
                profile.Identity.LeagueName = profile.League;
            if (string.IsNullOrWhiteSpace(profile.Identity.Country))
                profile.Identity.Country = Coalesce(profile.FlashscoreCountry, profile.DataSources.Flashscore.Country);
            if (string.IsNullOrWhiteSpace(profile.Identity.CountryCode))
                profile.Identity.CountryCode = Coalesce(profile.FlashscoreCountryCode, profile.DataSources.Flashscore.CountryCode);

            FlashscoreSourceSettings flashscore = profile.DataSources.Flashscore;
            profile.FlashscoreFixturesUrl = Coalesce(profile.FlashscoreFixturesUrl, flashscore.FixturesUrl);
            profile.FlashscoreResultsUrl = Coalesce(profile.FlashscoreResultsUrl, flashscore.ResultsUrl);
            profile.FlashscoreTournamentId = profile.FlashscoreTournamentId > 0 ? profile.FlashscoreTournamentId : flashscore.TournamentId;
            profile.FlashscoreSeasonId = profile.FlashscoreSeasonId > 0 ? profile.FlashscoreSeasonId : flashscore.SeasonId;
            profile.FlashscoreSeasonName = Coalesce(profile.FlashscoreSeasonName, flashscore.SeasonName);
            profile.FlashscoreSeasonYear = Coalesce(profile.FlashscoreSeasonYear, flashscore.SeasonYear);
            profile.FlashscoreCountry = Coalesce(profile.FlashscoreCountry, flashscore.Country, profile.Identity.Country);
            profile.FlashscoreCountryCode = Coalesce(profile.FlashscoreCountryCode, flashscore.CountryCode, profile.Identity.CountryCode);

            if (profile.CurrentSeasonId <= 0 && profile.Seasons.DefaultTestSeason > 0)
                profile.CurrentSeasonId = profile.Seasons.DefaultTestSeason;
            if (profile.CurrentSeasonId <= 0 && profile.FlashscoreSeasonId > 0)
                profile.CurrentSeasonId = profile.FlashscoreSeasonId;
            if (profile.FlashscoreSeasonId <= 0 && profile.CurrentSeasonId > 0)
                profile.FlashscoreSeasonId = profile.CurrentSeasonId;
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear) && profile.FlashscoreSeasonId > 0)
                profile.FlashscoreSeasonYear = profile.FlashscoreSeasonId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(profile.FlashscoreSeasonName) && !string.IsNullOrWhiteSpace(profile.FlashscoreSeasonYear))
                profile.FlashscoreSeasonName = profile.FlashscoreSeasonYear;

            if (profile.BaseSeasonIds.Count == 0 && profile.Seasons.DefaultTrainFrom > 0 && profile.Seasons.DefaultTrainTo >= profile.Seasons.DefaultTrainFrom)
            {
                for (int season = profile.Seasons.DefaultTrainFrom; season <= profile.Seasons.DefaultTrainTo; season++)
                    profile.BaseSeasonIds.Add(season);
            }
            if (profile.TargetLines.Count == 0)
                profile.TargetLines = config.DefaultTargetLines.Count > 0 ? config.DefaultTargetLines.ToList() : [2.5, 3.5];
            if (profile.AllowedLines.Count == 0)
                profile.AllowedLines = config.DefaultAllowedLines.Count > 0 ? config.DefaultAllowedLines.ToList() : profile.TargetLines.ToList();
            if (string.IsNullOrWhiteSpace(profile.Notes))
                profile.Notes = "Model V4 after-goal angle profile. Team profiles are directional filters only, not automatic bet triggers.";
        }
    }

    private static void ValidateOrThrow(IReadOnlyList<LeagueProfile> profiles)
    {
        LeagueProfileValidationResult validation = Validate(profiles);
        if (!validation.IsValid)
            throw new ArgumentException("League profile validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, validation.Errors.Select(x => "- " + x)));
    }

    private static void ValidateProfile(LeagueProfile profile, LeagueProfileValidationResult result)
    {
        string prefix = string.IsNullOrWhiteSpace(profile.Key) ? "<unknown>" : profile.Key;
        if (!profile.ModelVersion.Equals(LeagueProfile.V4ModelVersion, StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"{prefix}: modelVersion must be '{LeagueProfile.V4ModelVersion}'.");
        if (string.IsNullOrWhiteSpace(profile.Identity.LeagueKey))
            result.Errors.Add($"{prefix}: identity.leagueKey is required.");
        if (string.IsNullOrWhiteSpace(profile.Identity.LeagueName))
            result.Errors.Add($"{prefix}: identity.leagueName is required.");
        if (profile.DataSources.Flashscore.Enabled && string.IsNullOrWhiteSpace(profile.DataSources.Flashscore.FixturesUrl) && string.IsNullOrWhiteSpace(profile.FlashscoreFixturesUrl))
            result.Errors.Add($"{prefix}: enabled Flashscore source requires fixturesUrl for downloader defaults.");

        if (profile.Seasons.DefaultTrainFrom > 0 || profile.Seasons.DefaultTrainTo > 0 || profile.Seasons.DefaultTestSeason > 0)
        {
            if (profile.Seasons.DefaultTrainFrom <= 0 || profile.Seasons.DefaultTrainTo <= 0 || profile.Seasons.DefaultTestSeason <= 0)
                result.Errors.Add($"{prefix}: seasons defaultTrainFrom/defaultTrainTo/defaultTestSeason must be specified together.");
            if (profile.Seasons.DefaultTrainTo < profile.Seasons.DefaultTrainFrom)
                result.Errors.Add($"{prefix}: seasons.defaultTrainTo must be >= defaultTrainFrom.");
            if (profile.Safety.FailOnTrainTestOverlap &&
                profile.Seasons.DefaultTestSeason >= profile.Seasons.DefaultTrainFrom &&
                profile.Seasons.DefaultTestSeason <= profile.Seasons.DefaultTrainTo)
                result.Errors.Add($"{prefix}: test season overlaps training season range.");
            if (profile.Seasons.Available.Count > 0 && !profile.Seasons.Available.Contains(profile.Seasons.DefaultTestSeason))
                result.Errors.Add($"{prefix}: default test season is not listed in seasons.available.");
        }

        if (profile.AfterGoalAngles.MinSample <= 0)
            result.Errors.Add($"{prefix}: afterGoalAngles.minSample must be positive.");
        if (profile.AfterGoalAngles.StrongSample < profile.AfterGoalAngles.MinSample)
            result.Errors.Add($"{prefix}: afterGoalAngles.strongSample must be >= minSample.");
        if (profile.AfterGoalAngles.ShrinkK < 0)
            result.Errors.Add($"{prefix}: afterGoalAngles.shrinkK must be non-negative.");
        if (profile.AfterGoalTeamProfiles.MinTrainSample <= 0)
            result.Errors.Add($"{prefix}: afterGoalTeamProfiles.minTrainSample must be positive.");
        if (profile.AfterGoalTeamProfiles.MinTestSample <= 0)
            result.Errors.Add($"{prefix}: afterGoalTeamProfiles.minTestSample must be positive.");
        if (profile.AfterGoalTeamProfiles.MinTrainAbsResidual < 0 ||
            profile.AfterGoalTeamProfiles.MinTestAbsResidual < 0 ||
            profile.AfterGoalTeamProfiles.StrongTestAbsResidual < 0)
            result.Errors.Add($"{prefix}: afterGoalTeamProfiles residual thresholds must be non-negative.");
        if (profile.AfterGoalTeamProfiles.Watchlist.TrainSampleTolerance < 0 ||
            profile.AfterGoalTeamProfiles.Watchlist.TestSampleTolerance < 0 ||
            profile.AfterGoalTeamProfiles.Watchlist.ResidualTolerance < 0)
            result.Errors.Add($"{prefix}: watchlist tolerances must be non-negative.");

        if (profile.AfterGoalEntryGates.Enabled)
            result.Errors.Add($"{prefix}: afterGoalEntryGates.enabled must remain false until Patch 4.");
        if (!profile.AfterGoalEntryGates.MarketGateRequired)
            result.Errors.Add($"{prefix}: afterGoalEntryGates.marketGateRequired must be true.");
        if (!profile.AfterGoalEntryGates.ConflictPolicy.Equals("NoBet", StringComparison.OrdinalIgnoreCase))
            result.Errors.Add($"{prefix}: afterGoalEntryGates.conflictPolicy must default to NoBet.");
        if (!profile.Safety.DoNotUseProfilesAsAutomaticBetTriggers)
            result.Errors.Add($"{prefix}: safety.doNotUseProfilesAsAutomaticBetTriggers must be true.");

        if (profile.Legacy.Enabled)
            result.Errors.Add($"{prefix}: legacy.enabled must be false.");

        foreach (string oldField in ActiveLegacyFieldNames)
        {
            if (profile.ExtensionData.ContainsKey(oldField))
                result.Errors.Add($"{prefix}: old active V3 field '{oldField}' must be removed or moved under disabled legacy.");
        }
    }

    private static readonly string[] ActiveLegacyFieldNames =
    [
        "modelPath",
        "stateCorrectionPath",
        "empiricalSettlementPath",
        "calibrationDatasetPath",
        "validationStateCorrectionPath",
        "stateCorrectionMinBucketMatches",
        "stateCorrectionMinFactor",
        "stateCorrectionMaxFactor",
        "stateCorrectionShrinkMatches",
        "stateCorrectionLateGameMode",
        "stateCorrectionLateGameStartMinute",
        "stateCorrectionLateGameFactorMultiplier",
        "stateCorrectionLateGameMaxFactor",
        "stateCorrectionLateGameMaxLine",
        "defaultEmpiricalWeight",
        "liveBettingRules"
    ];

    private static string Coalesce(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

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
