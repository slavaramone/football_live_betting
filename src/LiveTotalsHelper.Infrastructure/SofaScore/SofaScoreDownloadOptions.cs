namespace LiveTotalsHelper.Infrastructure.SofaScore;

public sealed class SofaScoreDownloadOptions
{
    public string League { get; init; } = string.Empty;
    public int TournamentId { get; init; }
    public int SeasonId { get; init; }
    public List<int> Rounds { get; } = [];
    public string CalendarMode { get; init; } = "round";
    public string OutputRoot { get; init; } = "data/sofascore";
    public int DelayMs { get; init; } = 450;
    public bool Overwrite { get; init; }
    public bool DownloadIncidents { get; init; } = true;
    public bool DownloadStatistics { get; init; } = true;

    // Future fixtures usually have calendar metadata only. SofaScore often returns
    // 404/403 for incidents/statistics before a match has started or before stats exist.
    // Keep calendar/event-meta downloads successful and treat missing per-event details as warnings.
    public bool SkipDetailsForNotStartedEvents { get; init; } = true;
    public bool StrictEventDetails { get; init; }

    // SofaScore often returns HTTP 403 for plain HttpClient requests.
    // We keep a real browser context alive and perform API requests through Playwright.
    public bool Headless { get; init; } = true;
    public int WarmupDelayMs { get; init; } = 1_000;
    public string WarmupUrl { get; init; } = "https://www.sofascore.com";
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
}

public sealed class SofaScoreDownloadResult
{
    public int RoundsDownloaded { get; set; }
    public int EventsDiscovered { get; set; }
    public int FilesWritten { get; set; }
    public int FilesSkipped { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
}
