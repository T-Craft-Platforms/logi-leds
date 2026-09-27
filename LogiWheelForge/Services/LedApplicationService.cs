using System.Net.Sockets;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed class LedApplicationService : IAsyncDisposable
{
    private static readonly TimeSpan TelemetryTimeout = TimeSpan.FromSeconds(1);
    private readonly object _frameLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly WheelProfileStore _profileStore;
    private readonly SmartRedlineLearner _redlineLearner;
    private readonly SettingsStore _settingsStore;
    private readonly ITelemetryReceiver _telemetryReceiver;
    private readonly TimeProvider _timeProvider;
    private readonly IWheelLedController _wheelController;
    private CancellationTokenSource? _animationCts;
    private TelemetryFrame? _latestFrame;
    private double? _learnedRedlinePercent;
    private bool _manualOutput;
    private Task? _monitorTask;
    private string? _previewWheelId;
    private bool _readyAnimationPlayed;
    private string? _runtimeError;
    private bool _windowInitialized;

    public LedApplicationService(ITelemetryReceiver telemetryReceiver, IWheelLedController wheelController,
        SettingsStore settingsStore, TimeProvider? timeProvider = null, SmartRedlineLearner? redlineLearner = null,
        WheelProfileStore? profileStore = null)
    {
        _telemetryReceiver = telemetryReceiver;
        _wheelController = wheelController;
        _settingsStore = settingsStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _redlineLearner = redlineLearner ?? new SmartRedlineLearner();
        _profileStore = profileStore ?? new WheelProfileStore();
        _telemetryReceiver.FrameReceived += OnFrameReceived;
        _telemetryReceiver.ErrorOccurred += OnReceiverError;
    }

    public bool IsRunning { get; private set; }

    public LedProfileSettings Settings { get; private set; } = LedProfileSettings.Defaults;

    public IReadOnlyList<WheelDefinition> Wheels => _wheelController.AvailableDefinitions;
    public IReadOnlyList<string> WheelDefinitionDiagnostics => _wheelController.DefinitionDiagnostics;

    public async ValueTask DisposeAsync()
    {
        // Clear before waiting on UDP shutdown or the monitor task. HID
        // cleanup must happen even if another subsystem is slow to stop.
        try
        {
            _wheelController.ClearLeds();
        }
        catch
        {
        }

        try
        {
            await StopAsync();
        }
        catch
        {
        }

        _lifetimeCts.Cancel();
        if (_monitorTask is not null)
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
            }

        _telemetryReceiver.FrameReceived -= OnFrameReceived;
        _telemetryReceiver.ErrorOccurred -= OnReceiverError;
        await _redlineLearner.FlushAsync();
        await _telemetryReceiver.DisposeAsync();
        _wheelController.Shutdown();
        _wheelController.Dispose();
        _lifetimeCts.Dispose();
        _lifecycleGate.Dispose();
    }

    public event EventHandler<AppSnapshot>? SnapshotChanged;

    public void SetPreviewWheel(string? wheelId)
    {
        _previewWheelId = wheelId;
        PublishSnapshot();
    }

    public void InitializeWindow(nint windowHandle)
    {
        if (_windowInitialized) return;
        _windowInitialized = true;
        _wheelController.Initialize(windowHandle);
        _wheelController.SetPreferredWheel(Settings.PreferredWheelId);
        _monitorTask = MonitorLoopAsync(_lifetimeCts.Token);
        PublishSnapshot();
    }

    public async Task<LedProfileSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        Settings = await _settingsStore.LoadAsync(cancellationToken);
        // These are product behavior, not profile choices. Normalize older
        // settings so automatic animation and tray lifecycle cannot silently
        // disappear after an upgrade.
        if (!Settings.ReadyAnimation || !Settings.MinimizeToTray)
            Settings = Settings with { ReadyAnimation = true, MinimizeToTray = true };
        await _redlineLearner.LoadAsync(cancellationToken);
        _wheelController.SetPreferredWheel(Settings.PreferredWheelId);
        return Settings;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (IsRunning) return;
            if (!_windowInitialized)
                throw new InvalidOperationException("The application window is not initialized yet.");
            try
            {
                await _telemetryReceiver.StartAsync(Settings, cancellationToken);
            }
            catch (SocketException ex)
            {
                _runtimeError = $"Cannot start telemetry listener: {ex.Message}";
                PublishSnapshot();
                throw;
            }

            // Re-open the LED interface and establish a known-off level at
            // every start. This avoids requiring a second manual restart when
            // the wheel was re-enumerated while the app was stopped.
            _wheelController.RefreshNow();
            _wheelController.ClearLeds();
            _runtimeError = null;
            IsRunning = true;
            _readyAnimationPlayed = Settings.ReadyAnimation && _wheelController.IsConnected;
            PublishSnapshot();
            if (_readyAnimationPlayed)
            {
                _animationCts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
                _ = RunReadyAnimationGuardedAsync(_animationCts);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            IsRunning = false;
            CancelAnimation();
            await _telemetryReceiver.StopAsync();
            lock (_frameLock)
            {
                _latestFrame = null;
            }

            _wheelController.ClearLeds();
            PublishSnapshot();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task UpdateSettingsAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out var error)) throw new ArgumentException(error, nameof(settings));
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var old = Settings;
            var telemetryChanged = old.TelemetryWatch != settings.TelemetryWatch ||
                                   !old.TelemetryGames.SequenceEqual(settings.TelemetryGames);
            if (IsRunning && telemetryChanged)
            {
                await _telemetryReceiver.StopAsync();
                try
                {
                    await _telemetryReceiver.StartAsync(settings, cancellationToken);
                    lock (_frameLock) _latestFrame = null;
                    _learnedRedlinePercent = null;
                }
                catch
                {
                    try
                    {
                        await _telemetryReceiver.StartAsync(old, cancellationToken);
                    }
                    catch
                    {
                    }

                    throw;
                }
            }

            Settings = settings;
            _wheelController.SetPreferredWheel(settings.PreferredWheelId);
            await _settingsStore.SaveAsync(settings, cancellationToken);
            _runtimeError = null;
            PublishSnapshot("Settings applied");
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public Task TestLedsAsync(CancellationToken cancellationToken = default)
    {
        return RunManualOutputAsync(false, cancellationToken);
    }

    public Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default)
    {
        return RunManualOutputAsync(true, cancellationToken);
    }

    public async Task ResetLearningAsync()
    {
        await _redlineLearner.ClearAsync();
        _learnedRedlinePercent = null;
        PublishSnapshot("Learned redlines reset");
    }

    public IReadOnlyList<CarTrainingMapping> GetCarTrainingMappings()
    {
        return _redlineLearner.GetMappings();
    }

    public CarTrainingOverview GetCarTrainingOverview()
    {
        TelemetryFrame? frame;
        lock (_frameLock) frame = _latestFrame;
        var live = IsRunning && frame is { Game: TelemetryGame.Forza, IsRaceOn: true,
            CarOrdinal: not null, EngineMaxRpm: > 0 } &&
                   _timeProvider.GetUtcNow() - frame.Value.ReceivedAt <= TelemetryTimeout;
        var enabled = Settings.LearnPerCarShift && Settings.ProfileMode == RpmProfileMode.Easy;
        var sampling = live && enabled && frame!.Value.Gear is >= 1 and <= 10 &&
                       frame.Value.Accelerator is >= 220 &&
                       frame.Value.CurrentEngineRpm >= frame.Value.EngineMaxRpm * .72f;
        var mappings = _redlineLearner.GetMappings()
            .Select(mapping => new CarTrainingEntry(mapping,
                live && _redlineLearner.IsCurrentCar(mapping, frame!.Value, Settings.GameTitle)))
            .ToArray();
        return new CarTrainingOverview(mappings, enabled, live, sampling,
            live ? $"Car #{frame!.Value.CarOrdinal}" : "No live vehicle");
    }

    public Task<WheelProfile> LoadWheelProfileAsync(WheelDefinition wheel,
        CancellationToken cancellationToken = default)
    {
        return _profileStore.LoadAsync(wheel, Settings, cancellationToken);
    }

    public Task SaveWheelProfileAsync(WheelProfile profile, int groupCount,
        CancellationToken cancellationToken = default)
    {
        return _profileStore.SaveAsync(profile, groupCount, cancellationToken);
    }

    public Task<IReadOnlyList<WheelProfile>> ListNamedWheelProfilesAsync(string wheelId,
        CancellationToken cancellationToken = default)
    {
        return _profileStore.ListNamedAsync(wheelId, cancellationToken);
    }

    public Task<WheelProfile> SaveNamedWheelProfileAsync(WheelProfile profile, int groupCount,
        CancellationToken cancellationToken = default)
    {
        return _profileStore.SaveNamedAsync(profile, groupCount, cancellationToken);
    }

    public Task DeleteNamedWheelProfileAsync(string wheelId, string profileId,
        CancellationToken cancellationToken = default)
    {
        return _profileStore.DeleteNamedAsync(wheelId, profileId, cancellationToken);
    }

    private async Task RunManualOutputAsync(bool readyAnimation, CancellationToken cancellationToken)
    {
        if (_manualOutput) return;
        _wheelController.Refresh();
        if (!_wheelController.IsConnected) throw new InvalidOperationException(_wheelController.StatusMessage);
        _manualOutput = true;
        try
        {
            if (readyAnimation) await _wheelController.PlayReadyAnimationAsync(cancellationToken);
            else await _wheelController.TestLedsAsync(cancellationToken);
        }
        finally
        {
            _manualOutput = false;
            _wheelController.ClearLeds();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16), _timeProvider);
        var lastUi = DateTimeOffset.MinValue;
        var lastClear = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            _wheelController.Refresh();
            TelemetryFrame? frame;
            lock (_frameLock)
            {
                frame = _latestFrame;
            }

            var now = _timeProvider.GetUtcNow();
            var fresh = IsRunning && frame.HasValue && now - frame.Value.ReceivedAt <= TelemetryTimeout;
            var driving = fresh && frame!.Value.IsRaceOn && frame.Value.EngineMaxRpm > 0;

            if (driving)
            {
                var activeFrame = frame!.Value;
                if (!_manualOutput) CancelAnimation();
                if (!_manualOutput)
                {
                    var definition = _wheelController.CurrentDefinition ?? GetPreviewDefinition();
                    var groups = definition?.ControlGroupCount ?? 5;
                    var advanced = Settings.ProfileMode == RpmProfileMode.Advanced &&
                                   Settings.AdvancedThresholds.Length == groups
                        ? Settings.AdvancedThresholds
                        : null;
                    var effectiveRedline = advanced is null
                        ? _learnedRedlinePercent ?? Settings.RedlinePercent
                        : Settings.RedlinePercent;
                    var preview = LedMath.CalculatePreview(activeFrame.CurrentEngineRpm, activeFrame.EngineMaxRpm,
                        Settings.FirstLedPercent, effectiveRedline, groups, advanced);
                    _wheelController.SetLevel(GetVisibleLedCount(preview, now));
                }
            }
            else if (!_manualOutput && now - lastClear >= TimeSpan.FromMilliseconds(250))
            {
                _wheelController.ClearLeds();
                lastClear = now;
            }

            if (!fresh || !_wheelController.IsConnected) _readyAnimationPlayed = false;
            if (IsRunning && fresh && !driving && _wheelController.IsConnected && Settings.ReadyAnimation &&
                !_readyAnimationPlayed && !_manualOutput)
            {
                _readyAnimationPlayed = true;
                _animationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = RunReadyAnimationGuardedAsync(_animationCts);
            }

            if (now - lastUi >= TimeSpan.FromMilliseconds(50))
            {
                PublishSnapshot();
                lastUi = now;
            }
        }
    }

    private async Task RunReadyAnimationGuardedAsync(CancellationTokenSource cts)
    {
        _manualOutput = true;
        try
        {
            await _wheelController.PlayReadyAnimationAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _manualOutput = false;
            if (ReferenceEquals(_animationCts, cts)) _animationCts = null;
            cts.Dispose();
        }
    }

    private void CancelAnimation()
    {
        try
        {
            _animationCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void OnFrameReceived(TelemetryFrame frame)
    {
        lock (_frameLock)
        {
            if (_latestFrame is { } current && current.Game != frame.Game &&
                frame.ReceivedAt - current.ReceivedAt <= TelemetryTimeout) return;
            _latestFrame = frame;
        }

        if (frame.Game == TelemetryGame.Forza && Settings.LearnPerCarShift &&
            Settings.ProfileMode == RpmProfileMode.Easy)
            _learnedRedlinePercent = _redlineLearner.Observe(frame, Settings.GameTitle) ??
                                     _redlineLearner.Get(frame, Settings.GameTitle);
        else
            _learnedRedlinePercent = null;
    }

    private void OnReceiverError(string error)
    {
        _runtimeError = error;
        PublishSnapshot();
    }

    private int GetVisibleLedCount((int Count, bool IsFlashing) preview, DateTimeOffset now)
    {
        return !preview.IsFlashing || !Settings.BlinkAtRedline || now.ToUnixTimeMilliseconds() / 62 % 2 == 0
            ? preview.Count
            : 0;
    }

    private void PublishSnapshot(string? transientMessage = null)
    {
        TelemetryFrame? frame;
        lock (_frameLock)
        {
            frame = _latestFrame;
        }

        var now = _timeProvider.GetUtcNow();
        var fresh = IsRunning && frame.HasValue && now - frame.Value.ReceivedAt <= TelemetryTimeout;
        var connectedDefinition = _wheelController.CurrentDefinition;
        var previewDefinition = connectedDefinition ?? GetPreviewDefinition();
        var groups = previewDefinition?.ControlGroupCount ?? 5;
        var advanced = Settings.ProfileMode == RpmProfileMode.Advanced && Settings.AdvancedThresholds.Length == groups
            ? Settings.AdvancedThresholds
            : null;
        var effectiveRedline =
            advanced is null ? _learnedRedlinePercent ?? Settings.RedlinePercent : Settings.RedlinePercent;
        var preview = LedMath.CalculatePreview(fresh ? frame!.Value.CurrentEngineRpm : 0,
            fresh ? frame!.Value.EngineMaxRpm : 0,
            Settings.FirstLedPercent, effectiveRedline, groups, advanced);
        var visibleCount = GetVisibleLedCount(preview, now);
        var state = GetState(frame, fresh);
        var currentVehicle = frame is { } vehicleFrame && vehicleFrame.CarOrdinal is int carOrdinal
            ? $"Car #{carOrdinal}"
            : frame is { Game: TelemetryGame.BeamNg } ? "BeamNG vehicle" : "No vehicle data";
        var message = transientMessage ?? _runtimeError ?? state switch
        {
            ReadinessState.ControlPaused => "App control is paused",
            ReadinessState.SearchingForWheel => _wheelController.StatusMessage,
            ReadinessState.WaitingForTelemetry => "Listening for game telemetry",
            ReadinessState.TelemetryStale => "Telemetry stream stopped",
            ReadinessState.Ready => "Game detected — ready to drive",
            ReadinessState.Driving => "Live RPM control active",
            _ => "Check Settings for details"
        };
        SnapshotChanged?.Invoke(this, new AppSnapshot(IsRunning, fresh, _wheelController.IsConnected,
            fresh && frame!.Value.IsRaceOn, fresh ? frame!.Value.CurrentEngineRpm : 0,
            fresh ? frame!.Value.EngineMaxRpm : 0, visibleCount, preview.IsFlashing,
            _wheelController.WheelName, message, state, fresh ? frame!.Value.ProtocolVariant : "—", currentVehicle,
            connectedDefinition,
            _learnedRedlinePercent, previewDefinition));
    }

    private WheelDefinition? GetPreviewDefinition()
    {
        return _wheelController.AvailableDefinitions.FirstOrDefault(x =>
                   string.Equals(x.Id, _previewWheelId, StringComparison.OrdinalIgnoreCase))
               ?? _wheelController.AvailableDefinitions.FirstOrDefault(x =>
                   string.Equals(x.Id, Settings.PreferredWheelId, StringComparison.OrdinalIgnoreCase))
               ?? _wheelController.AvailableDefinitions.FirstOrDefault();
    }

    private ReadinessState GetState(TelemetryFrame? frame, bool fresh)
    {
        if (_runtimeError is not null) return ReadinessState.NeedsAttention;
        if (!IsRunning) return ReadinessState.ControlPaused;
        if (!_wheelController.IsConnected) return ReadinessState.SearchingForWheel;
        if (!frame.HasValue) return ReadinessState.WaitingForTelemetry;
        if (!fresh) return ReadinessState.TelemetryStale;
        return frame.Value.IsRaceOn ? ReadinessState.Driving : ReadinessState.Ready;
    }
}

public sealed record CarTrainingEntry(CarTrainingMapping Mapping, bool IsCurrent);

public sealed record CarTrainingOverview(
    IReadOnlyList<CarTrainingEntry> Entries,
    bool IsEnabled,
    bool IsLive,
    bool IsSampling,
    string CurrentVehicle);
