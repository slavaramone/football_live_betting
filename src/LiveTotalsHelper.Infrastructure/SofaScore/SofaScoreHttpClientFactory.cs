namespace LiveTotalsHelper.Infrastructure.SofaScore;

public static class SofaScoreHttpClientFactory
{
    public static HttpClient Create()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://www.sofascore.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; LiveTotalsHelper/1.0)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/plain,*/*");
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.sofascore.com/");

        return client;
    }
}
