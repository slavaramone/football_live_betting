using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using LiveTotalsHelper.App.ViewModels;
using LiveTotalsHelper.App.Views;
using LiveTotalsHelper.Infrastructure;
using LiveTotalsHelper.Infrastructure.Persistence;
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
        AppStartupTrace.Write("OnFrameworkInitializationCompleted entered");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppStartupTrace.Write("Classic desktop lifetime detected");
            try
            {
                AppStartupTrace.Write("Loading startup settings");
                AppStartupSettings settings = LoadStartupSettings();
                AppStartupTrace.Write("Startup settings loaded");

                var dbOptions = new DbContextOptionsBuilder<LiveTotalsDbContext>()
                    .UseNpgsql(settings.LiveTotalsDbConnectionString)
                    .Options;
                var dbContext = new LiveTotalsDbContext(dbOptions);

                AppStartupTrace.Write($"Loading profiles synchronously: {settings.ProfilesFile}");
                string resolvedProfilesFile = LeagueProfileStore.ResolvePath(settings.ProfilesFile);
                AppStartupTrace.Write($"Resolved profiles path: {resolvedProfilesFile}");
                LeagueProfileStore profileStore = LeagueProfileStore.Load(settings.ProfilesFile);
                AppStartupTrace.Write($"Profiles loaded: {profileStore.Profiles.Count}");
                string logsFolder = settings.LogsFolder;

                var matchRepository = new LiveTotalsHelper.Infrastructure.DbMatchRepository(dbContext);
                var liveSessionService = new LiveTotalsHelper.App.Services.LiveBettingSessionService(dbContext, profileStore.Profiles, logsFolder);

                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                desktop.MainWindow = new MainWindow
                {
                    DataContext = new MainWindowViewModel(matchRepository, liveSessionService)
                };
                AppStartupTrace.Write("MainWindow assigned");

                // Robust explicit display. Some debug/startup paths may assign MainWindow
                // but not visibly activate it, so post Show/Activate after the UI loop starts.
                Dispatcher.UIThread.Post(() =>
                {
                    AppStartupTrace.Write("Showing MainWindow");
                    desktop.MainWindow.Show();
                    desktop.MainWindow.Activate();
                });
            }
            catch (Exception ex)
            {
                string errorText = ex.ToString();
                TryWriteStartupError(errorText);

                AppStartupTrace.Write("Startup exception caught: " + errorText);
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

                Dispatcher.UIThread.Post(() =>
                {
                    AppStartupTrace.Write("Showing startup error window");
                    desktop.MainWindow.Show();
                    desktop.MainWindow.Activate();
                });
            }
        }

        AppStartupTrace.Write("Calling base.OnFrameworkInitializationCompleted");
        base.OnFrameworkInitializationCompleted();
        AppStartupTrace.Write("OnFrameworkInitializationCompleted finished");
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
