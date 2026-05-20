using Avalonia;
using System;
using System.IO;

namespace LiveTotalsHelper.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppStartupTrace.Write("Program.Main entered");
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            AppStartupTrace.Write("StartWithClassicDesktopLifetime returned");
        }
        catch (Exception ex)
        {
            AppStartupTrace.Write("FATAL before/inside Avalonia lifetime: " + ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        AppStartupTrace.Write("BuildAvaloniaApp entered");
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}

internal static class AppStartupTrace
{
    public static void Write(string message)
    {
        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LiveTotalsHelper");

            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "app-startup-trace.txt"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Last-resort diagnostic only.
        }
    }
}
