using System.Collections.ObjectModel;
using System.Windows.Input;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;
using Application = System.Windows.Application;

namespace LogiLeds.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly AsyncRelayCommand _restartCommand;
    private readonly LedApplicationService _service;
    private readonly Action<string> _setStatus;
    private readonly AsyncRelayCommand _testCommand;
    private float _currentRpm, _maximumRpm;
    private string _currentVehicle = "No vehicle data", _wheelName = "No Logitech wheel", _telemetryFormat = "—";
    private bool _isFlashing, _isWheelConnected;

    public DashboardViewModel(LedApplicationService service, Action<string> setStatus)
    {
        _service = service;
        _setStatus = setStatus;
        _restartCommand =
            new AsyncRelayCommand(RestartAsync, onError: ex => _setStatus($"Could not restart: {ex.Message}"));
        _testCommand = new AsyncRelayCommand(() => _service.TestLedsAsync(), () => _isWheelConnected,
            ex => _setStatus($"LED test failed: {ex.Message}"));
        _service.SnapshotChanged += OnSnapshotChanged;
    }

    public ObservableCollection<LedIndicatorViewModel> Leds { get; } = [];
    public ICommand RestartCommand => _restartCommand;
    public ICommand TestCommand => _testCommand;

    public string WheelName
    {
        get => _wheelName;
        private set => SetField(ref _wheelName, value);
    }

    public string TelemetryFormat
    {
        get => _telemetryFormat;
        private set => SetField(ref _telemetryFormat, value);
    }

    public string CurrentVehicle
    {
        get => _currentVehicle;
        private set => SetField(ref _currentVehicle, value);
    }

    public float CurrentRpm
    {
        get => _currentRpm;
        private set
        {
            if (SetField(ref _currentRpm, value)) NotifyRpm();
        }
    }

    public float MaximumRpm
    {
        get => _maximumRpm;
        private set
        {
            if (SetField(ref _maximumRpm, value)) NotifyRpm();
        }
    }

    public bool IsFlashing
    {
        get => _isFlashing;
        private set
        {
            if (SetField(ref _isFlashing, value)) OnPropertyChanged(nameof(ShiftState));
        }
    }

    public string RpmDisplay => MaximumRpm > 0 ? $"{CurrentRpm:N0}" : "—";
    public string MaximumRpmDisplay => MaximumRpm > 0 ? $"/ {MaximumRpm:N0} RPM" : string.Empty;
    public string RpmPercentDisplay => MaximumRpm > 0 ? $"{CurrentRpm / MaximumRpm:P0}" : "—";
    public string ShiftState => IsFlashing ? "SHIFT NOW" : MaximumRpm > 0 ? "LIVE RPM" : "WAITING FOR DATA";

    public void Dispose() => _service.SnapshotChanged -= OnSnapshotChanged;

    private async Task RestartAsync()
    {
        if (_service.IsRunning) await _service.StopAsync();
        await _service.StartAsync();
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
        _testCommand.RaiseCanExecuteChanged();
        WheelName = snapshot.WheelName;
        TelemetryFormat = snapshot.TelemetryFormat;
        CurrentVehicle = snapshot.CurrentVehicle;
        CurrentRpm = snapshot.CurrentRpm;
        MaximumRpm = snapshot.MaximumRpm;
        IsFlashing = snapshot.IsFlashing;
        if (snapshot.PreviewWheel is not null) BuildLeds(snapshot.PreviewWheel, snapshot.IlluminatedLedCount);
        else
            foreach (var led in Leds)
                led.IsLit = false;
    }

    private void BuildLeds(WheelDefinition definition, int litGroups)
    {
        if (Leds.Count != definition.PhysicalLedCount)
        {
            Leds.Clear();
            for (var i = 0; i < definition.PhysicalLedCount; i++)
            {
                var group = definition.Direction == "outside-in"
                    ? Math.Min(i, definition.PhysicalLedCount - 1 - i) + 1
                    : Math.Min(i + 1, definition.ControlGroupCount);
                Leds.Add(new LedIndicatorViewModel(definition.Colors[i], group));
            }
        }

        foreach (var led in Leds) led.IsLit = led.Group <= litGroups;
    }

    private void NotifyRpm()
    {
        OnPropertyChanged(nameof(RpmDisplay));
        OnPropertyChanged(nameof(MaximumRpmDisplay));
        OnPropertyChanged(nameof(RpmPercentDisplay));
        OnPropertyChanged(nameof(ShiftState));
    }
}