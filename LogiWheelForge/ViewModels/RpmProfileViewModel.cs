using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.Views;
using Media = System.Windows.Media;
using Application = System.Windows.Application;

namespace LogiWheelForge.ViewModels;

public sealed class RpmProfileViewModel : ObservableObject, IDisposable
{
    private readonly SettingsDraft _draft;
    private readonly HashSet<ThresholdViewModel> _observedThresholds = [];
    private readonly AsyncRelayCommand _primaryProfileCommand;
    private readonly AsyncRelayCommand _testLedsCommand;
    private readonly LedIndicatorService _service;
    private readonly Action<string> _setStatus;
    private WheelDefinition? _activeDefinition;
    private IReadOnlyList<CarTrainingViewModel> _carTrainings = [];
    private bool? _learnPerCarShiftBeforeAdvanced;
    private double? _learnedRedlinePercent;
    private string? _loadedProfileWheelId;
    private bool _isWheelConnected;
    private WheelProfile? _selectedSavedProfile;
    private string _pageTitle = "LED Indicator";

    public RpmProfileViewModel(LedIndicatorService service, SettingsDraft draft, Action<string> setStatus)
    {
        _service = service;
        _draft = draft;
        _setStatus = setStatus;
        _primaryProfileCommand = new AsyncRelayCommand(ExecutePrimaryProfileActionAsync,
            () => ShowSaveProfile || ShowLoadProfile, ex => _setStatus(ex.Message));
        _testLedsCommand = new AsyncRelayCommand(() => _service.TestLedsAsync(), () => _isWheelConnected,
            ex => _setStatus($"LED test failed: {ex.Message}"));
        ResetProfileCommand = new RelayCommand(ResetProfile);
        SaveProfileCommand = new AsyncRelayCommand(SaveSelectedProfileAsync, () => SelectedSavedProfile is not null,
            ex => _setStatus(ex.Message));
        LoadProfileCommand = new AsyncRelayCommand(LoadSelectedProfileAsync,
            () => SelectedSavedProfile?.ProfileId is not null, ex => _setStatus(ex.Message));
        DeleteProfileCommand = new AsyncRelayCommand(DeleteSelectedProfileAsync,
            () => SelectedSavedProfile?.ProfileId is not null, ex => _setStatus(ex.Message));
        _draft.PropertyChanged += OnDraftPropertyChanged;
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<ThresholdViewModel> Thresholds { get; } = [];
    public string PageTitle { get => _pageTitle; set => SetField(ref _pageTitle, value); }
    public ICommand ResetProfileCommand { get; }
    public ICommand SaveProfileCommand { get; }
    public ICommand LoadProfileCommand { get; }
    public ICommand DeleteProfileCommand { get; }
    public ICommand PrimaryProfileCommand => _primaryProfileCommand;
    public ICommand TestLedsCommand => _testLedsCommand;
    public ObservableCollection<WheelProfile> SavedProfiles { get; } = [];

    public WheelProfile? SelectedSavedProfile
    {
        get => _selectedSavedProfile;
        set
        {
            if (!SetField(ref _selectedSavedProfile, value)) return;
            OnPropertyChanged(nameof(HasSelectedSavedProfile));
            RefreshProfileCommandStates();
            NotifyPrimaryProfileState();
        }
    }

    public bool HasSelectedSavedProfile => SelectedSavedProfile?.ProfileId is not null;

    public bool IsNewProfileSelected => SelectedSavedProfile is { ProfileId: null };

    public bool HasUnsavedProfileChanges => SelectedSavedProfile?.ProfileId is not null && IsCurrentProfileDirty();

    public bool ShowSaveProfile => IsNewProfileSelected || HasUnsavedProfileChanges;

    public bool ShowLoadProfile => SelectedSavedProfile?.ProfileId is not null && !HasUnsavedProfileChanges;

    public string PrimaryProfileActionText => ShowSaveProfile ? "Save" : "Load";

    public string ProfileWheelName => _draft.SelectedWheel?.Id is null
        ? _activeDefinition?.DisplayName ?? _service.Wheels.FirstOrDefault()?.DisplayName ?? "Selected wheel"
        : _draft.SelectedWheel.Name;

    public IReadOnlyList<CarTrainingViewModel> CarTrainings
    {
        get => _carTrainings;
        private set => SetField(ref _carTrainings, value);
    }

    public double FirstLedPercent
    {
        get => _draft.FirstLedPercent;
        set => _draft.FirstLedPercent = value;
    }

    public double RedlinePercent
    {
        get => _draft.RedlinePercent;
        set => _draft.RedlinePercent = value;
    }

    public bool BlinkAtRedline
    {
        get => _draft.BlinkAtRedline;
        set => _draft.BlinkAtRedline = value;
    }

    public bool LearnPerCarShift
    {
        get => _draft.LearnPerCarShift;
        set => _draft.LearnPerCarShift = value;
    }

    public RpmProfileMode ProfileMode
    {
        get => _draft.ProfileMode;
        set => _draft.ProfileMode = value;
    }

    public bool IsEasyMode
    {
        get => _draft.IsEasyMode;
        set => _draft.IsEasyMode = value;
    }

    public bool IsAdvancedMode
    {
        get => _draft.IsAdvancedMode;
        set => _draft.IsAdvancedMode = value;
    }

    public string CalibrationStatus => _learnedRedlinePercent is double value
        ? $"Learned shift point: {value:0.0}%"
        : "Learning per-car shift";

    private WheelDefinition? SelectedDefinition => _draft.SelectedWheel?.Id is { } selectedId
        ? _service.Wheels.FirstOrDefault(x => x.Id == selectedId)
        : _activeDefinition ?? _service.Wheels.FirstOrDefault();

    public void Dispose()
    {
        foreach (var threshold in _observedThresholds) threshold.PropertyChanged -= OnThresholdChanged;
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _service.SnapshotChanged -= OnSnapshotChanged;
    }

    public void RefreshCarTrainings()
    {
        CarTrainings = _service.GetCarTrainingMappings().Select(mapping => new CarTrainingViewModel(mapping)).ToArray();
    }

    public CarTrainingOverview GetTrainingOverview() => _service.GetCarTrainingOverview();

    public async Task ResetCarTrainingsAsync()
    {
        await _service.ResetLearningAsync();
        RefreshCarTrainings();
    }

    public void Initialize(WheelDefinition? selectedWheel, IReadOnlyList<double> thresholds)
    {
        ConfigureThresholds(selectedWheel, thresholds);
        _service.SetPreviewWheel(_draft.SelectedWheel?.Id);
        var wheel = selectedWheel ?? SelectedDefinition;
        if (wheel is not null) _ = RefreshSavedProfilesAsync(wheel);
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsDraft.SelectedWheel))
        {
            OnPropertyChanged(nameof(ProfileWheelName));
            _service.SetPreviewWheel(_draft.SelectedWheel?.Id);
            var wheel = _service.Wheels.FirstOrDefault(x => x.Id == _draft.SelectedWheel?.Id);
            if (wheel is not null)
            {
                _ = LoadWheelProfileAsync(wheel, false);
                _ = RefreshSavedProfilesAsync(wheel);
            }
        }
        else if (e.PropertyName == nameof(SettingsDraft.FirstLedPercent))
        {
            OnPropertyChanged(nameof(FirstLedPercent));
        }
        else if (e.PropertyName == nameof(SettingsDraft.RedlinePercent))
        {
            OnPropertyChanged(nameof(RedlinePercent));
        }
        else if (e.PropertyName == nameof(SettingsDraft.BlinkAtRedline))
        {
            OnPropertyChanged(nameof(BlinkAtRedline));
        }
        else if (e.PropertyName == nameof(SettingsDraft.LearnPerCarShift))
        {
            OnPropertyChanged(nameof(LearnPerCarShift));
        }
        else if (e.PropertyName == nameof(SettingsDraft.ProfileMode))
        {
            if (_draft.ProfileMode == RpmProfileMode.Advanced)
            {
                _learnPerCarShiftBeforeAdvanced ??= _draft.LearnPerCarShift;
                if (_draft.LearnPerCarShift) _draft.LearnPerCarShift = false;
            }
            else if (_learnPerCarShiftBeforeAdvanced is bool previousLearnState)
            {
                _draft.LearnPerCarShift = previousLearnState;
                _learnPerCarShiftBeforeAdvanced = null;
            }
            else if (!_draft.LearnPerCarShift)
            {
                _draft.LearnPerCarShift = true;
            }

            OnPropertyChanged(nameof(ProfileMode));
            OnPropertyChanged(nameof(IsEasyMode));
            OnPropertyChanged(nameof(IsAdvancedMode));
        }

