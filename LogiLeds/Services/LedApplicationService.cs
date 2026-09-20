using System.Net.Sockets;
using LogiLeds.Models;

namespace LogiLeds.Services;

public sealed class LedApplicationService : IAsyncDisposable
{
    private static readonly TimeSpan TelemetryTimeout = TimeSpan.FromSeconds(1);
    private readonly ITelemetryReceiver _telemetryReceiver;
    private readonly IWheelLedController _wheelController;
    private readonly SettingsStore _settingsStore;
    private readonly TimeProvider _timeProvider;
    private readonly SmartRedlineLearner _redlineLearner;
    private readonly WheelProfileStore _profileStore;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly object _frameLock = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private Task? _monitorTask;
    private ForzaTelemetryFrame? _latestFrame;
    private LedProfileSettings _settings = LedProfileSettings.Defaults;
    private bool _windowInitialized;
    private bool _isRunning;
    private bool _manualOutput;
    private bool _readyAnimationPlayed;
    private CancellationTokenSource? _animationCts;
    private string? _runtimeError;
    private double? _learnedRedlinePercent;

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

    public event EventHandler<AppSnapshot>? SnapshotChanged;
    public bool IsRunning => _isRunning;
    public LedProfileSettings Settings => _settings;
    public IReadOnlyList<WheelDefinition> Wheels => _wheelController.AvailableDefinitions;
    public IReadOnlyList<string> WheelDefinitionDiagnostics => _wheelController.DefinitionDiagnostics;

    public void InitializeWindow(nint windowHandle)
    {
        if (_windowInitialized) return;
        _windowInitialized = true;
        _wheelController.Initialize(windowHandle);
        _wheelController.SetPreferredWheel(_settings.PreferredWheelId);
        _monitorTask = MonitorLoopAsync(_lifetimeCts.Token);
        PublishSnapshot();
    }

