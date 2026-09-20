using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Media = System.Windows.Media;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;

namespace LogiLeds.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly LedApplicationService _service;
    private readonly AsyncRelayCommand _startStopCommand;
    private readonly AsyncRelayCommand _restartCommand;
    private readonly AsyncRelayCommand _saveCommand;
    private readonly AsyncRelayCommand _testCommand;
    private readonly AsyncRelayCommand _readyAnimationCommand;
    private readonly AsyncRelayCommand _resetLearningCommand;
    private int _selectedTab;
    private string _bindAddress = LedProfileSettings.DefaultBindAddress;
    private string _port = LedProfileSettings.DefaultPort.ToString(CultureInfo.InvariantCulture);
    private double _firstLedPercent = LedProfileSettings.DefaultFirstLedPercent;
    private double _redlinePercent = LedProfileSettings.DefaultRedlinePercent;
    private bool _blinkAtRedline = true, _autoStart = true, _minimizeToTray = true, _closeToTray = true, _readyAnimation = true;
    private AppTheme _theme = AppTheme.System;
    private RpmProfileMode _profileMode;
    private WheelOption? _selectedWheel;
    private string _gameTitle = "Auto";
    private string _statusMessage = "Starting LogiLeds";
    private ReadinessState _state;
    private string _wheelName = "No Logitech wheel";
    private string _telemetryFormat = "—";
    private float _currentRpm, _maximumRpm;
    private bool _isRunning, _wheelConnected, _telemetryConnected, _isFlashing;
    private WheelDefinition? _activeDefinition;
    private double? _learnedRedlinePercent;
    private string? _loadedProfileWheelId;

    public MainViewModel(LedApplicationService service)
    {
        _service = service;
        _service.SnapshotChanged += OnSnapshotChanged;
        _startStopCommand = new AsyncRelayCommand(ToggleRunningAsync);
        _restartCommand = new AsyncRelayCommand(RestartControlAsync);
        _saveCommand = new AsyncRelayCommand(SaveSettingsAsync);
        _testCommand = new AsyncRelayCommand(() => _service.TestLedsAsync(), () => _wheelConnected,
            ex => StatusMessage = $"LED test failed: {ex.Message}");
        _readyAnimationCommand = new AsyncRelayCommand(() => _service.PlayReadyAnimationAsync(), () => _wheelConnected,
            ex => StatusMessage = $"Ready animation failed: {ex.Message}");
        _resetLearningCommand = new AsyncRelayCommand(() => _service.ResetLearningAsync());
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        ResetProfileCommand = new RelayCommand(ResetProfile);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ExitRequested;
    public ICommand StartStopCommand => _startStopCommand;
    public ICommand RestartCommand => _restartCommand;
    public ICommand SaveCommand => _saveCommand;
    public ICommand TestCommand => _testCommand;
    public ICommand ReadyAnimationCommand => _readyAnimationCommand;
    public ICommand ResetLearningCommand => _resetLearningCommand;
    public ICommand ExitCommand { get; }
    public ICommand ResetProfileCommand { get; }
    public ObservableCollection<LedIndicatorViewModel> Leds { get; } = [];
    public ObservableCollection<ThresholdViewModel> Thresholds { get; } = [];
    public ObservableCollection<WheelOption> WheelOptions { get; } = [];
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();

    public int SelectedTab { get => _selectedTab; set => SetField(ref _selectedTab, value); }
    public string BindAddress { get => _bindAddress; set => SetField(ref _bindAddress, value); }
    public string Port { get => _port; set => SetField(ref _port, value); }
    public double FirstLedPercent { get => _firstLedPercent; set => SetField(ref _firstLedPercent, Math.Round(value, 1)); }
    public double RedlinePercent { get => _redlinePercent; set => SetField(ref _redlinePercent, Math.Round(value, 1)); }
    public bool BlinkAtRedline { get => _blinkAtRedline; set => SetField(ref _blinkAtRedline, value); }
    public bool AutoStartControl { get => _autoStart; set => SetField(ref _autoStart, value); }
    public bool MinimizeToTray { get => _minimizeToTray; set => SetField(ref _minimizeToTray, value); }
    public bool CloseToTray { get => _closeToTray; set => SetField(ref _closeToTray, value); }
    public bool ReadyAnimation { get => _readyAnimation; set => SetField(ref _readyAnimation, value); }
    public AppTheme Theme { get => _theme; set { if (SetField(ref _theme, value)) ThemeService.Apply(value); } }
    public RpmProfileMode ProfileMode { get => _profileMode; set { if (SetField(ref _profileMode, value)) { OnPropertyChanged(nameof(IsEasyMode)); OnPropertyChanged(nameof(IsAdvancedMode)); } } }
    public bool IsEasyMode { get => ProfileMode == RpmProfileMode.Easy; set { if (value) ProfileMode = RpmProfileMode.Easy; } }
    public bool IsAdvancedMode { get => ProfileMode == RpmProfileMode.Advanced; set { if (value) ProfileMode = RpmProfileMode.Advanced; } }
    public WheelOption? SelectedWheel { get => _selectedWheel; set { if (SetField(ref _selectedWheel, value)) { ConfigurePreview(value?.Id); var wheel = _service.Wheels.FirstOrDefault(x => x.Id == value?.Id); if (wheel is not null) _ = LoadWheelProfileAsync(wheel, false); } } }
    public string GameTitle { get => _gameTitle; set => SetField(ref _gameTitle, value); }
    public string StatusMessage { get => _statusMessage; private set => SetField(ref _statusMessage, value); }
    public ReadinessState State { get => _state; private set { if (SetField(ref _state, value)) { OnPropertyChanged(nameof(StateTitle)); OnPropertyChanged(nameof(StateDetail)); OnPropertyChanged(nameof(StateGlyph)); } } }
    public string WheelName { get => _wheelName; private set => SetField(ref _wheelName, value); }
    public string TelemetryFormat { get => _telemetryFormat; private set => SetField(ref _telemetryFormat, value); }
    public float CurrentRpm { get => _currentRpm; private set { if (SetField(ref _currentRpm, value)) NotifyRpm(); } }
    public float MaximumRpm { get => _maximumRpm; private set { if (SetField(ref _maximumRpm, value)) NotifyRpm(); } }
    public bool IsRunning { get => _isRunning; private set { if (SetField(ref _isRunning, value)) OnPropertyChanged(nameof(StartStopText)); } }
    public bool IsWheelConnected { get => _wheelConnected; private set => SetField(ref _wheelConnected, value); }
    public bool IsTelemetryConnected { get => _telemetryConnected; private set => SetField(ref _telemetryConnected, value); }
    public bool IsFlashing { get => _isFlashing; private set { if (SetField(ref _isFlashing, value)) OnPropertyChanged(nameof(ShiftState)); } }
    public string StartStopText => IsRunning ? "Stop control" : "Start control";
    public string RpmDisplay => MaximumRpm > 0 ? $"{CurrentRpm:N0}" : "WAITING";
    public string MaximumRpmDisplay => MaximumRpm > 0 ? $"/ {MaximumRpm:N0} RPM" : "for telemetry data";
    public string RpmPercentDisplay => MaximumRpm > 0 ? $"{CurrentRpm / MaximumRpm:P0}" : "—";
    public string ShiftState => IsFlashing ? "SHIFT NOW" : MaximumRpm > 0 ? "LIVE RPM" : "WAITING FOR DATA";
    public string StateTitle => State switch { ReadinessState.Driving => "Driving", ReadinessState.Ready => "Ready", ReadinessState.SearchingForWheel => "Wheel not found", ReadinessState.WaitingForTelemetry => "Waiting for telemetry", ReadinessState.TelemetryStale => "Telemetry paused", ReadinessState.NeedsAttention => "Needs attention", _ => "Control paused" };
    public string StateDetail => StatusMessage;
    public string StateGlyph => State switch { ReadinessState.Driving => "●", ReadinessState.Ready => "✓", ReadinessState.NeedsAttention => "!", ReadinessState.SearchingForWheel => "○", _ => "◌" };
    public string WheelVerification => _activeDefinition is null ? "Waiting for detection" : _activeDefinition.HardwareVerified ? "Hardware verified" : "Protocol compatible — hardware validation pending";
    public string DefinitionDiagnostics => _service.WheelDefinitionDiagnostics.Count == 0 ? "All wheel definitions loaded" : string.Join(Environment.NewLine, _service.WheelDefinitionDiagnostics);
    public string CalibrationStatus => _learnedRedlinePercent is double value ? $"Learned shift point: {value:0.0}%" : "Learning per-car shift point — using profile default";
    public LedProfileSettings CurrentSettings => _service.Settings;

    public async Task InitializeAsync(nint windowHandle)
    {
        var settings = await _service.LoadSettingsAsync();
        BindAddress = settings.BindAddress; Port = settings.Port.ToString(CultureInfo.InvariantCulture);
        FirstLedPercent = settings.FirstLedPercent; RedlinePercent = settings.RedlinePercent;
        BlinkAtRedline = settings.BlinkAtRedline; AutoStartControl = settings.AutoStartControl;
        // These are intentionally product defaults rather than user-facing
        // switches: minimize-to-tray and the ready animation are always on.
        MinimizeToTray = true; CloseToTray = settings.CloseToTray; ReadyAnimation = true;
        ProfileMode = settings.ProfileMode; GameTitle = "Auto"; Theme = settings.Theme;
        WheelOptions.Add(new WheelOption(null, "Auto-detect (recommended)"));
        foreach (var wheel in _service.Wheels) WheelOptions.Add(new WheelOption(wheel.Id, wheel.DisplayName));
        SelectedWheel = WheelOptions.FirstOrDefault(x => x.Id == settings.PreferredWheelId) ?? WheelOptions[0];
        ConfigureThresholds(_service.Wheels.FirstOrDefault(x => x.Id == settings.PreferredWheelId), settings.AdvancedThresholds);
        _service.InitializeWindow(windowHandle);
        if (settings.AutoStartControl) await StartServiceWithErrorHandlingAsync();
    }

    private async Task ToggleRunningAsync() { if (_service.IsRunning) await _service.StopAsync(); else await StartServiceWithErrorHandlingAsync(); }
    private async Task RestartControlAsync()
    {
        try
        {
            if (_service.IsRunning) await _service.StopAsync();
            await _service.StartAsync();
        }
        catch (Exception ex) { StatusMessage = $"Could not restart: {ex.Message}"; }
    }
    private async Task StartServiceWithErrorHandlingAsync() { try { await _service.StartAsync(); } catch (Exception ex) { StatusMessage = $"Could not start: {ex.Message}"; } }

    private async Task SaveSettingsAsync()
    {
        if (!int.TryParse(Port, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port)) { StatusMessage = "Enter a valid UDP port."; return; }
        var settings = _service.Settings with
        {
            BindAddress = BindAddress.Trim(), Port = port, FirstLedPercent = FirstLedPercent, RedlinePercent = RedlinePercent,
            BlinkAtRedline = BlinkAtRedline, AutoStartControl = AutoStartControl, MinimizeToTray = true,
            CloseToTray = CloseToTray, ReadyAnimation = true, Theme = Theme, ProfileMode = ProfileMode,
            GameTitle = "Auto", PreferredWheelId = SelectedWheel?.Id, AdvancedThresholds = Thresholds.Select(x => x.Value).ToArray()
        };
        if (!settings.TryValidate(out var error)) { StatusMessage = error; return; }
        try
        {
            await _service.UpdateSettingsAsync(settings);
            var wheel = _activeDefinition ?? _service.Wheels.FirstOrDefault(x => x.Id == SelectedWheel?.Id);
            if (wheel is not null)
                await _service.SaveWheelProfileAsync(new WheelProfile { WheelId = wheel.Id, Mode = ProfileMode, FirstLedPercent = FirstLedPercent, RedlinePercent = RedlinePercent, BlinkAtRedline = BlinkAtRedline, AdvancedThresholds = Thresholds.Select(x => x.Value).ToArray() }, wheel.ControlGroupCount);
        }
        catch (Exception ex) { StatusMessage = $"Could not apply settings: {ex.Message}"; }
    }

    public async Task SaveWindowPlacementAsync(double width, double height, double left, double top, bool maximized)
    {
        var settings = _service.Settings with
        {
            WindowWidth = Math.Max(900, width), WindowHeight = Math.Max(620, height),
            WindowLeft = double.IsFinite(left) ? left : null, WindowTop = double.IsFinite(top) ? top : null,
            WindowMaximized = maximized
        };
        try { await _service.UpdateSettingsAsync(settings); } catch { /* Window placement must never block shutdown. */ }
    }

    private void ResetProfile()
    {
        FirstLedPercent = _activeDefinition?.DefaultFirstPercent ?? LedProfileSettings.DefaultFirstLedPercent;
        RedlinePercent = _activeDefinition?.DefaultRedlinePercent ?? LedProfileSettings.DefaultRedlinePercent;
        BlinkAtRedline = true;
        ConfigureThresholds(_activeDefinition, []);
    }

    private void ConfigurePreview(string? id)
    {
        var definition = _service.Wheels.FirstOrDefault(x => x.Id == id) ?? _activeDefinition ?? _service.Wheels.FirstOrDefault();
        if (definition is not null) BuildLeds(definition, 0, false);
    }

    private void ConfigureThresholds(WheelDefinition? definition, IReadOnlyList<double> saved)
    {
        var wheel = definition ?? _service.Wheels.FirstOrDefault();
        var count = wheel?.ControlGroupCount ?? 5;
        var values = saved.Count == count ? saved.ToArray() : LedMath.BuildRecommendedThresholds(count);
        var colors = wheel?.Colors ?? ["#38D982", "#6EE65A", "#F0D84A", "#FFAA3B", "#FF5265"];
        Thresholds.Clear();
        for (var i = 0; i < count; i++) Thresholds.Add(new ThresholdViewModel { Label = i == count - 1 ? "Red light" : $"LED group {i + 1}", Color = (Media.Brush)new Media.BrushConverter().ConvertFromString(colors[Math.Min(i, colors.Length - 1)])!, Value = values[i] });
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot); else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        IsRunning = snapshot.IsRunning; IsTelemetryConnected = snapshot.IsTelemetryConnected; IsWheelConnected = snapshot.IsWheelConnected;
        State = snapshot.State; StatusMessage = snapshot.StatusMessage; WheelName = snapshot.WheelName; TelemetryFormat = snapshot.TelemetryFormat;
        CurrentRpm = snapshot.CurrentRpm; MaximumRpm = snapshot.MaximumRpm; IsFlashing = snapshot.IsFlashing;
        if (_learnedRedlinePercent != snapshot.LearnedRedlinePercent) { _learnedRedlinePercent = snapshot.LearnedRedlinePercent; OnPropertyChanged(nameof(CalibrationStatus)); }
        if (snapshot.Wheel is not null && snapshot.Wheel.Id != _activeDefinition?.Id)
        {
            _activeDefinition = snapshot.Wheel;
            _ = LoadWheelProfileAsync(snapshot.Wheel, true);
            OnPropertyChanged(nameof(WheelVerification));
        }
        if (snapshot.Wheel is not null) BuildLeds(snapshot.Wheel, snapshot.IlluminatedLedCount, snapshot.IsFlashing);
        else foreach (var led in Leds) { led.IsLit = false; led.IsBlinking = false; }
        _testCommand.RaiseCanExecuteChanged(); _readyAnimationCommand.RaiseCanExecuteChanged();
    }

    private async Task LoadWheelProfileAsync(WheelDefinition wheel, bool applyToService)
    {
        try
        {
            if (!applyToService && _loadedProfileWheelId == wheel.Id) return;
            var profile = await _service.LoadWheelProfileAsync(wheel);
            _loadedProfileWheelId = wheel.Id;
            ProfileMode = profile.Mode; FirstLedPercent = profile.FirstLedPercent; RedlinePercent = profile.RedlinePercent;
            BlinkAtRedline = profile.BlinkAtRedline; ConfigureThresholds(wheel, profile.AdvancedThresholds);
            if (applyToService)
                await _service.UpdateSettingsAsync(_service.Settings with
                {
                    ProfileMode = profile.Mode, FirstLedPercent = profile.FirstLedPercent,
                    RedlinePercent = profile.RedlinePercent, BlinkAtRedline = profile.BlinkAtRedline,
                    AdvancedThresholds = profile.AdvancedThresholds
                });
        }
        catch (Exception ex) { StatusMessage = $"Could not load wheel profile: {ex.Message}"; }
    }

    private void BuildLeds(WheelDefinition definition, int litGroups, bool flashing)
    {
        if (Leds.Count != definition.PhysicalLedCount)
        {
            Leds.Clear();
            for (var i = 0; i < definition.PhysicalLedCount; i++)
            {
                var group = definition.Direction == "outside-in" ? Math.Min(i, definition.PhysicalLedCount - 1 - i) + 1 : Math.Min(i + 1, definition.ControlGroupCount);
                Leds.Add(new LedIndicatorViewModel(definition.Colors[i], group));
            }
        }
        // The service already sends the exact visible group count for the
        // current blink phase. Do not start a second UI-only storyboard: it
        // can drift from the wheel and leave the preview flashing forever.
        foreach (var led in Leds) { led.IsLit = led.Group <= litGroups; led.IsBlinking = false; }
    }

    private void NotifyRpm() { OnPropertyChanged(nameof(RpmDisplay)); OnPropertyChanged(nameof(MaximumRpmDisplay)); OnPropertyChanged(nameof(RpmPercentDisplay)); OnPropertyChanged(nameof(ShiftState)); }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return false; field = value; OnPropertyChanged(name); return true; }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public async ValueTask DisposeAsync() { _service.SnapshotChanged -= OnSnapshotChanged; await _service.DisposeAsync(); }
}
