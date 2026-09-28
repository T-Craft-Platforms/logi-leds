using System.Runtime.InteropServices;
using LogiWheelForge.Models;

namespace LogiWheelForge.Services;

/// <summary>Uses system DirectInput spring/damper effects; no vendor command is emitted.</summary>
public sealed class DirectInputResistanceController : IWheelResistanceController
{
    private static readonly Guid DirectInputId = new("BF798031-483A-4DA2-AA99-5D64ED369700");
    private static readonly Guid XAxis = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
    private static readonly Guid Spring = new("13541C27-8E33-11D0-9AD0-00A0C9A06E35");
    private static readonly Guid Damper = new("13541C28-8E33-11D0-9AD0-00A0C9A06E35");
    private readonly IReadOnlyList<WheelDefinition> _definitions;
    private nint _window;
    private IDirectInput8? _directInput;
    private IDirectInputDevice8? _device;
    private IDirectInputEffect? _spring;
    private IDirectInputEffect? _damper;
    private MapperResistance? _settings;
    private double _position, _target, _holdStrength;
    private bool _holding;
    private int _lastOffset = int.MinValue, _lastStrength = -1;

    public DirectInputResistanceController(IReadOnlyList<WheelDefinition>? definitions = null, nint window = default)
    {
        _definitions = definitions ?? [];
        _window = window;
    }

    public bool IsAvailable => _spring is not null;
    public string Status { get; private set; } = "Resistance is off";
    public void SetWindowHandle(nint window) => _window = window;

    public bool Start(string wheelId, MapperResistance settings)
    {
        Stop();
        _settings = settings;
        try
        {
            var targetIds = _definitions.FirstOrDefault(item => item.Id == wheelId)?.ProductIds ??
                            (wheelId.StartsWith("logitech-", StringComparison.OrdinalIgnoreCase) &&
                             int.TryParse(wheelId[9..], System.Globalization.NumberStyles.HexNumber, null, out var product)
                                ? [product] : []);
            if (targetIds.Length == 0) { Status = "No matching force-feedback device"; return false; }
            var iid = DirectInputId;
            if (DirectInput8Create(GetModuleHandle(null), 0x0800, ref iid,
                    out _directInput, 0) < 0 || _directInput is null)
            { Status = "DirectInput is unavailable"; return false; }
            Guid? instance = null;
            _directInput.EnumDevices(4, (pointer, _) =>
            {
                var info = Marshal.PtrToStructure<DeviceInstance>(pointer);
                var vendor = (int)(info.ProductGuid.ToByteArray()[0] | info.ProductGuid.ToByteArray()[1] << 8);
                var pid = (int)(info.ProductGuid.ToByteArray()[2] | info.ProductGuid.ToByteArray()[3] << 8);
                if (vendor != 0x046d || !targetIds.Contains(pid)) return 1;
                instance = info.InstanceGuid;
                return 0;
            }, 0, 0x00000100);
            if (instance is null) { Status = "Wheel driver exposes no force-feedback device"; StopDevice(); return false; }
            var guid = instance.Value;
            if (_directInput.CreateDevice(ref guid, out _device, 0) < 0 || _device is null)
            { Status = "Could not open wheel force-feedback device"; StopDevice(); return false; }
            if (!SetXAxisFormat(_device) || _device.SetCooperativeLevel(_window, 0x11) < 0 ||
                _device.Acquire() < 0)
            { Status = "Wheel force feedback is busy or unavailable"; StopDevice(); return false; }
            if (settings.CenterStrength > 0 || settings.DetentsEnabled)
                _spring = CreateConditionEffect(_device, Spring, 0, settings.CenterStrength);
            if (settings.DampingStrength > 0)
                _damper = CreateConditionEffect(_device, Damper, 0, settings.DampingStrength);
            if (_spring is null && _damper is null)
            { Status = "Spring and damper effects are unsupported"; StopDevice(); return false; }
            Status = _spring is null ? "Damping active; spring is unsupported" :
                _damper is null && settings.DampingStrength > 0 ? "Spring active; damper is unsupported" :
                "Resistance active";
            UpdateSpring();
            return true;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException)
        {
            StopDevice();
            Status = $"Resistance unavailable: {ex.Message}";
            return false;
        }
    }

