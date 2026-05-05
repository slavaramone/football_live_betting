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

    private void Recalculate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.Recalculate();
    }
}
