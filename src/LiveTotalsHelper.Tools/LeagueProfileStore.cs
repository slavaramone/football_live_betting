using System.Text.Json;
using LiveTotalsHelper.Core.MonteCarlo;

namespace LiveTotalsHelper.Tools;

public sealed class LeagueProfilesConfig
{
    public List<int> DefaultSnapshotMinutes { get; set; } = [10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85];
    public List<double> DefaultTargetLines { get; set; } = [2.5, 3.5];
    public List<double> DefaultAllowedLines { get; set; } = [2.5, 3.5];
    public MonteCarloConfig MonteCarlo { get; set; } = new();
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

    // UI shell settings retained so the Avalonia app can compile while the old model is replaced.
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
    public string DecisionRulesNotes { get; set; } = "Old live-total model removed; waiting for redesigned model.";
    public string RiskLevel { get; set; } = "Model disabled";
    public string Notes { get; set; } = string.Empty;
    public MonteCarloConfig MonteCarlo { get; set; } = new();
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
            if (profile.TargetLines.Count == 0)
                profile.TargetLines = config.DefaultTargetLines.Count > 0 ? config.DefaultTargetLines.ToList() : [2.5, 3.5];
            if (profile.AllowedLines.Count == 0)
                profile.AllowedLines = config.DefaultAllowedLines.Count > 0 ? config.DefaultAllowedLines.ToList() : profile.TargetLines.ToList();
            profile.MonteCarlo = profile.MonteCarlo.WithDefaultsFrom(config.MonteCarlo);
            if (string.IsNullOrWhiteSpace(profile.Notes))
                profile.Notes = "Old live-total model removed; this profile is retained for downloading/importing and the future Avalonia model shell.";
        }
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
