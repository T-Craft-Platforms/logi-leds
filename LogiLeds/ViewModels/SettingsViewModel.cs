using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;
using Application = System.Windows.Application;

namespace LogiLeds.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsDraft _draft;
    private readonly LedApplicationService _service;
    private readonly Action<string> _setStatus;
    private readonly AsyncRelayCommand _startStopCommand;
    private readonly AsyncRelayCommand _testCommand;
    private WheelDefinition? _activeDefinition;
    private bool _isRunning, _isWheelConnected;

    public SettingsViewModel(LedApplicationService service, SettingsDraft draft, Action<string> setStatus)
    {
        _service = service;
        _draft = draft;
        _setStatus = setStatus;
        _startStopCommand = new AsyncRelayCommand(ToggleRunningAsync,
            onError: ex => _setStatus($"Could not change control state: {ex.Message}"));
        _testCommand = new AsyncRelayCommand(() => _service.TestLedsAsync(), () => _isWheelConnected,
            ex => _setStatus($"LED test failed: {ex.Message}"));
        _draft.PropertyChanged += OnDraftPropertyChanged;
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<WheelOption> WheelOptions { get; } = [];
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public ICommand TestCommand => _testCommand;
    public ICommand StartStopCommand => _startStopCommand;

    public string BindAddress
    {
        get => _draft.BindAddress;
        set => _draft.BindAddress = value;
    }

    public string Port
    {
        get => _draft.Port;
        set => _draft.Port = value;
    }

    public bool AutoStartControl
    {
        get => _draft.AutoStartControl;
        set => _draft.AutoStartControl = value;
    }

    public bool CloseToTray
    {
        get => _draft.CloseToTray;
        set => _draft.CloseToTray = value;
    }

    public AppTheme Theme
    {
        get => _draft.Theme;
        set => _draft.Theme = value;
    }

    public WheelOption? SelectedWheel
    {
        get => _draft.SelectedWheel;
        set => _draft.SelectedWheel = value;
    }

    public string StartStopText => _isRunning ? "Stop control" : "Start control";

    public string WheelVerification => _activeDefinition is null ? "Waiting for detection" :
        _activeDefinition.HardwareVerified ? "Hardware verified" : "Protocol compatible — hardware validation pending";

    public void Dispose()
    {
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _service.SnapshotChanged -= OnSnapshotChanged;
    }

    public void Initialize(LedProfileSettings settings, IReadOnlyList<WheelDefinition> wheels)
    {
        WheelOptions.Clear();
        WheelOptions.Add(new WheelOption(null, "Auto-detect (recommended)"));
        foreach (var wheel in wheels) WheelOptions.Add(new WheelOption(wheel.Id, wheel.DisplayName));
        SelectedWheel = WheelOptions.FirstOrDefault(x => x.Id == settings.PreferredWheelId) ?? WheelOptions[0];
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName switch
        {
            nameof(SettingsDraft.BindAddress) => nameof(BindAddress),
            nameof(SettingsDraft.Port) => nameof(Port),
            nameof(SettingsDraft.AutoStartControl) => nameof(AutoStartControl),
            nameof(SettingsDraft.CloseToTray) => nameof(CloseToTray),
            nameof(SettingsDraft.Theme) => nameof(Theme),
            nameof(SettingsDraft.SelectedWheel) => nameof(SelectedWheel),
            _ => null
        };
        if (name is not null) OnPropertyChanged(name);
    }

    private async Task ToggleRunningAsync()
    {
        if (_service.IsRunning) await _service.StopAsync();
        else await _service.StartAsync();
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot);
        else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        _isRunning = snapshot.IsRunning;
        _isWheelConnected = snapshot.IsWheelConnected;
        OnPropertyChanged(nameof(StartStopText));
        _testCommand.RaiseCanExecuteChanged();
        if (snapshot.Wheel is null || snapshot.Wheel.Id == _activeDefinition?.Id) return;
        _activeDefinition = snapshot.Wheel;
        OnPropertyChanged(nameof(WheelVerification));
    }
}