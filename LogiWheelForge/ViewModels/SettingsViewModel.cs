using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using LogiWheelForge.Commands;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.Views;
using Application = System.Windows.Application;

namespace LogiWheelForge.ViewModels;

public sealed class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly SettingsDraft _draft;
    private readonly LedIndicatorService _service;
    private readonly Action<string> _setStatus;
    private readonly AsyncRelayCommand _startStopCommand;
    private readonly AsyncRelayCommand _testCommand;
    private WheelDefinition? _activeDefinition;
    private bool _isWheelConnected;
    private TelemetryWatchOption? _selectedWatch;

    public SettingsViewModel(LedIndicatorService service, SettingsDraft draft, Action<string> setStatus)
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

    public string WheelVerification => _activeDefinition is null ? "Waiting for detection" :
        _activeDefinition.HardwareVerified ? "Hardware verified" : "Protocol compatible — hardware validation pending";

    public void Dispose()
    {
        _draft.PropertyChanged -= OnDraftPropertyChanged;
        _service.SnapshotChanged -= OnSnapshotChanged;
    }

    public event EventHandler? TelemetryChanged;

    public void Initialize(LedProfileSettings settings, IReadOnlyList<WheelDefinition> wheels)
    {
        WheelOptions.Clear();
        WheelOptions.Add(new WheelOption(null, "Auto-detect (recommended)"));
        foreach (var wheel in wheels) WheelOptions.Add(new WheelOption(wheel.Id, wheel.DisplayName));
        SelectedWheel = WheelOptions.FirstOrDefault(x => x.Id == settings.PreferredWheelId) ?? WheelOptions[0];
        TelemetryGames.Clear();
        var usedPorts = settings.TelemetryGames.Select(item => item.Port).ToHashSet();
        foreach (var game in Enum.GetValues<TelemetryGame>())
        {
            var configured = settings.TelemetryGames.FirstOrDefault(item => item.Game == game);
            var fallback = CreateFallbackGame(game, usedPorts);
            TelemetryGames.Add(configured ?? fallback);
            if (configured is null) usedPorts.Add(fallback.Port);
        }

        if (!TelemetryGames.Any(game => game.Enabled)) TelemetryGames[0].Enabled = true;
        RefreshWatchOptions(settings.TelemetryWatch);
    }

    private static TelemetryGameSettings CreateFallbackGame(TelemetryGame game, ISet<int> usedPorts)
    {
        var fallback = game == TelemetryGame.Forza
            ? TelemetryGameSettings.DefaultForza
            : TelemetryGameSettings.DefaultBeamNg with { Enabled = false };
        while (usedPorts.Contains(fallback.Port)) fallback = fallback with { Port = fallback.Port + 1 };
        return fallback;
    }

    public bool SetGameEnabled(TelemetryGameSettings game, bool enabled)
    {
        if (!TelemetryGames.Contains(game)) return false;
        if (!enabled && TelemetryGames.All(item => ReferenceEquals(item, game) || !item.Enabled)) return false;
        game.Enabled = enabled;
        RefreshWatchOptions(WatchMode);
        TelemetryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void ConfigureGame(TelemetryGameSettings game)
    {
        var result = TelemetryGameDialog.Show(Application.Current?.MainWindow, game,
            TelemetryGames.Where(other => other != game).Select(other => other.Port));
        if (result is null) return;
        var index = TelemetryGames.IndexOf(game);
        if (index < 0) return;
        if (result.Game is not { } updated) return;
        TelemetryGames[index] = updated with { Enabled = game.Enabled };
        TelemetryChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RefreshWatchOptions(TelemetryWatchMode preferred)
    {
        WatchOptions.Clear();
        WatchOptions.Add(new TelemetryWatchOption(TelemetryWatchMode.Auto, "Auto-detect (recommended)"));
        foreach (var game in TelemetryGames.Where(game => game.Enabled))
            WatchOptions.Add(new TelemetryWatchOption(game.Game == TelemetryGame.Forza
                ? TelemetryWatchMode.Forza
                : TelemetryWatchMode.BeamNg, game.Name));
        SelectedWatch = WatchOptions.FirstOrDefault(option => option.Mode == preferred) ?? WatchOptions[0];
    }

    private void OnDraftPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName switch
        {
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
        _isWheelConnected = snapshot.IsWheelConnected;
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
