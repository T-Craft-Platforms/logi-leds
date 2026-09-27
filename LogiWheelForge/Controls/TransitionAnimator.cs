using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Panel = System.Windows.Controls.Panel;

namespace LogiWheelForge.Controls;

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

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIndex(DependencyObject element, int value) => element.SetValue(IndexProperty, value);
    public static int GetIndex(DependencyObject element) => (int)element.GetValue(IndexProperty);
    public static void SetIsActive(DependencyObject element, bool value) => element.SetValue(IsActiveProperty, value);
    public static bool GetIsActive(DependencyObject element) => (bool)element.GetValue(IsActiveProperty);

    public static void Play(FrameworkElement element, int direction) => Animate(element, Math.Sign(direction));

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
                Animate(element, Math.Sign(currentIndex - previousIndex));
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
            Animate(presenter, direction);
        });
    }

    private static void Animate(FrameworkElement element, int direction)
    {
        const double slideDistance = 72;
        var offset = direction == 0
            ? new TranslateTransform(0, 9)
            : new TranslateTransform(direction * slideDistance, 0);
        element.RenderTransform = offset;
        element.Opacity = 0;

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        var slide = new DoubleAnimation(direction == 0 ? 9 : direction * slideDistance, 0,
            TimeSpan.FromMilliseconds(420))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        offset.BeginAnimation(direction == 0 ? TranslateTransform.YProperty : TranslateTransform.XProperty,
            slide, HandoffBehavior.SnapshotAndReplace);
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