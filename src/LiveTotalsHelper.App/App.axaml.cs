using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveTotalsHelper.App.ViewModels;
using LiveTotalsHelper.App.Views;
using LiveTotalsHelper.Infrastructure;
using LiveTotalsHelper.Infrastructure.Persistence;
using LiveTotalsHelper.Modeling;
using LiveTotalsHelper.Tools;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LiveTotalsHelper.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try
            {
                AppStartupSettings settings = LoadStartupSettings();

                var dbOptions = new DbContextOptionsBuilder<LiveTotalsDbContext>()
                    .UseNpgsql(settings.LiveTotalsDbConnectionString)
                    .Options;
                var dbContext = new LiveTotalsDbContext(dbOptions);

                LeagueProfileStore profileStore = LeagueProfileStore.LoadAsync(settings.ProfilesFile, CancellationToken.None).GetAwaiter().GetResult();
                string logsFolder = settings.LogsFolder;

                var matchRepository = new LiveTotalsHelper.Infrastructure.DbMatchRepository(dbContext);
                var weibullProvider = new SampleWeibullParameterProvider();
                var bettingModel = new BettingModelService(weibullProvider);
                var liveSessionService = new LiveTotalsHelper.App.Services.LiveBettingSessionService(dbContext, profileStore.Profiles, logsFolder);

                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(matchRepository, bettingModel, liveSessionService)
                };
            }
            catch (Exception ex)
            {
                desktop.MainWindow = new Window
                {
                    Title = "LiveTotalsHelper startup error",
                    Width = 900,
                    Height = 520,
                    Content = new TextBox
                    {
                        Text = ex.ToString(),
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static AppStartupSettings LoadStartupSettings()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"App settings file was not found: {path}", path);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;

        string connectionString = GetNestedString(root, "ConnectionStrings", "LiveTotalsDb");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings:LiveTotalsDb is missing in appsettings.json.");

        string profilesFile = GetNestedString(root, "LiveBetting", "ProfilesFile");
        if (string.IsNullOrWhiteSpace(profilesFile))
            profilesFile = "league-profiles.json";

        string logsFolder = GetNestedString(root, "LiveBetting", "LogsFolder");

        return new AppStartupSettings(connectionString, profilesFile, logsFolder);
    }

    private static string GetNestedString(JsonElement root, string section, string key)
    {
        if (!root.TryGetProperty(section, out JsonElement sectionElement))
            return string.Empty;

        if (!sectionElement.TryGetProperty(key, out JsonElement valueElement))
            return string.Empty;

        return valueElement.ValueKind == JsonValueKind.String
            ? valueElement.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record AppStartupSettings(
        string LiveTotalsDbConnectionString,
        string ProfilesFile,
        string LogsFolder);

}
