using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using LogiWheelForge.Controls;
using LogiWheelForge.ViewModels;
using Application = System.Windows.Application;
using Icon = System.Drawing.Icon;
using RadioButton = System.Windows.Controls.RadioButton;

namespace LogiWheelForge;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmDoNotRound = 1;
    private const double CompactNavigationBreakpoint = 1000;
    private const double ExpandedNavigationWidth = 216;
    private const double CompactNavigationWidth = 72;
    private const double NavigationCornerRadius = 16;
    private readonly ToolStripMenuItem _startStopMenuItem;
    private readonly ToolStripMenuItem _mapperMenuItem;
    private readonly NotifyIcon _trayIcon;
    private readonly MainViewModel _viewModel;
    private bool _allowClose, _shownTrayHint, _exiting, _trayDisposed, _startupComplete;
    private bool _userCollapsedNavigation, _isNarrowWindow, _isNavigationOverlayOpen;
    private double _navigationColumnTarget = ExpandedNavigationWidth;
    private bool? _navigationLabelsVisible;
    private int _navigationLayoutVersion;
    private int _lastSelectedTab;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        OwnedWindowDimmer.Attach(this);
        UpdateWindowChromeMetrics();
        _viewModel = viewModel;
        _lastSelectedTab = viewModel.SelectedTab;
        DataContext = viewModel;
        TitleBar.NavigationToggleRequested += (_, _) => ToggleNavigation();
        _viewModel.ExitRequested += async (_, _) => await ExitAsync();
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        SizeChanged += (_, _) => UpdateNavigationLayout(animate: true);
        Loaded += (_, _) =>
        {
            SynchronizeNavigationSelection(_viewModel.SelectedTab);
            UpdateNavigationLayout(animate: false);
        };

        _startStopMenuItem = new ToolStripMenuItem("Stop LED Indicator");
        _startStopMenuItem.Click += (_, _) => _viewModel.Settings.StartStopCommand.Execute(null);
        _mapperMenuItem = new ToolStripMenuItem("Start Input Mapper");
        _mapperMenuItem.Click += (_, _) => _viewModel.Mapper.ToggleCommand.Execute(null);
        var openItem = new ToolStripMenuItem("Open LogiWheel Forge");
        openItem.Click += (_, _) => RestoreWindow();
        var exitItem = new ToolStripMenuItem("Exit LogiWheel Forge");
        exitItem.Click += async (_, _) => await ExitAsync();
        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_startStopMenuItem);
        menu.Items.Add(_mapperMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) =>
        {
            _startStopMenuItem.Text = _viewModel.IsRunning ? "Stop LED Indicator" : "Start LED Indicator";
            _mapperMenuItem.Text = _viewModel.Mapper.IsRunning ? "Stop Input Mapper" : "Start Input Mapper";
        };
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LogiWheelForge.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : null;
        _trayIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application, Text = "LogiWheel Forge", Visible = false, ContextMenuStrip = menu
        };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += (_, _) => DisposeTrayIcon();
        StateChanged += OnStateChanged;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            var cornerPreference = DwmDoNotRound;
            _ = DwmSetWindowAttribute(windowHandle, DwmWindowCornerPreferenceAttribute, ref cornerPreference,
                sizeof(int));
            WindowState = WindowState.Normal;
            await _viewModel.InitializeAsync(windowHandle);
            var settings = _viewModel.CurrentSettings;
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
            if (settings.WindowMaximized) WindowState = WindowState.Maximized;
            else CenterWindowOnScreen();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Startup failed: {ex}");
        }
        finally
        {
            _startupComplete = true;
            if (!IsVisible) Show();
            Activate();
            UpdateTrayIconVisibility();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (!_startupComplete)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        if (_viewModel.CloseToTray) HideToTray();
        else _ = ExitAsync();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowChromeMetrics();
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedTab))
        {
            var selectedTab = _viewModel.SelectedTab;
            var direction = Math.Sign(selectedTab - _lastSelectedTab);
            _lastSelectedTab = selectedTab;
            SynchronizeNavigationSelection(selectedTab);
            if (direction != 0) PlaySelectedNavigationIcon(selectedTab);
            if (direction != 0)
                Dispatcher.BeginInvoke(DispatcherPriority.Render,
                    new Action(() => TransitionAnimator.Play(PageTransitionPresenter, direction,
                        TransitionAxis.Vertical, 420)));
        }

        if (e.PropertyName == nameof(MainViewModel.CloseToTray)) UpdateTrayIconVisibility();
    }

    private void NavigationButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { IsChecked: true, Tag: string tag } && int.TryParse(tag, out var index))
            _viewModel.SelectedTab = index;
        if (_isNavigationOverlayOpen) CloseNavigationOverlay();
    }

    private void NavigationBackdrop_OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        CloseNavigationOverlay();
        e.Handled = true;
    }

    private void ToggleNavigation()
    {
        if (_isNarrowWindow)
        {
            if (_isNavigationOverlayOpen) CloseNavigationOverlay();
            else OpenNavigationOverlay();
            return;
        }

        _userCollapsedNavigation = !_userCollapsedNavigation;
        UpdateNavigationLayout(animate: true);
    }

    private void OpenNavigationOverlay()
    {
        _isNavigationOverlayOpen = true;
        NavigationBackdrop.Visibility = Visibility.Visible;
        NavigationBackdrop.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
        AnimateSidebarWidth(ExpandedNavigationWidth);
        UpdateNavigationVisuals();
    }

    private void CloseNavigationOverlay()
    {
        if (!_isNavigationOverlayOpen) return;
        _isNavigationOverlayOpen = false;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (!_isNavigationOverlayOpen) NavigationBackdrop.Visibility = Visibility.Collapsed;
        };
        NavigationBackdrop.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        AnimateSidebarWidth(CompactNavigationWidth);
        UpdateNavigationVisuals();
    }

    private void UpdateNavigationLayout(bool animate)
    {
        var isNarrow = ActualWidth < CompactNavigationBreakpoint;
        if (isNarrow && !_isNarrowWindow) _isNavigationOverlayOpen = false;
        var crossedCompactBreakpoint = isNarrow != _isNarrowWindow;
        _isNarrowWindow = isNarrow;
        var targetWidth = isNarrow || _userCollapsedNavigation
            ? CompactNavigationWidth
            : ExpandedNavigationWidth;
        if (Math.Abs(_navigationColumnTarget - targetWidth) > .5)
        {
            _navigationColumnTarget = targetWidth;
            AnimateSidebarColumn(targetWidth, animate);
        }

        if (isNarrow)
        {
            if (crossedCompactBreakpoint && animate)
                AnimateSidebarWidth(_isNavigationOverlayOpen ? ExpandedNavigationWidth : CompactNavigationWidth);
            else if (_isNavigationOverlayOpen) AnimateSidebarWidth(ExpandedNavigationWidth);
            else SetSidebarWidth(CompactNavigationWidth);
            NavigationBackdrop.Visibility = _isNavigationOverlayOpen ? Visibility.Visible : Visibility.Collapsed;
            NavigationBackdrop.Opacity = _isNavigationOverlayOpen ? 1 : 0;
        }
        else
        {
            _isNavigationOverlayOpen = false;
            if (crossedCompactBreakpoint && animate) AnimateSidebarWidth(targetWidth);
            else if (Math.Abs(SidebarPanel.Width - targetWidth) > .5 || double.IsNaN(SidebarPanel.Width))
            {
                if (animate) AnimateSidebarWidth(targetWidth);
                else SetSidebarWidth(targetWidth);
            }
            NavigationBackdrop.BeginAnimation(OpacityProperty, null);
            NavigationBackdrop.Visibility = Visibility.Collapsed;
            NavigationBackdrop.Opacity = 0;
        }

        UpdateNavigationVisuals();
    }

    private void AnimateSidebarColumn(double targetWidth, bool animate)
    {
        var fromWidth = SidebarColumn.ActualWidth;
        SidebarColumn.Width = new GridLength(targetWidth);
        if (!animate || Math.Abs(fromWidth - targetWidth) < .5)
        {
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            return;
        }

        var animation = new GridLengthAnimation
        {
            From = new GridLength(fromWidth),
            To = new GridLength(targetWidth),
            Duration = TimeSpan.FromMilliseconds(260),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) => SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
        SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateSidebarWidth(double targetWidth)
    {
        var fromWidth = double.IsNaN(SidebarPanel.Width) ? SidebarPanel.ActualWidth : SidebarPanel.Width;
        SidebarPanel.Width = targetWidth;
        if (Math.Abs(fromWidth - targetWidth) < .5)
        {
            SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            return;
        }

        var animation = new DoubleAnimation(fromWidth, targetWidth, TimeSpan.FromMilliseconds(240))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) => SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
        SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void SetSidebarWidth(double width)
    {
        SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
        SidebarPanel.Width = width;
    }

    private void UpdateNavigationVisuals()
    {
        var isCompact = _isNarrowWindow || _userCollapsedNavigation;
        var showLabels = !isCompact || _isNavigationOverlayOpen;
        var animateLabels = _navigationLabelsVisible is bool wasVisible && wasVisible != showLabels;
        _navigationLabelsVisible = showLabels;
        var layoutVersion = ++_navigationLayoutVersion;
        var visibleSidebarWidth = _isNavigationOverlayOpen
            ? ExpandedNavigationWidth
            : isCompact ? CompactNavigationWidth : ExpandedNavigationWidth;
        var targetDividerMargin = new Thickness(Math.Max(0, visibleSidebarWidth - NavigationCornerRadius), 0, 0, 0);
        SidebarPanel.BeginAnimation(Border.PaddingProperty, null);
        SidebarPanel.Padding = new Thickness(12, 18, 12, 18);
        if (animateLabels)
        {
            TopBarDivider.BeginAnimation(FrameworkElement.MarginProperty,
                new ThicknessAnimation(TopBarDivider.Margin, targetDividerMargin, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                }, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            TopBarDivider.BeginAnimation(FrameworkElement.MarginProperty, null);
            TopBarDivider.Margin = targetDividerMargin;
        }

        SetNavigationItemLayout(DashboardNavigationButton, DashboardNavigationLabel, DashboardNavigationIcon, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(LedNavigationButton, LedNavigationLabel, LedNavigationIcon, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(MapperNavigationButton, MapperNavigationLabel, MapperNavigationIcon, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(SettingsNavigationButton, SettingsNavigationLabel, SettingsNavigationIcon, showLabels, animateLabels, layoutVersion);
        TitleBar.SetNavigationIconState(isCompact && !_isNavigationOverlayOpen,
            _isNavigationOverlayOpen, _isNarrowWindow);
    }

    private void SetNavigationItemLayout(RadioButton button, TextBlock label, FrameworkElement icon,
        bool showLabel, bool animate, int layoutVersion)
    {
        button.Width = showLabel ? 188 : 47;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        button.Padding = showLabel ? new Thickness(16, 9, 16, 9) : new Thickness(0);
        button.HorizontalContentAlignment = showLabel
            ? System.Windows.HorizontalAlignment.Left
            : System.Windows.HorizontalAlignment.Center;
        var targetIconMargin = showLabel ? new Thickness(0, 0, 12, 0) : new Thickness(0);
        if (animate)
        {
            var iconMargin = new ThicknessAnimation(icon.Margin, targetIconMargin, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            icon.BeginAnimation(FrameworkElement.MarginProperty, iconMargin, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            icon.BeginAnimation(FrameworkElement.MarginProperty, null);
            icon.Margin = targetIconMargin;
        }

        if (showLabel)
        {
            label.Visibility = Visibility.Visible;
            if (!animate)
            {
                label.BeginAnimation(OpacityProperty, null);
                label.Opacity = 1;
                label.RenderTransform = Transform.Identity;
                return;
            }

            label.Opacity = 0;
            var slideIn = new TranslateTransform(-8, 0);
            label.RenderTransform = slideIn;
            label.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
            slideIn.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        }
        else if (animate && label.Visibility == Visibility.Visible)
        {
            var slideOut = new TranslateTransform();
            label.RenderTransform = slideOut;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
            fadeOut.Completed += (_, _) =>
            {
                if (layoutVersion != _navigationLayoutVersion || _navigationLabelsVisible == true) return;
                label.Visibility = Visibility.Collapsed;
                label.BeginAnimation(OpacityProperty, null);
                label.Opacity = 1;
                label.RenderTransform = Transform.Identity;
            };
            label.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
            slideOut.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(170))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                }, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            label.Visibility = Visibility.Collapsed;
            label.Opacity = 1;
            label.RenderTransform = Transform.Identity;
        }
    }

    private void SynchronizeNavigationSelection(int selectedIndex)
    {
        DashboardNavigationButton.IsChecked = selectedIndex == 0;
        LedNavigationButton.IsChecked = selectedIndex == 1;
        MapperNavigationButton.IsChecked = selectedIndex == 2;
        SettingsNavigationButton.IsChecked = selectedIndex == 3;
    }

    private void PlaySelectedNavigationIcon(int selectedIndex)
    {
        var icon = selectedIndex switch
        {
            1 => LedNavigationIcon,
            2 => MapperNavigationIcon,
            3 => (FrameworkElement)SettingsNavigationIcon,
            _ => DashboardNavigationIcon
        };
        if (selectedIndex == 3) NavigationIconAnimator.PlayFullTurn(icon);
        else NavigationIconAnimator.PlayWiggle(icon);
    }

    private void UpdateTrayIconVisibility()
    {
        if (_trayDisposed) return;
        _trayIcon.Visible = _viewModel.CloseToTray;
        if (_viewModel.CloseToTray)
        {
            _shownTrayHint = false;
            return;
        }

        if (_startupComplete && !IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }
    }

    private void HideToTray()
    {
        if (!_trayDisposed) _trayIcon.Visible = _viewModel.CloseToTray;
        Hide();
        if (_shownTrayHint) return;
        _shownTrayHint = true;
        _trayIcon.ShowBalloonTip(2200, "LogiWheel Forge is running", "Open or exit from the notification area.",
            ToolTipIcon.Info);
    }

    private void RestoreWindow()
    {
        if (!_trayDisposed) _trayIcon.Visible = _viewModel.CloseToTray;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        if (_viewModel.Mapper.HasUnsavedChanges)
        {
            RestoreWindow();
            var choice = System.Windows.MessageBox.Show(this, "Save Input Mapper profile changes before exiting?",
                "Unsaved profiles", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel) { _exiting = false; return; }
            if (choice == MessageBoxResult.Yes)
            {
                try { await _viewModel.Mapper.SaveChangesAsync(); }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(this, ex.Message, "Could not save profiles",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    _exiting = false;
                    return;
                }
            }
        }
        _allowClose = true;
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        try
        {
            var save = _viewModel.SaveWindowPlacementAsync(bounds.Width, bounds.Height, bounds.Left, bounds.Top,
                WindowState == WindowState.Maximized);
            await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(2)));
            var dispose = _viewModel.DisposeAsync().AsTask();
            await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            DisposeTrayIcon();
            Close();
            Application.Current.Shutdown();
        }
    }

    internal void DisposeTrayIcon()
    {
        if (_trayDisposed) return;
        _trayDisposed = true;
        try
        {
            _trayIcon.Visible = false;
        }
        catch
        {
        }

        try
        {
            _trayIcon.Dispose();
        }
        catch
        {
        }
    }

    private void UpdateWindowChromeMetrics()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowChrome.CornerRadius = new CornerRadius(0);
        WindowChrome.ResizeBorderThickness = maximized ? new Thickness(0) : new Thickness(4);
        WindowFrame.Margin = maximized ? new Thickness(4) : new Thickness(0);
        WindowFrame.CornerRadius = new CornerRadius(0);
    }

    private void CenterWindowOnScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }
}
