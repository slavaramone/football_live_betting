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
        Closing += (_, _) =>
        {
            if (DataContext is MainWindowViewModel vm)
            {
                SyncVisibleInputs(vm);
                vm.SaveState();
            }
        };
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

        vm.LiveInput.StartingOverOdds25 = ReadDouble(StartingOverOdds25Box, vm.LiveInput.StartingOverOdds25);
        vm.LiveInput.StartingUnderOdds25 = ReadDouble(StartingUnderOdds25Box, vm.LiveInput.StartingUnderOdds25);
        vm.LiveInput.StartingOverOdds35 = ReadDouble(StartingOverOdds35Box, vm.LiveInput.StartingOverOdds35);
        vm.LiveInput.StartingUnderOdds35 = ReadDouble(StartingUnderOdds35Box, vm.LiveInput.StartingUnderOdds35);
        ApplyStartingAnchor(vm);

        vm.LiveInput.LiveOverOdds25 = ReadDouble(LiveOverOdds25Box, vm.LiveInput.LiveOverOdds25);
        vm.LiveInput.LiveUnderOdds25 = ReadDouble(LiveUnderOdds25Box, vm.LiveInput.LiveUnderOdds25);
        vm.LiveInput.LiveOverOdds35 = ReadDouble(LiveOverOdds35Box, vm.LiveInput.LiveOverOdds35);
        vm.LiveInput.LiveUnderOdds35 = ReadDouble(LiveUnderOdds35Box, vm.LiveInput.LiveUnderOdds35);
        vm.LiveInput.LiveOddsLine = 2.5;
        vm.LiveInput.LiveOverOdds = vm.LiveInput.LiveOverOdds25;
        vm.LiveInput.LiveUnderOdds = vm.LiveInput.LiveUnderOdds25;
        vm.LiveInput.LiveOverOddsText = BuildOddsText((2.5, vm.LiveInput.LiveOverOdds25), (3.5, vm.LiveInput.LiveOverOdds35));
        vm.LiveInput.LiveUnderOddsText = BuildOddsText((2.5, vm.LiveInput.LiveUnderOdds25), (3.5, vm.LiveInput.LiveUnderOdds35));

        vm.LiveInput.SelectedBetLine = ReadSelectedDouble(SelectedBetLineBox, vm.LiveInput.SelectedBetLine);
        vm.LiveInput.SelectedBetLineText = vm.LiveInput.SelectedBetLine.ToString("0.##", CultureInfo.InvariantCulture);
        vm.LiveInput.SelectedBetSide = SelectedBetSideBox.SelectedItem?.ToString() ?? vm.LiveInput.SelectedBetSide;
        vm.LiveInput.SelectedBetOdds = ReadDouble(SelectedBetOddsBox, vm.LiveInput.SelectedBetOdds);
        vm.LiveInput.Stake = ReadDouble(StakeBox, vm.LiveInput.Stake);
        vm.LiveInput.BetMode = BetModeBox.SelectedItem?.ToString() ?? vm.LiveInput.BetMode;
        vm.LiveInput.BetNotes = BetNotesBox.Text ?? string.Empty;
    }

    private static void ApplyStartingAnchor(MainWindowViewModel vm)
    {
        if (vm.LiveInput.StartingOverOdds25 > 1.0 && vm.LiveInput.StartingUnderOdds25 > 1.0)
        {
            vm.LiveInput.StartingLine = 2.5;
            vm.LiveInput.StartingOverOdds = vm.LiveInput.StartingOverOdds25;
            vm.LiveInput.StartingUnderOdds = vm.LiveInput.StartingUnderOdds25;
            return;
        }

        if (vm.LiveInput.StartingOverOdds35 > 1.0 && vm.LiveInput.StartingUnderOdds35 > 1.0)
        {
            vm.LiveInput.StartingLine = 3.5;
            vm.LiveInput.StartingOverOdds = vm.LiveInput.StartingOverOdds35;
            vm.LiveInput.StartingUnderOdds = vm.LiveInput.StartingUnderOdds35;
        }
    }

    private static string BuildOddsText(params (double Line, double Odds)[] odds)
    {
        return string.Join(",",
            odds
                .Where(x => x.Line > 0 && x.Odds > 1.0)
                .Select(x => $"{x.Line.ToString("0.##", CultureInfo.InvariantCulture)}={x.Odds.ToString("0.######", CultureInfo.InvariantCulture)}"));
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

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
            return value;

        box.Text = fallback?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        return fallback;
    }

    private static double ReadDouble(TextBox box, double fallback)
    {
        string text = Normalize(box.Text);
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return value;

        box.Text = fallback.ToString("0.######", CultureInfo.InvariantCulture);
        return fallback;
    }

    private static string Normalize(string? text)
    {
        return (text ?? string.Empty).Trim().Replace(',', '.');
    }
}
