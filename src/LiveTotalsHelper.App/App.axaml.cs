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

                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(matchRepository, bettingModel, liveSessionService)
                };
                desktop.MainWindow.Show();
            }
            catch (Exception ex)
            {
                string errorText = ex.ToString();
                TryWriteStartupError(errorText);

                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new Window
                {
                    Title = "LiveTotalsHelper startup error",
                    Width = 980,
                    Height = 620,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Content = new TextBox
                    {
                        Text = errorText,
                        IsReadOnly = true,
                        AcceptsReturn = true,
                        TextWrapping = Avalonia.Media.TextWrapping.Wrap
                    }
                };
                desktop.MainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }


    private static void TryWriteStartupError(string errorText)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveTotalsHelper");

            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "startup-error.txt"), errorText);
        }
        catch
        {
            // Startup error window is the primary diagnostic path.
        }
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
