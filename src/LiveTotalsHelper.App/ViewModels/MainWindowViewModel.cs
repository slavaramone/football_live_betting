using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using LiveTotalsHelper.Core.Models;
using LiveTotalsHelper.Core.Services;

namespace LiveTotalsHelper.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IMatchRepository _matchRepository;
    private readonly ILiveBettingSessionService _liveSessionService;
    private readonly string _stateFilePath;
    private string _leagueFilter = string.Empty;
    private MatchSnapshot? _selectedMatch;
    private ModelSummary _summary = new();
    private LiveBettingCheckResult _liveResult = new()
    {
        Status = "READY - select fixture and click Price"
    };
    private LiveBettingProfile? _selectedProfile;
    private string _paperLogPath = string.Empty;
    private LiveBettingCheckInput _liveInput = new();
    private readonly Dictionary<string, LiveBettingCheckInput> _fixtureInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<MatchSnapshot>> _leagueMatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _leagueSelectedFixtureKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LiveBettingCheckResult> _fixtureResults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<LiveBettingDecisionRow>> _fixtureDecisionRows = new(StringComparer.OrdinalIgnoreCase);
    private bool _isRestoringState;

    public MainWindowViewModel(IMatchRepository matchRepository, ILiveBettingSessionService liveSessionService, string stateFilePath = "")
    {
        _matchRepository = matchRepository;
        _liveSessionService = liveSessionService;
        _stateFilePath = stateFilePath;

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

        RestoreState();
    }

    public ObservableCollection<string> Leagues { get; } = [];
    public ObservableCollection<LiveBettingProfile> Profiles { get; } = [];
    public ObservableCollection<MatchSnapshot> Matches { get; } = [];
    public ObservableCollection<double> TotalLines { get; } = [];
    public OddsInput Odds { get; } = new();

    public LiveBettingCheckInput LiveInput
    {
        get => _liveInput;
        private set => SetProperty(ref _liveInput, value);
    }
    public ObservableCollection<LiveBettingDecisionRow> LiveDecisions { get; } = [];
    public IReadOnlyList<string> StateTriggers { get; } = ["fixed-minute", "after-goal", "after-red-card"];
    public IReadOnlyList<int> Minutes { get; } = Enumerable.Range(0, 91).ToArray();
    public IReadOnlyList<int> GoalCounts { get; } = Enumerable.Range(0, 10).ToArray();
    public IReadOnlyList<int> RedCardCounts { get; } = [0, 1, 2, 3];
    public IReadOnlyList<int> LastGoalMinuteOptions { get; } = [-1, .. Enumerable.Range(0, 91)];
    public IReadOnlyList<string> BetSides { get; } = ["OVER", "UNDER"];
    public IReadOnlyList<string> BetModes { get; } = ["Paper", "Real"];

    public string AppTitle => "Live O/U Paper Betting Helper";
    public string ConnectionStatus => "Manual live session";
    public string Notes { get; set; } = "Select league and DB fixture. Pricing is run only when you click Price.";

    public LiveBettingProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value) && value is not null)
            {
                LiveInput.ProfileKey = value.Key;
                ApplyProfileDefaults(value);
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
            if (string.Equals(_leagueFilter, value, StringComparison.Ordinal))
                return;

            if (!_isRestoringState)
                SaveCurrentLeagueState();

            if (SetProperty(ref _leagueFilter, value))
            {
                LiveBettingProfile? profile = _liveSessionService.FindProfileByLeague(value);
                if (profile is not null)
                    SelectedProfile = profile;

                RestoreLeagueState(value);
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

            SaveCurrentFixtureState();

            _selectedMatch = value;
            OnPropertyChanged(nameof(SelectedMatch));
            OnPropertyChanged(nameof(SelectedMatchTitle));

            LoadFixtureInputForSelectedMatch();

            if (RestoreFixtureResultForSelectedMatch())
                return;

            // Do not auto-price here. Auto-pricing can freeze UI if model files/DB volume are slow.
            LiveResult = new LiveBettingCheckResult
            {
                Status = value is null
                    ? "READY - select fixture and click Price"
                    : "READY - fixture input restored; click Price"
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
        string? previousSelectedKey = _selectedMatch is null ? null : GetFixtureInputKey(_selectedMatch);
        SaveCurrentLeagueState();

        ReloadMatches();
        MatchSnapshot? nextSelection = !string.IsNullOrWhiteSpace(previousSelectedKey)
            ? Matches.FirstOrDefault(x => GetFixtureInputKey(x).Equals(previousSelectedKey, StringComparison.OrdinalIgnoreCase))
            : null;
        SelectedMatch = nextSelection ?? Matches.FirstOrDefault();
        SaveCurrentLeagueState();

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

        SaveCurrentFixtureState();
    }

    public async Task AppendPaperLogAsync()
    {
        await BuildLiveCheckAsync();
        PaperLogPath = _liveSessionService.AppendPaperLog(LiveInput, LiveResult);
        SaveState();
    }

    public async Task LogBetAsync()
    {
        await BuildLiveCheckAsync();
        PaperLogPath = _liveSessionService.LogBet(LiveInput, LiveResult);
        SaveState();
    }

    public void SaveState()
    {
        if (string.IsNullOrWhiteSpace(_stateFilePath))
            return;

        try
        {
            SaveCurrentLeagueState();

            string? directory = Path.GetDirectoryName(Path.GetFullPath(_stateFilePath));
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var state = new AppStateDto
            {
                SavedAtUtc = DateTimeOffset.UtcNow,
                LeagueFilter = LeagueFilter,
                PaperLogPath = PaperLogPath,
                FixtureInputs = new Dictionary<string, LiveBettingCheckInput>(_fixtureInputs, StringComparer.OrdinalIgnoreCase),
                LeagueMatches = _leagueMatches.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.OrdinalIgnoreCase),
                LeagueSelectedFixtureKeys = new Dictionary<string, string>(_leagueSelectedFixtureKeys, StringComparer.OrdinalIgnoreCase),
                FixtureResults = new Dictionary<string, LiveBettingCheckResult>(_fixtureResults, StringComparer.OrdinalIgnoreCase),
                FixtureDecisionRows = _fixtureDecisionRows.ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.OrdinalIgnoreCase)
            };

            File.WriteAllText(_stateFilePath, JsonSerializer.Serialize(state, StateJsonOptions));
        }
        catch (Exception ex)
        {
            LiveResult = new LiveBettingCheckResult
            {
                Status = "STATE SAVE ERROR",
                Warnings = ex.Message
            };
        }
    }

    public void CaptureCurrentState()
    {
        SaveCurrentLeagueState();
    }

    private void RestoreState()
    {
        if (string.IsNullOrWhiteSpace(_stateFilePath) || !File.Exists(_stateFilePath))
            return;

        try
        {
            AppStateDto? state = JsonSerializer.Deserialize<AppStateDto>(File.ReadAllText(_stateFilePath), StateJsonOptions);
            if (state is null)
                return;

            _isRestoringState = true;

            _fixtureInputs.Clear();
            foreach ((string key, LiveBettingCheckInput input) in state.FixtureInputs)
                _fixtureInputs[key] = input;

            _leagueMatches.Clear();
            foreach ((string league, List<MatchSnapshot> matches) in state.LeagueMatches)
                _leagueMatches[league] = matches;

            _leagueSelectedFixtureKeys.Clear();
            foreach ((string league, string key) in state.LeagueSelectedFixtureKeys)
                _leagueSelectedFixtureKeys[league] = key;

            _fixtureResults.Clear();
            foreach ((string key, LiveBettingCheckResult result) in state.FixtureResults)
                _fixtureResults[key] = result;

            _fixtureDecisionRows.Clear();
            foreach ((string key, List<LiveBettingDecisionRow> rows) in state.FixtureDecisionRows)
                _fixtureDecisionRows[key] = rows;

            PaperLogPath = state.PaperLogPath;

            string leagueToRestore = Leagues.Any(x => x.Equals(state.LeagueFilter, StringComparison.OrdinalIgnoreCase))
                ? state.LeagueFilter
                : _leagueFilter;

            LeagueFilter = leagueToRestore;
            RestoreLeagueState(leagueToRestore);
        }
        catch (Exception ex)
        {
            LiveResult = new LiveBettingCheckResult
            {
                Status = "STATE RESTORE ERROR",
                Warnings = ex.Message
            };
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    private void SaveCurrentFixtureInput()
    {
        if (_selectedMatch is null)
            return;

        _fixtureInputs[GetFixtureInputKey(_selectedMatch)] = CloneInput(LiveInput);
    }

    private void SaveCurrentFixtureState()
    {
        if (_selectedMatch is null)
            return;

        string key = GetFixtureInputKey(_selectedMatch);
        _fixtureInputs[key] = CloneInput(LiveInput);
        _leagueSelectedFixtureKeys[LeagueFilter] = key;

        if (!LiveResult.Status.Equals("PRICING...", StringComparison.OrdinalIgnoreCase))
            _fixtureResults[key] = LiveResult;
        _fixtureDecisionRows[key] = LiveDecisions.ToList();
    }

    private void SaveCurrentLeagueState()
    {
        if (string.IsNullOrWhiteSpace(_leagueFilter))
            return;

        SaveCurrentFixtureState();
        _leagueMatches[_leagueFilter] = Matches.ToList();
    }

    private void RestoreLeagueState(string league)
    {
        Matches.Clear();
        LiveDecisions.Clear();
        _selectedMatch = null;
        OnPropertyChanged(nameof(SelectedMatch));
        OnPropertyChanged(nameof(SelectedMatchTitle));

        if (_leagueMatches.TryGetValue(league, out List<MatchSnapshot>? savedMatches))
        {
            foreach (MatchSnapshot match in savedMatches)
                Matches.Add(match);
        }

        MatchSnapshot? selected = null;
        if (_leagueSelectedFixtureKeys.TryGetValue(league, out string? selectedKey))
        {
            selected = Matches.FirstOrDefault(x =>
                GetFixtureInputKey(x).Equals(selectedKey, StringComparison.OrdinalIgnoreCase));
        }

        if (selected is not null)
        {
            SelectedMatch = selected;
            return;
        }

        LiveResult = new LiveBettingCheckResult
        {
            Status = Matches.Count == 0
                ? "READY - click Load fixtures"
                : $"READY - {Matches.Count} fixtures restored"
        };
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

    private bool RestoreFixtureResultForSelectedMatch()
    {
        if (SelectedMatch is null)
            return false;

        string key = GetFixtureInputKey(SelectedMatch);
        if (!_fixtureResults.TryGetValue(key, out LiveBettingCheckResult? savedResult))
            return false;

        LiveResult = savedResult;
        LiveDecisions.Clear();
        if (_fixtureDecisionRows.TryGetValue(key, out List<LiveBettingDecisionRow>? rows))
        {
            foreach (LiveBettingDecisionRow row in rows)
                LiveDecisions.Add(row);
        }

        return true;
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
            StartingOverOdds25 = source.StartingOverOdds25,
            StartingUnderOdds25 = source.StartingUnderOdds25,
            StartingOverOdds35 = source.StartingOverOdds35,
            StartingUnderOdds35 = source.StartingUnderOdds35,
            LiveOddsLine = source.LiveOddsLine,
            LiveOverOdds = source.LiveOverOdds,
            LiveUnderOdds = source.LiveUnderOdds,
            LiveOverOdds25 = source.LiveOverOdds25,
            LiveUnderOdds25 = source.LiveUnderOdds25,
            LiveOverOdds35 = source.LiveOverOdds35,
            LiveUnderOdds35 = source.LiveUnderOdds35,
            LiveOverOddsText = source.LiveOverOddsText,
            LiveUnderOddsText = source.LiveUnderOddsText,
            TargetLinesText = source.TargetLinesText,
            SelectedBetLine = source.SelectedBetLine,
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
            _leagueMatches[LeagueFilter] = Matches.ToList();
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

    private void ApplyProfileDefaults(LiveBettingProfile profile)
    {
        TotalLines.Clear();

        IEnumerable<double> sourceLines = profile.TargetLines.Count > 0
            ? profile.TargetLines
            : profile.AllowedLines.Count > 0
                ? profile.AllowedLines
                : [2.5, 3.5];

        foreach (double line in sourceLines.Distinct().OrderBy(x => x))
            TotalLines.Add(line);

        if (TotalLines.Count == 0)
            TotalLines.Add(2.5);

        LiveInput.TargetLinesText = string.Join(",", TotalLines.Select(x => x.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
        if (!TotalLines.Any(x => Math.Abs(x - LiveInput.StartingLine) < 0.001))
            LiveInput.StartingLine = TotalLines[0];
        if (!TotalLines.Any(x => Math.Abs(x - LiveInput.LiveOddsLine) < 0.001))
            LiveInput.LiveOddsLine = TotalLines[0];
        if (!TotalLines.Any(x => Math.Abs(x - LiveInput.SelectedBetLine) < 0.001))
            LiveInput.SelectedBetLine = TotalLines[0];
        LiveInput.SelectedBetLineText = LiveInput.SelectedBetLine.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        if (!LiveInput.BeforeRound.HasValue && profile.DefaultBeforeRound.HasValue)
            LiveInput.BeforeRound = profile.DefaultBeforeRound;

        OnPropertyChanged(nameof(TotalLines));
        OnPropertyChanged(nameof(LiveInput));
    }

    private static readonly JsonSerializerOptions StateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class AppStateDto
    {
        public DateTimeOffset SavedAtUtc { get; set; }
        public string LeagueFilter { get; set; } = string.Empty;
        public string PaperLogPath { get; set; } = string.Empty;
        public Dictionary<string, LiveBettingCheckInput> FixtureInputs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<MatchSnapshot>> LeagueMatches { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LeagueSelectedFixtureKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, LiveBettingCheckResult> FixtureResults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<LiveBettingDecisionRow>> FixtureDecisionRows { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
