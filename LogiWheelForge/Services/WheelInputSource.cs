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

    private sealed record ParsedDevice(string Path, string WheelId, ReportDescriptor Descriptor,
        IReadOnlyList<WheelInputControl> Controls);

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice { public ushort UsagePage, Usage; public uint Flags; public nint Target; }

    public WheelInputSource(IReadOnlyList<WheelDefinition> definitions) => _definitions = definitions;
    public event EventHandler<WheelInputSample>? InputReceived;
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
            throw new InvalidOperationException($"Could not register wheel input: {Marshal.GetLastWin32Error()}");
    }

    public void SetPreferredWheel(string? wheelId)
    {
        _preferredWheelId = wheelId;
        _activeDevicePath = null;
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
                if (device is not null && (_preferredWheelId is null || device.WheelId == _preferredWheelId))
                {
                    _activeDevicePath = device.Path;
                    ActiveWheelId = device.WheelId;
                    ActiveWheelChanged?.Invoke(this, ActiveWheelId);
                }
            }
            if (device is null) return 0;
            if (_preferredWheelId is not null && device.WheelId != _preferredWheelId) return 0;
            if (_activeDevicePath != device.Path) return 0;
            var reportLength = Marshal.ReadInt32(buffer, 8 + 2 * IntPtr.Size);
            var count = Marshal.ReadInt32(buffer, 12 + 2 * IntPtr.Size);
            if (reportLength <= 0 || count <= 0 || (long)reportLength * count > size - (16 + 2 * IntPtr.Size))
                return 0;
            for (var i = 0; i < count; i++)
            {
                var reportBytes = new byte[reportLength];
                Marshal.Copy(buffer + 16 + 2 * IntPtr.Size + i * reportLength, reportBytes, 0, reportLength);
                Decode(device, reportBytes);
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
            var candidates = DeviceList.Local.GetHidDevices(0x046d, productId)
                .OrderByDescending(hid => string.Equals(hid.DevicePath, rawPath, StringComparison.OrdinalIgnoreCase));
            foreach (var hid in candidates)
            {
                try
                {
                    var descriptor = hid.GetReportDescriptor();
                    if (!descriptor.DeviceItems.Any(item => item.InputReports.Any())) continue;
                    var definition = _definitions.FirstOrDefault(wheel => wheel.ProductIds.Contains(productId));
                    var wheelId = definition?.Id ?? $"logitech-{productId:x4}";
                    var controls = definition?.InputControls ?? [];
                    AvailableControls = descriptor.InputReports.SelectMany(report => report.DataItems)
                        .SelectMany(item => item.Usages.GetAllValues())
                        .Select(usage => ResolveName((uint)usage, controls)).Distinct().ToArray();
                    return new ParsedDevice(hid.DevicePath, wheelId, descriptor, controls);
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
        foreach (var usage in report.DataItems.Where(item => item.IsBoolean)
                     .SelectMany(item => item.Usages.GetAllValues()))
            seenButtons.Add(ResolveName((uint)usage, device.Controls));
        var activeButtons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        report.Read(bytes, 0, value =>
        {
            if (!value.IsValid || value.IsNull) return;
            foreach (var usage in value.Usages.GetAllValues())
            {
                var name = ResolveName((uint)usage, device.Controls);
                if (value.DataItem.IsBoolean)
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

    private static string ResolveName(uint usage, IReadOnlyList<WheelInputControl> controls)
    {
        var declared = controls.FirstOrDefault(control =>
            usage == ((uint)control.UsagePage << 16 | (uint)control.Usage));
        if (declared is not null) return declared.Name;
        var page = usage >> 16;
        var id = usage & 0xffff;
        if (page == 9) return $"Button{id}";
        if (page == 1) return id switch
        {
            0x30 => "Steering", 0x31 => "Accelerator", 0x32 => "Brake", 0x35 => "Clutch",
            _ => $"Axis {id:X2}"
        };
        return $"HID {page:X2}:{id:X2}";
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);
    [DllImport("user32.dll", EntryPoint = "GetRawInputDeviceInfoW", SetLastError = true)]
    private static extern uint GetRawInputDeviceInfo(nint device, uint command, nint data, ref uint size);
}
