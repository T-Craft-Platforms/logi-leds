using System.Runtime.InteropServices;
using System.Windows.Threading;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed record InputMapperSnapshot(bool IsRunning, string Status, string? ActiveProfile,
    string? ActiveWheel, string ForceStatus, int DroppedPulses);

public sealed class InputMapperService : IDisposable
{
    private readonly InputMapperProfileStore _store;
    private readonly WheelInputSource _source;
    private readonly MapperRuleEngine _engine;
    private readonly IMapperOutput _output;
    private readonly IWheelResistanceController _force;
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<InputMapperProfile> _profiles = [];
    private InputMapperProfile? _active;
    private nint _ownWindow;
    private string? _status;
    private bool _initialized;
    private string? _loadError;
    private string? _lastOutputError;
    private DateTimeOffset _nextPresenceCheck;

    public InputMapperService(IReadOnlyList<WheelDefinition> wheels, InputMapperProfileStore? store = null,
        IWheelResistanceController? force = null, IMapperOutput? output = null,
        WheelInputSource? source = null, TimeProvider? clock = null)
    {
        _store = store ?? new InputMapperProfileStore();
        _force = force ?? new DirectInputResistanceController(wheels);
        _source = source ?? new WheelInputSource(wheels);
        _output = output ?? new WindowsInputEmitter();
        _engine = new MapperRuleEngine(_output, _force, clock);
        _source.InputReceived += OnInput;
        _source.ActiveWheelChanged += (_, _) =>
        {
            _engine.Deactivate(); _active = null;
            RefreshTarget(); Publish();
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) =>
        {
            if (DateTimeOffset.UtcNow >= _nextPresenceCheck)
            {
                _nextPresenceCheck = DateTimeOffset.UtcNow.AddSeconds(1);
                _source.CheckPresence();
            }
            if (!IsRunning) return;
            RefreshTarget();
            if (_active is not null) _engine.Tick();
            PublishOutputError();
        };
    }

    public bool IsRunning { get; private set; }
    public IReadOnlyList<InputMapperProfile> Profiles => _profiles;
    public IReadOnlyCollection<string> AvailableControls => _source.AvailableControls;
    public event EventHandler<InputMapperSnapshot>? SnapshotChanged;
    public event EventHandler<WheelInputSample>? InputPreview;

    public async Task InitializeAsync(nint windowHandle, string? preferredWheelId)
    {
        if (_initialized) return;
        try { _profiles = await _store.LoadAsync(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            _profiles = [];
            _loadError = $"Mapper profiles could not be loaded: {ex.Message}";
        }
        _ownWindow = windowHandle;
        PreferredWheelId = preferredWheelId;
        if (_force is DirectInputResistanceController directInput) directInput.SetWindowHandle(windowHandle);
        _source.SetPreferredWheel(preferredWheelId);
        _source.Initialize(windowHandle);
        _initialized = true;
        _timer.Start();
        Publish();
    }

    public void SetPreferredWheel(string? wheelId)
    {
        if (string.Equals(PreferredWheelId, wheelId, StringComparison.OrdinalIgnoreCase)) return;
        PreferredWheelId = wheelId;
        _source.SetPreferredWheel(wheelId);
        _engine.Deactivate();
        _active = null;
        Publish();
    }
    public string? PreferredWheelId { get; private set; }

    public void Start()
    {
        if (!_initialized) throw new InvalidOperationException("Input Mapper is not initialized.");
        if (_loadError is not null) throw new InvalidOperationException(_loadError);
        IsRunning = true;
        _status = null;
        RefreshTarget();
        Publish();
    }

    public void Stop()
    {
        IsRunning = false;
        _active = null;
        _engine.Deactivate();
        _status = null;
        Publish();
    }

    public async Task SaveProfilesAsync(IReadOnlyList<InputMapperProfile> profiles,
        CancellationToken cancellationToken = default)
    {
        if (_loadError is not null) throw new InvalidOperationException(_loadError);
        await _store.SaveAsync(profiles, cancellationToken);
        _profiles = profiles.ToArray();
        _active = null;
        _engine.Deactivate();
        RefreshTarget();
        Publish();
    }

    public void Dispose()
    {
        _timer.Stop();
        Stop();
        _source.Dispose();
        _force.Dispose();
    }

    private void OnInput(object? sender, WheelInputSample sample)
    {
        InputPreview?.Invoke(this, sample);
        if (!IsRunning) return;
        RefreshTarget();
        if (_active is not null) _engine.Update(sample);
        PublishOutputError();
    }

    private void RefreshTarget()
    {
        if (!IsRunning) return;
        var window = GetForegroundWindow();
        string? path = null;
        if (window != 0 && window != _ownWindow)
        {
            GetWindowThreadProcessId(window, out var processId);
            path = GetProcessPath(processId);
        }
        var match = path is null ? null : _profiles.FirstOrDefault(profile => profile.Enabled &&
            profile.ProcessPaths.Contains(path, StringComparer.OrdinalIgnoreCase));
        if (match?.Id == _active?.Id) return;
        _engine.Deactivate();
        _active = match;
        if (_active is not null && _source.ActiveWheelId is { } wheelId)
        {
            _engine.Activate(_active, wheelId);
            _status = null;
        }
        else if (_active is not null) _status = "Target active; waiting for wheel input";
        else _status = path is null && window != 0 && window != _ownWindow
            ? "Cannot inspect foreground process" : path is null
                ? "Waiting for a foreground target" : "No profile for foreground process";
        Publish();
    }

    private void PublishOutputError()
    {
        if (_lastOutputError == _output.LastError) return;
        _lastOutputError = _output.LastError;
        if (_lastOutputError is not null) _status = _lastOutputError;
        else if (_active is not null) _status = null;
        Publish();
    }

    private void Publish() => SnapshotChanged?.Invoke(this, new InputMapperSnapshot(IsRunning,
        _loadError ?? (!IsRunning ? "Input Mapper is stopped" : _status ?? "Mapping active"),
        _active?.Name, _source.ActiveWheelId, _force.Status, _engine.DroppedPulses));

    public static string? GetProcessPath(uint processId)
    {
        if (processId == 0) return null;
        var handle = OpenProcess(0x1000, false, processId);
        if (handle == 0) return null;
        try
        {
            var path = new System.Text.StringBuilder(32768);
            var length = path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref length) ? path.ToString() : null;
        }
        finally { CloseHandle(handle); }
    }

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags,
        System.Text.StringBuilder path, ref int length);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(nint handle);
}
