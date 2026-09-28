using System.Runtime.InteropServices;
using System.Windows.Interop;
using HidSharp;
using HidSharp.Reports;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

public sealed record WheelInputSample(string Control, double Percent, bool IsButton);

/// <summary>Reads shared Windows raw HID input; it does not take exclusive device ownership.</summary>
public sealed class WheelInputSource : IDisposable
{
    private const int WmInput = 0x00ff;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceInfo = 0x2000000b;
    private const uint RidevInputSink = 0x00000100;
    private readonly Dictionary<nint, ParsedDevice?> _devices = [];
    private readonly IReadOnlyList<WheelDefinition> _definitions;
    private HwndSource? _source;
    private string? _preferredWheelId;
    private string? _activeDevicePath;
    private string? _activePedalPath;
    private readonly HashSet<string> _auxiliaryPaths = new(StringComparer.OrdinalIgnoreCase);

    private sealed record ParsedDevice(string Path, string WheelId, ReportDescriptor Descriptor,
        IReadOnlyList<WheelInputControl> Controls, bool IsPedal, bool IsPrimaryWheel);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }

    public WheelInputSource(IReadOnlyList<WheelDefinition> definitions) => _definitions = definitions;
    public event EventHandler<WheelInputSample>? InputReceived;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string?>? ActiveWheelChanged;
    public string? ActiveWheelId { get; private set; }
    public IReadOnlyCollection<string> AvailableControls { get; private set; } = [];

    public void Initialize(nint windowHandle)
    {
        _source = HwndSource.FromHwnd(windowHandle) ?? throw new InvalidOperationException("Window is unavailable.");
        _source.AddHook(WndProc);
        var registrations = new[] { 4, 5, 8 }.Select(usage => new RawInputDevice
            { UsagePage = 1, Usage = (ushort)usage, Flags = RidevInputSink, Target = windowHandle }).ToArray();
        if (!RegisterRawInputDevices(registrations, (uint)registrations.Length,
                (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            _source.RemoveHook(WndProc);
            _source = null;
            throw new InvalidOperationException($"Could not register wheel input: {Marshal.GetLastWin32Error()}");
        }
    }

    public void SetPreferredWheel(string? wheelId)
    {
        if (string.Equals(_preferredWheelId, wheelId, StringComparison.OrdinalIgnoreCase)) return;
        _preferredWheelId = wheelId;
        _activeDevicePath = null;
        _activePedalPath = null;
        _auxiliaryPaths.Clear();
        ActiveWheelId = null;
        ActiveWheelChanged?.Invoke(this, null);
    }

    public void Dispose()
    {
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            var registrations = new[] { 4, 5, 8 }.Select(usage => new RawInputDevice
                { UsagePage = 1, Usage = (ushort)usage, Flags = 1, Target = 0 }).ToArray();
            RegisterRawInputDevices(registrations, (uint)registrations.Length,
                (uint)Marshal.SizeOf<RawInputDevice>());
        }
        _devices.Clear();
    }

    public void CheckPresence()
    {
        if (_activeDevicePath is null && _activePedalPath is null) return;
        try
        {
            var paths = DeviceList.Local.GetHidDevices(0x046d).Select(device => device.DevicePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (_activePedalPath is not null && !paths.Contains(_activePedalPath))
            {
                _activePedalPath = null;
                _devices.Clear();
                ActiveWheelChanged?.Invoke(this, ActiveWheelId);
            }
            if (_activeDevicePath is null || paths.Contains(_activeDevicePath)) return;
        }
        catch { return; }
        _activeDevicePath = null;
        _auxiliaryPaths.Clear();
        ActiveWheelId = null;
        _devices.Clear();
        ActiveWheelChanged?.Invoke(this, null);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg != WmInput) return 0;
        uint size = 0;
        if (GetRawInputData(lParam, RidInput, nint.Zero, ref size, (uint)(8 + 2 * IntPtr.Size)) != 0 ||
            size < 32 || size > 65536) return 0;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, (uint)(8 + 2 * IntPtr.Size)) == uint.MaxValue ||
                Marshal.ReadInt32(buffer) != 2) return 0;
            var deviceHandle = Marshal.ReadIntPtr(buffer, 8);
            if (!_devices.TryGetValue(deviceHandle, out var device))
            {
                _devices[deviceHandle] = device = ResolveDevice(deviceHandle);
                if (device is { IsPedal: true }) _activePedalPath = device.Path;
                else if (device is { IsPrimaryWheel: true } &&
                         (_preferredWheelId is null || device.WheelId == _preferredWheelId))
                {
                    _activeDevicePath = device.Path;
                    ActiveWheelId = device.WheelId;
                    foreach (var auxiliary in _devices.Values.Where(other =>
                                 other is { IsPedal: false, IsPrimaryWheel: false } &&
                                 other.WheelId == device.WheelId))
                        _auxiliaryPaths.Add(auxiliary!.Path);
                    ActiveWheelChanged?.Invoke(this, ActiveWheelId);
                }
                else if (device is not null && device.WheelId == ActiveWheelId)
                    _auxiliaryPaths.Add(device.Path);
            }
            if (device is null) return 0;
            if (!device.IsPedal && _preferredWheelId is not null && device.WheelId != _preferredWheelId) return 0;
            if (device.IsPedal ? _activePedalPath != device.Path || _activeDevicePath is null :
                _activeDevicePath != device.Path && !_auxiliaryPaths.Contains(device.Path)) return 0;
            var reportLength = Marshal.ReadInt32(buffer, 8 + 2 * IntPtr.Size);
            var count = Marshal.ReadInt32(buffer, 12 + 2 * IntPtr.Size);
            if (reportLength <= 0 || count <= 0 || (long)reportLength * count > size - (16 + 2 * IntPtr.Size))
                return 0;
            for (var i = 0; i < count; i++)
            {
                var reportBytes = new byte[reportLength];
                Marshal.Copy(buffer + 16 + 2 * IntPtr.Size + i * reportLength, reportBytes, 0, reportLength);
                try { Decode(device, reportBytes); }
                catch (Exception ex) { ErrorOccurred?.Invoke(this, $"Wheel report could not be read: {ex.Message}"); }
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return 0;
    }

    private ParsedDevice? ResolveDevice(nint handle)
    {
        uint size = 32;
        var info = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.WriteInt32(info, 32);
            if (GetRawInputDeviceInfo(handle, RidiDeviceInfo, info, ref size) == uint.MaxValue ||
                Marshal.ReadInt32(info, 4) != 2 || Marshal.ReadInt32(info, 8) != 0x046d) return null;
            var productId = Marshal.ReadInt32(info, 12);
            uint nameLength = 0;
            GetRawInputDeviceInfo(handle, 0x20000007, nint.Zero, ref nameLength);
            string? rawPath = null;
            if (nameLength is > 0 and < 4096)
            {
                var name = Marshal.AllocHGlobal((int)nameLength * 2);
                try
                {
                    if (GetRawInputDeviceInfo(handle, 0x20000007, name, ref nameLength) != uint.MaxValue)
                        rawPath = Marshal.PtrToStringUni(name);
                }
                finally { Marshal.FreeHGlobal(name); }
            }
            var allCandidates = DeviceList.Local.GetHidDevices(0x046d, productId).ToArray();
            var candidates = allCandidates.Where(hid => DevicePathsMatch(hid.DevicePath, rawPath)).ToArray();
            if (candidates.Length == 0 && allCandidates.Length == 1) candidates = allCandidates;
            foreach (var hid in candidates)
            {
                try
                {
                    var descriptor = hid.GetReportDescriptor();
                    if (!descriptor.DeviceItems.Any(item => item.InputReports.Any())) continue;
                    var definition = _definitions.FirstOrDefault(wheel => wheel.ProductIds.Contains(productId));
                    string productName;
                    try { productName = hid.GetProductName(); }
                    catch { productName = string.Empty; }
                    if (definition is null && !productName.Contains("wheel", StringComparison.OrdinalIgnoreCase) &&
                        !productName.Contains("racing", StringComparison.OrdinalIgnoreCase) &&
                        !productName.Contains("pedal", StringComparison.OrdinalIgnoreCase)) continue;
                    var wheelId = definition?.Id ?? $"logitech-{productId:x4}";
                    var controls = definition?.InputControls ?? [];
                    var isPedal = definition?.IsPedalSet == true ||
                                  productName.Contains("pedal", StringComparison.OrdinalIgnoreCase);
                    var isPrimaryWheel = !isPedal && descriptor.InputReports.SelectMany(report => report.DataItems)
                        .Any(item => item.Usages.GetAllValues().Any(usage => (uint)usage == 0x00010030));
                    AvailableControls = AvailableControls.Concat(descriptor.InputReports.SelectMany(report => report.DataItems)
                        .SelectMany(item => item.Usages.GetAllValues())
                        .Select(usage => ResolveName((uint)usage, controls, isPedal))).Distinct().ToArray();
                    return new ParsedDevice(hid.DevicePath, wheelId, descriptor, controls, isPedal, isPrimaryWheel);
                }
                catch { /* A vendor-only interface may have no readable input descriptor. */ }
            }
        }
        finally { Marshal.FreeHGlobal(info); }
        return null;
    }

    private void Decode(ParsedDevice device, byte[] bytes)
    {
        if (bytes.Length == 0) return;
        var report = device.Descriptor.InputReports.FirstOrDefault(candidate => candidate.ReportID == bytes[0]);
        if (report is null) return;
        var seenButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var usage in report.DataItems.SelectMany(item => item.Usages.GetAllValues())
                     .Where(usage => (uint)usage >> 16 == 9))
            seenButtons.Add(ResolveName((uint)usage, device.Controls, device.IsPedal));
        var activeButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        report.Read(bytes, 0, value =>
        {
            if (!value.IsValid || value.IsNull) return;
            foreach (var usage in value.Usages)
            {
                var name = ResolveName((uint)usage, device.Controls, device.IsPedal);
                if (value.DataItem.IsBoolean || (uint)usage >> 16 == 9)
                {
                    if (value.GetLogicalValue() != 0) activeButtons.Add(name);
                }
                else
                {
                    var minimum = value.DataItem.LogicalMinimum;
                    var maximum = value.DataItem.LogicalMaximum;
                    if (maximum <= minimum) continue;
                    var fraction = Math.Clamp((value.GetLogicalValue() - minimum) / (double)(maximum - minimum), 0, 1);
                    var declared = device.Controls.FirstOrDefault(control => control.Name == name);
                    if (declared?.Inverted == true) fraction = 1 - fraction;
                    InputReceived?.Invoke(this, new WheelInputSample(name,
                        (declared?.Centered == true || name == "Steering" ? fraction * 200 - 100 : fraction * 100), false));
                }
            }
        });
        foreach (var button in seenButtons)
            InputReceived?.Invoke(this, new WheelInputSample(button, activeButtons.Contains(button) ? 100 : 0, true));
    }

    private static string ResolveName(uint usage, IReadOnlyList<WheelInputControl> controls, bool isPedal)
    {
        var declared = controls.FirstOrDefault(control =>
            usage == ((uint)control.UsagePage << 16 | (uint)control.Usage));
        if (declared is not null) return declared.Name;
        var page = usage >> 16;
        var id = usage & 0xffff;
        if (page == 9) return $"Button{id}";
        if (page == 1 && isPedal) return id switch
        {
            0x30 => "Accelerator", 0x31 => "Brake", 0x32 => "Clutch", _ => $"Pedal axis {id:X2}"
        };
        if (page == 1) return id switch
        {
            0x30 => "Steering", 0x31 => "Accelerator", 0x32 => "Brake", 0x35 => "Clutch",
            _ => $"Axis {id:X2}"
        };
        return $"HID {page:X2}:{id:X2}";
    }

    private static bool DevicePathsMatch(string left, string? right)
    {
        if (right is null) return false;
        static string Normalize(string path) => path.Replace(@"\??\", @"\\?\", StringComparison.OrdinalIgnoreCase);
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
}
