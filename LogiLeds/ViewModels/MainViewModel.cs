using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;
using Application = System.Windows.Application;

namespace LogiLeds.ViewModels;

/// <summary>Window-level state and composition root for the three page viewmodels.</summary>
public sealed class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly SettingsDraft _draft = new();
    private readonly HashSet<ThresholdViewModel> _observedThresholds = [];
    private readonly LedApplicationService _service;
    private readonly SemaphoreSlim _settingsSaveGate = new(1, 1);
    private WheelDefinition? _activeDefinition;
    private bool _isWheelConnected, _isTelemetryConnected, _settingsLoaded;
    private float _maximumRpm;
    private int _selectedTab;
    private CancellationTokenSource? _settingsSaveDelay;
    private ReadinessState _state;
    private string _statusMessage = "Starting LogiLeds", _wheelName = "No Logitech wheel", _telemetryFormat = "—";

    public MainViewModel(LedApplicationService service)
    {
        _service = service;
        Dashboard = new DashboardViewModel(service, SetStatusMessage);
        RpmProfile = new RpmProfileViewModel(service, _draft, SetStatusMessage);
        Settings = new SettingsViewModel(service, _draft, SetStatusMessage);
        _draft.PropertyChanged += OnDraftPropertyChanged;
        RpmProfile.Thresholds.CollectionChanged += OnThresholdsCollectionChanged;
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public DashboardViewModel Dashboard { get; }
    public RpmProfileViewModel RpmProfile { get; }
    public SettingsViewModel Settings { get; }
    public ICommand ExitCommand { get; }
    public bool MinimizeToTray => _draft.CloseToTray;
    public bool CloseToTray => _draft.CloseToTray;
    public bool IsRunning { get; private set; }

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
        2 => Settings,
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

    public string TelemetryStatus => !IsRunning
        ? "Disconnected"
        : _maximumRpm > 0 && _isTelemetryConnected
            ? "Connected"
            : _isTelemetryConnected || State is ReadinessState.WaitingForTelemetry or ReadinessState.TelemetryStale
                or ReadinessState.Ready
                ? "Standby"
                : "Disconnected";

    public string AppControlStatusText => !IsRunning ? "Control is stopped" :
        State == ReadinessState.Driving ? "RPM control is active" : "Control is ready";

    public string WheelStatusText => _isWheelConnected ? $"{WheelName} connected" : "No wheel detected";

    public string TelemetryStatusText => _maximumRpm > 0 && _isTelemetryConnected
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
        _draft.BindAddress = settings.BindAddress;
        _draft.Port = settings.Port.ToString(CultureInfo.InvariantCulture);
        _draft.FirstLedPercent = settings.FirstLedPercent;
        _draft.RedlinePercent = settings.RedlinePercent;
        _draft.BlinkAtRedline = settings.BlinkAtRedline;
        _draft.ProfileMode = settings.ProfileMode;
        _draft.LearnPerCarShift = settings.LearnPerCarShift;
        _draft.AutoStartControl = settings.AutoStartControl;
        _draft.CloseToTray = settings.CloseToTray;
        _draft.Theme = settings.Theme;
        Settings.Initialize(settings, _service.Wheels);
        RpmProfile.Initialize(_service.Wheels.FirstOrDefault(x => x.Id == settings.PreferredWheelId),
            settings.AdvancedThresholds);
        _service.InitializeWindow(windowHandle);
        _settingsLoaded = true;
        UpdateSettingsDraftAutoSaveSubscriptions();
        if (settings.AutoStartControl) await StartServiceWithErrorHandlingAsync();
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
        if (!int.TryParse(_draft.Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            StatusMessage = "Enter a valid UDP port.";
            return;
        }

        var settings = _service.Settings with
        {
            BindAddress = _draft.BindAddress.Trim(), Port = port,
            FirstLedPercent = _draft.FirstLedPercent, RedlinePercent = _draft.RedlinePercent,
            BlinkAtRedline = _draft.BlinkAtRedline, AutoStartControl = _draft.AutoStartControl,
            MinimizeToTray = _draft.CloseToTray, CloseToTray = _draft.CloseToTray, ReadyAnimation = true,
            Theme = _draft.Theme, ProfileMode = _draft.ProfileMode, LearnPerCarShift = _draft.LearnPerCarShift,
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
            var wheel = _activeDefinition ?? _service.Wheels.FirstOrDefault(x => x.Id == _draft.SelectedWheel?.Id);
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

    private void SetStatusMessage(string value)
    {
        StatusMessage = value;
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