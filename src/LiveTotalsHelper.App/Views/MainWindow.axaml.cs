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

        vm.LiveInput.Minute = ReadSelectedInt(MinuteBox, vm.LiveInput.Minute);
        vm.LiveInput.HomeGoals = ReadSelectedInt(HomeGoalsBox, vm.LiveInput.HomeGoals);
        vm.LiveInput.AwayGoals = ReadSelectedInt(AwayGoalsBox, vm.LiveInput.AwayGoals);
        vm.LiveInput.HomeRedCards = ReadSelectedInt(HomeRedCardsBox, vm.LiveInput.HomeRedCards);
        vm.LiveInput.AwayRedCards = ReadSelectedInt(AwayRedCardsBox, vm.LiveInput.AwayRedCards);
        vm.LiveInput.LastGoalMinute = ReadSelectedInt(LastGoalMinuteBox, vm.LiveInput.LastGoalMinute);
        vm.LiveInput.BeforeRound = ReadNullableInt(BeforeRoundBox, vm.LiveInput.BeforeRound);

        vm.LiveInput.StartingLine = ReadSelectedDouble(StartingLineBox, vm.LiveInput.StartingLine);
        vm.LiveInput.StartingOverOdds = ReadDouble(StartingOverOddsBox, vm.LiveInput.StartingOverOdds);
        vm.LiveInput.StartingUnderOdds = ReadDouble(StartingUnderOddsBox, vm.LiveInput.StartingUnderOdds);
        vm.LiveInput.LiveOddsLine = ReadSelectedDouble(LiveOddsLineBox, vm.LiveInput.LiveOddsLine);
        vm.LiveInput.LiveOverOdds = ReadDouble(LiveOverOddsBox, vm.LiveInput.LiveOverOdds);
        vm.LiveInput.LiveUnderOdds = ReadDouble(LiveUnderOddsBox, vm.LiveInput.LiveUnderOdds);
        string liveLine = vm.LiveInput.LiveOddsLine.ToString("0.##", CultureInfo.InvariantCulture);
        vm.LiveInput.LiveOverOddsText = $"{liveLine}={vm.LiveInput.LiveOverOdds.ToString("0.######", CultureInfo.InvariantCulture)}";
        vm.LiveInput.LiveUnderOddsText = $"{liveLine}={vm.LiveInput.LiveUnderOdds.ToString("0.######", CultureInfo.InvariantCulture)}";

        vm.LiveInput.SelectedBetLine = ReadSelectedDouble(SelectedBetLineBox, vm.LiveInput.SelectedBetLine);
        vm.LiveInput.SelectedBetLineText = vm.LiveInput.SelectedBetLine.ToString("0.##", CultureInfo.InvariantCulture);
        vm.LiveInput.SelectedBetSide = SelectedBetSideBox.SelectedItem?.ToString() ?? vm.LiveInput.SelectedBetSide;
        vm.LiveInput.SelectedBetOdds = ReadDouble(SelectedBetOddsBox, vm.LiveInput.SelectedBetOdds);
        vm.LiveInput.Stake = ReadDouble(StakeBox, vm.LiveInput.Stake);
        vm.LiveInput.BetMode = BetModeBox.SelectedItem?.ToString() ?? vm.LiveInput.BetMode;
        vm.LiveInput.BetNotes = BetNotesBox.Text ?? string.Empty;
    }

    private static int ReadSelectedInt(ComboBox box, int fallback)
    {
        return box.SelectedItem switch
        {
            int value => value,
            string text when int.TryParse(Normalize(text), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) => value,
            _ => fallback
        };
    }

    private static double ReadSelectedDouble(ComboBox box, double fallback)
    {
        return box.SelectedItem switch
        {
            double value => value,
            decimal value => (double)value,
            int value => value,
            string text when double.TryParse(Normalize(text), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) => value,
            _ => fallback
        };
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
