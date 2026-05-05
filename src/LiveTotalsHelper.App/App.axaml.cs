using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using LiveTotalsHelper.App.ViewModels;
using LiveTotalsHelper.App.Views;
using LiveTotalsHelper.Infrastructure;
using LiveTotalsHelper.Modeling;

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
            var matchRepository = new SampleMatchRepository();
            var weibullProvider = new SampleWeibullParameterProvider();
            var bettingModel = new BettingModelService(weibullProvider);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(matchRepository, bettingModel)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
