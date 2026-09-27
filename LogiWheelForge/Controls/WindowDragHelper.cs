using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace LogiWheelForge.Controls;

internal static class WindowDragHelper
{
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 2;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint windowHandle, int message, nint wParam, nint lParam);

    public static void BeginDrag(Window window)
    {
        var windowHandle = new WindowInteropHelper(window).Handle;
        if (windowHandle == 0) return;

        ReleaseCapture();
        SendMessage(windowHandle, WmNcLButtonDown, HtCaption, 0);
    }
}