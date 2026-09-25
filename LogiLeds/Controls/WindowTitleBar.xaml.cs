using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FontAwesome.Sharp;
using UserControl = System.Windows.Controls.UserControl;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace LogiLeds.Controls;

public partial class WindowTitleBar : UserControl
{
    public WindowTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.StateChanged += (_, _) => UpdateMaximizeIcon(window);
                UpdateMaximizeIcon(window);
            }
        };
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject)) return;
        var window = Window.GetWindow(this);
        if (window is null) return;
        if (e.ClickCount == 2)
        {
            window.WindowState =
                window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else WindowDragHelper.BeginDrag(window);
    }

    private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this) is { } window)
            SystemCommands.ShowSystemMenu(window, window.PointToScreen(e.GetPosition(this)));
    }

    private static bool IsInteractiveTitleBarSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase or TabItem) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is { } window) window.WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is not { } window) return;
        window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        Window.GetWindow(this)?.Close();
    }

    private void UpdateMaximizeIcon(Window window)
    {
        var isMaximized = window.WindowState == WindowState.Maximized;
        MaximizeIcon.Icon = isMaximized ? IconChar.Compress : IconChar.Expand;
        MaximizeButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }
}