using Microsoft.Playwright;

namespace LiveTotalsHelper.Infrastructure.SofaScore;

public sealed class SofaScoreClient : IAsyncDisposable
{
    private const string BaseUrl = "https://www.sofascore.com";

    private readonly IPlaywright _playwright;
    private readonly IBrowser _browser;
    private readonly IBrowserContext _context;
    private readonly IPage _page;

    private SofaScoreClient(
        IPlaywright playwright,
        IBrowser browser,
        IBrowserContext context,
        IPage page)
    {
        _playwright = playwright;
        _browser = browser;
        _context = context;
        _page = page;
    }

    public static async Task<SofaScoreClient> CreateAsync(
        SofaScoreDownloadOptions options,
        TextWriter log,
        CancellationToken cancellationToken)
    {
        await log.WriteLineAsync("Starting Playwright Chromium session for SofaScore...");

        IPlaywright playwright = await Playwright.CreateAsync();
        IBrowser? browser = null;
        IBrowserContext? context = null;
        IPage? page = null;

        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless
            });

            // Keep this intentionally close to the working SofaScoreGrabber.cs pattern.
            // Adding custom request headers here can trigger 403 on SofaScore, while a plain
            // Chromium context with a normal user-agent works more reliably.
            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = options.UserAgent
            });

            page = await context.NewPageAsync();

            page.SetDefaultTimeout(120000);

            // Important: go to the main site first. This allows SofaScore to initialise the
            // browser session/cookies before we call API endpoints from the same browser page.
            await log.WriteLineAsync($"Opening warmup page: {options.WarmupUrl}");
            await page.GotoAsync(options.WarmupUrl);

            if (options.WarmupDelayMs > 0)
                await Task.Delay(options.WarmupDelayMs, cancellationToken);

            await log.WriteLineAsync("SofaScore browser context is ready.");
            return new SofaScoreClient(playwright, browser, context, page);
        }
        catch
        {
            if (page is not null)
                await page.CloseAsync();
            if (context is not null)
                await context.CloseAsync();
            if (browser is not null)
                await browser.CloseAsync();
            playwright.Dispose();
            throw;
        }
    }

    public Task<string> GetCalendarAsync(SofaScoreDownloadOptions options, int round, CancellationToken cancellationToken)
    {
        string mode = string.IsNullOrWhiteSpace(options.CalendarMode) ? "round" : options.CalendarMode.Trim().ToLowerInvariant();
        string url = $"{BaseUrl}/api/v1/unique-tournament/{options.TournamentId}/season/{options.SeasonId}/events/{mode}/{round}";
        return GetStringWithRetryAsync(url, cancellationToken);
    }

    public Task<string> GetIncidentsAsync(long eventId, CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/api/v1/event/{eventId}/incidents";
        return GetStringWithRetryAsync(url, cancellationToken);
    }

    public Task<string> GetStatisticsAsync(long eventId, CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/api/v1/event/{eventId}/statistics";
        return GetStringWithRetryAsync(url, cancellationToken);
    }

    private async Task<string> GetStringWithRetryAsync(string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                IAPIResponse response = await _page.APIRequest.GetAsync(url);
                string content = await response.TextAsync();

                if (response.Ok)
                    return content;

                // Some SofaScore pages are stricter for APIRequest than for real in-page fetch.
                // Fall back to fetch() executed inside the warmed-up SofaScore page context.
                if (response.Status == 403)
                    return await FetchFromPageContextAsync(url);

                throw new HttpRequestException($"GET {url} failed with {response.Status} {response.StatusText}. Body: {Truncate(content, 500)}");
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), cancellationToken);
            }
        }

        throw lastException ?? new HttpRequestException($"GET {url} failed.");
    }

    private async Task<string> FetchFromPageContextAsync(string url)
    {
        const string script = @"
            async (url) => {
                const response = await fetch(url, {
                    method: 'GET',
                    credentials: 'include',
                    headers: {
                        'accept': 'application/json, text/plain, */*'
                    }
                });
                const text = await response.text();
                if (!response.ok) {
                    throw new Error(`GET ${url} failed with ${response.status} ${response.statusText}. Body: ${text.substring(0, 500)}`);
                }
                return text;
            }";

        return await _page.EvaluateAsync<string>(script, url);
    }

    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...";

    public async ValueTask DisposeAsync()
    {
        await _page.CloseAsync();
        await _context.CloseAsync();
        await _browser.CloseAsync();
        _playwright.Dispose();
    }
}
