using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LiveTotalsHelper.Infrastructure.SofaScore;
using Microsoft.Playwright;

namespace LiveTotalsHelper.Infrastructure.Flashscore;

public sealed class FlashscoreDownloader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly SofaScoreJsonFileStore _fileStore;

    public FlashscoreDownloader(SofaScoreJsonFileStore fileStore)
    {
        _fileStore = fileStore;
    }

    public async Task<FlashscoreDownloadResult> DownloadAsync(
        FlashscoreDownloadOptions options,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        Validate(options);

        var result = new FlashscoreDownloadResult();
        await log.WriteLineAsync($"Starting Playwright Chromium for Flashscore. Headless: {options.Headless}");
        await log.WriteLineAsync($"Opening: {options.Url}");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = options.Headless
        });

        IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            UserAgent = options.UserAgent,
            ViewportSize = new ViewportSize { Width = 1366, Height = 900 }
        });

        try
        {
            IPage page = await context.NewPageAsync();
            page.SetDefaultTimeout(120_000);

            await page.GotoAsync(options.Url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90_000
            });

            await TryAcceptCookiesAsync(page);

            if (options.RenderWaitMs > 0)
                await Task.Delay(options.RenderWaitMs, cancellationToken);

            int clicks = await ClickShowMoreUntilDoneAsync(page, options, log, cancellationToken);
            await log.WriteLineAsync($"Show more clicks: {clicks}");

            IReadOnlyList<FlashscoreRenderedMatch> matches = await ExtractMatchesAsync(page, cancellationToken);
            if (matches.Count == 0)
            {
                result.Failures.Add("No Flashscore match rows were found on the rendered page.");
                return result;
            }

            IReadOnlyList<FlashscoreCalendarEvent> events = matches
                .Select(x => ToCalendarEvent(x, options))
                .Where(x => x.Round > 0)
                .Where(x => options.Rounds.Count == 0 || options.Rounds.Contains(x.Round))
                .OrderByDescending(x => x.StartTimestamp ?? 0)
                .ThenBy(x => x.HomeTeam.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            result.EventsDiscovered = events.Count;
            if (events.Count == 0)
            {
                result.Failures.Add("Flashscore rows were found, but none matched the requested rounds.");
                return result;
            }

            string leagueSlug = FileNameSanitizer.Slugify(options.League);
            string baseFolder = Path.Combine(options.OutputRoot, leagueSlug, $"season-{options.SeasonId}");

            foreach (IGrouping<int, FlashscoreCalendarEvent> roundGroup in events.GroupBy(x => x.Round).OrderBy(x => x.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();

                int round = roundGroup.Key;
                List<FlashscoreCalendarEvent> roundEvents = roundGroup.ToList();
                string roundFolder = Path.Combine(baseFolder, $"round-{round:00}");
                string eventsFolder = Path.Combine(roundFolder, "events");
                string calendarPath = Path.Combine(roundFolder, "calendar.json");

                await log.WriteLineAsync($"Round {round}: saving Flashscore calendar ({roundEvents.Count} events)...");

                string calendarJson = JsonSerializer.Serialize(new { events = roundEvents }, JsonOptions);
                Count(await _fileStore.WriteJsonAsync(calendarPath, calendarJson, options.Overwrite, cancellationToken), result);

                var manifest = new SofaScoreRoundManifest
                {
                    League = options.League,
                    LeagueSlug = leagueSlug,
                    TournamentId = options.TournamentId,
                    SeasonId = options.SeasonId,
                    Round = round,
                    DownloadedAtUtc = DateTimeOffset.UtcNow,
                    EventCount = roundEvents.Count,
                    Events = roundEvents.Select(e => new SofaScoreRoundManifestEvent
                    {
                        EventId = e.Id,
                        Slug = e.Slug,
                        HomeTeam = e.HomeTeam.Name,
                        AwayTeam = e.AwayTeam.Name,
                        StartTimestamp = e.StartTimestamp,
                        StatusType = e.Status.Type,
                        StatusDescription = e.Status.Description,
                        TournamentName = e.Tournament.UniqueTournament.Name,
                        TournamentSlug = e.Tournament.UniqueTournament.Slug,
                        SeasonName = e.Season.Name,
                        SeasonYear = e.Season.Year,
                        Round = e.RoundInfo.Round,
                        Folder = Path.Combine("events", e.Id.ToString(CultureInfo.InvariantCulture))
                    }).ToList()
                };

                Count(await _fileStore.WriteObjectAsync(Path.Combine(roundFolder, "manifest.json"), manifest, options.Overwrite, cancellationToken), result);

                foreach (FlashscoreCalendarEvent calendarEvent in roundEvents)
                {
                    string eventFolder = Path.Combine(eventsFolder, calendarEvent.Id.ToString(CultureInfo.InvariantCulture));
                    Count(await _fileStore.WriteObjectAsync(Path.Combine(eventFolder, "event-meta.json"), calendarEvent, options.Overwrite, cancellationToken), result);

                    if (options.DownloadIncidents && IsFinished(calendarEvent))
                    {
                        await DownloadIncidentsAsync(
                            page,
                            calendarEvent,
                            Path.Combine(eventFolder, "incidents.json"),
                            options,
                            result,
                            log,
                            cancellationToken);
                    }

                    if (options.DelayMs > 0)
                        await Task.Delay(options.DelayMs, cancellationToken);
                }

                result.RoundsDownloaded++;
            }
        }
        finally
        {
            await context.CloseAsync();
        }

        return result;
    }

    private async Task DownloadIncidentsAsync(
        IPage page,
        FlashscoreCalendarEvent calendarEvent,
        string targetPath,
        FlashscoreDownloadOptions options,
        FlashscoreDownloadResult result,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetPath) && !options.Overwrite)
        {
            result.FilesSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(calendarEvent.SourceUrl))
        {
            result.Warnings.Add($"event {calendarEvent.Id}: missing Flashscore detail URL; incidents skipped");
            return;
        }

        try
        {
            await page.GotoAsync(calendarEvent.SourceUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90_000
            });

            if (options.DetailWaitMs > 0)
                await Task.Delay(options.DetailWaitMs, cancellationToken);

            IReadOnlyList<FlashscoreIncident> incidents = await ExtractIncidentsAsync(page, calendarEvent, cancellationToken);
            string json = JsonSerializer.Serialize(new { incidents }, JsonOptions);
            Count(await _fileStore.WriteJsonAsync(targetPath, json, options.Overwrite, cancellationToken), result);
            await log.WriteLineAsync($"    saved incidents: {targetPath} ({incidents.Count})");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
        {
            string warning = $"event {calendarEvent.Id} {calendarEvent.HomeTeam.Name} vs {calendarEvent.AwayTeam.Name}: incidents failed: {ex.Message}";
            result.Warnings.Add(warning);
            await log.WriteLineAsync($"    WARN incidents: {warning}");
        }
    }

    private static async Task<int> ClickShowMoreUntilDoneAsync(
        IPage page,
        FlashscoreDownloadOptions options,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        int clicks = 0;
        for (int attempt = 0; attempt < options.MaxShowMoreClicks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool hasVisibleShowMore = await page.EvaluateAsync<bool>(
                """
                () => Array.from(document.querySelectorAll('section[class*="scores"] button'))
                    .some(el => {
                        const text = (el.textContent || '').replace(/\s+/g, ' ').trim();
                        if (text !== 'Show more matches') return false;
                        const style = window.getComputedStyle(el);
                        const rect = el.getBoundingClientRect();
                        return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
                    })
                """);

            if (!hasVisibleShowMore)
                break;

            int before = await page.Locator("[data-event-row='true']").CountAsync();

            bool clicked = await page.EvaluateAsync<bool>(
                """
                () => {
                    const elements = Array.from(document.querySelectorAll('section[class*="scores"] button'));
                    const target = elements.find(el => {
                        const text = (el.textContent || '').replace(/\s+/g, ' ').trim();
                        if (text !== 'Show more matches') return false;
                        const style = window.getComputedStyle(el);
                        const rect = el.getBoundingClientRect();
                        return style.visibility !== 'hidden' && style.display !== 'none' && rect.width > 0 && rect.height > 0;
                    });
                    if (!target) return false;
                    target.scrollIntoView({ block: 'center' });
                    target.click();
                    return true;
                }
                """);

            if (!clicked)
                break;

            clicks++;

            if (options.ShowMoreWaitMs > 0)
                await Task.Delay(options.ShowMoreWaitMs, cancellationToken);

            int after = await page.Locator("[data-event-row='true']").CountAsync();
            if (after <= before)
            {
                try
                {
                    await page.WaitForFunctionAsync(
                        $"() => document.querySelectorAll('[data-event-row=\"true\"]').length > {before}",
                        null,
                        new PageWaitForFunctionOptions { Timeout = Math.Max(1_000, options.ShowMoreWaitMs) });
                    after = await page.Locator("[data-event-row='true']").CountAsync();
                }
                catch (TimeoutException)
                {
                    // Some pages keep the footer visible briefly after the last click. The row-count
                    // check below will stop the loop if no additional matches were appended.
                }
                catch (PlaywrightException)
                {
                    // Same as above: a failed wait is not a failed download.
                }
            }

            await log.WriteLineAsync($"  Show more {clicks}: rows {before} -> {after}");

            if (after <= before)
                break;
        }

        return clicks;
    }

    private static async Task<IReadOnlyList<FlashscoreRenderedMatch>> ExtractMatchesAsync(IPage page, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await page.EvaluateAsync<string>(
            """
            () => {
                const rows = [];
                let currentRound = "";
                const text = (node) => (node?.textContent || "").replace(/\s+/g, " ").trim();
                const cleanParticipant = (node) => {
                    if (!node) return "";
                    const name = node.querySelector('[class*="wcl-name"]');
                    return text(name || node);
                };

                document.querySelectorAll('.event__round, [data-event-row="true"]').forEach(node => {
                    if (node.classList.contains('event__round')) {
                        currentRound = text(node);
                        return;
                    }

                    if (!node.matches('[data-event-row="true"]')) return;

                    const link = node.querySelector('a.eventRowLink, a[href*="/match/"]');
                    const rawId = (node.id || '').replace(/^g_\d_/, '') || new URL(link?.href || location.href).searchParams.get('mid') || '';
                    const home = cleanParticipant(node.querySelector('.event__homeParticipant'));
                    const away = cleanParticipant(node.querySelector('.event__awayParticipant'));
                    const homeScore = text(node.querySelector('.event__score--home'));
                    const awayScore = text(node.querySelector('.event__score--away'));

                    rows.push({
                        sourceId: rawId,
                        roundText: currentRound,
                        timeText: text(node.querySelector('.event__time')),
                        homeTeam: home,
                        awayTeam: away,
                        homeScore: homeScore,
                        awayScore: awayScore,
                        url: link?.href || ''
                    });
                });

                return JSON.stringify(rows);
            }
            """);

        return JsonSerializer.Deserialize<List<FlashscoreRenderedMatch>>(json, JsonOptions) ?? [];
    }

    private static async Task<IReadOnlyList<FlashscoreIncident>> ExtractIncidentsAsync(
        IPage page,
        FlashscoreCalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await page.EvaluateAsync<string>(
            """
            () => {
                const text = (node) => (node?.textContent || "").replace(/\s+/g, " ").trim();
                const hrefId = (node) => {
                    const href = node?.getAttribute("href") || "";
                    const parts = href.split("/").filter(Boolean);
                    return parts.length ? parts[parts.length - 1] : "";
                };

                return JSON.stringify(Array.from(document.querySelectorAll(".smv__participantRow")).map(row => {
                    const icon = row.querySelector(".smv__incidentIcon");
                    const title = text(icon?.querySelector("title"));
                    const player = row.querySelector("a.smv__playerName");
                    const assist = row.querySelector(".smv__assist a");
                    const score = text(row.querySelector(".smv__incidentHomeScore, .smv__incidentAwayScore"));

                    return {
                        timeText: text(row.querySelector(".smv__timeBox")),
                        title,
                        score,
                        isHome: row.classList.contains("smv__homeParticipant"),
                        playerName: text(player),
                        playerSourceId: hrefId(player),
                        assistName: text(assist),
                        assistSourceId: hrefId(assist),
                        rowText: text(row)
                    };
                }));
            }
            """);

        List<FlashscoreRenderedIncident> rendered = JsonSerializer.Deserialize<List<FlashscoreRenderedIncident>>(json, JsonOptions) ?? [];
        var incidents = new List<FlashscoreIncident>();

        foreach (FlashscoreRenderedIncident row in rendered)
        {
            if (!TryMapIncident(row, calendarEvent, out FlashscoreIncident? incident))
                continue;

            incidents.Add(incident);
        }

        return incidents
            .OrderBy(x => x.Time)
            .ThenBy(x => x.AddedTime ?? 0)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static bool TryMapIncident(
        FlashscoreRenderedIncident row,
        FlashscoreCalendarEvent calendarEvent,
        out FlashscoreIncident? incident)
    {
        incident = null;

        if (!TryParseIncidentTime(row.TimeText, out int minute, out int? addedTime))
            return false;

        string title = row.Title.Trim();
        string normalizedTitle = title.ToLowerInvariant();
        string incidentType;
        string incidentClass;

        if (normalizedTitle.Contains("goal disallowed", StringComparison.OrdinalIgnoreCase))
            return false;

        if (normalizedTitle.Contains("goal", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "goal";
            incidentClass = normalizedTitle.Contains("penalty", StringComparison.OrdinalIgnoreCase)
                ? "penalty"
                : normalizedTitle.Contains("own", StringComparison.OrdinalIgnoreCase)
                    ? "ownGoal"
                    : "regular";
        }
        else if (normalizedTitle.Contains("yellow card", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "card";
            incidentClass = normalizedTitle.Contains("second", StringComparison.OrdinalIgnoreCase)
                ? "yellowRed"
                : "yellow";
        }
        else if (normalizedTitle.Contains("red card", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "card";
            incidentClass = "red";
        }
        else
        {
            return false;
        }

        ParseScore(row.Score, out int? homeScore, out int? awayScore);

        long incidentId = StablePositiveId(
            $"flashscore:incident:{calendarEvent.FlashscoreId}:{row.TimeText}:{title}:{row.IsHome}:{row.PlayerName}:{row.Score}");

        incident = new FlashscoreIncident
        {
            Id = incidentId,
            IncidentType = incidentType,
            IncidentClass = incidentClass,
            Time = minute,
            AddedTime = addedTime,
            TimeSeconds = ((minute + (addedTime ?? 0)) * 60),
            IsHome = row.IsHome,
            HomeScore = homeScore,
            AwayScore = awayScore,
            Player = string.IsNullOrWhiteSpace(row.PlayerName)
                ? null
                : new FlashscoreIncidentPerson
                {
                    Name = row.PlayerName,
                    Id = StablePositiveId($"flashscore:player:{Coalesce(row.PlayerSourceId, row.PlayerName)}")
                },
            Assist1 = string.IsNullOrWhiteSpace(row.AssistName)
                ? null
                : new FlashscoreIncidentPerson
                {
                    Name = row.AssistName,
                    Id = StablePositiveId($"flashscore:player:{Coalesce(row.AssistSourceId, row.AssistName)}")
                },
            Reason = title
        };

        return true;
    }

    private static bool TryParseIncidentTime(string value, out int minute, out int? addedTime)
    {
        minute = 0;
        addedTime = null;

        string normalized = (value ?? string.Empty).Replace("'", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        string[] parts = normalized.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute))
            return false;

        if (parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAddedTime))
            addedTime = parsedAddedTime;

        return minute > 0;
    }

    private static void ParseScore(string value, out int? homeScore, out int? awayScore)
    {
        homeScore = null;
        awayScore = null;

        string[] parts = (value ?? string.Empty).Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return;

        if (int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHome))
            homeScore = parsedHome;
        if (int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAway))
            awayScore = parsedAway;
    }

    private static async Task TryAcceptCookiesAsync(IPage page)
    {
        string[] labels =
        [
            "Accept all",
            "I Accept",
            "Agree",
            "Consent",
            "Allow all"
        ];

        foreach (string label in labels)
        {
            try
            {
                ILocator button = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = label });
                if (await button.CountAsync() > 0)
                {
                    await button.First.ClickAsync(new LocatorClickOptions { Timeout = 2_000 });
                    return;
                }
            }
            catch
            {
                // Cookie banners vary by country; failure to click is non-fatal.
            }
        }
    }

    private static FlashscoreCalendarEvent ToCalendarEvent(FlashscoreRenderedMatch match, FlashscoreDownloadOptions options)
    {
        int round = ParseRound(match.RoundText);
        int? homeScore = ParseNullableScore(match.HomeScore);
        int? awayScore = ParseNullableScore(match.AwayScore);
        long? startTimestamp = ParseStartTimestamp(match.TimeText, options.DefaultYear);
        string sourceId = string.IsNullOrWhiteSpace(match.SourceId)
            ? $"{match.HomeTeam}-{match.AwayTeam}-{match.TimeText}"
            : match.SourceId;

        string seasonYear = string.IsNullOrWhiteSpace(options.SeasonYear)
            ? options.DefaultYear.ToString(CultureInfo.InvariantCulture)
            : options.SeasonYear;
        string seasonName = string.IsNullOrWhiteSpace(options.SeasonName)
            ? seasonYear
            : options.SeasonName;

        bool hasScore = homeScore.HasValue && awayScore.HasValue;

        return new FlashscoreCalendarEvent
        {
            Id = StablePositiveId($"flashscore:event:{sourceId}"),
            FlashscoreId = sourceId,
            Slug = BuildSlug(match.HomeTeam, match.AwayTeam, sourceId),
            SourceUrl = match.Url,
            Tournament = new FlashscoreTournament
            {
                Name = options.League,
                Slug = FileNameSanitizer.Slugify(options.League),
                UniqueTournament = new FlashscoreUniqueTournament
                {
                    Id = options.TournamentId,
                    Name = options.League,
                    Slug = FileNameSanitizer.Slugify(options.League)
                },
                Category = new FlashscoreCategory
                {
                    Country = new FlashscoreCountry
                    {
                        Name = options.CountryName,
                        Alpha2 = options.CountryCode,
                        Alpha3 = options.CountryCode
                    }
                }
            },
            Season = new FlashscoreSeason
            {
                Id = options.SeasonId,
                Name = seasonName,
                Year = seasonYear
            },
            RoundInfo = new FlashscoreRoundInfo
            {
                Round = round
            },
            HomeTeam = new FlashscoreTeam
            {
                Id = StablePositiveId($"flashscore:team:{match.HomeTeam}"),
                Name = match.HomeTeam,
                Slug = FileNameSanitizer.Slugify(match.HomeTeam),
                ShortName = match.HomeTeam
            },
            AwayTeam = new FlashscoreTeam
            {
                Id = StablePositiveId($"flashscore:team:{match.AwayTeam}"),
                Name = match.AwayTeam,
                Slug = FileNameSanitizer.Slugify(match.AwayTeam),
                ShortName = match.AwayTeam
            },
            StartTimestamp = startTimestamp,
            Status = new FlashscoreStatus
            {
                Code = hasScore ? 100 : 0,
                Type = hasScore ? "finished" : "notstarted",
                Description = hasScore ? "Ended" : "Not started"
            },
            HomeScore = hasScore ? new FlashscoreScore { Current = homeScore } : null,
            AwayScore = hasScore ? new FlashscoreScore { Current = awayScore } : null
        };
    }

    private static long? ParseStartTimestamp(string value, int year)
    {
        string normalized = value.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        string[] formats =
        [
            "dd.MM. HH:mm",
            "d.MM. HH:mm",
            "dd.MM.yyyy HH:mm",
            "d.MM.yyyy HH:mm",
            "dd.MM.yy HH:mm",
            "d.MM.yy HH:mm"
        ];

        foreach (string format in formats)
        {
            string candidate = format.Contains("y", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : $"{normalized}";

            if (DateTime.TryParseExact(candidate, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
            {
                if (!format.Contains("y", StringComparison.OrdinalIgnoreCase))
                    parsed = new DateTime(year, parsed.Month, parsed.Day, parsed.Hour, parsed.Minute, 0, DateTimeKind.Local);

                return new DateTimeOffset(parsed).ToUnixTimeSeconds();
            }
        }

        return null;
    }

    private static int ParseRound(string value)
    {
        Match match = Regex.Match(value ?? string.Empty, @"\d+");
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int round)
            ? round
            : 0;
    }

    private static int? ParseNullableScore(string value)
        => int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int score) ? score : null;

    private static string BuildSlug(string homeTeam, string awayTeam, string sourceId)
        => $"{FileNameSanitizer.Slugify(homeTeam)}-{FileNameSanitizer.Slugify(awayTeam)}-{sourceId}".Trim('-');

    private static bool IsFinished(FlashscoreCalendarEvent calendarEvent)
        => calendarEvent.Status.Type.Equals("finished", StringComparison.OrdinalIgnoreCase);

    private static string Coalesce(params string[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static long StablePositiveId(string value)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;

        ulong hash = offset;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= prime;
        }

        return (long)(hash & 0x7fffffffffffffffUL);
    }

    private static void Count(FileWriteResult fileResult, FlashscoreDownloadResult result)
    {
        if (fileResult.WasWritten)
            result.FilesWritten++;
        else
            result.FilesSkipped++;
    }

    private static void Validate(FlashscoreDownloadOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
            throw new ArgumentException("Url is required.");
        if (string.IsNullOrWhiteSpace(options.League))
            throw new ArgumentException("League is required.");
        if (options.TournamentId <= 0)
            throw new ArgumentException("TournamentId must be positive.");
        if (options.SeasonId <= 0)
            throw new ArgumentException("SeasonId must be positive.");
        if (options.RenderWaitMs < 0)
            throw new ArgumentException("RenderWaitMs cannot be negative.");
        if (options.DetailWaitMs < 0)
            throw new ArgumentException("DetailWaitMs cannot be negative.");
        if (options.ShowMoreWaitMs < 0)
            throw new ArgumentException("ShowMoreWaitMs cannot be negative.");
        if (options.MaxShowMoreClicks < 0)
            throw new ArgumentException("MaxShowMoreClicks cannot be negative.");
        if (options.DelayMs < 0)
            throw new ArgumentException("DelayMs cannot be negative.");
        if (options.DefaultYear < 1900 || options.DefaultYear > 2200)
            throw new ArgumentException("DefaultYear must be a four-digit year.");
    }
}

