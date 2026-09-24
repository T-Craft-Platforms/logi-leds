using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LogiLeds.Commands;
using LogiLeds.Models;
using LogiLeds.Services;
using LogiLeds.Views;
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
    private TelemetryWatchOption? _selectedWatch;

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
    public ObservableCollection<TelemetryGameSettings> TelemetryGames { get; } = [];
    public ObservableCollection<TelemetryWatchOption> WatchOptions { get; } = [];
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public event EventHandler? TelemetryChanged;
    public ICommand TestCommand => _testCommand;
    public ICommand StartStopCommand => _startStopCommand;

    public TelemetryWatchOption? SelectedWatch
    {
        get => _selectedWatch;
        set
        {
            if (!SetField(ref _selectedWatch, value)) return;
            TelemetryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public TelemetryWatchMode WatchMode => SelectedWatch?.Mode ?? TelemetryWatchMode.Auto;

    public bool CanAddGame => TelemetryGames.Count < Enum.GetValues<TelemetryGame>().Length;

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

    public bool UsePointerCursors
    {
        get => _draft.UsePointerCursors;
        set => _draft.UsePointerCursors = value;
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
        TelemetryGames.Clear();
        foreach (var game in settings.TelemetryGames) TelemetryGames.Add(game);
        RefreshWatchOptions(settings.TelemetryWatch);
        OnPropertyChanged(nameof(CanAddGame));
    }

    public void AddGame()
    {
        var available = Enum.GetValues<TelemetryGame>()
            .Where(game => TelemetryGames.All(existing => existing.Game != game)).ToArray();
        if (available.Length == 0) return;
        var result = TelemetryGameDialog.Show(Application.Current?.MainWindow, null, available,
            TelemetryGames.Select(game => game.Port));
        if (result?.Game is not { } game) return;
        TelemetryGames.Add(game);
        RefreshWatchOptions(WatchMode);
        OnPropertyChanged(nameof(CanAddGame));
        TelemetryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ManageGame(TelemetryGameSettings game)
    {
        var result = TelemetryGameDialog.Show(Application.Current?.MainWindow, game, [game.Game],
            TelemetryGames.Where(other => other != game).Select(other => other.Port), TelemetryGames.Count > 1);
        if (result is null) return;
        var index = TelemetryGames.IndexOf(game);
        if (index < 0) return;
        if (result.Remove)
        {
            if (TelemetryGames.Count == 1) return;
            TelemetryGames.RemoveAt(index);
            RefreshWatchOptions(WatchMode);
            OnPropertyChanged(nameof(CanAddGame));
        }
        else if (result.Game is { } updated)
        {
            TelemetryGames[index] = updated;
        }
        TelemetryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshWatchOptions(TelemetryWatchMode preferred)
    {
        WatchOptions.Clear();
        WatchOptions.Add(new TelemetryWatchOption(TelemetryWatchMode.Auto, "Auto · first active game"));
        foreach (var game in TelemetryGames)
            WatchOptions.Add(new TelemetryWatchOption(game.Game == TelemetryGame.Forza
                ? TelemetryWatchMode.Forza : TelemetryWatchMode.BeamNg, game.Name));
        SelectedWatch = WatchOptions.FirstOrDefault(option => option.Mode == preferred) ?? WatchOptions[0];
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName switch
        {
            nameof(SettingsDraft.AutoStartControl) => nameof(AutoStartControl),
            nameof(SettingsDraft.CloseToTray) => nameof(CloseToTray),
            nameof(SettingsDraft.UsePointerCursors) => nameof(UsePointerCursors),
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

public sealed record TelemetryWatchOption(TelemetryWatchMode Mode, string Label)
{
    public override string ToString() => Label;
}
