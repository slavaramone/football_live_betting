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

            context = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = options.UserAgent,
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["accept"] = "application/json,text/plain,*/*",
                    ["accept-language"] = "en-US,en;q=0.9",
                    ["referer"] = "https://www.sofascore.com/"
                }
            });

            page = await context.NewPageAsync();

            // Important: visit the main site first so the browser context receives the cookies/session
            // SofaScore expects. API requests made after this use the same browser context.
            await log.WriteLineAsync($"Opening warmup page: {options.WarmupUrl}");
            await page.GotoAsync(options.WarmupUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60_000
            });

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

    public Task<string> GetCalendarAsync(int tournamentId, int seasonId, int round, CancellationToken cancellationToken)
    {
        string url = $"{BaseUrl}/api/v1/unique-tournament/{tournamentId}/season/{seasonId}/events/round/{round}";
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
                // Keep this call intentionally simple. Some Microsoft.Playwright versions
                // expose different generated option type names for GetAsync options,
                // which caused compile errors. The browser context already has the
                // required SofaScore cookies/user-agent/headers from the warmup page.
                IAPIResponse response = await _page.APIRequest.GetAsync(url);

                string content = await response.TextAsync();

                if (response.Ok)
                    return content;

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
