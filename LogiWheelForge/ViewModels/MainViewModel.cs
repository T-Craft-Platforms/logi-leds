using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Application = System.Windows.Application;

namespace LogiWheelForge.ViewModels;

/// <summary>Window-level state and composition root for the three page viewmodels.</summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsDraft _draft = new();
    private readonly HashSet<ThresholdViewModel> _observedThresholds = [];
    private readonly LedIndicatorService _service;
    private readonly InputMapperService _mapperService;
    private readonly WheelSelectionService _wheelSelection;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private WheelDefinition? _activeDefinition;
    private bool _isWheelConnected, _isTelemetryConnected, _isRaceOn, _settingsLoaded;
    private float _maximumRpm;
    private int _selectedTab;
    private bool _isLedModuleEnabled = true, _isInputMapperModuleEnabled = true;
    private CancellationTokenSource? _settingsSaveDelay;
    private ReadinessState _state;
    private string _statusMessage = "Starting LogiWheel Forge", _wheelName = "No Logitech wheel", _telemetryFormat = "—";

    public MainViewModel(LedIndicatorService service, InputMapperService mapperService,
        WheelSelectionService wheelSelection)
    {
        _service = service;
        _mapperService = mapperService;
        _wheelSelection = wheelSelection;
        _wheelSelection.ActiveWheelChanged += OnSelectedWheelChanged;
        Dashboard = new DashboardViewModel(service, mapperService, wheelSelection);
        RpmProfile = new RpmProfileViewModel(service, _draft, SetStatusMessage);
        Settings = new SettingsViewModel(service, _draft, SetStatusMessage);
        Mapper = new InputMapperViewModel(mapperService);
        Settings.TelemetryChanged += OnTelemetryChanged;
        _draft.PropertyChanged += OnDraftPropertyChanged;
        RpmProfile.Thresholds.CollectionChanged += OnThresholdsCollectionChanged;
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public DashboardViewModel Dashboard { get; }
    public RpmProfileViewModel RpmProfile { get; }
    public SettingsViewModel Settings { get; }
    public InputMapperViewModel Mapper { get; }
    public ICommand ExitCommand { get; }
    public bool MinimizeToTray => _draft.CloseToTray;
    public bool CloseToTray => _draft.CloseToTray;
    public bool IsRunning { get; private set; }

    public bool IsLedModuleEnabled
    {
        get => _isLedModuleEnabled;
        set
        {
            if (!SetField(ref _isLedModuleEnabled, value) || !_settingsLoaded) return;
            ScheduleSettingsSave();
            _ = SetLedModuleEnabledAsync(value);
        }
    }

    public bool IsInputMapperModuleEnabled
    {
        get => _isInputMapperModuleEnabled;
        set
        {
            if (!SetField(ref _isInputMapperModuleEnabled, value) || !_settingsLoaded) return;
            ScheduleSettingsSave();
            SetInputMapperModuleEnabled(value);
        }
    }

    public LedProfileSettings CurrentSettings => _service.Settings;

    public int SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (!SetField(ref _selectedTab, value)) return;
            OnPropertyChanged(nameof(SelectedPage));
        }
    }

    public object SelectedPage => SelectedTab switch
    {
        1 => RpmProfile,
        2 => Mapper,
        3 => Settings,
        _ => Dashboard
    };

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public string WheelName
    {
        get => _wheelName;
        private set
        {
            if (SetField(ref _wheelName, value)) OnPropertyChanged(nameof(WheelStatusText));
        }
    }

    public string TelemetryFormat
    {
        get => _telemetryFormat;
        private set
        {
            if (SetField(ref _telemetryFormat, value)) OnPropertyChanged(nameof(TelemetryStatusText));
        }
    }

    public ReadinessState State
    {
        get => _state;
        private set
        {
            if (SetField(ref _state, value)) NotifyStatusProperties();
        }
    }

    public string AppControlStatus => IsRunning ? "Connected" : "Disconnected";
    public string WheelStatus => !_isWheelConnected ? "Disconnected" : !IsRunning ? "Standby" : "Connected";

    public string TelemetryStatus => !_isTelemetryConnected
        ? "Disconnected"
        : _isRaceOn && _maximumRpm > 0
            ? "Connected"
            : "Standby";

    public string AppControlStatusText => !IsRunning ? "LED Indicator is stopped" :
        State == ReadinessState.Driving ? "RPM lights are active" : "LED Indicator is ready";

    public string WheelStatusText => _isWheelConnected ? $"{WheelName} LED interface connected" :
        "No LED-capable wheel detected";

    public string TelemetryStatusText => _isTelemetryConnected && _isRaceOn && _maximumRpm > 0
        ? $"{TelemetryFormat} drive data active"
        : _isTelemetryConnected
            ? "Telemetry connected, no drive data"
            : State == ReadinessState.TelemetryStale
                ? "Telemetry paused"
                : State == ReadinessState.WaitingForTelemetry
                    ? "Listening for telemetry"
                    : "No telemetry signal";

    public async ValueTask DisposeAsync()
    {
        var saveOnDispose = _settingsLoaded;
        _settingsLoaded = false;
        _settingsSaveDelay?.Cancel();
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        Settings.TelemetryChanged -= OnTelemetryChanged;
        RpmProfile.Thresholds.CollectionChanged -= OnThresholdsCollectionChanged;
        foreach (var threshold in _observedThresholds) threshold.PropertyChanged -= OnThresholdValueChanged;
        _observedThresholds.Clear();
        await _settingsSaveGate.WaitAsync();
        try
        {
            if (saveOnDispose) await SaveSettingsAsync();
        }
        finally
        {
            _settingsSaveGate.Release();
            _settingsSaveGate.Dispose();
            _settingsSaveDelay?.Dispose();
        }

        _service.SnapshotChanged -= OnSnapshotChanged;
        Dashboard.Dispose();
        RpmProfile.Dispose();
        Settings.Dispose();
        Mapper.Dispose();
        _mapperService.Dispose();
        _wheelSelection.ActiveWheelChanged -= OnSelectedWheelChanged;
        _wheelSelection.Dispose();
        await _service.DisposeAsync();
    }

    public event EventHandler? ExitRequested;

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsDraft.CloseToTray))
        {
            OnPropertyChanged(nameof(CloseToTray));
            OnPropertyChanged(nameof(MinimizeToTray));
        }

        ScheduleSettingsSave();
    }

    private void OnThresholdsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var threshold in _observedThresholds) threshold.PropertyChanged -= OnThresholdValueChanged;
        _observedThresholds.Clear();
        foreach (var threshold in RpmProfile.Thresholds)
        {
            threshold.PropertyChanged += OnThresholdValueChanged;
            _observedThresholds.Add(threshold);
        }

        ScheduleSettingsSave();
    }

    private void OnThresholdValueChanged(object? sender, PropertyChangedEventArgs e)
    {
        ScheduleSettingsSave();
    }

    private void OnTelemetryChanged(object? sender, EventArgs e) => ScheduleSettingsSave();

    private void ScheduleSettingsSave()
    {
        if (!_settingsLoaded) return;
        var next = new CancellationTokenSource();
        var previous = _settingsSaveDelay;
        _settingsSaveDelay = next;
        previous?.Cancel();
        _ = SaveSettingsAfterDelayAsync(next);
    }

    private async Task SaveSettingsAfterDelayAsync(CancellationTokenSource delay)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), delay.Token);
            if (!ReferenceEquals(_settingsSaveDelay, delay)) return;
            await _settingsSaveGate.WaitAsync(delay.Token);
            try
            {
                await SaveSettingsAsync();
            }
            finally
            {
                _settingsSaveGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_settingsSaveDelay, delay)) _settingsSaveDelay = null;
            delay.Dispose();
        }
    }

    private void UpdateSettingsDraftAutoSaveSubscriptions()
    {
        foreach (var threshold in _observedThresholds) threshold.PropertyChanged -= OnThresholdValueChanged;
        _observedThresholds.Clear();
        foreach (var threshold in RpmProfile.Thresholds)
        {
            threshold.PropertyChanged += OnThresholdValueChanged;
            _observedThresholds.Add(threshold);
        }
    }

    public async Task InitializeAsync(nint windowHandle)
    {
        var settings = await _service.LoadSettingsAsync();
        _draft.FirstLedPercent = settings.FirstLedPercent;
        _draft.RedlinePercent = settings.RedlinePercent;
        _draft.BlinkAtRedline = settings.BlinkAtRedline;
        _draft.ProfileMode = settings.ProfileMode;
        _draft.LearnPerCarShift = settings.LearnPerCarShift;
        IsLedModuleEnabled = settings.LedModuleEnabled;
        IsInputMapperModuleEnabled = settings.InputMapperModuleEnabled;
        _draft.CloseToTray = settings.CloseToTray;
        _draft.UsePointerCursors = settings.UsePointerCursors;
        ThemeService.UsePointerCursors = settings.UsePointerCursors;
        _draft.Theme = settings.Theme;
        Settings.Initialize(settings, _service.Wheels.Where(wheel => !wheel.IsPedalSet).ToArray());
        RpmProfile.Initialize(_service.Wheels.FirstOrDefault(x => x.HasLedOutput &&
                x.Id == settings.PreferredWheelId),
            settings.AdvancedThresholds);
        _service.InitializeWindow(windowHandle);
        _wheelSelection.SetPreferredWheel(settings.PreferredWheelId);
        _wheelSelection.Start();
        await _mapperService.InitializeAsync(windowHandle, _wheelSelection.ActiveWheelId);
        Mapper.Initialize();
        _settingsLoaded = true;
        UpdateSettingsDraftAutoSaveSubscriptions();
        if (IsLedModuleEnabled) await StartServiceWithErrorHandlingAsync();
        if (IsInputMapperModuleEnabled)
            try { _mapperService.Start(); }
            catch (Exception ex) { StatusMessage = $"Could not start Input Mapper: {ex.Message}"; }
    }

    public async Task SaveWindowPlacementAsync(double width, double height, double left, double top, bool maximized)
    {
        var settings = _service.Settings with
        {
            WindowWidth = Math.Max(900, width), WindowHeight = Math.Max(620, height),
            WindowLeft = double.IsFinite(left) ? left : null, WindowTop = double.IsFinite(top) ? top : null,
            WindowMaximized = maximized
        };
        try
        {
            await _service.UpdateSettingsAsync(settings);
        }
        catch
        {
            /* Window placement must never block shutdown. */
        }
    }

    private async Task SaveSettingsAsync()
    {
        var forza = Settings.TelemetryGames.FirstOrDefault(game => game.Game == TelemetryGame.Forza);
        var settings = _service.Settings with
        {
            BindAddress = forza?.BindAddress ?? _service.Settings.BindAddress,
            Port = forza?.Port ?? _service.Settings.Port,
            TelemetryWatch = Settings.WatchMode, TelemetryGames = Settings.TelemetryGames.ToArray(),
            FirstLedPercent = _draft.FirstLedPercent, RedlinePercent = _draft.RedlinePercent,
            BlinkAtRedline = _draft.BlinkAtRedline,
            LedModuleEnabled = _isLedModuleEnabled, InputMapperModuleEnabled = _isInputMapperModuleEnabled,
            MinimizeToTray = _draft.CloseToTray, CloseToTray = _draft.CloseToTray, ReadyAnimation = true,
            Theme = _draft.Theme, UsePointerCursors = _draft.UsePointerCursors,
            ProfileMode = _draft.ProfileMode, LearnPerCarShift = _draft.LearnPerCarShift,
            GameTitle = "Auto", PreferredWheelId = _draft.SelectedWheel?.Id,
            AdvancedThresholds = RpmProfile.Thresholds.Select(x => x.Value).ToArray()
        };
        if (!settings.TryValidate(out var error))
        {
            StatusMessage = error;
            return;
        }

        try
        {
            await _service.UpdateSettingsAsync(settings);
            _wheelSelection.SetPreferredWheel(settings.PreferredWheelId);
            _service.SetSelectedWheel(_wheelSelection.ActiveWheelId ?? settings.PreferredWheelId);
            var wheel = _activeDefinition ?? _service.Wheels.FirstOrDefault(x => x.HasLedOutput &&
                x.Id == _draft.SelectedWheel?.Id);
            if (wheel is not null)
                await _service.SaveWheelProfileAsync(new WheelProfile
                {
                    WheelId = wheel.Id, Mode = _draft.ProfileMode, FirstLedPercent = _draft.FirstLedPercent,
                    RedlinePercent = _draft.RedlinePercent, BlinkAtRedline = _draft.BlinkAtRedline,
                    AdvancedThresholds = settings.AdvancedThresholds
                }, wheel.ControlGroupCount);
            OnPropertyChanged(nameof(CurrentSettings));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not apply settings: {ex.Message}";
        }
    }

    private async Task StartServiceWithErrorHandlingAsync()
    {
        try
        {
            await _service.StartAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not start: {ex.Message}";
        }
    }

    private async Task SetLedModuleEnabledAsync(bool enabled)
    {
        try
        {
            if (enabled) await _service.StartAsync();
            else await _service.StopAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not change LED Indicator state: {ex.Message}";
        }
    }

    private void SetInputMapperModuleEnabled(bool enabled)
    {
        try
        {
            if (enabled) _mapperService.Start();
            else _mapperService.Stop();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not change Input Mapper state: {ex.Message}";
        }
    }

    private void SetStatusMessage(string value)
    {
        StatusMessage = value;
    }

    private void OnSelectedWheelChanged(object? sender, string? wheelId)
    {
        _service.SetSelectedWheel(wheelId ?? _service.Settings.PreferredWheelId);
        _mapperService.SetPreferredWheel(wheelId ?? _service.Settings.PreferredWheelId);
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot);
        else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        IsRunning = snapshot.IsRunning;
        _isWheelConnected = snapshot.IsWheelConnected;
        _isTelemetryConnected = snapshot.IsTelemetryConnected;
        _isRaceOn = snapshot.IsRaceOn;
        _maximumRpm = snapshot.MaximumRpm;
        if (snapshot.Wheel is not null) _activeDefinition = snapshot.Wheel;
        State = snapshot.State;
        StatusMessage = snapshot.StatusMessage;
        WheelName = snapshot.WheelName;
        TelemetryFormat = snapshot.TelemetryFormat;
        OnPropertyChanged(nameof(IsRunning));
        NotifyStatusProperties();
    }

    private void NotifyStatusProperties()
    {
        OnPropertyChanged(nameof(AppControlStatus));
        OnPropertyChanged(nameof(WheelStatus));
        OnPropertyChanged(nameof(TelemetryStatus));
        OnPropertyChanged(nameof(AppControlStatusText));
        OnPropertyChanged(nameof(WheelStatusText));
        OnPropertyChanged(nameof(TelemetryStatusText));
    }
}
