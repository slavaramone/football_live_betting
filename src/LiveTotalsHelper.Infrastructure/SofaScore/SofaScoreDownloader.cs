namespace LiveTotalsHelper.Infrastructure.SofaScore;

public sealed class SofaScoreDownloader
{
    private readonly SofaScoreClient _client;
    private readonly SofaScoreJsonFileStore _fileStore;
    public SofaScoreDownloader(SofaScoreClient client, SofaScoreJsonFileStore fileStore)
    {
        _client = client;
        _fileStore = fileStore;
    }

    public async Task<SofaScoreDownloadResult> DownloadAsync(
        SofaScoreDownloadOptions options,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        Validate(options);

        var result = new SofaScoreDownloadResult();
        string leagueSlug = FileNameSanitizer.Slugify(options.League);
        string seasonFolder = $"season-{options.SeasonId}";
        string baseFolder = Path.Combine(options.OutputRoot, leagueSlug, seasonFolder);

        foreach (int round in options.Rounds.Distinct().OrderBy(x => x))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string roundFolder = Path.Combine(baseFolder, $"round-{round:00}");
            string eventsFolder = Path.Combine(roundFolder, "events");

            await log.WriteLineAsync($"Round {round}: downloading calendar...");

            string calendarJson;
            try
            {
                calendarJson = await _client.GetCalendarAsync(options, round, cancellationToken);
            }
            catch (Exception ex)
            {
                string failure = $"Round {round} calendar failed: {ex.Message}";
                result.Failures.Add(failure);
                await log.WriteLineAsync($"  ERROR: {failure}");
                continue;
            }

            string calendarPath = Path.Combine(roundFolder, "calendar.json");
            Count(await _fileStore.WriteJsonAsync(calendarPath, calendarJson, options.Overwrite, cancellationToken), result);

            IReadOnlyList<SofaScoreEventSummary> events = SofaScoreEventSummary.FromCalendarJson(calendarJson);
            result.RoundsDownloaded++;
            result.EventsDiscovered += events.Count;

            await log.WriteLineAsync($"  Events: {events.Count}");

            var manifest = new SofaScoreRoundManifest
            {
                League = options.League,
                LeagueSlug = leagueSlug,
                TournamentId = options.TournamentId,
                SeasonId = options.SeasonId,
                Round = round,
                DownloadedAtUtc = DateTimeOffset.UtcNow,
                EventCount = events.Count,
                Events = events.Select(e => new SofaScoreRoundManifestEvent
                {
                    EventId = e.EventId,
                    Slug = e.Slug,
                    HomeTeam = e.HomeTeam,
                    AwayTeam = e.AwayTeam,
                    StartTimestamp = e.StartTimestamp,
                    StatusType = e.StatusType,
                    StatusDescription = e.StatusDescription,
                    TournamentName = e.TournamentName,
                    TournamentSlug = e.TournamentSlug,
                    SeasonName = e.SeasonName,
                    SeasonYear = e.SeasonYear,
                    Round = e.Round,
                    Folder = Path.Combine("events", e.EventId.ToString())
                }).ToList()
            };

            Count(await _fileStore.WriteObjectAsync(Path.Combine(roundFolder, "manifest.json"), manifest, options.Overwrite, cancellationToken), result);

            foreach (SofaScoreEventSummary eventSummary in events)
            {
                string eventFolder = Path.Combine(eventsFolder, eventSummary.EventId.ToString());
                Count(await _fileStore.WriteObjectAsync(Path.Combine(eventFolder, "event-meta.json"), eventSummary, options.Overwrite, cancellationToken), result);

                bool skipEventDetails = options.SkipDetailsForNotStartedEvents && IsNotStartedOrFutureFixture(eventSummary);
                if (skipEventDetails)
                {
                    string warning = $"event {eventSummary.EventId} {eventSummary.HomeTeam} vs {eventSummary.AwayTeam}: status '{eventSummary.StatusType}' - calendar/event-meta saved, incidents/statistics skipped";
                    result.Warnings.Add(warning);
                    await log.WriteLineAsync($"    SKIP details: {warning}");
                }
                else
                {
                    if (options.DownloadIncidents)
                        await DownloadEventEndpoint(
                            endpointName: "incidents",
                            targetPath: Path.Combine(eventFolder, "incidents.json"),
                            download: ct => _client.GetIncidentsAsync(eventSummary.EventId, ct),
                            options,
                            result,
                            log,
                            cancellationToken);

                    if (options.DownloadStatistics)
                        await DownloadEventEndpoint(
                            endpointName: "statistics",
                            targetPath: Path.Combine(eventFolder, "statistics.json"),
                            download: ct => _client.GetStatisticsAsync(eventSummary.EventId, ct),
                            options,
                            result,
                            log,
                            cancellationToken);
                }

                if (options.DelayMs > 0)
                    await Task.Delay(options.DelayMs, cancellationToken);
            }
        }

