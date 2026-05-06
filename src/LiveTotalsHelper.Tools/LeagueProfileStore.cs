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
    public string ModelPath { get; set; } = string.Empty;
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
