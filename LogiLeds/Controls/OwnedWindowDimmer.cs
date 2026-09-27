using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Panel = System.Windows.Controls.Panel;

namespace LogiLeds.Controls;

internal static class OwnedWindowDimmer
{
    private static readonly DependencyProperty OverlayProperty = DependencyProperty.RegisterAttached(
        "Overlay", typeof(FrameworkElement), typeof(OwnedWindowDimmer));

    public static void Attach(Window window)
    {
        window.Loaded += (_, _) => EnsureOverlay(window);
        window.Activated += (_, _) => Update(window);
        window.Deactivated += (_, _) => Update(window);
        window.LocationChanged += (_, _) => Update(window);
        window.Closed += (_, _) =>
        {
            if (window.Owner is Window owner) Update(owner);
        };
    }

    private static void EnsureOverlay(Window window)
    {
        if (window.GetValue(OverlayProperty) is FrameworkElement) return;
        if (window.Content is not UIElement content) return;

        var root = new Grid();
        window.Content = root;
        root.Children.Add(content);

        var overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xAA, 0, 0, 0)),
            IsHitTestVisible = true,
            Visibility = Visibility.Collapsed
        };
        overlay.PreviewMouseDown += (_, args) => args.Handled = true;
        overlay.PreviewMouseUp += (_, args) => args.Handled = true;
        overlay.PreviewMouseWheel += (_, args) => args.Handled = true;
        Panel.SetZIndex(overlay, 1);
        root.Children.Add(overlay);
        window.SetValue(OverlayProperty, overlay);
        Update(window);
    }

    private static void Update(Window window)
    {
        if (window.GetValue(OverlayProperty) is not FrameworkElement overlay) return;

        overlay.Visibility = window.OwnedWindows
            .Cast<Window>()
            .Any(owned => owned.IsVisible && owned.WindowState != WindowState.Minimized)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}