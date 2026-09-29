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
using Control = System.Windows.Controls.Control;
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
    private static readonly TimeSpan NavigationResizeDuration = TimeSpan.FromMilliseconds(260);
    private readonly ToolStripMenuItem _startStopMenuItem;
    private readonly ToolStripMenuItem _mapperMenuItem;
    private readonly NotifyIcon _trayIcon;
    private readonly MainViewModel _viewModel;
    private bool _allowClose, _shownTrayHint, _exiting, _trayDisposed, _startupComplete;
    private bool _userCollapsedNavigation, _isNarrowWindow, _isNavigationOverlayOpen;
    private double _navigationColumnTarget = ExpandedNavigationWidth;
    private double? _navigationVisualWidth;
    private bool? _navigationLabelsVisible;
    private int _navigationLayoutVersion;
    private int _sidebarWidthAnimationVersion;
    private int _sidebarColumnAnimationVersion;
    private int _lastSelectedTab;
    private bool _isOverlayClosing;
    private bool _sidebarColumnAnimating;
    private bool _overlayCloseSidebarWidthDone;
    private double _overlayCloseTargetWidth;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        viewModel.RpmProfile.PageTitle = LedNavigationLabel.Text;
        viewModel.Mapper.PageTitle = MapperNavigationLabel.Text;
        OwnedWindowDimmer.Attach(this);
        UpdateWindowChromeMetrics();
        _viewModel = viewModel;
        _lastSelectedTab = viewModel.SelectedTab;
        DataContext = viewModel;
        TitleBar.NavigationToggleRequested += (_, _) => ToggleNavigation();
        _viewModel.ExitRequested += async (_, _) => await ExitAsync();
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
        SizeChanged += (_, _) =>
        {
            if (WindowState != WindowState.Minimized) UpdateNavigationLayout(animate: true);
        };
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
        if (WindowState == WindowState.Minimized) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
            new Action(() => UpdateNavigationLayout(animate: false)));
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
        _isOverlayClosing = false;
        _overlayCloseSidebarWidthDone = false;
        _isNavigationOverlayOpen = true;
        var fromWidth = SidebarPanel.ActualWidth;
        SidebarPanel.SetValue(Grid.ColumnSpanProperty, 2);
        SidebarPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        NavigationBackdrop.Visibility = Visibility.Visible;
        NavigationBackdrop.BeginAnimation(OpacityProperty,
            new DoubleAnimation(NavigationBackdrop.Opacity, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        }, HandoffBehavior.SnapshotAndReplace);
        AnimateSidebarWidth(ExpandedNavigationWidth, fromWidth);
        UpdateNavigationVisuals();
    }

    private void CloseNavigationOverlay()
    {
        if (!_isNavigationOverlayOpen) return;
        BeginOverlayClose(CompactNavigationWidth);
        UpdateNavigationVisuals();
    }

    private void BeginOverlayClose(double targetWidth)
    {
        _isNavigationOverlayOpen = false;
        _isOverlayClosing = true;
        _overlayCloseSidebarWidthDone = false;
        _overlayCloseTargetWidth = targetWidth;
        var fadeOut = new DoubleAnimation(NavigationBackdrop.Opacity, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        fadeOut.Completed += (_, _) =>
        {
            if (!_isNavigationOverlayOpen && NavigationBackdrop.Opacity < .01)
                NavigationBackdrop.Visibility = Visibility.Collapsed;
        };
        NavigationBackdrop.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        AnimateSidebarWidth(targetWidth, completed: CompleteOverlayClose);
    }

    private void CompleteOverlayClose()
    {
        if (!_isOverlayClosing || _isNavigationOverlayOpen) return;
        _overlayCloseSidebarWidthDone = true;
        TryCompleteOverlayClose();
    }

    private void TryCompleteOverlayClose()
    {
        if (!_isOverlayClosing || _isNavigationOverlayOpen || !_overlayCloseSidebarWidthDone ||
            _sidebarColumnAnimating) return;
        _isOverlayClosing = false;
        UseColumnSidebar();
    }

    private void UpdateNavigationLayout(bool animate)
    {
        var isNarrow = ActualWidth < CompactNavigationBreakpoint;
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
            if (_isOverlayClosing)
            {
                if (Math.Abs(_overlayCloseTargetWidth - CompactNavigationWidth) > .5)
                {
                    _overlayCloseTargetWidth = CompactNavigationWidth;
                    _overlayCloseSidebarWidthDone = false;
                    AnimateSidebarWidth(CompactNavigationWidth, completed: CompleteOverlayClose);
                }
            }

            if (_isNavigationOverlayOpen)
            {
                if ((int)SidebarPanel.GetValue(Grid.ColumnSpanProperty) != 2)
                {
                    var fromWidth = SidebarPanel.ActualWidth;
                    SidebarPanel.SetValue(Grid.ColumnSpanProperty, 2);
                    SidebarPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                    AnimateSidebarWidth(ExpandedNavigationWidth, fromWidth);
                }
            }
            else if (!_isOverlayClosing) UseColumnSidebar();
            if (_isNavigationOverlayOpen)
                NavigationBackdrop.Visibility = Visibility.Visible;
            else if (!_isOverlayClosing)
            {
                NavigationBackdrop.BeginAnimation(OpacityProperty, null);
                NavigationBackdrop.Visibility = Visibility.Collapsed;
                NavigationBackdrop.Opacity = 0;
            }
        }
        else
        {
            if (_isNavigationOverlayOpen)
                BeginOverlayClose(targetWidth);
            else if (_isOverlayClosing)
            {
                if (Math.Abs(_overlayCloseTargetWidth - targetWidth) > .5)
                {
                    _overlayCloseTargetWidth = targetWidth;
                    _overlayCloseSidebarWidthDone = false;
                    AnimateSidebarWidth(targetWidth, completed: CompleteOverlayClose);
                }
            }
            else
            {
                UseColumnSidebar();
                NavigationBackdrop.BeginAnimation(OpacityProperty, null);
                NavigationBackdrop.Visibility = Visibility.Collapsed;
                NavigationBackdrop.Opacity = 0;
            }
        }

        UpdateNavigationVisuals();
        TryCompleteOverlayClose();
    }

    private void AnimateSidebarColumn(double targetWidth, bool animate)
    {
        var fromWidth = SidebarColumn.ActualWidth;
        var animationVersion = ++_sidebarColumnAnimationVersion;
        SidebarColumn.Width = new GridLength(targetWidth);
        if (!animate || Math.Abs(fromWidth - targetWidth) < .5)
        {
            _sidebarColumnAnimating = false;
            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            return;
        }

        _sidebarColumnAnimating = true;
        var animation = new GridLengthAnimation
        {
            From = new GridLength(fromWidth),
            To = new GridLength(targetWidth),
            Duration = NavigationResizeDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            if (animationVersion == _sidebarColumnAnimationVersion)
            {
                _sidebarColumnAnimating = false;
                SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                TryCompleteOverlayClose();
            }
        };
        SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateSidebarWidth(double targetWidth, double? fromWidth = null, Action? completed = null)
    {
        var startWidth = fromWidth ?? SidebarPanel.ActualWidth;
        var animationVersion = ++_sidebarWidthAnimationVersion;
        SidebarPanel.Width = targetWidth;
        if (Math.Abs(startWidth - targetWidth) < .5)
        {
            SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            completed?.Invoke();
            return;
        }

        var animation = new DoubleAnimation(startWidth, targetWidth, NavigationResizeDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        animation.Completed += (_, _) =>
        {
            if (animationVersion != _sidebarWidthAnimationVersion) return;
            SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
            completed?.Invoke();
        };
        SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void UseColumnSidebar()
    {
        ++_sidebarWidthAnimationVersion;
        SidebarPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
        SidebarPanel.ClearValue(FrameworkElement.WidthProperty);
        SidebarPanel.SetValue(Grid.ColumnSpanProperty, 1);
        SidebarPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
    }

    private void UpdateNavigationVisuals()
    {
        var isCompact = _isNarrowWindow || _userCollapsedNavigation;
        var showLabels = !isCompact || _isNavigationOverlayOpen;
        var animateLabels = _navigationLabelsVisible is bool wasVisible && wasVisible != showLabels;
        var visibleSidebarWidth = _isNavigationOverlayOpen
            ? ExpandedNavigationWidth
            : isCompact ? CompactNavigationWidth : ExpandedNavigationWidth;
        var layoutChanged = _navigationLabelsVisible != showLabels || _navigationVisualWidth != visibleSidebarWidth;
        _navigationLabelsVisible = showLabels;
        _navigationVisualWidth = visibleSidebarWidth;
        if (!layoutChanged)
        {
            TitleBar.SetNavigationIconState(isCompact && !_isNavigationOverlayOpen,
                _isNavigationOverlayOpen, _isNarrowWindow);
            return;
        }

        var layoutVersion = ++_navigationLayoutVersion;
        SidebarPanel.BeginAnimation(Border.PaddingProperty, null);
        SidebarPanel.Padding = new Thickness(12);

        SetNavigationItemLayout(DashboardNavigationButton, DashboardNavigationLabel,
            DashboardIconColumn, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(LedNavigationButton, LedNavigationLabel,
            LedIconColumn, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(MapperNavigationButton, MapperNavigationLabel,
            MapperIconColumn, showLabels, animateLabels, layoutVersion);
        SetNavigationItemLayout(SettingsNavigationButton, SettingsNavigationLabel,
            SettingsIconColumn, showLabels, animateLabels, layoutVersion);
        TitleBar.SetNavigationIconState(isCompact && !_isNavigationOverlayOpen,
            _isNavigationOverlayOpen, _isNarrowWindow);
    }

    private void SetNavigationItemLayout(RadioButton button, TextBlock label,
        ColumnDefinition iconColumn, bool showLabel, bool animate, int layoutVersion)
    {
        button.ClearValue(FrameworkElement.WidthProperty);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
        var targetPadding = showLabel ? new Thickness(16, 9, 16, 9) : new Thickness(0);
        var targetIconColumnWidth = showLabel ? 18 : 48;
        if (animate)
        {
            button.BeginAnimation(Control.PaddingProperty,
                new ThicknessAnimation(button.Padding, targetPadding, NavigationResizeDuration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                }, HandoffBehavior.SnapshotAndReplace);
            var columnAnimation = new GridLengthAnimation
            {
                From = new GridLength(iconColumn.ActualWidth),
                To = new GridLength(targetIconColumnWidth),
                Duration = NavigationResizeDuration,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            columnAnimation.Completed += (_, _) =>
            {
                if (layoutVersion == _navigationLayoutVersion)
                    iconColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            };
            iconColumn.Width = new GridLength(targetIconColumnWidth);
            iconColumn.BeginAnimation(ColumnDefinition.WidthProperty, columnAnimation,
                HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            button.BeginAnimation(Control.PaddingProperty, null);
            button.Padding = targetPadding;
            iconColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
            iconColumn.Width = new GridLength(targetIconColumnWidth);
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
            var slideIn = new TranslateTransform(0, 4);
            label.RenderTransform = slideIn;
            label.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
            {
                BeginTime = TimeSpan.FromMilliseconds(130),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
            slideIn.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(4, 0, TimeSpan.FromMilliseconds(140))
            {
                BeginTime = TimeSpan.FromMilliseconds(110),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            }, HandoffBehavior.SnapshotAndReplace);
        }
        else if (animate && label.Visibility == Visibility.Visible)
        {
            var slideOut = new TranslateTransform();
            label.RenderTransform = slideOut;
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(85));
            fadeOut.Completed += (_, _) =>
            {
                if (layoutVersion != _navigationLayoutVersion || _navigationLabelsVisible == true) return;
                label.Visibility = Visibility.Collapsed;
                label.BeginAnimation(OpacityProperty, null);
                label.Opacity = 1;
                label.RenderTransform = Transform.Identity;
            };
            label.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
            slideOut.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(100))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                }, HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            label.Visibility = Visibility.Collapsed;
            label.BeginAnimation(OpacityProperty, null);
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