        return result;
    }

    private async Task DownloadEventEndpoint(
        string endpointName,
        string targetPath,
        Func<CancellationToken, Task<string>> download,
        SofaScoreDownloadOptions options,
        SofaScoreDownloadResult result,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath) && !options.Overwrite)
        {
            result.FilesSkipped++;
            return;
        }

        try
        {
            string json = await download(cancellationToken);
            Count(await _fileStore.WriteJsonAsync(targetPath, json, options.Overwrite, cancellationToken), result);

            await log.WriteLineAsync($"    saved {endpointName}: {targetPath}");
        }
        catch (Exception ex)
        {
            string message = $"{targetPath}: {ex.Message}";

            if (options.StrictEventDetails)
            {
                result.Failures.Add(message);
                await log.WriteLineAsync($"    ERROR {endpointName}: {ex.Message}");
            }
            else
            {
                result.Warnings.Add(message);
                await log.WriteLineAsync($"    WARN {endpointName}: {ex.Message}");
            }
        }
    }

    private static bool IsNotStartedOrFutureFixture(SofaScoreEventSummary eventSummary)
    {
        string status = (eventSummary.StatusType ?? string.Empty).Trim().ToLowerInvariant();

        if (status is "notstarted" or "not_started" or "scheduled" or "postponed" or "canceled" or "cancelled")
            return true;

        // Finished and in-progress games can have incidents/statistics. Unknown statuses are
        // attempted and, if missing, are downgraded to warnings unless strict mode is enabled.
        return false;
    }

    private static void Count(FileWriteResult fileResult, SofaScoreDownloadResult result)
    {
        if (fileResult.WasWritten)
            result.FilesWritten++;
        else
            result.FilesSkipped++;
    }

    private static void Validate(SofaScoreDownloadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.League))
            throw new ArgumentException("League is required.");
        if (options.TournamentId <= 0)
            throw new ArgumentException("TournamentId must be positive.");
        if (options.SeasonId <= 0)
            throw new ArgumentException("SeasonId must be positive.");
        if (options.Rounds.Count == 0)
            throw new ArgumentException("At least one round is required.");
        if (options.DelayMs < 0)
            throw new ArgumentException("DelayMs cannot be negative.");
    }
}

public sealed class SofaScoreRoundManifest
{
    public string League { get; init; } = string.Empty;
    public string LeagueSlug { get; init; } = string.Empty;
    public int TournamentId { get; init; }
    public int SeasonId { get; init; }
    public int Round { get; init; }
    public DateTimeOffset DownloadedAtUtc { get; init; }
    public int EventCount { get; init; }
    public List<SofaScoreRoundManifestEvent> Events { get; init; } = [];
}

public sealed class SofaScoreRoundManifestEvent
{
    public long EventId { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public long? StartTimestamp { get; init; }
    public string StatusType { get; init; } = string.Empty;
    public string StatusDescription { get; init; } = string.Empty;
    public string TournamentName { get; init; } = string.Empty;
    public string TournamentSlug { get; init; } = string.Empty;
    public string SeasonName { get; init; } = string.Empty;
    public string SeasonYear { get; init; } = string.Empty;
    public int? Round { get; init; }
    public string Folder { get; init; } = string.Empty;
}