public sealed class FlashscoreRenderedMatch
{
    public string SourceId { get; init; } = string.Empty;
    public string RoundText { get; init; } = string.Empty;
    public string TimeText { get; init; } = string.Empty;
    public string HomeTeam { get; init; } = string.Empty;
    public string AwayTeam { get; init; } = string.Empty;
    public string HomeScore { get; init; } = string.Empty;
    public string AwayScore { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class FlashscoreRenderedIncident
{
    public string TimeText { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Score { get; init; } = string.Empty;
    public bool IsHome { get; init; }
    public string PlayerName { get; init; } = string.Empty;
    public string PlayerSourceId { get; init; } = string.Empty;
    public string AssistName { get; init; } = string.Empty;
    public string AssistSourceId { get; init; } = string.Empty;
    public string RowText { get; init; } = string.Empty;
}

public sealed class FlashscoreIncident
{
    public long Id { get; init; }
    public string IncidentType { get; init; } = string.Empty;
    public string IncidentClass { get; init; } = string.Empty;
    public int Time { get; init; }
    public int? AddedTime { get; init; }
    public int? TimeSeconds { get; init; }
    public bool IsHome { get; init; }
    public int? HomeScore { get; init; }
    public int? AwayScore { get; init; }
    public FlashscoreIncidentPerson? Player { get; init; }
    public FlashscoreIncidentPerson? Assist1 { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class FlashscoreIncidentPerson
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class FlashscoreCalendarEvent
{
    public long Id { get; init; }
    public string FlashscoreId { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public FlashscoreTournament Tournament { get; init; } = new();
    public FlashscoreSeason Season { get; init; } = new();
    public FlashscoreRoundInfo RoundInfo { get; init; } = new();
    public FlashscoreTeam HomeTeam { get; init; } = new();
    public FlashscoreTeam AwayTeam { get; init; } = new();
    public long? StartTimestamp { get; init; }
    public FlashscoreStatus Status { get; init; } = new();
    public FlashscoreScore? HomeScore { get; init; }
    public FlashscoreScore? AwayScore { get; init; }

    [JsonIgnore]
    public int Round => RoundInfo.Round;
}

public sealed class FlashscoreTournament
{
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public FlashscoreUniqueTournament UniqueTournament { get; init; } = new();
    public FlashscoreCategory Category { get; init; } = new();
}

public sealed class FlashscoreUniqueTournament
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
}

public sealed class FlashscoreCategory
{
    public FlashscoreCountry Country { get; init; } = new();
}

public sealed class FlashscoreCountry
{
    public string Name { get; init; } = string.Empty;
    public string Alpha2 { get; init; } = string.Empty;
    public string Alpha3 { get; init; } = string.Empty;
}

public sealed class FlashscoreSeason
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Year { get; init; } = string.Empty;
}

public sealed class FlashscoreRoundInfo
{
    public int Round { get; init; }
}

public sealed class FlashscoreTeam
{
    public long Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Slug { get; init; } = string.Empty;
    public string ShortName { get; init; } = string.Empty;
}

public sealed class FlashscoreStatus
{
    public int Code { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
}

public sealed class FlashscoreScore
{
    public int? Current { get; init; }
}
