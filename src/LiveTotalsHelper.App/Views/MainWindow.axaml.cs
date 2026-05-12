using Avalonia.Controls;
using Avalonia.Interactivity;
using LiveTotalsHelper.App.ViewModels;

namespace LiveTotalsHelper.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
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