    public void UpdatePosition(double steeringPercent)
    {
        _position = Math.Clamp(steeringPercent, -100, 100);
        UpdateSpring();
    }

    public void Hold(double targetPercent, double strength)
    {
        _holding = true;
        _target = Math.Clamp(targetPercent, -100, 100);
        _holdStrength = Math.Clamp(strength, 0, 100);
        UpdateSpring();
    }

    public void ReleaseHold() { _holding = false; UpdateSpring(); }

    public void Stop()
    {
        StopDevice();
        _settings = null;
        _holding = false;
        _lastOffset = int.MinValue;
        _lastStrength = -1;
        Status = "Resistance is off";
    }

    public void Dispose() => Stop();

    private void UpdateSpring()
    {
        if (_spring is null || _settings is null) return;
        var target = _holding ? _target : _settings.DetentsEnabled
            ? Math.Round(_position / _settings.DetentSpacingPercent) * _settings.DetentSpacingPercent : 0;
        var strength = _holding ? _holdStrength : _settings.DetentsEnabled
            ? _settings.DetentStrength : _settings.CenterStrength;
        var offset = (int)Math.Round(Math.Clamp(target, -100, 100) * 100);
        var coefficient = (int)Math.Round(Math.Clamp(strength, 0, 100) * 100);
        if (offset == _lastOffset && coefficient == _lastStrength) return;
        _lastOffset = offset; _lastStrength = coefficient;
        var condition = new Condition
        {
            Offset = offset, PositiveCoefficient = -coefficient, NegativeCoefficient = -coefficient,
            PositiveSaturation = 10000, NegativeSaturation = 10000
        };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<Condition>());
        try
        {
            Marshal.StructureToPtr(condition, pointer, false);
            var effect = new EffectData { Size = (uint)Marshal.SizeOf<EffectData>(), TypeSize =
                (uint)Marshal.SizeOf<Condition>(), TypeData = pointer };
            try
            {
                if (_spring.SetParameters(ref effect, 0x00000100) < 0)
                    Status = "Wheel rejected resistance update";
            }
            catch (COMException)
            {
                Status = "Wheel force feedback disconnected";
                StopDevice();
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }

    private static IDirectInputEffect? CreateConditionEffect(IDirectInputDevice8 device, Guid kind,
        int offset, double strength)
    {
        var axis = Marshal.AllocHGlobal(4);
        var direction = Marshal.AllocHGlobal(4);
        var condition = Marshal.AllocHGlobal(Marshal.SizeOf<Condition>());
        try
        {
            Marshal.WriteInt32(axis, 0); Marshal.WriteInt32(direction, 1);
            Marshal.StructureToPtr(new Condition
            {
                Offset = offset, PositiveCoefficient = -(int)(strength * 100),
                NegativeCoefficient = -(int)(strength * 100), PositiveSaturation = 10000,
                NegativeSaturation = 10000
            }, condition, false);
            var effect = new EffectData
            {
                Size = (uint)Marshal.SizeOf<EffectData>(), Flags = 0x12, Duration = uint.MaxValue,
                Gain = 10000, TriggerButton = uint.MaxValue, AxisCount = 1, Axes = axis,
                Direction = direction, TypeSize = (uint)Marshal.SizeOf<Condition>(), TypeData = condition
            };
            if (device.CreateEffect(ref kind, ref effect, out var created, 0) < 0 || created is null)
                return null;
            if (created.Start(1, 0) >= 0) return created;
            Marshal.ReleaseComObject(created);
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(axis); Marshal.FreeHGlobal(direction); Marshal.FreeHGlobal(condition);
        }
    }

    private static bool SetXAxisFormat(IDirectInputDevice8 device)
    {
        var guid = Marshal.AllocHGlobal(16);
        var obj = Marshal.AllocHGlobal(Marshal.SizeOf<ObjectFormat>());
        try
        {
            Marshal.Copy(XAxis.ToByteArray(), 0, guid, 16);
            Marshal.StructureToPtr(new ObjectFormat { Guid = guid, Offset = 0, Type = 0x00ffff03 }, obj, false);
            var format = new DataFormat
            {
                Size = (uint)Marshal.SizeOf<DataFormat>(), ObjectSize = (uint)Marshal.SizeOf<ObjectFormat>(),
                Flags = 1, DataSize = 4, ObjectCount = 1, Objects = obj
            };
            return device.SetDataFormat(ref format) >= 0;
        }
        finally { Marshal.FreeHGlobal(guid); Marshal.FreeHGlobal(obj); }
    }

    private void StopDevice()
    {
        if (_spring is not null)
        { try { _spring.Stop(); } catch { } Marshal.ReleaseComObject(_spring); _spring = null; }
        if (_damper is not null)
        { try { _damper.Stop(); } catch { } Marshal.ReleaseComObject(_damper); _damper = null; }
        if (_device is not null)
        { try { _device.Unacquire(); } catch { } Marshal.ReleaseComObject(_device); _device = null; }
        if (_directInput is not null) { Marshal.ReleaseComObject(_directInput); _directInput = null; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DeviceInstance
    {
        public uint Size;
        public Guid InstanceGuid, ProductGuid;
        public uint DeviceType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string InstanceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ProductName;
        public Guid ForceDriver;
        public ushort UsagePage, Usage;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DataFormat
    { public uint Size, ObjectSize, Flags, DataSize, ObjectCount; public nint Objects; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectFormat
    { public nint Guid; public uint Offset, Type, Flags; }
    [StructLayout(LayoutKind.Sequential)]
    private struct EffectData
    {
        public uint Size, Flags, Duration, SamplePeriod, Gain, TriggerButton, TriggerRepeatInterval, AxisCount;
        public nint Axes, Direction, Envelope;
        public uint TypeSize;
        public nint TypeData;
        public uint StartDelay;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Condition
    {
        public int Offset, PositiveCoefficient, NegativeCoefficient;
        public uint PositiveSaturation, NegativeSaturation, DeadBand;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumDeviceCallback(nint instance, nint context);

    [ComImport, Guid("BF798031-483A-4DA2-AA99-5D64ED369700"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirectInput8
    {
        [PreserveSig] int CreateDevice(ref Guid id, out IDirectInputDevice8 device, nint outer);
        [PreserveSig] int EnumDevices(uint type, EnumDeviceCallback callback, nint context, uint flags);
    }

    [ComImport, Guid("54D41081-DC15-4833-A41B-748F73A38179"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirectInputDevice8
    {
        void GetCapabilities(); void EnumObjects(); void GetProperty(); void SetProperty();
        [PreserveSig] int Acquire(); [PreserveSig] int Unacquire();
        void GetDeviceState(); void GetDeviceData();
        [PreserveSig] int SetDataFormat(ref DataFormat format);
        void SetEventNotification();
        [PreserveSig] int SetCooperativeLevel(nint window, uint flags);
        void GetObjectInfo(); void GetDeviceInfo(); void RunControlPanel(); void Initialize();
        [PreserveSig] int CreateEffect(ref Guid kind, ref EffectData data,
            out IDirectInputEffect effect, nint outer);
    }

    [ComImport, Guid("E7E1F7C0-88D2-11D0-9AD0-00A0C9A06E35"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirectInputEffect
    {
        void Initialize(); void GetEffectGuid(); void GetParameters();
        [PreserveSig] int SetParameters(ref EffectData data, uint flags);
        [PreserveSig] int Start(uint iterations, uint flags);
        [PreserveSig] int Stop();
    }

    [DllImport("dinput8.dll", PreserveSig = true)]
    private static extern int DirectInput8Create(nint instance, uint version, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IDirectInput8 input, nint outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