        NotifyPrimaryProfileState();
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot);
        else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        _isWheelConnected = snapshot.IsWheelConnected;
        _testLedsCommand.RaiseCanExecuteChanged();
        if (_learnedRedlinePercent != snapshot.LearnedRedlinePercent)
        {
            _learnedRedlinePercent = snapshot.LearnedRedlinePercent;
            OnPropertyChanged(nameof(CalibrationStatus));
        }

        if (snapshot.Wheel is null || snapshot.Wheel.Id == _activeDefinition?.Id) return;
        _activeDefinition = snapshot.Wheel;
        if (_draft.SelectedWheel?.Id is null) _ = RefreshSavedProfilesAsync(snapshot.Wheel);
        OnPropertyChanged(nameof(ProfileWheelName));
        _ = LoadWheelProfileAsync(snapshot.Wheel, true);
    }

    private void ResetProfile()
    {
        FirstLedPercent = _activeDefinition?.DefaultFirstPercent ?? LedProfileSettings.DefaultFirstLedPercent;
        RedlinePercent = _activeDefinition?.DefaultRedlinePercent ?? LedProfileSettings.DefaultRedlinePercent;
        BlinkAtRedline = true;
        ConfigureThresholds(_activeDefinition, []);
        NotifyPrimaryProfileState();
    }

    private async Task ExecutePrimaryProfileActionAsync()
    {
        if (ShowSaveProfile) await SaveSelectedProfileAsync();
        else if (ShowLoadProfile) await LoadSelectedProfileAsync();
    }

    private WheelProfile CaptureProfile(string? profileId = null, string? name = null)
    {
        var wheel = SelectedDefinition ??
                    throw new InvalidOperationException("Select a wheel before managing RPM profiles.");
        return new WheelProfile
        {
            WheelId = wheel.Id, ProfileId = profileId, Name = name, Mode = ProfileMode,
            FirstLedPercent = FirstLedPercent, RedlinePercent = RedlinePercent,
            BlinkAtRedline = BlinkAtRedline, AdvancedThresholds = Thresholds.Select(x => x.Value).ToArray()
        };
    }

    private async Task RefreshSavedProfilesAsync(WheelDefinition wheel)
    {
        try
        {
            var previousId = SelectedSavedProfile?.ProfileId;
            var profiles = await _service.ListNamedWheelProfilesAsync(wheel.Id);
            SavedProfiles.Clear();
            foreach (var profile in profiles) SavedProfiles.Add(profile);
            SavedProfiles.Add(new WheelProfile { WheelId = wheel.Id, Name = "New profile" });
            SelectedSavedProfile = SavedProfiles.FirstOrDefault(x => x.ProfileId == previousId);
            OnPropertyChanged(nameof(ProfileWheelName));
        }
        catch (Exception ex)
        {
            _setStatus($"Could not read saved RPM profiles: {ex.Message}");
        }
    }

    private async Task SaveSelectedProfileAsync()
    {
        var selected = SelectedSavedProfile;
        if (selected is null) return;
        var wheel = SelectedDefinition ?? throw new InvalidOperationException("Select a wheel first.");
        var profileName = selected.ProfileId is null
            ? ProfileNameDialog.Show(Application.Current?.MainWindow)
            : selected.Name;
        if (string.IsNullOrWhiteSpace(profileName)) return;
        var saved = await _service.SaveNamedWheelProfileAsync(
            CaptureProfile(selected.ProfileId, profileName), wheel.ControlGroupCount);
        await RefreshSavedProfilesAsync(wheel);
        SelectedSavedProfile = SavedProfiles.FirstOrDefault(x => x.ProfileId == saved.ProfileId);
        _setStatus(selected.ProfileId is null ? $"Created “{saved.Name}”." : $"Saved changes to “{saved.Name}”.");
    }

    private async Task LoadSelectedProfileAsync()
    {
        var profile = SelectedSavedProfile;
        if (profile is null) return;
        var wheel = SelectedDefinition ?? throw new InvalidOperationException("Select a wheel first.");
        if (profile.WheelId != wheel.Id)
            throw new InvalidOperationException("This profile belongs to a different wheel.");
        if (!profile.TryValidate(wheel.ControlGroupCount, out var error))
            throw new InvalidOperationException(error);
        ProfileMode = profile.Mode;
        FirstLedPercent = profile.FirstLedPercent;
        RedlinePercent = profile.RedlinePercent;
        BlinkAtRedline = profile.BlinkAtRedline;
        ConfigureThresholds(wheel, profile.AdvancedThresholds);
        await _service.UpdateSettingsAsync(_service.Settings with
        {
            PreferredWheelId = wheel.Id, ProfileMode = profile.Mode,
            FirstLedPercent = profile.FirstLedPercent, RedlinePercent = profile.RedlinePercent,
            BlinkAtRedline = profile.BlinkAtRedline, AdvancedThresholds = profile.AdvancedThresholds
        });
        _setStatus($"Loaded “{profile.Name}”.");
    }

    private async Task DeleteSelectedProfileAsync()
    {
        var profile = SelectedSavedProfile;
        if (profile?.ProfileId is null) return;
        var wheel = SelectedDefinition ?? throw new InvalidOperationException("Select a wheel first.");
        await _service.DeleteNamedWheelProfileAsync(wheel.Id, profile.ProfileId);
        await RefreshSavedProfilesAsync(wheel);
        _setStatus($"Deleted “{profile.Name}”.");
    }

    private void RefreshProfileCommandStates()
    {
        (SaveProfileCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (LoadProfileCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
        (DeleteProfileCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
    }

    private void NotifyPrimaryProfileState()
    {
        OnPropertyChanged(nameof(IsNewProfileSelected));
        OnPropertyChanged(nameof(HasUnsavedProfileChanges));
        OnPropertyChanged(nameof(ShowSaveProfile));
        OnPropertyChanged(nameof(ShowLoadProfile));
        OnPropertyChanged(nameof(PrimaryProfileActionText));
        _primaryProfileCommand.RaiseCanExecuteChanged();
    }

    private bool IsCurrentProfileDirty()
    {
        var profile = SelectedSavedProfile;
        var wheel = SelectedDefinition;
        if (profile?.ProfileId is null || wheel is null) return false;

        return profile.WheelId != wheel.Id || profile.Mode != ProfileMode ||
               Math.Abs(profile.FirstLedPercent - FirstLedPercent) > .01 ||
               Math.Abs(profile.RedlinePercent - RedlinePercent) > .01 ||
               profile.BlinkAtRedline != BlinkAtRedline ||
               !profile.AdvancedThresholds.SequenceEqual(Thresholds.Select(threshold => threshold.Value));
    }

    private void OnThresholdChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ThresholdViewModel.Value)) NotifyPrimaryProfileState();
    }

    private void ConfigureThresholds(WheelDefinition? definition, IReadOnlyList<double> saved)
    {
        var wheel = definition ?? _service.Wheels.FirstOrDefault();
        var count = wheel?.ControlGroupCount ?? 5;
        var firstThreshold = Math.Min(FirstLedPercent, RedlinePercent - 10);
        var lastThreshold = Math.Max(firstThreshold, RedlinePercent - 5);
        var values = saved.Count == count
            ? saved.ToArray()
            : LedMath.BuildRecommendedThresholds(count, firstThreshold, lastThreshold);
        var colors = wheel?.Colors ?? ["#38D982", "#6EE65A", "#F0D84A", "#FFAA3B", "#FF5265"];
        Thresholds.Clear();
        for (var i = 0; i < count; i++)
            Thresholds.Add(new ThresholdViewModel
            {
                Label = i == count - 1 ? "Red light" : $"LED group {i + 1}",
                Color = (Media.Brush)new Media.BrushConverter().ConvertFromString(
                    colors[Math.Min(i, colors.Length - 1)])!,
                Value = values[i]
            });

        foreach (var threshold in _observedThresholds) threshold.PropertyChanged -= OnThresholdChanged;
        _observedThresholds.Clear();
        foreach (var threshold in Thresholds)
        {
            threshold.PropertyChanged += OnThresholdChanged;
            _observedThresholds.Add(threshold);
        }

        NotifyPrimaryProfileState();
    }

    private async Task LoadWheelProfileAsync(WheelDefinition wheel, bool applyToService)
    {
        try
        {
            if (!applyToService && _loadedProfileWheelId == wheel.Id) return;
            var profile = await _service.LoadWheelProfileAsync(wheel);
            _loadedProfileWheelId = wheel.Id;
            ProfileMode = profile.Mode;
            FirstLedPercent = profile.FirstLedPercent;
            RedlinePercent = profile.RedlinePercent;
            BlinkAtRedline = profile.BlinkAtRedline;
            ConfigureThresholds(wheel, profile.AdvancedThresholds);
            if (applyToService)
                await _service.UpdateSettingsAsync(_service.Settings with
                {
                    ProfileMode = profile.Mode, FirstLedPercent = profile.FirstLedPercent,
                    RedlinePercent = profile.RedlinePercent, BlinkAtRedline = profile.BlinkAtRedline,
                    AdvancedThresholds = profile.AdvancedThresholds
                });
        }
        catch (Exception ex)
        {
            _setStatus($"Could not load wheel profile: {ex.Message}");
        }
    }
}
