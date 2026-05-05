using System.Collections.ObjectModel;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;
    private readonly IBettingModelService _modelService;
    private string _leagueFilter = "NPL Queensland";
    private MatchSnapshot? _selectedMatch;
    private ModelSummary _summary = new();

    public MainWindowViewModel(IMatchRepository matchRepository, IBettingModelService modelService)
    {
        _matchRepository = matchRepository;
        _modelService = modelService;

        ReloadMatches();
        SelectedMatch = Matches.FirstOrDefault();
        Recalculate();
    }

    public IReadOnlyList<string> Leagues { get; } = ["NPL Queensland", "NPL NSW", "NPL Victoria"];
    public ObservableCollection<MatchSnapshot> Matches { get; } = [];
    public OddsInput Odds { get; } = new();
    public ObservableCollection<BetDecision> Decisions { get; } = [];

    public string AppTitle => "Live O/U Betting Helper";
    public string ConnectionStatus => "Connected";
    public string Notes { get; set; } = "Wait 1-2 minutes after a goal. Do not bet if score or odds feed is stale.";

    public string LeagueFilter
    {
        get => _leagueFilter;
        set
        {
            if (SetProperty(ref _leagueFilter, value))
            {
                ReloadMatches();
                SelectedMatch = Matches.FirstOrDefault();
            }
        }
    }

    public MatchSnapshot? SelectedMatch
    {
        get => _selectedMatch;
        set
        {
            if (SetProperty(ref _selectedMatch, value))
            {
                OnPropertyChanged(nameof(SelectedMatchTitle));
                Recalculate();
            }
        }
    }

    public string SelectedMatchTitle => SelectedMatch?.MatchName ?? "No match selected";

    public ModelSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public void Recalculate()
    {
        if (SelectedMatch is null)
            return;

        Summary = _modelService.CalculateSummary(SelectedMatch, Odds);
        Decisions.Clear();
        foreach (var decision in _modelService.CalculateDecisions(SelectedMatch, Odds, Summary))
            Decisions.Add(decision);
    }

    private void ReloadMatches()
    {
        Matches.Clear();
        foreach (var match in _matchRepository.GetLiveMatches(LeagueFilter))
            Matches.Add(match);
    }
}
