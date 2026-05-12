using System.Collections.ObjectModel;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;
    private readonly IBettingModelService _modelService;
    private readonly ILiveBettingSessionService _liveSessionService;
    private string _leagueFilter = string.Empty;
    private MatchSnapshot? _selectedMatch;
    private ModelSummary _summary = new();
    private LiveBettingCheckResult _liveResult = new()
    {
        Status = "READY - select fixture and click Price live total"
    };
    private LiveBettingProfile? _selectedProfile;
    private string _paperLogPath = string.Empty;

    public MainWindowViewModel(IMatchRepository matchRepository, IBettingModelService modelService, ILiveBettingSessionService liveSessionService)
    {
        _matchRepository = matchRepository;
        _modelService = modelService;
        _liveSessionService = liveSessionService;

        foreach (LiveBettingProfile profile in _liveSessionService.GetProfiles())
        {
            Profiles.Add(profile);
            Leagues.Add(profile.DisplayName);
        }

        if (Leagues.Count == 0)
        {
            Leagues.Add("No profiles loaded");
        }

        LeagueFilter = Leagues[0];
    }

    public ObservableCollection<string> Leagues { get; } = [];
    public ObservableCollection<LiveBettingProfile> Profiles { get; } = [];
    public ObservableCollection<MatchSnapshot> Matches { get; } = [];
    public OddsInput Odds { get; } = new();
    public LiveBettingCheckInput LiveInput { get; } = new();
    public ObservableCollection<LiveBettingDecisionRow> LiveDecisions { get; } = [];
    public IReadOnlyList<string> StateTriggers { get; } = ["fixed-minute", "after-goal", "after-red-card"];
    public IReadOnlyList<string> BetSides { get; } = ["OVER", "UNDER"];
    public IReadOnlyList<string> BetModes { get; } = ["Paper", "Real"];

    public string AppTitle => "Live O/U Paper Betting Helper";
    public string ConnectionStatus => "Manual live session";
    public string Notes { get; set; } = "Select league and DB fixture. Pricing is run only when you click Price live total.";

    public LiveBettingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value) && value is not null)
            {
                LiveInput.ProfileKey = value.Key;
                OnPropertyChanged(nameof(ProfileNotes));
            }
        }
    }

    public string ProfileNotes => SelectedProfile?.Notes ?? string.Empty;

    public LiveBettingCheckResult LiveResult
    {
        get => _liveResult;
        private set
        {
            if (SetProperty(ref _liveResult, value))
            {
                OnPropertyChanged(nameof(LiveStatus));
                OnPropertyChanged(nameof(Warnings));
                OnPropertyChanged(nameof(ModelSummaryText));
                OnPropertyChanged(nameof(StateCorrectionText));
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string LiveStatus => LiveResult.Status;
    public string Warnings => LiveResult.Warnings;
    public string ModelSummaryText => LiveResult.ModelSummary;
    public string StateCorrectionText => $"{(LiveResult.StateCorrectionSupported ? "supported" : "unsupported")} | {LiveResult.StateCorrectionFactor:0.###} | {LiveResult.StateCorrectionSource}";
    public string VolumeText => $"{LiveResult.VolumeFactor:0.###} | {LiveResult.VolumeFactorSource}";
    public string PaperLogPath
    {
        get => _paperLogPath;
        private set => SetProperty(ref _paperLogPath, value);
    }

    public string LeagueFilter
    {
        get => _leagueFilter;
        set
        {
            if (SetProperty(ref _leagueFilter, value))
            {
                LiveBettingProfile? profile = _liveSessionService.FindProfileByLeague(value);
                if (profile is not null)
                    SelectedProfile = profile;

                ReloadMatches();
                SelectedMatch = Matches.FirstOrDefault();

                LiveResult = new LiveBettingCheckResult
                {
                    Status = $"READY - {Matches.Count} fixtures loaded"
                };
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
                SyncLiveInputFromSelectedMatch();

                // Do not auto-price here. Auto-pricing can freeze UI if model files/DB volume are slow.
                LiveResult = new LiveBettingCheckResult
                {
                    Status = "READY - click Price live total"
                };
                LiveDecisions.Clear();
            }
        }
    }

    public string SelectedMatchTitle => SelectedMatch?.MatchName ?? "No match selected";

    public ModelSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public async Task BuildLiveCheckAsync()
    {
        LiveResult = new LiveBettingCheckResult { Status = "PRICING..." };
        LiveResult = await _liveSessionService.BuildCheckAsync(LiveInput);
        LiveDecisions.Clear();
        foreach (LiveBettingDecisionRow decision in LiveResult.Decisions)
            LiveDecisions.Add(decision);
    }

    public async Task AppendPaperLogAsync()
    {
        await BuildLiveCheckAsync();
        PaperLogPath = _liveSessionService.AppendPaperLog(LiveInput, LiveResult);
    }

    public async Task LogBetAsync()
    {
        await BuildLiveCheckAsync();
        PaperLogPath = _liveSessionService.LogBet(LiveInput, LiveResult);
    }

    private void SyncLiveInputFromSelectedMatch()
    {
        if (SelectedMatch is null)
            return;

        LiveInput.MatchName = SelectedMatch.MatchName;
        LiveInput.Minute = SelectedMatch.Minute;
        LiveInput.HomeGoals = SelectedMatch.HomeGoals;
        LiveInput.AwayGoals = SelectedMatch.AwayGoals;
        LiveInput.HomeRedCards = SelectedMatch.HomeRedCards;
        LiveInput.AwayRedCards = SelectedMatch.AwayRedCards;
    }

    private void ReloadMatches()
    {
        Matches.Clear();

        try
        {
            foreach (MatchSnapshot match in _matchRepository.GetLiveMatches(LeagueFilter))
                Matches.Add(match);
        }
        catch (Exception ex)
        {
            LiveResult = new LiveBettingCheckResult
            {
                Status = "FIXTURE LOAD ERROR",
                Warnings = ex.Message
            };
        }
    }
}
