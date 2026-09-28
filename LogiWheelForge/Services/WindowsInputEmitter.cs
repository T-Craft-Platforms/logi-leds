using System.Runtime.InteropServices;
using System.Windows.Input;

namespace LogiWheelForge.Services;

public sealed class WindowsInputEmitter : IMapperOutput
{
    private readonly Dictionary<string, int> _held = new(StringComparer.OrdinalIgnoreCase);
    public string? LastError { get; private set; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public nint ExtraInfo;
    }

    public bool Press(string output, bool mouseButton = false)
    {
        var name = (mouseButton ? "mouse:" : "key:") + output;
        if (_held.TryGetValue(name, out var count)) { _held[name] = count + 1; return true; }
        if (!Send(output, false, mouseButton)) return false;
        _held[name] = 1;
        return true;
    }

    public bool Release(string output, bool mouseButton = false)
    {
        var name = (mouseButton ? "mouse:" : "key:") + output;
        if (!_held.TryGetValue(name, out var count)) return true;
        if (count > 1) { _held[name] = count - 1; return true; }
        _held.Remove(name);
        return Send(output, true, mouseButton);
    }

    public void ReleaseAll()
    {
        foreach (var name in _held.Keys.ToArray())
            Send(name.StartsWith("mouse:", StringComparison.Ordinal) ? name[6..] : name[4..],
                true, name.StartsWith("mouse:", StringComparison.Ordinal));
        _held.Clear();
    }

    public bool Move(int x, int y) => SendOne(new Input
        { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = x, Y = y, Flags = 1 } } });

    public bool Scroll(int clicks) => SendOne(new Input
        { Type = 0, Data = new InputUnion { Mouse = new MouseInput
            { MouseData = unchecked((uint)(clicks * 120)), Flags = 0x0800 } } });

    private bool Send(string output, bool up, bool mouseButton)
    {
        if (mouseButton)
        {
            var flags = output.Trim().ToLowerInvariant() switch
            {
                "left" => up ? 0x0004u : 0x0002u,
                "right" => up ? 0x0010u : 0x0008u,
                "middle" => up ? 0x0040u : 0x0020u,
                "x1" or "x2" => up ? 0x0100u : 0x0080u,
                _ => 0u
            };
            if (flags == 0) return false;
            return SendOne(new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput
                { Flags = flags, MouseData = output.Equals("x2", StringComparison.OrdinalIgnoreCase) ? 2u : 1u } } });
        }

        var parts = output.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;
        if (up) Array.Reverse(parts);
        var events = new List<Input>();
        foreach (var part in parts)
        {
            if (!Enum.TryParse<Key>(part, true, out var key)) return false;
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey <= 0) return false;
            var scan = MapVirtualKey((uint)virtualKey, 4);
            var flags = 0x0008u | (up ? 0x0002u : 0u) | ((scan & 0xff00) != 0 ? 0x0001u : 0u);
            events.Add(new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput
                { ScanCode = (ushort)(scan & 0xff), Flags = flags } } });
        }
        var sent = SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<Input>());
        if (sent == events.Count) { LastError = null; return true; }
        LastError = $"Windows rejected simulated input (error {Marshal.GetLastWin32Error()})";
        if (!up && sent > 0)
        {
            var releases = events.Take((int)sent).Reverse().Select(input =>
            {
                input.Data.Keyboard.Flags |= 0x0002u;
                return input;
            }).ToArray();
            SendInput((uint)releases.Length, releases, Marshal.SizeOf<Input>());
        }
        return false;
    }

    private bool SendOne(Input input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 1)
        { LastError = null; return true; }
        LastError = $"Windows rejected simulated input (error {Marshal.GetLastWin32Error()})";
        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
