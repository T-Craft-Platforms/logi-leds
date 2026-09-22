using HidSharp;
using LogiLeds.Models;

namespace LogiLeds.Services;

/// <summary>Managed, LED-only Logitech HID transport. It never opens or writes force-feedback endpoints.</summary>
public sealed class LogitechWheelLedController : IWheelLedController
{
    private readonly List<WheelDefinition> _definitions;
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private bool _armed;
    private int _consecutiveWriteFailures;
    private string? _devicePath;
    private int _lastLevel = -1;
    private DateTimeOffset _lastWriteAt;
    private DateTimeOffset _nextDiscoveryAt;
    private DateTimeOffset _nextPresenceCheckAt;
    private string? _preferredWheelId;
    private bool _shutdown;
    private HidStream? _stream;

    public LogitechWheelLedController(WheelDefinitionCatalog? catalog = null, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var result = (catalog ?? new WheelDefinitionCatalog()).Load();
        _definitions = result.Definitions.ToList();
        DefinitionDiagnostics = result.Diagnostics;
    }

    public bool IsConnected => _stream is not null;
    public string WheelName => CurrentDefinition?.DisplayName ?? "No Logitech wheel";
    public string StatusMessage { get; private set; } = "Searching for a supported Logitech wheel";
    public WheelDefinition? CurrentDefinition { get; private set; }
    public IReadOnlyList<WheelDefinition> AvailableDefinitions => _definitions;
    public IReadOnlyList<string> DefinitionDiagnostics { get; }

    public bool Initialize(nint windowHandle)
    {
        Refresh();
        return true;
    }

    public void SetPreferredWheel(string? wheelId)
    {
        lock (_gate)
        {
            if (string.Equals(_preferredWheelId, wheelId, StringComparison.OrdinalIgnoreCase)) return;
            _preferredWheelId = wheelId;
            DisconnectCore();
            _nextDiscoveryAt = DateTimeOffset.MinValue;
        }
    }

