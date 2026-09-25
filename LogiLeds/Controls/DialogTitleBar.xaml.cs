using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using FontAwesome.Sharp;
using UserControl = System.Windows.Controls.UserControl;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace LogiLeds.Controls;

public partial class DialogTitleBar : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(DialogTitleBar), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ShowMinimizeButtonProperty = DependencyProperty.Register(
        nameof(ShowMinimizeButton), typeof(bool), typeof(DialogTitleBar), new PropertyMetadata(false));

    public static readonly DependencyProperty ShowMaximizeButtonProperty = DependencyProperty.Register(
        nameof(ShowMaximizeButton), typeof(bool), typeof(DialogTitleBar), new PropertyMetadata(false));

    public DialogTitleBar()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (Window.GetWindow(this) is { } window)
            {
                window.StateChanged += (_, _) =>
                {
                    if (window.WindowState == WindowState.Minimized)
                        window.WindowState = WindowState.Normal;
                    UpdateMaximizeIcon(window);
                };
                UpdateMaximizeIcon(window);
            }
        };
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public bool ShowMinimizeButton
    {
        get => (bool)GetValue(ShowMinimizeButtonProperty);
        set => SetValue(ShowMinimizeButtonProperty, value);
    }

    public bool ShowMaximizeButton
    {
        get => (bool)GetValue(ShowMaximizeButtonProperty);
        set => SetValue(ShowMaximizeButtonProperty, value);
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveSource(e.OriginalSource as DependencyObject)) return;
        if (Window.GetWindow(this) is not { } window) return;
        if (e.ClickCount == 2 && ShowMaximizeButton)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            return;
        }

        window.DragMove();
    }

    private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ShowMinimizeButton && Window.GetWindow(this) is { } window)
            SystemCommands.ShowSystemMenu(window, window.PointToScreen(e.GetPosition(this)));
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
        if (MaximizeIcon is null) return;
        var isMaximized = window.WindowState == WindowState.Maximized;
        MaximizeIcon.Icon = isMaximized ? IconChar.Compress : IconChar.Expand;
        MaximizeButton.ToolTip = isMaximized ? "Restore" : "Maximize";
    }

    private static bool IsInteractiveSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ButtonBase) return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }
}