using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Panel = System.Windows.Controls.Panel;

namespace LogiWheelForge.Controls;

public enum TransitionAxis
{
    Vertical,
    Horizontal
}

public static class TransitionAnimator
{
    private static readonly ConditionalWeakTable<ContentPresenter, PresenterState> States = new();
    private static readonly ConditionalWeakTable<Panel, ActivePanelState> ActivePanels = new();

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(TransitionAnimator), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty IndexProperty = DependencyProperty.RegisterAttached(
        "Index", typeof(int), typeof(TransitionAnimator), new PropertyMetadata(-1));

    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.RegisterAttached(
        "IsActive", typeof(bool), typeof(TransitionAnimator), new PropertyMetadata(false, OnIsActiveChanged));

    public static readonly DependencyProperty AxisProperty = DependencyProperty.RegisterAttached(
        "Axis", typeof(TransitionAxis), typeof(TransitionAnimator), new PropertyMetadata(TransitionAxis.Vertical));

    public static readonly DependencyProperty DurationProperty = DependencyProperty.RegisterAttached(
        "Duration", typeof(double), typeof(TransitionAnimator), new PropertyMetadata(420d));

    public static readonly DependencyProperty DistanceProperty = DependencyProperty.RegisterAttached(
        "Distance", typeof(double), typeof(TransitionAnimator), new PropertyMetadata(28d));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIndex(DependencyObject element, int value) => element.SetValue(IndexProperty, value);
    public static int GetIndex(DependencyObject element) => (int)element.GetValue(IndexProperty);
    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);
    public static void SetAxis(DependencyObject element, TransitionAxis value) => element.SetValue(AxisProperty, value);
    public static TransitionAxis GetAxis(DependencyObject element) => (TransitionAxis)element.GetValue(AxisProperty);
    public static void SetDuration(DependencyObject element, double value) => element.SetValue(DurationProperty, value);
    public static double GetDuration(DependencyObject element) => (double)element.GetValue(DurationProperty);
    public static void SetDistance(DependencyObject element, double value) => element.SetValue(DistanceProperty, value);
    public static double GetDistance(DependencyObject element) => (double)element.GetValue(DistanceProperty);

    public static void Play(FrameworkElement element, int direction) =>
        Play(element, direction, TransitionAxis.Vertical, 420);

    public static void Play(FrameworkElement element, int direction, TransitionAxis axis,
        double durationMilliseconds = 420, double distance = 28) =>
        Animate(element, Math.Sign(direction), axis, durationMilliseconds, distance);

    private static void OnIsActiveChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement frameworkElement) return;
        frameworkElement.Loaded -= OnActiveElementLoaded;
        if ((bool)args.NewValue)
        {
            frameworkElement.Loaded += OnActiveElementLoaded;
            if (frameworkElement.IsLoaded) QueueActiveAnimation(frameworkElement);
        }
    }

    private static void OnActiveElementLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is not FrameworkElement element) return;
        if (element.Parent is Panel parent)
        {
            var state = ActivePanels.GetValue(parent, _ => new ActivePanelState());
            if (state.LastIndex is null) state.LastIndex = GetIndex(element);
        }
    }

    private static void QueueActiveAnimation(FrameworkElement element)
    {
        element.Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            if (!element.IsLoaded || !GetIsActive(element) ||
                element.Parent is not Panel parent) return;
            var state = ActivePanels.GetValue(parent, _ => new ActivePanelState());
            var currentIndex = GetIndex(element);
            if (state.LastIndex is int previousIndex && previousIndex != currentIndex)
                Animate(element, Math.Sign(currentIndex - previousIndex), GetAxis(element), GetDuration(element), GetDistance(element));
            state.LastIndex = currentIndex;
        });
    }

    private static void OnIsEnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ContentPresenter presenter) return;
        if ((bool)args.NewValue)
        {
            presenter.Loaded += OnPresenterLoaded;
            presenter.Unloaded += OnPresenterUnloaded;
            if (presenter.IsLoaded) Attach(presenter);
        }
        else
        {
            presenter.Loaded -= OnPresenterLoaded;
            presenter.Unloaded -= OnPresenterUnloaded;
            Detach(presenter);
        }
    }

    private static void OnPresenterLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentPresenter presenter) Attach(presenter);
    }

    private static void OnPresenterUnloaded(object sender, RoutedEventArgs args)
    {
        if (sender is ContentPresenter presenter) Detach(presenter);
    }

    private static void Attach(ContentPresenter presenter)
    {
        var state = States.GetValue(presenter, _ => new PresenterState());
        if (state.IsAttached) return;
        state.LastContent = presenter.Content;
        state.LastIndex = GetIndex(presenter);
        state.IsAttached = true;
        state.ContentDescriptor = DependencyPropertyDescriptor.FromProperty(
            ContentPresenter.ContentProperty, typeof(ContentPresenter));
        state.ContentChangedHandler = (_, _) => OnContentChanged(presenter, state);
        state.ContentDescriptor?.AddValueChanged(presenter, state.ContentChangedHandler);
    }

    private static void Detach(ContentPresenter presenter)
    {
        if (!States.TryGetValue(presenter, out var state) || !state.IsAttached) return;
        if (state.ContentChangedHandler is not null)
            state.ContentDescriptor?.RemoveValueChanged(presenter, state.ContentChangedHandler);
        state.ContentChangedHandler = null;
        state.ContentDescriptor = null;
        state.IsAttached = false;
    }

    private static void OnContentChanged(ContentPresenter presenter, PresenterState state)
    {
        if (ReferenceEquals(state.LastContent, presenter.Content)) return;
        state.LastContent = presenter.Content;
        var changeVersion = ++state.ChangeVersion;
        presenter.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, () =>
        {
            if (!state.IsAttached || changeVersion != state.ChangeVersion) return;
            var currentIndex = GetIndex(presenter);
            var direction = state.LastIndex < 0 || currentIndex < 0
                ? 0
                : Math.Sign(currentIndex - state.LastIndex);
            state.LastIndex = currentIndex;
            Animate(presenter, direction, GetAxis(presenter), GetDuration(presenter), GetDistance(presenter));
        });
    }

    private static void Animate(FrameworkElement element, int direction, TransitionAxis axis,
        double durationMilliseconds, double distance)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Clamp(durationMilliseconds, 180, 1200));
        var offset = new TranslateTransform();
        var offsetProperty = axis == TransitionAxis.Horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        var slideDistance = Math.Clamp(Math.Abs(distance), 0, 120);
        var initialOffset = direction == 0 ? Math.Min(7, slideDistance) : direction * slideDistance;
        element.RenderTransform = offset;
        element.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(initialOffset, 0, duration)
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        offset.BeginAnimation(offsetProperty, slide, HandoffBehavior.SnapshotAndReplace);
    }

    private sealed class PresenterState
    {
        public object? LastContent { get; set; }
        public bool IsAttached { get; set; }
        public int LastIndex { get; set; }
        public int ChangeVersion { get; set; }
        public DependencyPropertyDescriptor? ContentDescriptor { get; set; }
        public EventHandler? ContentChangedHandler { get; set; }
    }

    private sealed class ActivePanelState
    {
        public int? LastIndex { get; set; }
    }
}
