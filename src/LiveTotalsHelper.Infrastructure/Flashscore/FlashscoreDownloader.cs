using System.Globalization;
using System.Text.Encodings.Web;
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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly IReadOnlyDictionary<string, string> StatKeyAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["expectedgoalsxg"] = "expectedGoals",
        ["expectedgoals"] = "expectedGoals",
        ["ballpossession"] = "ballPossession",
        ["totalshots"] = "totalShotsOnGoal",
        ["shotsontarget"] = "shotsOnGoal",
        ["cornerkicks"] = "cornerKicks",
        ["yellowcards"] = "yellowCards",
        ["redcards"] = "redCards"
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
                bool roundMetadataEnriched = false;

                foreach (FlashscoreCalendarEvent calendarEvent in roundEvents)
                {
                    string eventFolder = Path.Combine(eventsFolder, calendarEvent.Id.ToString(CultureInfo.InvariantCulture));
                    bool hadStartTimestamp = calendarEvent.StartTimestamp.HasValue;

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

                    if (options.DownloadStatistics && IsFinished(calendarEvent))
                    {
                        await DownloadStatisticsAsync(
                            page,
                            calendarEvent,
                            Path.Combine(eventFolder, "statistics.json"),
                            options,
                            result,
                            log,
                            cancellationToken);
                    }

                    if (options.DownloadOdds && IsFinished(calendarEvent))
                    {
                        await DownloadOddsAsync(
                            page,
                            calendarEvent,
                            Path.Combine(eventFolder, "odds.json"),
                            options,
                            result,
                            log,
                            cancellationToken);
                    }

                    bool eventMetadataEnriched = !hadStartTimestamp && calendarEvent.StartTimestamp.HasValue;
                    roundMetadataEnriched |= eventMetadataEnriched;
                    Count(await _fileStore.WriteObjectAsync(
                        Path.Combine(eventFolder, "event-meta.json"),
                        calendarEvent,
                        options.Overwrite || eventMetadataEnriched,
                        cancellationToken), result);

                    if (options.DelayMs > 0)
                        await Task.Delay(options.DelayMs, cancellationToken);
                }

                await log.WriteLineAsync($"Round {round}: saving Flashscore calendar ({roundEvents.Count} events)...");

                string calendarJson = JsonSerializer.Serialize(new { events = roundEvents }, JsonOptions);
                Count(await _fileStore.WriteJsonAsync(calendarPath, calendarJson, options.Overwrite || roundMetadataEnriched, cancellationToken), result);

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

                Count(await _fileStore.WriteObjectAsync(
                    Path.Combine(roundFolder, "manifest.json"),
                    manifest,
                    options.Overwrite || roundMetadataEnriched,
                    cancellationToken), result);

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
        bool skipWrite = File.Exists(targetPath) && !options.Overwrite;
        if (skipWrite && calendarEvent.StartTimestamp.HasValue)
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

            await TrySetStartTimestampFromDetailPageAsync(page, calendarEvent, options.DefaultYear, log);
            if (skipWrite)
            {
                result.FilesSkipped++;
                return;
            }

            IReadOnlyList<FlashscoreIncident> incidents = await ExtractIncidentsAsync(page, calendarEvent, cancellationToken);
            string json = JsonSerializer.Serialize(new { calendarEvent.StartTimestamp, incidents }, JsonOptions);
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

    private async Task DownloadStatisticsAsync(
        IPage page,
        FlashscoreCalendarEvent calendarEvent,
        string targetPath,
        FlashscoreDownloadOptions options,
        FlashscoreDownloadResult result,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        bool skipWrite = File.Exists(targetPath) && !options.Overwrite;
        if (skipWrite && calendarEvent.StartTimestamp.HasValue)
        {
            result.FilesSkipped++;
            return;
        }

        if (string.IsNullOrWhiteSpace(calendarEvent.SourceUrl))
        {
            result.Warnings.Add($"event {calendarEvent.Id}: missing Flashscore detail URL; statistics skipped");
            return;
        }

        try
        {
            var statistics = new List<FlashscoreStatisticsPeriod>();
            (string Period, string Segment)[] periods =
            [
                ("ALL", "overall"),
                ("1ST", "1st-half"),
                ("2ND", "2nd-half")
            ];

            foreach ((string period, string segment) in periods)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string url = BuildMatchDetailUrl(calendarEvent.SourceUrl, $"summary/stats/{segment}");

                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 90_000
                });

                if (options.DetailWaitMs > 0)
                    await Task.Delay(options.DetailWaitMs, cancellationToken);

                await TrySetStartTimestampFromDetailPageAsync(page, calendarEvent, options.DefaultYear, log);
                if (skipWrite)
                {
                    result.FilesSkipped++;
                    return;
                }

                IReadOnlyList<FlashscoreStatisticsGroup> groups = await ExtractStatisticsGroupsAsync(page, cancellationToken);
                if (groups.Count > 0)
                {
                    statistics.Add(new FlashscoreStatisticsPeriod
                    {
                        Period = period,
                        Groups = groups.ToList()
                    });
                }
            }

            string json = JsonSerializer.Serialize(new { calendarEvent.StartTimestamp, statistics }, JsonOptions);
            Count(await _fileStore.WriteJsonAsync(targetPath, json, options.Overwrite, cancellationToken), result);
            await log.WriteLineAsync($"    saved statistics: {targetPath} ({statistics.Sum(x => x.Groups.Sum(g => g.StatisticsItems.Count))})");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
        {
            string warning = $"event {calendarEvent.Id} {calendarEvent.HomeTeam.Name} vs {calendarEvent.AwayTeam.Name}: statistics failed: {ex.Message}";
            result.Warnings.Add(warning);
            await log.WriteLineAsync($"    WARN statistics: {warning}");
        }
    }

    private async Task DownloadOddsAsync(
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
            result.Warnings.Add($"event {calendarEvent.Id}: missing Flashscore detail URL; odds skipped");
            return;
        }

        try
        {
            string oddsUrl = BuildMatchDetailUrl(calendarEvent.SourceUrl, "odds");
            await page.GotoAsync(oddsUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 90_000
            });

            if (options.DetailWaitMs > 0)
                await Task.Delay(options.DetailWaitMs, cancellationToken);

            string[] requestedMarkets = ["OVER/UNDER"];

            var markets = new List<FlashscoreOddsMarket>();
            foreach (string marketName in requestedMarkets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool selected = await TrySelectOddsMarketAsync(page, marketName);
                if (!selected)
                    continue;

                if (options.DetailWaitMs > 0)
                    await Task.Delay(Math.Max(250, options.DetailWaitMs / 2), cancellationToken);

                await ClickOddsShowMoreUntilDoneAsync(page, options, cancellationToken);

                IReadOnlyList<FlashscoreRawOddsRow> rawRows = await ExtractRawOddsRowsAsync(page, cancellationToken);
                List<FlashscoreOddsRow> rows = NormalizeOddsRows(marketName, rawRows);
                if (rows.Count == 0)
                    continue;

                markets.Add(new FlashscoreOddsMarket
                {
                    Name = marketName,
                    SourceUrl = page.Url,
                    Rows = rows
                });
            }

            var snapshot = new FlashscoreOddsSnapshot
            {
                SourceUrl = oddsUrl,
                DownloadedAtUtc = DateTime.UtcNow,
                Markets = markets
            };

            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            Count(await _fileStore.WriteJsonAsync(targetPath, json, options.Overwrite, cancellationToken), result);
            await log.WriteLineAsync($"    saved odds: {targetPath} ({markets.Sum(x => x.Rows.Count)})");
        }
        catch (Exception ex) when (ex is PlaywrightException or TimeoutException or JsonException)
        {
            string warning = $"event {calendarEvent.Id} {calendarEvent.HomeTeam.Name} vs {calendarEvent.AwayTeam.Name}: odds failed: {ex.Message}";
            result.Warnings.Add(warning);
            await log.WriteLineAsync($"    WARN odds: {warning}");
        }
    }

    private static async Task<int> ClickShowMoreUntilDoneAsync(
        IPage page,
        FlashscoreDownloadOptions options,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        int clicks = 0;
        bool targetRoundsRequested = options.Rounds.Count > 0;

        if (targetRoundsRequested && await RequestedRoundsAreLoadedAsync(page, options, cancellationToken))
        {
            await log.WriteLineAsync($"Requested round filter already loaded: {FormatRounds(options.Rounds)}");
            return clicks;
        }

        for (int attempt = 0; attempt < options.MaxShowMoreClicks; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool hasVisibleShowMore = await HasVisibleShowMoreMatchesButtonAsync(page);
            if (!hasVisibleShowMore)
                break;

            int before = await page.Locator("[data-event-row='true']").CountAsync();

            bool clicked = await ClickShowMoreMatchesButtonAsync(page);
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

            if (targetRoundsRequested && await RequestedRoundsAreLoadedAsync(page, options, cancellationToken))
            {
                await log.WriteLineAsync($"Requested round filter loaded after {clicks} Show more click(s): {FormatRounds(options.Rounds)}");
                break;
            }

            if (after <= before)
                break;
        }

        return clicks;
    }

    private static async Task<bool> HasVisibleShowMoreMatchesButtonAsync(IPage page)
    {
        return await page.EvaluateAsync<bool>(
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
    }

    private static async Task<bool> ClickShowMoreMatchesButtonAsync(IPage page)
    {
        return await page.EvaluateAsync<bool>(
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
    }

    private static async Task<bool> RequestedRoundsAreLoadedAsync(
        IPage page,
        FlashscoreDownloadOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Rounds.Count == 0)
            return false;

        IReadOnlyList<FlashscoreRenderedMatch> visibleMatches = await ExtractMatchesAsync(page, cancellationToken);
        Dictionary<int, int> visibleRoundCounts = visibleMatches
            .Select(match => ParseRound(match.RoundText))
            .Where(round => round > 0)
            .GroupBy(round => round)
            .ToDictionary(group => group.Key, group => group.Count());

        if (visibleRoundCounts.Count == 0)
            return false;

        int[] requestedRounds = options.Rounds.Distinct().OrderBy(round => round).ToArray();
        bool allRequestedRoundsVisible = requestedRounds.All(round =>
            visibleRoundCounts.TryGetValue(round, out int count) && count > 0);

        if (!allRequestedRoundsVisible)
            return false;

        // Flashscore appends older matches after each Show more click. If the first requested
        // round appears at the bottom, it can be only partially loaded. Stop only when an older
        // round is already visible, or when there is no Show more button left.
        int minRequestedRound = requestedRounds.Min();
        bool olderRoundVisible = visibleRoundCounts.Keys.Any(round => round < minRequestedRound);
        if (olderRoundVisible)
            return true;

        return !await HasVisibleShowMoreMatchesButtonAsync(page);
    }

    private static string FormatRounds(IEnumerable<int> rounds)
        => string.Join(",", rounds.Distinct().OrderBy(round => round));

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
                const first = (values, predicate) => values.map(x => (x || '').replace(/\s+/g, ' ').trim()).find(x => x && (!predicate || predicate(x))) || '';
                const looksLikeDateTime = (value) => /\b\d{1,2}\.\d{1,2}\.?(?:\d{2,4})?\s+\d{1,2}:\d{2}\b/.test(value);
                const looksLikeTime = (value) => /\b\d{1,2}:\d{2}\b/.test(value);
                const extractTimeText = (row) => {
                    const timeNode = row.querySelector('.event__time, [class*="event__time"], [class*="time"]');
                    const candidates = [];
                    if (timeNode) {
                        candidates.push(timeNode.getAttribute('title'));
                        candidates.push(timeNode.getAttribute('aria-label'));
                        candidates.push(timeNode.getAttribute('data-time'));
                        candidates.push(text(timeNode));
                    }
                    Array.from(row.querySelectorAll('[title], [aria-label], [data-time]')).forEach(el => {
                        candidates.push(el.getAttribute('title'));
                        candidates.push(el.getAttribute('aria-label'));
                        candidates.push(el.getAttribute('data-time'));
                    });
                    candidates.push(text(row));
                    return first(candidates, looksLikeDateTime) || first(candidates, looksLikeTime) || text(timeNode);
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
                    const timeText = extractTimeText(node);

                    rows.push({
                        sourceId: rawId,
                        roundText: currentRound,
                        timeText,
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
                const detectCardIcon = (icon) => {
                    if (!icon) return "";

                    const colorValues = [];
                    [icon, ...icon.querySelectorAll('*')].forEach(node => {
                        const style = window.getComputedStyle(node);
                        colorValues.push(style.color, style.backgroundColor, style.fill, style.stroke);
                        ['fill', 'stroke', 'color'].forEach(attribute => colorValues.push(node.getAttribute?.(attribute) || ''));
                    });

                    let hasRed = false;
                    let hasYellow = false;
                    colorValues.filter(Boolean).forEach(value => {
                        const numbers = value.match(/\d+(?:\.\d+)?/g)?.slice(0, 3).map(Number);
                        if (!numbers || numbers.length < 3) return;
                        const [red, green, blue] = numbers;
                        if (red >= 140 && red > green * 1.35 && red > blue * 1.35) hasRed = true;
                        if (red >= 150 && green >= 100 && blue <= Math.min(red, green) * 0.75) hasYellow = true;
                    });

                    if (hasRed && hasYellow) return "yellowRed";
                    if (hasRed) return "red";
                    if (hasYellow) return "yellow";
                    return "";
                };

                return JSON.stringify(Array.from(document.querySelectorAll(".smv__participantRow")).map(row => {
                    const icon = row.querySelector(".smv__incidentIcon");
                    const title = text(icon?.querySelector("title"));
                    const iconType = detectCardIcon(icon);
                    const player = row.querySelector("a.smv__playerName");
                    const assist = row.querySelector(".smv__assist a");
                    const scoreNodes = Array.from(row.querySelectorAll(
                        ".smv__incidentHomeScore, .smv__incidentAwayScore, [class*='incidentHomeScore'], [class*='incidentAwayScore']"));
                    const scoreParts = scoreNodes.map(text).filter(Boolean);
                    let score = scoreParts.length >= 2 ? `${scoreParts[0]}-${scoreParts[scoreParts.length - 1]}` : (scoreParts[0] || "");
                    if (!score || !/^\d+\s*-\s*\d+$/.test(score)) {
                        const scoreText = (row.textContent || "").replace(/\s+/g, " ").trim();
                        const match = scoreText.match(/(?:^|\s)(\d+)\s*-\s*(\d+)(?:\s|$)/);
                        if (match) score = `${match[1]}-${match[2]}`;
                    }

                    return {
                        timeText: text(row.querySelector(".smv__timeBox")),
                        title,
                        iconType,
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

            if (incident is not null)
                incidents.Add(incident);
        }

        return incidents
            .OrderBy(x => x.Time)
            .ThenBy(x => x.AddedTime ?? 0)
            .ThenBy(x => x.Id)
            .ToList();
    }

    private static async Task<IReadOnlyList<FlashscoreStatisticsGroup>> ExtractStatisticsGroupsAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await page.EvaluateAsync<string>(
            """
            () => {
                const text = (node) => (node?.textContent || "").replace(/\s+/g, " ").trim();
                const groups = [];
                const containers = document.querySelectorAll(".tabContent__match-statistics .section, .sectionsWrapper .section");

                containers.forEach(section => {
                    const groupName = text(section.querySelector(".sectionHeader, .stat__header, .section__title")) || "Statistics";
                    const statisticsItems = Array.from(section.querySelectorAll('[data-testid="wcl-statistics"]')).map(row => {
                        const values = Array.from(row.querySelectorAll('[data-testid="wcl-statistics-value"]')).map(text);
                        const name = text(row.querySelector('[data-testid="wcl-statistics-category"]'));
                        return {
                            name,
                            home: values[0] || "",
                            away: values[1] || "",
                            sourceText: text(row)
                        };
                    }).filter(item => item.name && (item.home || item.away));

                    if (statisticsItems.length > 0)
                        groups.push({ groupName, statisticsItems });
                });

                return JSON.stringify(groups);
            }
            """);

        List<FlashscoreStatisticsGroup> groups = JsonSerializer.Deserialize<List<FlashscoreStatisticsGroup>>(json, JsonOptions) ?? [];
        return groups
            .Select(group => new FlashscoreStatisticsGroup
            {
                GroupName = string.IsNullOrWhiteSpace(group.GroupName) ? "Statistics" : group.GroupName,
                StatisticsItems = group.StatisticsItems
                    .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                    .Select(ToStatisticsItem)
                    .ToList()
            })
            .Where(group => group.StatisticsItems.Count > 0)
            .ToList();
    }

    private static async Task<IReadOnlyList<FlashscoreRawOddsRow>> ExtractRawOddsRowsAsync(
        IPage page,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string json = await page.EvaluateAsync<string>(
            """
            () => {
                const text = (node) => (node?.textContent || "").replace(/\s+/g, " ").trim();
                const isVisible = (node) => {
                    if (!node) return false;
                    const style = window.getComputedStyle(node);
                    const rect = node.getBoundingClientRect();
                    return style.visibility !== "hidden" && style.display !== "none" && rect.width > 0 && rect.height > 0;
                };
                const decimalPattern = /[+-]?\d+[.,]\d{1,3}/g;
                const exactDecimalPattern = /^[+-]?\d+[.,]\d{1,3}$/;
                const oddsValues = (value) => (value.match(decimalPattern) || [])
                    .map(x => x.trim())
                    .filter(Boolean);
                const nodeOddsValues = (node) => Array.from(node.querySelectorAll("*"))
                    .filter(element => {
                        const value = text(element);
                        if (!exactDecimalPattern.test(value)) return false;
                        return !Array.from(element.children).some(child => exactDecimalPattern.test(text(child)));
                    })
                    .map(text);
                const numericLeaves = Array.from(document.querySelectorAll("body *"))
                    .filter(isVisible)
                    .filter(element => exactDecimalPattern.test(text(element)))
                    .filter(element => !Array.from(element.children).some(child => exactDecimalPattern.test(text(child))))
                    .map(element => {
                        const rect = element.getBoundingClientRect();
                        return {
                            element,
                            value: text(element),
                            x: rect.left + rect.width / 2,
                            y: rect.top + rect.height / 2
                        };
                    });

                const visualGroups = [];
                numericLeaves.sort((a, b) => a.y - b.y || a.x - b.x).forEach(item => {
                    let group = visualGroups.find(candidate => Math.abs(candidate.y - item.y) <= 10);
                    if (!group) {
                        group = { y: item.y, items: [] };
                        visualGroups.push(group);
                    }
                    group.items.push(item);
                    group.y = group.items.reduce((sum, value) => sum + value.y, 0) / group.items.length;
                });

                const bookmakerImages = Array.from(document.querySelectorAll("img[alt], img[title]"))
                    .filter(isVisible)
                    .map(element => {
                        const rect = element.getBoundingClientRect();
                        return {
                            name: (element.getAttribute("alt") || element.getAttribute("title") || "").trim(),
                            x: rect.left + rect.width / 2,
                            y: rect.top + rect.height / 2
                        };
                    })
                    .filter(item => item.name);

                const visualRows = visualGroups
                    .map(group => ({ ...group, items: group.items.sort((a, b) => a.x - b.x) }))
                    .filter(group => group.items.length === 3)
                    .map(group => {
                        const firstX = group.items[0].x;
                        const bookmaker = bookmakerImages
                            .filter(image => image.x < firstX && Math.abs(image.y - group.y) <= 35)
                            .sort((a, b) => Math.abs(a.y - group.y) - Math.abs(b.y - group.y))[0]?.name || "";
                        const values = group.items.map(item => item.value);
                        return {
                            bookmaker,
                            rawText: values.join(" "),
                            values,
                            columns: values
                        };
                    })
                    .filter(row => {
                        const line = Number(row.values[0].replace(',', '.'));
                        return line === 2.5 || line === 3.5;
                    });

                if (visualRows.length > 0)
                    return JSON.stringify(visualRows);

                const candidateRows = Array.from(document.querySelectorAll([
                    ".ui-table__row",
                    "[class*='ui-table__row']",
                    "[class*='oddsRow']",
                    "[class*='oddsCell']",
                    "[class*='oddsCell__bookmakerPart']",
                    "[data-testid*='odds']"
                ].join(",")));
                const seen = new Set();
                const rows = [];

                const pushRow = (node) => {
                    const row = node.closest(".ui-table__row, [class*='ui-table__row'], [class*='oddsRow']") || node;
                    if (seen.has(row) || !isVisible(row)) return;
                    seen.add(row);

                    const rawText = text(row);
                    const childValues = nodeOddsValues(row);
                    const values = childValues.length >= 2 ? childValues : oddsValues(rawText);
                    if (values.length < 2 || rawText.length > 500) return;

                    const bookmakerNode =
                        row.querySelector("img[alt], img[title], [class*='bookmaker'] img, [class*='bookmaker'], a[href*='bookmaker']");
                    const bookmaker = (bookmakerNode?.getAttribute?.("alt") ||
                        bookmakerNode?.getAttribute?.("title") ||
                        text(bookmakerNode) ||
                        "").trim();

                    const columns = Array.from(row.children)
                        .map(text)
                        .filter(Boolean);

                    rows.push({ bookmaker, rawText, values, columns });
                };

                candidateRows.forEach(pushRow);

                if (rows.length === 0) {
                    Array.from(document.querySelectorAll("div, a, button, span"))
                        .filter(isVisible)
                        .forEach(node => {
                            const rawText = text(node);
                            if (rawText.length < 8 || rawText.length > 500 || oddsValues(rawText).length < 2)
                                return;

                            const childHasSameShape = Array.from(node.children)
                                .some(child => oddsValues(text(child)).length >= 2);
                            if (!childHasSameShape)
                                pushRow(node);
                        });
                }

                return JSON.stringify(rows);
            }
            """);

        return JsonSerializer.Deserialize<List<FlashscoreRawOddsRow>>(json, JsonOptions) ?? [];
    }

    private static async Task<bool> TrySelectOddsMarketAsync(IPage page, string marketName)
    {
        return await page.EvaluateAsync<bool>(
            """
            (marketName) => {
                const normalize = (value) => (value || "").replace(/\s+/g, " ").trim().toUpperCase();
                const isVisible = (node) => {
                    if (!node) return false;
                    const style = window.getComputedStyle(node);
                    const rect = node.getBoundingClientRect();
                    return style.visibility !== "hidden" && style.display !== "none" && rect.width > 0 && rect.height > 0;
                };
                const wanted = normalize(marketName);
                const candidates = Array.from(document.querySelectorAll(
                    "button, a, [role='tab'], [data-testid='wcl-tab']"
                ));
                const target = candidates.find(el => normalize(el.textContent) === wanted && isVisible(el));
                if (!target) return false;
                target.scrollIntoView({ block: "center", inline: "center" });
                target.click();
                return true;
            }
            """,
            marketName);
    }

    private static async Task ClickOddsShowMoreUntilDoneAsync(
        IPage page,
        FlashscoreDownloadOptions options,
        CancellationToken cancellationToken)
    {
        int maxClicks = Math.Min(options.MaxShowMoreClicks, 8);
        for (int i = 0; i < maxClicks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool clicked = await page.EvaluateAsync<bool>(
                """
                () => {
                    const isVisible = (node) => {
                        const style = window.getComputedStyle(node);
                        const rect = node.getBoundingClientRect();
                        return style.visibility !== "hidden" && style.display !== "none" && rect.width > 0 && rect.height > 0;
                    };
                    const candidates = Array.from(document.querySelectorAll("button, a"));
                    const target = candidates.find(el => {
                        const text = (el.textContent || "").replace(/\s+/g, " ").trim();
                        return text === "Show more" && isVisible(el);
                    });
                    if (!target) return false;
                    target.scrollIntoView({ block: "center", inline: "center" });
                    target.click();
                    return true;
                }
                """);

            if (!clicked)
                return;

            if (options.ShowMoreWaitMs > 0)
                await Task.Delay(options.ShowMoreWaitMs, cancellationToken);
        }
    }

    private static List<FlashscoreOddsRow> NormalizeOddsRows(string marketName, IReadOnlyList<FlashscoreRawOddsRow> rawRows)
    {
        if (!marketName.Contains("OVER/UNDER", StringComparison.OrdinalIgnoreCase))
            return [];

        var rows = new List<FlashscoreOddsRow>();
        foreach (FlashscoreRawOddsRow rawRow in rawRows)
        {
            List<(string Raw, double Value)> tokens = rawRow.Values
                .Select(raw => (Raw: raw, Value: ParseFlashscoreNumber(raw)))
                .Where(x => x.Value.HasValue)
                .Select(x => (x.Raw, x.Value!.Value))
                .ToList();
            List<double> values = tokens.Select(x => x.Value).ToList();

            if (values.Count == 0)
                continue;

            int? lineIndex = FindLineIndex(values, allowNegative: false);
            double? line = lineIndex.HasValue ? values[lineIndex.Value] : null;
            if (!IsWantedTotalLine(line))
                continue;

            string? lineRaw = lineIndex.HasValue ? tokens[lineIndex.Value].Raw : null;
            List<double> odds = CompactConsecutive(tokens
                .Where((x, index) => IsOddsToken(x.Raw, x.Value, index, lineIndex, lineRaw))
                .Select(x => x.Value))
                .Take(2)
                .ToList();
            AddSelectionRows(rows, rawRow, marketName, line, ["Over", "Under"], odds);
        }

        return rows
            .GroupBy(row => new
            {
                row.Market,
                Bookmaker = NormalizeBookmaker(row.Bookmaker),
                row.Selection,
                Line = NormalizeNullableDouble(row.Line),
                Odds = NormalizeDouble(row.Odds)
            })
            .Select(group => group.First())
            .OrderBy(row => row.Market, StringComparer.OrdinalIgnoreCase)
            .ThenBy(row => row.Line ?? double.MinValue)
            .ThenBy(row => SelectionOrder(row.Selection))
            .ThenBy(row => row.Odds)
            .ToList();
    }

    private static bool IsWantedTotalLine(double? line)
        => line.HasValue && (Math.Abs(line.Value - 2.5) < 0.0001
            || Math.Abs(line.Value - 3.5) < 0.0001);

    private static bool IsOddsToken(string raw, double value, int index, int? lineIndex, string? lineRaw)
    {
        if (value <= 1.0)
            return false;

        if (lineIndex.HasValue && index == lineIndex.Value)
            return false;

        return string.IsNullOrWhiteSpace(lineRaw) || !raw.Equals(lineRaw, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<double> CompactConsecutive(IEnumerable<double> values)
    {
        double? previous = null;
        foreach (double value in values)
        {
            if (previous.HasValue && Math.Abs(previous.Value - value) < 1e-9)
                continue;

            previous = value;
            yield return value;
        }
    }

    private static IEnumerable<string> CompactConsecutive(IEnumerable<string> values)
    {
        string? previous = null;
        foreach (string value in values)
        {
            if (previous is not null && previous.Equals(value, StringComparison.OrdinalIgnoreCase))
                continue;

            previous = value;
            yield return value;
        }
    }

    private static void AddSelectionRows(
        ICollection<FlashscoreOddsRow> target,
        FlashscoreRawOddsRow rawRow,
        string marketName,
        double? line,
        IReadOnlyList<string> selections,
        IReadOnlyList<double> odds)
    {
        for (int i = 0; i < odds.Count; i++)
        {
            string selection = i < selections.Count
                ? selections[i]
                : $"Selection {i + 1}";

            target.Add(new FlashscoreOddsRow
            {
                Market = marketName,
                Bookmaker = NormalizeBookmaker(rawRow.Bookmaker),
                Selection = selection,
                Line = line,
                Odds = odds[i]
            });
        }
    }

    private static string? NormalizeBookmaker(string? bookmaker)
    {
        if (string.IsNullOrWhiteSpace(bookmaker))
            return null;

        string normalized = bookmaker.Trim();
        return normalized.Equals("Bookmaker", StringComparison.OrdinalIgnoreCase) ? null : normalized;
    }

    private static double? NormalizeNullableDouble(double? value)
        => value.HasValue ? NormalizeDouble(value.Value) : null;

    private static double NormalizeDouble(double value)
        => Math.Round(value, 4, MidpointRounding.AwayFromZero);

    private static int SelectionOrder(string selection)
        => selection.ToUpperInvariant() switch
        {
            "1" => 0,
            "X" => 1,
            "2" => 2,
            "OVER" => 3,
            "YES" => 3,
            "HOME" => 3,
            "UNDER" => 4,
            "NO" => 4,
            "AWAY" => 4,
            _ => 9
        };

    private static int? FindLineIndex(IReadOnlyList<double> values, bool allowNegative)
    {
        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            double absolute = Math.Abs(value);
            bool signAllowed = allowNegative || value >= 0.0;
            if (signAllowed && absolute > 0.0 && absolute <= 20.0 && value <= 1.0)
                return i;
        }

        for (int i = 0; i < values.Count; i++)
        {
            double value = values[i];
            double absolute = Math.Abs(value);
            bool signAllowed = allowNegative || value >= 0.0;
            if (signAllowed && absolute > 0.0 && absolute <= 20.0 && Math.Abs(value % 0.25) < 1e-9)
                return i;
        }

        return null;
    }

    private static FlashscoreStatisticsItem ToStatisticsItem(FlashscoreStatisticsItem item)
    {
        double? homeValue = ParseFlashscoreNumber(item.Home);
        double? awayValue = ParseFlashscoreNumber(item.Away);

        return new FlashscoreStatisticsItem
        {
            Key = BuildStatKey(item.Name),
            Name = item.Name,
            Home = item.Home,
            Away = item.Away,
            HomeValue = homeValue,
            AwayValue = awayValue,
            HomeTotal = homeValue,
            AwayTotal = awayValue,
            ValueType = DetermineValueType(item.Home, item.Away),
            StatisticsType = "positive",
            SourceText = item.SourceText
        };
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
        string iconType = row.IconType.Trim();
        bool titleIsEmpty = string.IsNullOrWhiteSpace(normalizedTitle);
        bool titleMentionsCard = normalizedTitle.Contains("card", StringComparison.OrdinalIgnoreCase);
        bool trustVisualCardType = titleIsEmpty || titleMentionsCard;
        string incidentType;
        string incidentClass;

        ParseScore(row.Score, out int? homeScore, out int? awayScore);
        bool hasScoreSnapshot = homeScore.HasValue && awayScore.HasValue;

        if (normalizedTitle.Contains("goal disallowed", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("disallowed goal", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("goal cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bool looksLikeGoal = normalizedTitle.Contains("goal", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("penalty scored", StringComparison.OrdinalIgnoreCase) ||
            normalizedTitle.Contains("own goal", StringComparison.OrdinalIgnoreCase);

        if (looksLikeGoal || hasScoreSnapshot)
        {
            // Flashscore occasionally renders non-scoring/duplicated goal-like rows without a score snapshot.
            // Those rows break reconstructed timelines by adding a fake fallback goal. Real scoring rows
            // normally have a visible score; if no score could be extracted even from row text, skip it.
            if (looksLikeGoal && !hasScoreSnapshot)
                return false;

            incidentType = "goal";
            incidentClass = normalizedTitle.Contains("penalty", StringComparison.OrdinalIgnoreCase)
                ? "penalty"
                : normalizedTitle.Contains("own", StringComparison.OrdinalIgnoreCase)
                    ? "ownGoal"
                    : hasScoreSnapshot && !normalizedTitle.Contains("goal", StringComparison.OrdinalIgnoreCase)
                        ? "scoreSnapshot"
                        : "regular";
        }
        else if ((normalizedTitle.Contains("yellow", StringComparison.OrdinalIgnoreCase) &&
                  normalizedTitle.Contains("red", StringComparison.OrdinalIgnoreCase) &&
                  titleMentionsCard) ||
                 (trustVisualCardType && iconType.Equals("yellowRed", StringComparison.OrdinalIgnoreCase)) ||
                 normalizedTitle.Contains("second yellow", StringComparison.OrdinalIgnoreCase) ||
                 normalizedTitle.Contains("2nd yellow", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "card";
            incidentClass = "yellowRed";
        }
        else if ((trustVisualCardType && iconType.Equals("red", StringComparison.OrdinalIgnoreCase)) ||
                 normalizedTitle.Contains("red card", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "card";
            incidentClass = "red";
        }
        else if ((trustVisualCardType && iconType.Equals("yellow", StringComparison.OrdinalIgnoreCase)) ||
                 normalizedTitle.Contains("yellow card", StringComparison.OrdinalIgnoreCase))
        {
            incidentType = "card";
            incidentClass = "yellow";
        }
        else
        {
            return false;
        }

        long incidentId = StablePositiveId(
            $"flashscore:incident:{calendarEvent.FlashscoreId}:{row.TimeText}:{title}:{iconType}:{row.IsHome}:{row.PlayerName}:{row.Score}");

        string reason = !string.IsNullOrWhiteSpace(title)
            ? title
            : incidentClass switch
            {
                "yellowRed" => "Second Yellow Card",
                "red" => "Red Card",
                "yellow" => "Yellow Card",
                _ => string.Empty
            };

        incident = new FlashscoreIncident
        {
            Id = incidentId,
            IncidentType = incidentType,
            IncidentClass = incidentClass,
            Time = minute,
            AddedTime = addedTime,
            // Keep first-half stoppage time before the second half in chronological sorting.
            // 45+6 must sort before 46, not at the same position as 51.
            TimeSeconds = (minute * 60) + (addedTime ?? 0),
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
            Reason = reason
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

    private static async Task TrySetStartTimestampFromDetailPageAsync(
        IPage page,
        FlashscoreCalendarEvent calendarEvent,
        int defaultYear,
        TextWriter log)
    {
        if (calendarEvent.StartTimestamp.HasValue)
            return;

        string dateTimeText = await page.EvaluateAsync<string>(
            """
            () => {
                const text = (node) => (node?.textContent || '').replace(/\s+/g, ' ').trim();
                const candidates = [
                    text(document.querySelector('.duelParticipant__startTime')),
                    document.querySelector('.duelParticipant__startTime [title]')?.getAttribute('title') || '',
                    document.querySelector('meta[name="description"]')?.getAttribute('content') || '',
                    document.title || ''
                ];

                const dateTime = /\b\d{1,2}[./-]\d{1,2}[./-]\d{2,4}\s+\d{1,2}:\d{2}\b/;
                return candidates.find(value => dateTime.test(value)) || '';
            }
            """);

        long? startTimestamp = ParseStartTimestamp(dateTimeText, defaultYear);
        if (!startTimestamp.HasValue)
            return;

        calendarEvent.StartTimestamp = startTimestamp;
        await log.WriteLineAsync($"    kickoff: {dateTimeText} ({DateTimeOffset.FromUnixTimeSeconds(startTimestamp.Value):O})");
    }

    private static string BuildMatchDetailUrl(string sourceUrl, string segment)
    {
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out Uri? uri))
            return sourceUrl;

        string path = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        path = Regex.Replace(
            path,
            @"/(?:summary(?:/(?:stats(?:/(?:overall|1st-half|2nd-half))?|lineups|player-stats))?|odds|h2h|standings)/?$",
            string.Empty,
            RegexOptions.IgnoreCase);

        string normalizedSegment = segment.Trim('/');
        return $"{path}/{normalizedSegment}/{uri.Query}";
    }

    private static string BuildStatKey(string name)
    {
        string compact = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", string.Empty);
        if (StatKeyAliases.TryGetValue(compact, out string? alias))
            return alias;

        string normalized = Regex.Replace(name.ToLowerInvariant(), @"[^a-z0-9]+", "_").Trim('_');
        return string.IsNullOrWhiteSpace(normalized) ? "stat" : normalized;
    }

    private static string DetermineValueType(params string[] values)
    {
        if (values.Any(value => value.Contains('%', StringComparison.Ordinal)))
            return "percentage";

        if (values.Any(value => value.Contains('.', StringComparison.Ordinal) || value.Contains(',', StringComparison.Ordinal)))
            return "decimal";

        return "integer";
    }

    private static double? ParseFlashscoreNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        Match match = Regex.Match(value.Replace(",", ".", StringComparison.Ordinal), @"[-+]?\d+(?:\.\d+)?");
        if (!match.Success)
            return null;

        return double.TryParse(match.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed
            : null;
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
        string normalized = Regex.Replace((value ?? string.Empty).Replace('\u00a0', ' '), @"\s+", " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        Match match = Regex.Match(
            normalized,
            @"(?<day>\d{1,2})\.(?<month>\d{1,2})\.?(?:(?<year>\d{2,4})\.?)?\s+(?<hour>\d{1,2}):(?<minute>\d{2})",
            RegexOptions.CultureInvariant);

        if (match.Success)
        {
            int parsedYear = year;
            string yearText = match.Groups["year"].Value;
            if (!string.IsNullOrWhiteSpace(yearText) && int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int yearValue))
                parsedYear = yearText.Length == 2 ? 2000 + yearValue : yearValue;

            if (int.TryParse(match.Groups["day"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) &&
                int.TryParse(match.Groups["month"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int month) &&
                int.TryParse(match.Groups["hour"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hour) &&
                int.TryParse(match.Groups["minute"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minute))
            {
                try
                {
                    var local = new DateTime(parsedYear, month, day, hour, minute, 0, DateTimeKind.Local);
                    return new DateTimeOffset(local).ToUnixTimeSeconds();
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;
                }
            }
        }

        string[] formats =
        [
            "dd.MM. HH:mm",
            "d.MM. HH:mm",
            "dd.MM.yyyy HH:mm",
            "d.MM.yyyy HH:mm",
            "dd.MM.yyyy. HH:mm",
            "d.MM.yyyy. HH:mm",
            "dd.MM.yy HH:mm",
            "d.MM.yy HH:mm",
            "dd.MM.yy. HH:mm",
            "d.MM.yy. HH:mm"
        ];

        foreach (string format in formats)
        {
            if (DateTime.TryParseExact(normalized, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
            {
                if (!format.Contains('y', StringComparison.OrdinalIgnoreCase))
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
    public string IconType { get; init; } = string.Empty;
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

public sealed class FlashscoreStatisticsPeriod
{
    public string Period { get; init; } = string.Empty;
    public List<FlashscoreStatisticsGroup> Groups { get; init; } = [];
}

public sealed class FlashscoreStatisticsGroup
{
    public string GroupName { get; init; } = string.Empty;
    public List<FlashscoreStatisticsItem> StatisticsItems { get; init; } = [];
}

public sealed class FlashscoreStatisticsItem
{
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Home { get; init; } = string.Empty;
    public string Away { get; init; } = string.Empty;
    public double? HomeValue { get; init; }
    public double? AwayValue { get; init; }
    public double? HomeTotal { get; init; }
    public double? AwayTotal { get; init; }
    public string ValueType { get; init; } = string.Empty;
    public string StatisticsType { get; init; } = string.Empty;
    public string SourceText { get; init; } = string.Empty;
}

public sealed class FlashscoreOddsSnapshot
{
    public string SourceUrl { get; init; } = string.Empty;
    public DateTime DownloadedAtUtc { get; init; }
    public List<FlashscoreOddsMarket> Markets { get; init; } = [];
}

public sealed class FlashscoreOddsMarket
{
    public string Name { get; init; } = string.Empty;
    public string SourceUrl { get; init; } = string.Empty;
    public List<FlashscoreOddsRow> Rows { get; init; } = [];
}

public sealed class FlashscoreOddsRow
{
    public string Market { get; init; } = string.Empty;
    public string? Bookmaker { get; init; }
    public string Selection { get; init; } = string.Empty;
    public double? Line { get; init; }
    public double Odds { get; init; }
}

public sealed class FlashscoreRawOddsRow
{
    public string Bookmaker { get; init; } = string.Empty;
    public string RawText { get; init; } = string.Empty;
    public List<string> Values { get; init; } = [];
    public List<string> Columns { get; init; } = [];
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
    public long? StartTimestamp { get; set; }
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
