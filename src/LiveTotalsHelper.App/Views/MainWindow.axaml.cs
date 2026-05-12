using System.Globalization;
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

    private void LoadFixtures_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.LoadFixtures();
    }

    private async void BuildCheck_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            SyncVisibleInputs(vm);
            await vm.BuildLiveCheckAsync();
        }
    }

    private async void AppendPaperLog_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            SyncVisibleInputs(vm);
            await vm.AppendPaperLogAsync();
        }
    }

    private async void LogBet_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            SyncVisibleInputs(vm);
            await vm.LogBetAsync();
        }
    }

    private void SyncVisibleInputs(MainWindowViewModel vm)
    {
        vm.LiveInput.StateTrigger = StateTriggerBox.SelectedItem?.ToString() ?? vm.LiveInput.StateTrigger;

        vm.LiveInput.Minute = ReadInt(MinuteBox, vm.LiveInput.Minute);
        vm.LiveInput.HomeGoals = ReadInt(HomeGoalsBox, vm.LiveInput.HomeGoals);
        vm.LiveInput.AwayGoals = ReadInt(AwayGoalsBox, vm.LiveInput.AwayGoals);
        vm.LiveInput.HomeRedCards = ReadInt(HomeRedCardsBox, vm.LiveInput.HomeRedCards);
        vm.LiveInput.AwayRedCards = ReadInt(AwayRedCardsBox, vm.LiveInput.AwayRedCards);
        vm.LiveInput.LastGoalMinute = ReadInt(LastGoalMinuteBox, vm.LiveInput.LastGoalMinute);
        vm.LiveInput.BeforeRound = ReadNullableInt(BeforeRoundBox, vm.LiveInput.BeforeRound);

        vm.LiveInput.StartingLine = ReadDouble(StartingLineBox, vm.LiveInput.StartingLine);
        vm.LiveInput.StartingOverOdds = ReadDouble(StartingOverOddsBox, vm.LiveInput.StartingOverOdds);
        vm.LiveInput.StartingUnderOdds = ReadDouble(StartingUnderOddsBox, vm.LiveInput.StartingUnderOdds);
        vm.LiveInput.TargetLinesText = TargetLinesBox.Text ?? string.Empty;
        vm.LiveInput.LiveOverOddsText = LiveOverOddsBox.Text ?? string.Empty;
        vm.LiveInput.LiveUnderOddsText = LiveUnderOddsBox.Text ?? string.Empty;

        vm.LiveInput.SelectedBetLineText = SelectedBetLineBox.Text ?? vm.LiveInput.SelectedBetLineText;
        vm.LiveInput.SelectedBetSide = SelectedBetSideBox.SelectedItem?.ToString() ?? vm.LiveInput.SelectedBetSide;
        vm.LiveInput.SelectedBetOdds = ReadDouble(SelectedBetOddsBox, vm.LiveInput.SelectedBetOdds);
        vm.LiveInput.Stake = ReadDouble(StakeBox, vm.LiveInput.Stake);
        vm.LiveInput.BetMode = BetModeBox.SelectedItem?.ToString() ?? vm.LiveInput.BetMode;
        vm.LiveInput.BetNotes = BetNotesBox.Text ?? string.Empty;
    }

    private static int ReadInt(TextBox box, int fallback)
    {
        return int.TryParse(Normalize(box.Text), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private static int? ReadNullableInt(TextBox box, int? fallback)
    {
        string text = Normalize(box.Text);
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : fallback;
    }

    private static double ReadDouble(TextBox box, double fallback)
    {
        return double.TryParse(Normalize(box.Text), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;
    }

    private static string Normalize(string? text)
    {
        return (text ?? string.Empty).Trim().Replace(',', '.');
    }
}