    public async Task<LedProfileSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        _settings = await _settingsStore.LoadAsync(cancellationToken);
        await _redlineLearner.LoadAsync(cancellationToken);
        _wheelController.SetPreferredWheel(_settings.PreferredWheelId);
        return _settings;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            if (_isRunning) return;
            if (!_windowInitialized) throw new InvalidOperationException("The application window is not initialized yet.");
            try { await _telemetryReceiver.StartAsync(_settings, cancellationToken); }
            catch (SocketException ex)
            {
                _runtimeError = $"Cannot listen on {_settings.BindAddress}:{_settings.Port}: {ex.Message}";
                PublishSnapshot();
                throw;
            }
            _runtimeError = null;
            _isRunning = true;
            _readyAnimationPlayed = false;
            PublishSnapshot();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            _isRunning = false;
            CancelAnimation();
            await _telemetryReceiver.StopAsync();
            lock (_frameLock) _latestFrame = null;
            _wheelController.ClearLeds();
            PublishSnapshot();
        }
        finally { _lifecycleGate.Release(); }
    }

    public async Task UpdateSettingsAsync(LedProfileSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryValidate(out var error)) throw new ArgumentException(error, nameof(settings));
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            var old = _settings;
            var endpointChanged = old.BindAddress != settings.BindAddress || old.Port != settings.Port;
            if (_isRunning && endpointChanged)
            {
                await _telemetryReceiver.StopAsync();
                try { await _telemetryReceiver.StartAsync(settings, cancellationToken); }
                catch
                {
                    try { await _telemetryReceiver.StartAsync(old, cancellationToken); } catch { }
                    throw;
                }
            }
            _settings = settings;
            _wheelController.SetPreferredWheel(settings.PreferredWheelId);
            await _settingsStore.SaveAsync(settings, cancellationToken);
            _runtimeError = null;
            PublishSnapshot("Settings applied");
        }
        finally { _lifecycleGate.Release(); }
    }

    public Task TestLedsAsync(CancellationToken cancellationToken = default) => RunManualOutputAsync(false, cancellationToken);
    public Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default) => RunManualOutputAsync(true, cancellationToken);
    public async Task ResetLearningAsync() { await _redlineLearner.ClearAsync(); _learnedRedlinePercent = null; PublishSnapshot("Learned redlines reset"); }
    public Task<WheelProfile> LoadWheelProfileAsync(WheelDefinition wheel, CancellationToken cancellationToken = default) => _profileStore.LoadAsync(wheel, _settings, cancellationToken);
    public Task SaveWheelProfileAsync(WheelProfile profile, int groupCount, CancellationToken cancellationToken = default) => _profileStore.SaveAsync(profile, groupCount, cancellationToken);

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
        finally { _manualOutput = false; _wheelController.ClearLeds(); }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(16), _timeProvider);
        var lastUi = DateTimeOffset.MinValue;
        var lastClear = DateTimeOffset.MinValue;
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            _wheelController.Refresh();
            ForzaTelemetryFrame? frame;
            lock (_frameLock) frame = _latestFrame;
            var now = _timeProvider.GetUtcNow();
            var fresh = _isRunning && frame.HasValue && now - frame.Value.ReceivedAt <= TelemetryTimeout;
            var driving = fresh && frame!.Value.IsRaceOn && frame.Value.EngineMaxRpm > 0;

            if (driving)
            {
                var activeFrame = frame!.Value;
                CancelAnimation();
                if (!_manualOutput)
                {
                    var definition = _wheelController.CurrentDefinition;
                    var groups = definition?.ControlGroupCount ?? 5;
                    var advanced = _settings.ProfileMode == RpmProfileMode.Advanced && _settings.AdvancedThresholds.Length == groups
                        ? _settings.AdvancedThresholds : null;
                    var effectiveRedline = advanced is null ? _learnedRedlinePercent ?? _settings.RedlinePercent : _settings.RedlinePercent;
                    var preview = LedMath.CalculatePreview(activeFrame.CurrentEngineRpm, activeFrame.EngineMaxRpm,
                        _settings.FirstLedPercent, effectiveRedline, groups, advanced);
                    var visible = !preview.IsFlashing || !_settings.BlinkAtRedline || (now.ToUnixTimeMilliseconds() / 62) % 2 == 0;
                    _wheelController.SetLevel(visible ? preview.Count : 0);
                }
            }
            else if (!_manualOutput && now - lastClear >= TimeSpan.FromMilliseconds(250))
            {
                _wheelController.ClearLeds();
                lastClear = now;
            }

            if (!fresh || !_wheelController.IsConnected) _readyAnimationPlayed = false;
            if (_isRunning && fresh && !driving && _wheelController.IsConnected && _settings.ReadyAnimation && !_readyAnimationPlayed && !_manualOutput)
            {
                _readyAnimationPlayed = true;
                _animationCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _ = RunReadyAnimationGuardedAsync(_animationCts);
            }

            if (now - lastUi >= TimeSpan.FromMilliseconds(100)) { PublishSnapshot(); lastUi = now; }
        }
    }

    private async Task RunReadyAnimationGuardedAsync(CancellationTokenSource cts)
    {
        _manualOutput = true;
        try { await _wheelController.PlayReadyAnimationAsync(cts.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            _manualOutput = false;
            if (ReferenceEquals(_animationCts, cts)) _animationCts = null;
            cts.Dispose();
        }
    }

    private void CancelAnimation()
    {
        try { _animationCts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    private void OnFrameReceived(ForzaTelemetryFrame frame)
    {
        lock (_frameLock) _latestFrame = frame;
        if (_settings.ProfileMode == RpmProfileMode.Easy)
            _learnedRedlinePercent = _redlineLearner.Observe(frame, _settings.GameTitle) ?? _redlineLearner.Get(frame, _settings.GameTitle);
    }
    private void OnReceiverError(string error) { _runtimeError = error; PublishSnapshot(); }

    private void PublishSnapshot(string? transientMessage = null)
    {
        ForzaTelemetryFrame? frame;
        lock (_frameLock) frame = _latestFrame;
        var now = _timeProvider.GetUtcNow();
        var fresh = _isRunning && frame.HasValue && now - frame.Value.ReceivedAt <= TelemetryTimeout;
        var definition = _wheelController.CurrentDefinition;
        var groups = definition?.ControlGroupCount ?? 5;
        var advanced = _settings.ProfileMode == RpmProfileMode.Advanced && _settings.AdvancedThresholds.Length == groups
            ? _settings.AdvancedThresholds : null;
        var effectiveRedline = advanced is null ? _learnedRedlinePercent ?? _settings.RedlinePercent : _settings.RedlinePercent;
        var preview = LedMath.CalculatePreview(fresh ? frame!.Value.CurrentEngineRpm : 0, fresh ? frame!.Value.EngineMaxRpm : 0,
            _settings.FirstLedPercent, effectiveRedline, groups, advanced);
        var state = GetState(frame, fresh);
        var message = transientMessage ?? _runtimeError ?? state switch
        {
            ReadinessState.ControlPaused => "LED control is paused",
            ReadinessState.SearchingForWheel => _wheelController.StatusMessage,
            ReadinessState.WaitingForTelemetry => $"Listening on {_settings.BindAddress}:{_settings.Port}",
            ReadinessState.TelemetryStale => "Telemetry stream stopped",
            ReadinessState.Ready => "Game detected — ready to drive",
            ReadinessState.Driving => "Live RPM control active",
            _ => "Check Settings for details"
        };
        SnapshotChanged?.Invoke(this, new AppSnapshot(_isRunning, fresh, _wheelController.IsConnected,
            fresh && frame!.Value.IsRaceOn, fresh ? frame!.Value.CurrentEngineRpm : 0,
            fresh ? frame!.Value.EngineMaxRpm : 0, preview.Count, preview.IsFlashing,
            _wheelController.WheelName, message, state, fresh ? frame!.Value.ProtocolVariant : "—", definition, _learnedRedlinePercent));
    }

    private ReadinessState GetState(ForzaTelemetryFrame? frame, bool fresh)
    {
        if (_runtimeError is not null) return ReadinessState.NeedsAttention;
        if (!_isRunning) return ReadinessState.ControlPaused;
        if (!_wheelController.IsConnected) return ReadinessState.SearchingForWheel;
        if (!frame.HasValue) return ReadinessState.WaitingForTelemetry;
        if (!fresh) return ReadinessState.TelemetryStale;
        return frame.Value.IsRaceOn ? ReadinessState.Driving : ReadinessState.Ready;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { }
        _lifetimeCts.Cancel();
        if (_monitorTask is not null) try { await _monitorTask; } catch (OperationCanceledException) { }
        _telemetryReceiver.FrameReceived -= OnFrameReceived;
        _telemetryReceiver.ErrorOccurred -= OnReceiverError;
        await _telemetryReceiver.DisposeAsync();
        _wheelController.Shutdown();
        _wheelController.Dispose();
        _lifetimeCts.Dispose();
        _lifecycleGate.Dispose();
    }
}