    public void Refresh()
    {
        lock (_gate)
        {
            if (_shutdown) return;
            if (_stream is not null)
            {
                // Keep a successfully opened HID interface stable. CanWrite is
                // not a reliable liveness probe on Windows HID streams and can
                // briefly report false while the device is busy. Check presence
                // by path instead; actual I/O failures are handled by SetLevel.
                var presenceNow = _timeProvider.GetUtcNow();
                if (presenceNow < _nextPresenceCheckAt) return;
                _nextPresenceCheckAt = presenceNow.AddSeconds(1);
                if (CurrentDefinition is not null && IsDevicePresent(CurrentDefinition, _devicePath))
                {
                    StatusMessage = $"{WheelName} ready";
                    return;
                }

                DisconnectCore();
            }

            var now = _timeProvider.GetUtcNow();
            if (now < _nextDiscoveryAt) return;
            _nextDiscoveryAt = now.AddSeconds(1);

            var ordered = _definitions
                .OrderByDescending(x => string.Equals(x.Id, _preferredWheelId, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase);
            foreach (var definition in ordered)
            foreach (var productId in definition.ProductIds)
            {
                var devices = DeviceList.Local.GetHidDevices(definition.VendorId, productId)
                    .OrderBy(device => ReportPreference(device, definition.Transport));
                foreach (var device in devices)
                    try
                    {
                        // The classic G29/G27 LED command lives on the
                        // 17-byte vendor output collection. The 7/20-byte
                        // HID++ collections accept writes but do not drive
                        // the RPM LEDs.
                        if (definition.Transport == "classic-bitmask" &&
                            device.GetMaxOutputReportLength() < definition.OutputReportLength) continue;
                        if (definition.Transport == "hidpp-level" && device.GetMaxFeatureReportLength() < 20) continue;
                        if (!device.TryOpen(out var stream)) continue;
                        stream.ReadTimeout = 50;
                        stream.WriteTimeout = 500;
                        _stream = stream;
                        _devicePath = device.DevicePath;
                        CurrentDefinition = definition;
                        _lastLevel = -1;
                        _consecutiveWriteFailures = 0;
                        _armed = false;
                        StatusMessage = $"{definition.DisplayName} ready";
                        return;
                    }
                    catch
                    {
                        /* Try the next matching HID interface. */
                    }
            }

            StatusMessage = "No supported Logitech RPM wheel detected";
        }
    }

    public void RefreshNow()
    {
        lock (_gate)
        {
            _nextDiscoveryAt = DateTimeOffset.MinValue;
            _nextPresenceCheckAt = DateTimeOffset.MinValue;
        }

        Refresh();
    }

    public bool SetLevel(int illuminatedGroups)
    {
        lock (_gate)
        {
            if (_stream is null || CurrentDefinition is null || _shutdown) return false;
            var level = Math.Clamp(illuminatedGroups, 0, CurrentDefinition.ControlGroupCount);
            if (level == _lastLevel) return true;
            if (level != 0 && _timeProvider.GetUtcNow() - _lastWriteAt < TimeSpan.FromMilliseconds(10)) return true;
            try
            {
                if (CurrentDefinition.Transport == "classic-bitmask") WriteClassicLevel(level);
                else WriteHidppLevel(level);
                _lastLevel = level;
                _lastWriteAt = _timeProvider.GetUtcNow();
                _consecutiveWriteFailures = 0;
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"LED interface unavailable: {ex.Message}";
                // USB HID can transiently reject one report while G HUB or the
                // game is changing interfaces.  Do not make the UI flicker to
                // "Wheel not found" on the first failed write; reconnect only
                // after several consecutive failures.
                if (++_consecutiveWriteFailures >= 3) DisconnectCore();
                return false;
            }
        }
    }

    public void ClearLeds()
    {
        SetLevel(0);
    }

    public async Task TestLedsAsync(CancellationToken cancellationToken = default)
    {
        var count = CurrentDefinition?.ControlGroupCount ?? 0;
        for (var level = 0; level <= count; level++)
        {
            SetLevel(level);
            await Task.Delay(90, cancellationToken);
        }

        await Task.Delay(300, cancellationToken);
        for (var level = count; level >= 0; level--)
        {
            SetLevel(level);
            await Task.Delay(70, cancellationToken);
        }
    }

    public async Task PlayReadyAnimationAsync(CancellationToken cancellationToken = default)
    {
        var count = CurrentDefinition?.ControlGroupCount ?? 0;
        for (var level = 1; level <= count; level++)
        {
            SetAnimationStep(level);
            await Task.Delay(80, cancellationToken);
            ClearLeds();
        }

        for (var level = count; level >= 1; level--)
        {
            SetAnimationStep(level);
            await Task.Delay(80, cancellationToken);
            ClearLeds();
        }
    }

    public void Shutdown()
    {
        lock (_gate)
        {
            if (_shutdown) return;
            try
            {
                if (_stream is not null) SetLevel(0);
            }
            catch
            {
            }

            _shutdown = true;
            DisconnectCore();
            StatusMessage = "Wheel control stopped";
        }
    }

    public void Dispose()
    {
        Shutdown();
    }

    private void WriteClassicLevel(int level)
    {
        var mask = level == 0 ? 0 : (1 << Math.Min(level, 5)) - 1;
        WriteClassicMask(mask);
    }

    private void WriteClassicMask(int mask)
    {
        var definition = CurrentDefinition!;
        var report = new byte[definition.OutputReportLength];
        report[0] = (byte)definition.OutputReportId;
        report[1] = 0xF8;
        report[2] = 0x12;
        report[3] = (byte)mask;
        // The final byte is the command commit flag used by the G29/G27
        // vendor report. Without it Windows accepts the HID transfer but the
        // wheel silently ignores the LED mask.
        if (report.Length > 7) report[7] = 0x01;
        _stream!.Write(report);
    }

    private void WriteHidppLevel(int level)
    {
        var definition = CurrentDefinition!;
        var index = (byte)definition.HidppFeatureIndex;
        if (definition.RequiresArm && !_armed)
        {
            _stream!.SetFeature([0x10, 0xFF, index, 0x3C, 0x02, 0x00, 0x00]);
            _armed = true;
        }

        _stream!.SetFeature([0x10, 0xFF, index, 0x2C, 0x00, 0x00, 0x00]);
        var report = new byte[20];
        report[0] = 0x11;
        report[1] = 0xFF;
        report[2] = index;
        report[3] = 0x6C;
        report[5] = 0x01;
        report[7] = (byte)definition.ControlGroupCount;
        report[9] = (byte)level;
        _stream.SetFeature(report);
    }

    private void SetAnimationStep(int group)
    {
        lock (_gate)
        {
            if (_stream is null || CurrentDefinition is null) return;
            if (CurrentDefinition.Transport == "classic-bitmask")
                try
                {
                    WriteClassicMask(1 << Math.Min(group - 1, 4));
                    _lastLevel = -1;
                    _lastWriteAt = _timeProvider.GetUtcNow();
                    _consecutiveWriteFailures = 0;
                }
                catch (Exception ex)
                {
                    StatusMessage = $"LED interface unavailable: {ex.Message}";
                    if (++_consecutiveWriteFailures >= 3) DisconnectCore();
                }
            else SetLevel(group); // Level-only wheels use the documented fill/unfill fallback.
        }
    }

    private void DisconnectCore()
    {
        _stream?.Dispose();
        _stream = null;
        _devicePath = null;
        CurrentDefinition = null;
        _lastLevel = -1;
        _consecutiveWriteFailures = 0;
        _armed = false;
    }

    private static bool IsDevicePresent(WheelDefinition definition, string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath)) return false;
        try
        {
            return definition.ProductIds.Any(productId =>
                DeviceList.Local.GetHidDevices(definition.VendorId, productId)
                    .Any(device => string.Equals(device.DevicePath, devicePath, StringComparison.OrdinalIgnoreCase)));
        }
        catch
        {
            // Enumeration can briefly fail during USB re-enumeration. Keep the
            // opened stream for the next monitor tick instead of flickering.
            return true;
        }
    }

    private static int ReportPreference(HidDevice device, string transport)
    {
        try
        {
            if (transport == "classic-bitmask")
            {
                var outputLength = device.GetMaxOutputReportLength();
                return outputLength == 17 ? 0 : outputLength >= 17 ? 1 : 2;
            }

            var featureLength = device.GetMaxFeatureReportLength();
            return featureLength >= 20 ? 0 : 1;
        }
        catch
        {
            return 2;
        }
    }
}