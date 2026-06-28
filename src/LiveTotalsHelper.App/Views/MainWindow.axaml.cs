using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveTotalsHelper.App.ViewModels;

namespace LiveTotalsHelper.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateTriggerBox.SelectionChanged += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.RefreshMinuteOptions();
        };
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
                vm.SaveState();
        };
    }

    private void LoadFixtures_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.CaptureCurrentState();
            vm.LoadFixtures();
        }
    }

    private async void BuildCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.BuildLiveCheckAsync();
    }

    private async void AppendPaperLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.AppendPaperLogAsync();
    }

    private async void LogBet_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            await vm.LogBetAsync();
    }
}
