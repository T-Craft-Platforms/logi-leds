using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace LogiWheelForge.Controls;

/// <summary>Draws the continuous header/sidebar seam around the content's rounded top-left corner.</summary>
public sealed class NavigationJunction : FrameworkElement
{
    public static readonly DependencyProperty SidebarWidthProperty = DependencyProperty.Register(
        nameof(SidebarWidth), typeof(double), typeof(NavigationJunction),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(
        nameof(Radius), typeof(double), typeof(NavigationJunction),
        new FrameworkPropertyMetadata(16d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SurfaceBrushProperty = DependencyProperty.Register(
        nameof(SurfaceBrush), typeof(Brush), typeof(NavigationJunction),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
        nameof(BorderBrush), typeof(Brush), typeof(NavigationJunction),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public double SidebarWidth
    {
        get => (double)GetValue(SidebarWidthProperty);
        set => SetValue(SidebarWidthProperty, value);
    }

    public double Radius
    {
        get => (double)GetValue(RadiusProperty);
        set => SetValue(RadiusProperty, value);
    }

    public Brush? SurfaceBrush
    {
        get => (Brush?)GetValue(SurfaceBrushProperty);
        set => SetValue(SurfaceBrushProperty, value);
    }

    public Brush? BorderBrush
    {
        get => (Brush?)GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (SidebarWidth <= 0 || RenderSize.Width <= SidebarWidth || RenderSize.Height <= .5) return;

        // Align the one-DIP stroke inside the header baseline and sidebar edge.
        var x = SidebarWidth - .5;
        const double y = .5;
        var radius = Math.Clamp(Radius, 0, Math.Min(RenderSize.Width - x, RenderSize.Height - y));
        var top = new Point(x + radius, y);
        var left = new Point(x, y + radius);

        // This square-minus-quarter-circle extends the sidebar surface OUTSIDE
        // its rectangle; no sidebar contents or straight borders are clipped.
        var corner = new StreamGeometry();
        using (var context = corner.Open())
        {
            context.BeginFigure(new Point(x, 0), true, true);
            context.LineTo(new Point(top.X, 0), true, false);
            context.LineTo(top, true, false);
            if (radius > 0)
                context.ArcTo(left, new Size(radius, radius), 0, false,
                    SweepDirection.Counterclockwise, true, true);
        }
        corner.Freeze();
        drawingContext.DrawGeometry(SurfaceBrush, null, corner);

        var seam = new StreamGeometry();
        using (var context = seam.Open())
        {
            context.BeginFigure(new Point(RenderSize.Width, y), false, false);
            context.LineTo(top, true, false);
            if (radius > 0)
                context.ArcTo(left, new Size(radius, radius), 0, false,
                    SweepDirection.Counterclockwise, true, true);
            context.LineTo(new Point(x, RenderSize.Height), true, false);
        }
        seam.Freeze();
        drawingContext.DrawGeometry(null, new Pen(BorderBrush, 1), seam);
    }
}
