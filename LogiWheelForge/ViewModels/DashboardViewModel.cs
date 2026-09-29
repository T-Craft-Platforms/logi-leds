using System.Collections.ObjectModel;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using Application = System.Windows.Application;

namespace LogiWheelForge.ViewModels;

public sealed class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly LedIndicatorService _service;
    private readonly InputMapperService _mapper;
    private readonly WheelSelectionService _selection;
    private float _currentRpm, _maximumRpm;
    private string _currentVehicle = "No vehicle data", _wheelName = "No Logitech wheel", _telemetryFormat = "—";
    private string _mapperStatus = "Stopped";
    private bool _isFlashing;

    public DashboardViewModel(LedIndicatorService service, InputMapperService mapper,
        WheelSelectionService selection)
    {
        _service = service;
        _mapper = mapper;
        _selection = selection;
        _service.SnapshotChanged += OnSnapshotChanged;
        _mapper.SnapshotChanged += OnMapperSnapshot;
        _selection.ActiveWheelChanged += OnActiveWheelChanged;
    }

    public ObservableCollection<LedIndicatorViewModel> Leds { get; } = [];

    public string WheelName
    {
        get => _wheelName;
        private set => SetField(ref _wheelName, value);
    }
    public string MapperStatus { get => _mapperStatus; private set => SetField(ref _mapperStatus, value); }

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

    public void Dispose()
    {
        _service.SnapshotChanged -= OnSnapshotChanged;
        _mapper.SnapshotChanged -= OnMapperSnapshot;
        _selection.ActiveWheelChanged -= OnActiveWheelChanged;
    }

    private void OnSnapshotChanged(object? sender, AppSnapshot snapshot)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) ApplySnapshot(snapshot);
        else dispatcher.BeginInvoke(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(AppSnapshot snapshot)
    {
        if (_selection.ActiveWheelId is null) WheelName = snapshot.WheelName;
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

    private void OnActiveWheelChanged(object? sender, string? wheelId)
    {
        WheelName = wheelId is null ? "No Logitech wheel" :
            _service.Wheels.FirstOrDefault(wheel => wheel.Id == wheelId)?.DisplayName ??
            $"Logitech wheel ({wheelId})";
    }

    private void OnMapperSnapshot(object? sender, InputMapperSnapshot snapshot)
    {
        MapperStatus = snapshot.ActiveProfile is null ? snapshot.Status :
            $"{snapshot.ActiveProfile}: {snapshot.Status}";
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
