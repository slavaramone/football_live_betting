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
    private LiveBettingCheckInput _liveInput = new();
    private readonly Dictionary<string, LiveBettingCheckInput> _fixtureInputs = new(StringComparer.OrdinalIgnoreCase);

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

        _leagueFilter = Leagues[0];
        OnPropertyChanged(nameof(LeagueFilter));

        SelectedProfile = _liveSessionService.FindProfileByLeague(_leagueFilter) ?? Profiles.FirstOrDefault();
        LiveResult = new LiveBettingCheckResult
        {
            Status = "READY - click Load fixtures"
        };
    }

    public ObservableCollection<string> Leagues { get; } = [];
    public ObservableCollection<LiveBettingProfile> Profiles { get; } = [];
    public ObservableCollection<MatchSnapshot> Matches { get; } = [];
    public OddsInput Odds { get; } = new();

    public LiveBettingCheckInput LiveInput
    {
        get => _liveInput;
        private set => SetProperty(ref _liveInput, value);
    }
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
                OnPropertyChanged(nameof(DecisionRulesText));
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
                OnPropertyChanged(nameof(DecisionRulesText));
                OnPropertyChanged(nameof(StateCorrectionText));
                OnPropertyChanged(nameof(VolumeText));
            }
        }
    }

    public string LiveStatus => LiveResult.Status;
    public string Warnings => LiveResult.Warnings;
    public string ModelSummaryText => LiveResult.ModelSummary;
    public string DecisionRulesText => string.IsNullOrWhiteSpace(LiveResult.DecisionRulesSummary) ? SelectedProfile?.Notes ?? string.Empty : LiveResult.DecisionRulesSummary;
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

                SaveCurrentFixtureInput();
                _fixtureInputs.Clear();
                Matches.Clear();
                SelectedMatch = null;
                LiveDecisions.Clear();

                LiveResult = new LiveBettingCheckResult
                {
                    Status = "READY - click Load fixtures"
                };
            }
        }
    }

    public MatchSnapshot? SelectedMatch
    {
        get => _selectedMatch;
        set
        {
            if (ReferenceEquals(_selectedMatch, value))
                return;

            SaveCurrentFixtureInput();

            _selectedMatch = value;
            OnPropertyChanged(nameof(SelectedMatch));
            OnPropertyChanged(nameof(SelectedMatchTitle));

            LoadFixtureInputForSelectedMatch();

            // Do not auto-price here. Auto-pricing can freeze UI if model files/DB volume are slow.
            LiveResult = new LiveBettingCheckResult
            {
                Status = value is null
                    ? "READY - select fixture and click Price live total"
                    : "READY - fixture input restored; click Price live total"
            };
            LiveDecisions.Clear();
        }
    }

    public string SelectedMatchTitle => SelectedMatch?.MatchName ?? "No match selected";

    public ModelSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public void LoadFixtures()
    {
        ReloadMatches();
        SelectedMatch = Matches.FirstOrDefault();

        LiveResult = new LiveBettingCheckResult
        {
            Status = $"READY - {Matches.Count} fixtures loaded"
        };
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

    private void SaveCurrentFixtureInput()
    {
        if (_selectedMatch is null)
            return;

        _fixtureInputs[GetFixtureInputKey(_selectedMatch)] = CloneInput(LiveInput);
    }

    private void LoadFixtureInputForSelectedMatch()
    {
        if (SelectedMatch is null)
            return;

        string key = GetFixtureInputKey(SelectedMatch);
        if (_fixtureInputs.TryGetValue(key, out LiveBettingCheckInput? saved))
        {
            LiveInput = CloneInput(saved);
            return;
        }

        LiveBettingCheckInput input = CloneInput(LiveInput);
        SyncMatchState(input, SelectedMatch);
        _fixtureInputs[key] = CloneInput(input);
        LiveInput = input;
    }

    private static void SyncMatchState(LiveBettingCheckInput input, MatchSnapshot match)
    {
        input.MatchName = match.MatchName;
        input.Minute = match.Minute;
        input.HomeGoals = match.HomeGoals;
        input.AwayGoals = match.AwayGoals;
        input.HomeRedCards = match.HomeRedCards;
        input.AwayRedCards = match.AwayRedCards;
    }

    private static string GetFixtureInputKey(MatchSnapshot match)
    {
        if (!string.IsNullOrWhiteSpace(match.MatchId))
            return match.MatchId;

        return $"{match.League}|{match.HomeTeam}|{match.AwayTeam}";
    }

    private static LiveBettingCheckInput CloneInput(LiveBettingCheckInput source)
    {
        return new LiveBettingCheckInput
        {
            ProfileKey = source.ProfileKey,
            MatchName = source.MatchName,
            StateTrigger = source.StateTrigger,
            Minute = source.Minute,
            HomeGoals = source.HomeGoals,
            AwayGoals = source.AwayGoals,
            HomeRedCards = source.HomeRedCards,
            AwayRedCards = source.AwayRedCards,
            LastGoalMinute = source.LastGoalMinute,
            RecentGoalMinutes = source.RecentGoalMinutes,
            BeforeRound = source.BeforeRound,
            StartingLine = source.StartingLine,
            StartingOverOdds = source.StartingOverOdds,
            StartingUnderOdds = source.StartingUnderOdds,
            LiveOverOddsText = source.LiveOverOddsText,
            LiveUnderOddsText = source.LiveUnderOddsText,
            TargetLinesText = source.TargetLinesText,
            SelectedBetLineText = source.SelectedBetLineText,
            SelectedBetSide = source.SelectedBetSide,
            SelectedBetOdds = source.SelectedBetOdds,
            Stake = source.Stake,
            BetMode = source.BetMode,
            BetNotes = source.BetNotes
        };
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
