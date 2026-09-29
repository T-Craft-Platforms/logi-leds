using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TabControl = System.Windows.Controls.TabControl;
using Point = System.Windows.Point;

namespace LogiWheelForge.Controls;

/// <summary>Moves one shared selection indicator between the headers in a tab template.</summary>
public static class TabIndicatorAnimator
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TabIndicatorAnimator), new PropertyMetadata(false, OnEnabledChanged));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(IndicatorState), typeof(TabIndicatorAnimator));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static void OnEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not TabControl tabs) return;
        if (tabs.GetValue(StateProperty) is IndicatorState previous) previous.Detach();
        tabs.ClearValue(StateProperty);
        if (!(bool)args.NewValue) return;
        var state = new IndicatorState(tabs);
        tabs.SetValue(StateProperty, state);
        state.Attach();
    }

    private sealed class IndicatorState(TabControl tabs)
    {
        private FrameworkElement? _indicator;
        private double _left = double.NaN, _top = double.NaN, _width = double.NaN, _height = double.NaN;
        private bool _selectionChanged;

        public void Attach()
        {
            tabs.Loaded += OnLoaded;
            tabs.Unloaded += OnUnloaded;
            tabs.SelectionChanged += OnSelectionChanged;
            if (tabs.IsLoaded) OnLoaded(tabs, new RoutedEventArgs());
        }

        public void Detach()
        {
            tabs.Loaded -= OnLoaded;
            tabs.Unloaded -= OnUnloaded;
            tabs.SelectionChanged -= OnSelectionChanged;
            tabs.LayoutUpdated -= OnLayoutUpdated;
        }

        private void OnLoaded(object sender, RoutedEventArgs args)
        {
            _indicator = null;
            tabs.LayoutUpdated -= OnLayoutUpdated;
            tabs.LayoutUpdated += OnLayoutUpdated;
            Update(false);
        }

        private void OnUnloaded(object sender, RoutedEventArgs args)
        {
            tabs.LayoutUpdated -= OnLayoutUpdated;
            _indicator = null;
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            if (!ReferenceEquals(args.Source, tabs)) return;
            _selectionChanged = true;
            Update(true);
        }

        private void OnLayoutUpdated(object? sender, EventArgs args) => Update(_selectionChanged);

        private void Update(bool animate)
        {
            if (tabs.Template?.FindName("PART_SelectionIndicator", tabs) is not FrameworkElement indicator ||
                indicator.Parent is not FrameworkElement host) return;
            if (!ReferenceEquals(_indicator, indicator))
            {
                _indicator = indicator;
                _left = _top = _width = _height = double.NaN;
            }
            var item = tabs.SelectedItem as TabItem;
            if (item is null && tabs.SelectedIndex >= 0 && tabs.SelectedIndex < tabs.Items.Count)
                item = tabs.ItemContainerGenerator.ContainerFromIndex(tabs.SelectedIndex) as TabItem;
            if (item is null || item.Visibility != Visibility.Visible || item.ActualWidth <= 0)
            {
                indicator.Visibility = Visibility.Hidden;
                return;
            }
            var origin = item.TranslatePoint(new Point(0, 0), host);
            var width = item.ActualWidth;
            var height = item.ActualHeight;
            if (Close(_left, origin.X) && Close(_top, origin.Y) && Close(_width, width) && Close(_height, height))
            {
                _selectionChanged = false;
                return;
            }
            indicator.Visibility = Visibility.Visible;
            if (indicator.RenderTransform is not TranslateTransform) indicator.RenderTransform = new TranslateTransform();
            var transform = (TranslateTransform)indicator.RenderTransform;
            animate &= !double.IsNaN(_left) && SystemParameters.ClientAreaAnimation;
            Move(indicator, FrameworkElement.WidthProperty, width, animate);
            Move(indicator, FrameworkElement.HeightProperty, height, animate);
            Move(transform, TranslateTransform.XProperty, origin.X, animate);
            Move(transform, TranslateTransform.YProperty, origin.Y, animate);
            (_left, _top, _width, _height) = (origin.X, origin.Y, width, height);
            _selectionChanged = false;
        }

        private static bool Close(double a, double b) => Math.Abs(a - b) < .1;

        private static void Move(Animatable target, DependencyProperty property, double value, bool animate)
        {
            var current = (double)target.GetValue(property);
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            if (!animate || !double.IsFinite(current)) return;
            target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }

        private static void Move(FrameworkElement target, DependencyProperty property, double value, bool animate)
        {
            var current = (double)target.GetValue(property);
            target.BeginAnimation(property, null);
            target.SetValue(property, value);
            if (!animate || !double.IsFinite(current)) return;
            target.BeginAnimation(property, new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        }
    }
}
