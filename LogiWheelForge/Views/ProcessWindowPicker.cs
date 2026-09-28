using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using LogiWheelForge.Services;

namespace LogiWheelForge.Views;

/// <summary>Click-through selection of a window's executable path.</summary>
public sealed class ProcessWindowPicker : Window
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private string? _result;

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
        Content = new System.Windows.Controls.Border
        {
            Width = 390, Height = 88, Padding = new Thickness(20), CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 30, 0, 0), Background = new SolidColorBrush(Color.FromRgb(30, 34, 43)),
            Child = new System.Windows.Controls.TextBlock
            {
                Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap,
                Text = "Click a window to add its executable. Press Esc to cancel."
            }
        };
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
        Hide();
        GetCursorPos(out var point);
        var window = WindowFromPoint(point);
        if (window != 0)
        {
            InputMapperService.GetWindowThreadProcessId(window, out var processId);
            if (processId != Environment.ProcessId)
                _result = InputMapperService.GetProcessPath(processId);
        }
        Close();
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
}
