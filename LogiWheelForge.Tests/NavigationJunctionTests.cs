using System.IO;
using System.Runtime.ExceptionServices;
using System.Xml.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Markup;
using LogiWheelForge.Controls;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class NavigationJunctionTests
{
    [TestMethod]
    public void ShellMarkup_PlacesCornerAtHeaderBaseline_AndFollowsSidebarLayout()
    {
        OnStaThread(() =>
        {
            XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "RenderResources", "MainWindow.xaml")).Root!;
            var rootMarkup = new XElement(presentation + "Grid", source.Attributes().Where(a => a.IsNamespaceDeclaration));
            rootMarkup.SetAttributeValue(XNamespace.Xmlns + "controls", "clr-namespace:LogiWheelForge.Controls;assembly=LogiWheelForge");
            rootMarkup.SetAttributeValue(XNamespace.Xmlns + "fa", "clr-namespace:FontAwesome.Sharp;assembly=FontAwesome.Sharp");
            var dictionary = new XElement(presentation + "ResourceDictionary",
                new XElement(presentation + "ResourceDictionary.MergedDictionaries",
                    new XElement(presentation + "ResourceDictionary", new XAttribute("Source",
                        new Uri(Path.Combine(AppContext.BaseDirectory, "RenderResources", "Dark.xaml")).AbsoluteUri))),
                source.Element(presentation + "Window.Resources")!.Elements(presentation + "Style"));
            rootMarkup.Add(new XElement(presentation + "Grid.Resources", dictionary));
            var frame = new XElement(source.Element(presentation + "Border")!);
            // Render the real shell layout without constructing its hardware view model
            // or live window. Replace only the unrelated title-bar control and events.
            foreach (var attribute in frame.DescendantsAndSelf().Attributes().Where(a => a.Value.Contains("_On")).ToArray())
                attribute.Remove();
            frame.Descendants().Single(e => e.Name.LocalName == "WindowTitleBar").ReplaceWith(
                new XElement(presentation + "Border", new XAttribute("Background", "{DynamicResource SurfaceBrush}"),
                    new XElement(presentation + "TextBlock", new XAttribute("Text", "LogiWheel Forge"),
                        new XAttribute("Foreground", "{DynamicResource TextBrush}"), new XAttribute("Margin", "98,0,0,0"),
                        new XAttribute("VerticalAlignment", "Center"), new XAttribute("FontWeight", "SemiBold"))));
            foreach (var element in frame.Descendants())
            {
                if (element.Name.NamespaceName == "clr-namespace:LogiWheelForge.Controls")
                    element.Name = XName.Get(element.Name.LocalName, "clr-namespace:LogiWheelForge.Controls;assembly=LogiWheelForge");
                else if (element.Name.NamespaceName == "http://schemas.awesome.incremented/wpf/xaml/fontawesome.sharp")
                    element.Name = XName.Get(element.Name.LocalName, "clr-namespace:FontAwesome.Sharp;assembly=FontAwesome.Sharp");
            }
            rootMarkup.Add(frame);
            var root = (Grid)XamlReader.Parse(rootMarkup.ToString());
            root.Width = 420;
            root.Height = 620;
            var sidebar = (Border)root.FindName("SidebarPanel");
            var column = (ColumnDefinition)root.FindName("SidebarColumn");
            var page = (ContentPresenter)root.FindName("PageTransitionPresenter");
            page.Content = new TextBlock
            {
                Text = "Dashboard", FontSize = 30, FontWeight = FontWeights.Bold,
                Foreground = (Brush)root.Resources["TextBrush"], Margin = new Thickness(24, 38, 0, 0)
            };
            foreach (var (columnWidth, width, overlay) in new[]
                     { (72d, 72d, false), (144d, 144d, false), (216d, 216d, false), (72d, 216d, true) })
            {
                column.Width = new GridLength(columnWidth);
                Grid.SetColumnSpan(sidebar, overlay ? 2 : 1);
                sidebar.HorizontalAlignment = overlay ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
                sidebar.Width = overlay ? width : double.NaN;
                foreach (var buttonName in new[] { "Dashboard", "Led", "Mapper", "Settings" })
                {
                    var button = (RadioButton)root.FindName($"{buttonName}NavigationButton");
                    button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    button.Padding = width == 72 ? new Thickness(0) : new Thickness(16, 9, 16, 9);
                    ((ColumnDefinition)root.FindName($"{buttonName}IconColumn")).Width = new GridLength(width == 72 ? 48 : 18);
                }
                root.Measure(new Size(420, 620));
                root.Arrange(new Rect(0, 0, 420, 620));
                root.UpdateLayout();
                var bitmap = Render(root, 2);
                var origin = sidebar.TranslatePoint(new Point(0, 0), root);
                Assert.AreEqual(55d, origin.Y, .001, "Junction must begin directly below the 54-DIP title bar");
                var surface = ((SolidColorBrush)root.Resources["SurfaceBrush"]).Color;
                var body = ((SolidColorBrush)root.Resources["WindowBrush"]).Color;
                AssertPixel(bitmap, 2, origin.X + width + 2, origin.Y + 2, surface, "Missing outward corner at actual shell junction");
                AssertPixel(bitmap, 2, origin.X + width + 14, origin.Y + 14, body, "Misplaced curve in actual shell");
                var output = Environment.GetEnvironmentVariable("LOGIWHEELFORGE_RENDER_OUTPUT");
                if (output == null) continue;
                Directory.CreateDirectory(output);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(bitmap, new Int32Rect(0, 0, 840, 440))));
                using var stream = File.Create(Path.Combine(output, overlay ? "shell-overlay.png" : $"shell-{width}.png"));
                encoder.Save(stream);
            }
        });
    }

    [TestMethod]
    public void RenderedCorner_ExtendsOutsideSidebar_AndHasNoProtrudingBorders()
    {
        OnStaThread(() =>
        {
            foreach (var theme in new[] { "Dark", "Light" })
            {
                using var themeStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory,
                    "RenderResources", $"{theme}.xaml"));
                var resources = (ResourceDictionary)XamlReader.Load(themeStream);
                var surface = (SolidColorBrush)resources["SurfaceBrush"];
                var background = (SolidColorBrush)resources["WindowBrush"];
                var border = (SolidColorBrush)resources["BorderBrush"];
                var root = new Grid { Background = background, Width = 420, Height = 130 };
                var sidebar = new Border
                {
                    Background = surface, Width = 72, HorizontalAlignment = HorizontalAlignment.Left
                };
                var junction = new NavigationJunction { SurfaceBrush = surface, BorderBrush = border };
                junction.SetBinding(NavigationJunction.SidebarWidthProperty,
                    new Binding(nameof(FrameworkElement.ActualWidth)) { Source = sidebar });
                root.Children.Add(sidebar);
                root.Children.Add(junction);

                // Exercise one live binding through collapsed, intermediate, expanded,
                // and interrupted/reversed sidebar widths, including fractional DIPs.
                foreach (var width in new[] { 72d, 108.5, 144d, 180.25, 216d, 144d, 72d })
                {
                    sidebar.Width = width;
                    root.Measure(new Size(420, 130));
                    root.Arrange(new Rect(0, 0, 420, 130));
                    root.UpdateLayout();
                    Assert.AreEqual(width, junction.SidebarWidth, .001);
                    foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
                    {
                        var bitmap = Render(root, scale);
                        AssertPixel(bitmap, scale, width - 8, 0, surface.Color, "Header border extends into sidebar");
                        AssertPixel(bitmap, scale, width - 2, 12, surface.Color, "Corner cuts into sidebar");
                        AssertPixel(bitmap, scale, width + 2, 2, surface.Color, "Missing outward corner fill");
                        AssertPixel(bitmap, scale, width + 14, 14, background.Color, "Corner curves in the wrong direction");
                        AssertPixel(bitmap, scale, width, 3, surface.Color, "Vertical border protrudes through corner");
                        AssertPixel(bitmap, scale, width + 4, 0, surface.Color, "Horizontal border protrudes through corner");
                        // At integer widths and 100% scale the straight strokes align
                        // exactly to pixels, making their endpoints unambiguous.
                        if (scale == 1 && width == Math.Floor(width))
                        {
                            AssertPixel(bitmap, scale, width - 1, 40, border.Color, "Missing vertical seam");
                            AssertPixel(bitmap, scale, width + 30, 0, border.Color, "Missing horizontal seam");
                        }
                        var output = Environment.GetEnvironmentVariable("LOGIWHEELFORGE_RENDER_OUTPUT");
                        if (output != null && scale == 2 && (width == 72 || width == 216))
                        {
                            Directory.CreateDirectory(output);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"junction-{theme}-{width}.png"));
                            encoder.Save(stream);
                        }
                    }
                }
            }
        });
    }

    [TestMethod]
    public void RenderedCorner_HandlesHiddenAndVerySmallLayouts()
    {
        OnStaThread(() =>
        {
            foreach (var height in new[] { 0d, .25, .5, 1d, 8d })
            foreach (var width in new[] { 0d, 72d, 420d })
            {
                var junction = new NavigationJunction
                {
                    SidebarWidth = width, SurfaceBrush = Brushes.Black, BorderBrush = Brushes.White
                };
                junction.Measure(new Size(420, height));
                junction.Arrange(new Rect(0, 0, 420, height));
                junction.UpdateLayout();
                var bitmap = new RenderTargetBitmap(420, 10, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(junction);
            }
        });
    }

    private static RenderTargetBitmap Render(FrameworkElement element, double scale)
    {
        var bitmap = new RenderTargetBitmap((int)(element.ActualWidth * scale),
            (int)(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }

    private static void AssertPixel(BitmapSource bitmap, double scale, double x, double y,
        Color expected, string reason)
    {
        var pixel = new byte[4];
        bitmap.CopyPixels(new Int32Rect((int)(x * scale), (int)(y * scale), 1, 1), pixel, 4, 0);
        var actual = Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
        Assert.AreEqual(expected, actual, $"{reason} at ({x}, {y}), scale {scale}");
    }

    private static void OnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
