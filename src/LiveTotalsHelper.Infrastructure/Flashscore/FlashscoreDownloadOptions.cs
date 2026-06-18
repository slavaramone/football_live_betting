namespace LiveTotalsHelper.Infrastructure.Flashscore;

public sealed class FlashscoreDownloadOptions
{
    public string Url { get; init; } = string.Empty;
    public string League { get; init; } = string.Empty;
    public int TournamentId { get; init; }
    public int SeasonId { get; init; }
    public string SeasonName { get; init; } = string.Empty;
    public string SeasonYear { get; init; } = string.Empty;
    public string CountryName { get; init; } = string.Empty;
    public string CountryCode { get; init; } = string.Empty;
    public List<int> Rounds { get; } = [];
    public string OutputRoot { get; init; } = "data/flashscore";
    public bool Overwrite { get; init; }
    public bool DownloadIncidents { get; init; } = true;
    public bool DownloadStatistics { get; init; } = true;
    public bool DownloadOdds { get; init; } = true;
    public bool Headless { get; init; } = true;
    public int RenderWaitMs { get; init; } = 8_000;
    public int DetailWaitMs { get; init; } = 3_000;
    public int ShowMoreWaitMs { get; init; } = 2_000;
    public int MaxShowMoreClicks { get; init; } = 40;
    public int DelayMs { get; init; } = 450;
    public int DefaultYear { get; init; } = DateTimeOffset.UtcNow.Year;
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
}

public sealed class FlashscoreDownloadResult
{
    public int RoundsDownloaded { get; set; }
    public int EventsDiscovered { get; set; }
    public int FilesWritten { get; set; }
    public int FilesSkipped { get; set; }
    public List<string> Warnings { get; } = [];
    public List<string> Failures { get; } = [];
}
