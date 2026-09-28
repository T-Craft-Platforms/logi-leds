using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontAwesome.Sharp;
using UserControl = System.Windows.Controls.UserControl;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace LogiWheelForge.Controls;

public partial class WindowTitleBar : UserControl
{
    private (bool IsCompact, bool IsOverlayOpen, bool IsNarrow)? _navigationIconState;

    public event EventHandler? NavigationToggleRequested;

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

    public void SetNavigationIconState(bool isCompact, bool isOverlayOpen, bool isNarrow)
    {
        NavigationToggleButton.ToolTip = isOverlayOpen
            ? "Close navigation"
            : isCompact ? "Expand navigation" : "Collapse navigation";
        var nextState = (isCompact, isOverlayOpen, isNarrow);
        if (_navigationIconState == nextState) return;
        _navigationIconState = nextState;
        NavigationIconAnimator.PlayWiggle(NavigationToggleIcon);
    }

    public void SetNavigationLayoutWidth(double width, bool animate)
    {
        var toggleWidth = NavigationToggleButton.ActualWidth > 0
            ? NavigationToggleButton.ActualWidth
            : 38;
        var toggleAreaWidth = NavigationToggleButton.Margin.Left + toggleWidth + NavigationToggleButton.Margin.Right;
        var targetMargin = new Thickness(Math.Max(0, width - toggleAreaWidth), 0, 0, 0);
        if (animate)
        {
            BrandStack.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(BrandStack.Margin, targetMargin, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                }, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            BrandStack.BeginAnimation(FrameworkElement.MarginProperty, null);
            BrandStack.Margin = targetMargin;
        }
    }

    private void NavigationToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        NavigationToggleRequested?.Invoke(this, EventArgs.Empty);
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
