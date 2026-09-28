using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using LogiWheelForge.Services;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;

namespace LogiWheelForge.Views;

/// <summary>Click-through selection of a window's executable path.</summary>
public sealed class ProcessWindowPicker : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect
    { public int Left, Top, Right, Bottom; }
    private delegate bool WindowCallback(nint window, nint context);
    private string? _result;
    private nint _hoverWindow;
    private readonly Border _highlight;
    private readonly DispatcherTimer _hoverTimer;

    private ProcessWindowPicker()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        Cursor = Cursors.Cross;
        var canvas = new Canvas { Background = Brushes.Transparent };
        _highlight = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(87, 195, 255)),
            BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(5),
            IsHitTestVisible = false, Visibility = System.Windows.Visibility.Collapsed
        };
        canvas.Children.Add(_highlight);
        var instructions = new Border
        {
            Width = 390, Height = 88, Padding = new Thickness(20), CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromRgb(30, 34, 43)),
            Child = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap,
                Text = "Click a window to add its executable. Press Esc to cancel."
            }
        };
        Canvas.SetLeft(instructions, Math.Max(0, (Width - 390) / 2));
        Canvas.SetTop(instructions, 30);
        canvas.Children.Add(instructions);
        Content = canvas;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _hoverTimer.Tick += (_, _) => UpdateHover();
        Loaded += (_, _) => _hoverTimer.Start();
        Closed += (_, _) => _hoverTimer.Stop();
        MouseLeftButtonDown += (_, _) => SelectWindow();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
    }

    public static string? Pick()
    {
        var picker = new ProcessWindowPicker();
        picker.ShowDialog();
        return picker._result;
    }

    private void SelectWindow()
    {
        UpdateHover();
        var window = _hoverWindow;
        Hide();
        if (window == 0)
        {
            GetCursorPos(out var point);
            window = WindowFromPoint(point);
        }
        if (window != 0)
        {
            InputMapperService.GetWindowThreadProcessId(window, out var processId);
            if (processId != Environment.ProcessId)
                _result = InputMapperService.GetProcessPath(processId);
        }
        Close();
    }

    private void UpdateHover()
    {
        GetCursorPos(out var point);
        var own = new WindowInteropHelper(this).Handle;
        nint found = 0;
        NativeRect bounds = default;
        EnumWindows((window, _) =>
        {
            if (window == own || !IsWindowVisible(window) || !GetWindowRect(window, out var rect) ||
                point.X < rect.Left || point.X >= rect.Right || point.Y < rect.Top || point.Y >= rect.Bottom)
                return true;
            InputMapperService.GetWindowThreadProcessId(window, out var processId);
            if (processId == Environment.ProcessId || processId == 0) return true;
            found = window; bounds = rect;
            return false;
        }, 0);
        _hoverWindow = found;
        _highlight.Visibility = found == 0 ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        if (found == 0) return;
        var topLeft = PointFromScreen(new System.Windows.Point(bounds.Left, bounds.Top));
        var bottomRight = PointFromScreen(new System.Windows.Point(bounds.Right, bounds.Bottom));
        Canvas.SetLeft(_highlight, topLeft.X);
        Canvas.SetTop(_highlight, topLeft.Y);
        _highlight.Width = Math.Max(0, bottomRight.X - topLeft.X);
        _highlight.Height = Math.Max(0, bottomRight.Y - topLeft.Y);
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, nint context);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
}
