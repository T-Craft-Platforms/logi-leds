using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LogiWheelForge.Controls;
using LogiWheelForge.Models;
using LogiWheelForge.Services;
using LogiWheelForge.ViewModels;
using LogiWheelForge.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LogiWheelForge.Tests;

[TestClass]
public sealed class UiInteractionTests
{
    [TestMethod]
    public void MapperAndSharedControls_RenderAndRemainEditableAcrossThemesAndWidths()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { ExerciseUi(); }
            catch (Exception ex) { failure = ex; }
            finally { Application.Current?.Shutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "UI verification timed out");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void ExerciseUi()
    {
        var app = new Application();
        app.Resources.MergedDictionaries.Add(Dictionary("Dark"));
        app.Resources.MergedDictionaries.Add(Dictionary("Controls"));
        app.Resources["InteractiveCursor"] = System.Windows.Input.Cursors.Hand;
        using var service = new InputMapperService([]);
        using var model = new InputMapperViewModel(service);
        model.Initialize();
        var view = new InputMapperView { DataContext = model };
        var root = new Border { Child = view };
        root.Resources.MergedDictionaries.Add(Dictionary("Dark"));
        root.SetResourceReference(Border.BackgroundProperty, "WindowBrush");

        Layout(root, 944, 704);
        Render(root, "mapper-empty");
        model.AddProfileCommand.Execute(null);
        Layout(root, 944, 704);
        Assert.AreEqual(1, model.EditorTabIndex, "New profiles should guide the user to link an application");
        var tabs = Descendants<TabControl>(view).Single();
        tabs.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        Pump(30);
        Render(root, "mapper-applications-dark");

        model.EditorTabIndex = 0;
        Layout(root, 944, 704);
        Pump(300);
        var editor = Descendants<MapperRuleEditor>(view).Single();
        var combos = Descendants<ComboBox>(editor).ToArray();
        var control = combos.Single(combo => BindingOperations.GetBinding(combo, ComboBox.TextProperty)?.Path.Path == "Control");
        Assert.IsTrue(control.Items.Contains("Steering"));
        Assert.IsNotNull(control.Template.FindName("PART_EditableTextBox", control), "Custom selector must support typed control names");
        control.SetCurrentValue(ComboBox.TextProperty, "Button12");
        control.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();
        Assert.AreEqual("Button12", model.SelectedRule!.Control);
        control.SetCurrentValue(ComboBox.TextProperty, "Steering");
        control.GetBindingExpression(ComboBox.TextProperty)!.UpdateSource();
        var action = combos.Single(combo => BindingOperations.GetBinding(combo, Selector.SelectedItemProperty)?.Path.Path == "Action");
        Assert.AreEqual(6, action.Items.Count, "Rule editor must receive all output actions");
        action.SelectedItem = MapperActionKind.MouseButton;
        Assert.AreEqual(MapperActionKind.MouseButton, model.SelectedRule.Action);
        Assert.IsTrue(model.SelectedRule.OutputChoices.Contains("Middle"));
        action.SelectedItem = MapperActionKind.Key;
        Assert.IsTrue(model.HasUnsavedChanges);
        Assert.IsTrue(model.SelectedProfile!.Build().TryValidate(out var error), error);
        Render(root, "mapper-mappings-dark");

        var indicator = (FrameworkElement)tabs.Template.FindName("PART_SelectionIndicator", tabs);
        Assert.AreEqual(Visibility.Visible, indicator.Visibility);
        var start = ((TranslateTransform)indicator.RenderTransform).X;
        tabs.SelectedIndex = 2;
        Layout(root, 944, 704);
        Pump(60);
        var moving = ((TranslateTransform)indicator.RenderTransform).X;
        Pump(260);
        var end = ((TranslateTransform)indicator.RenderTransform).X;
        Assert.IsTrue(end > start, "Selection indicator must move to the selected header");
        if (SystemParameters.ClientAreaAnimation) Assert.IsTrue(moving > start && moving < end, "Indicator should travel between headers");
        var selectedHeader = (TabItem)tabs.ItemContainerGenerator.ContainerFromIndex(2);
        Assert.AreEqual(selectedHeader.ActualWidth, indicator.Width, .5);
        Render(root, "mapper-resistance-dark");

        foreach (var theme in new[] { "Dark", "Light" })
        {
            app.Resources.MergedDictionaries[0] = Dictionary(theme);
            root.Resources.MergedDictionaries[0] = Dictionary(theme);
            foreach (var width in new[] { 944d, 608d })
            {
                tabs.SelectedIndex = 0;
                Layout(root, width, 704);
                Pump(280);
                var title = Descendants<TextBlock>(view).Single(text => text.Text == model.PageTitle);
                var textColor = ((SolidColorBrush)root.Resources["TextBrush"]).Color;
                Assert.AreEqual(textColor, ((SolidColorBrush)title.Foreground).Color, "Headings must follow the active theme");
                var save = Descendants<Button>(view).Single(button => Equals(button.Content, "Save changes"));
                var caption = Descendants<TextBlock>(save).Single();
                Assert.AreEqual(Colors.White, ((SolidColorBrush)caption.Foreground).Color, "Accent buttons need readable captions in both themes");
                Render(root, $"mapper-{theme}-{width}");
                Assert.IsTrue(Descendants<ComboBox>(view).Where(combo => combo.ActualWidth > 0).All(combo => combo.ActualWidth > 100));
                tabs.SelectedIndex = 1;
                Layout(root, width, 704);
                Pump(280);
                Render(root, $"mapper-applications-{theme}-{width}");
                tabs.SelectedIndex = 2;
                Layout(root, width, 704);
                Pump(280);
                Render(root, $"mapper-resistance-{theme}-{width}");
            }
        }

        var checkbox = new CheckBox { Content = "Animated switch", Width = 220 };
        root.Child = checkbox;
        Layout(root, 300, 60);
        var knob = (FrameworkElement)checkbox.Template.FindName("Knob", checkbox);
        checkbox.IsChecked = true;
        Pump(60);
        var midway = ((TranslateTransform)knob.RenderTransform).X;
        Pump(200);
        Assert.IsTrue(midway > 0 && midway < 18, "Switch thumb should animate between states");
        Assert.AreEqual(18d, ((TranslateTransform)knob.RenderTransform).X, .01);
        checkbox.IsChecked = false;
        Pump(230);
        Assert.AreEqual(0d, ((TranslateTransform)knob.RenderTransform).X, .01);

        var module = new ToggleButton { Style = (Style)app.Resources["ModuleToggleStyle"] };
        root.Child = module;
        Layout(root, 46, 26);
        var thumb = (FrameworkElement)module.Template.FindName("Thumb", module);
        module.IsChecked = true;
        Pump(60);
        module.IsChecked = false;
        Pump(230);
        Assert.AreEqual(0d, ((TranslateTransform)thumb.RenderTransform).X, .01, "Interrupted switch animation must settle at its latest state");
        module.IsChecked = true;
        Pump(230);
        Assert.AreEqual(20d, ((TranslateTransform)thumb.RenderTransform).X, .01);
    }

    private static ResourceDictionary Dictionary(string name) => new()
    {
        Source = new Uri($"/LogiWheelForge;component/Themes/{name}.xaml", UriKind.Relative)
    };

    private static void Layout(FrameworkElement root, double width, double height)
    {
        root.Width = width;
        root.Height = height;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement root, string name)
    {
        var output = Environment.GetEnvironmentVariable("LOGIWHEELFORGE_RENDER_OUTPUT");
        if (output is null) return;
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png"));
        encoder.Save(stream);
    }
}
