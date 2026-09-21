using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using FontAwesome.Sharp;
using LogiLeds.ViewModels;
using Forms = System.Windows.Forms;

namespace LogiLeds;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmDoNotRound = 1;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _startStopMenuItem;
    private bool _allowClose, _shownTrayHint, _exiting, _trayDisposed, _startupComplete;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        StatusPopup.CustomPopupPlacementCallback = StatusPopup_OnCustomPopupPlacement;
        UpdateWindowChromeMetrics();
        UpdateMaximizeIcon();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ExitRequested += async (_, _) => await ExitAsync();

        _startStopMenuItem = new Forms.ToolStripMenuItem("Stop control");
        _startStopMenuItem.Click += (_, _) => _viewModel.StartStopCommand.Execute(null);
        var openItem = new Forms.ToolStripMenuItem("Open LogiLeds");
        openItem.Click += (_, _) => RestoreWindow();
        var exitItem = new Forms.ToolStripMenuItem("Exit LogiLeds");
        exitItem.Click += async (_, _) => await ExitAsync();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(openItem); menu.Items.Add(_startStopMenuItem); menu.Items.Add(new Forms.ToolStripSeparator()); menu.Items.Add(exitItem);
        menu.Opening += (_, _) => _startStopMenuItem.Text = _viewModel.IsRunning ? "Stop control" : "Start control";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LogiLeds.ico");
        var icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : null;
        _trayIcon = new Forms.NotifyIcon { Icon = icon ?? SystemIcons.Application, Text = "LogiLeds", Visible = true, ContextMenuStrip = menu };
        _trayIcon.DoubleClick += (_, _) => RestoreWindow();

        SourceInitialized += OnSourceInitialized;
        Closing += OnClosing;
        Closed += (_, _) => DisposeTrayIcon();
        StateChanged += OnStateChanged;
    }

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            var cornerPreference = DwmDoNotRound;
            _ = DwmSetWindowAttribute(windowHandle, DwmWindowCornerPreferenceAttribute, ref cornerPreference, sizeof(int));

            // Never inherit a minimized shell launch state. Tray minimization
            // is only meaningful after the window has completed initialization.
            WindowState = WindowState.Normal;
            await _viewModel.InitializeAsync(windowHandle);
            var settings = _viewModel.CurrentSettings;
            Width = settings.WindowWidth; Height = settings.WindowHeight;
            if (settings.WindowLeft is double left && settings.WindowTop is double top &&
                left + settings.WindowWidth > SystemParameters.VirtualScreenLeft &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top + settings.WindowHeight > SystemParameters.VirtualScreenTop &&
                top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight)
            { Left = left; Top = top; }
            if (settings.WindowMaximized) WindowState = WindowState.Maximized;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Startup failed: {ex}");
        }
        finally
        {
            _startupComplete = true;
            if (!IsVisible) Show();
            Activate();
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        if (!_startupComplete) { e.Cancel = true; return; }
        if (_viewModel.CloseToTray) { e.Cancel = true; HideToTray(); }
        else { e.Cancel = true; _ = ExitAsync(); }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        UpdateWindowChromeMetrics();
        UpdateMaximizeIcon();
        if (_startupComplete && WindowState == WindowState.Minimized && _viewModel.MinimizeToTray) HideToTray();
    }

    private void HideToTray()
    {
        if (!_trayDisposed) _trayIcon.Visible = true;
        Hide();
        if (_shownTrayHint) return;
        _shownTrayHint = true;
        _trayIcon.ShowBalloonTip(2200, "LogiLeds is running", "Open or exit from the notification area.", Forms.ToolTipIcon.Info);
    }

    private void RestoreWindow()
    {
        if (!_trayDisposed) _trayIcon.Visible = true;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private async Task ExitAsync()
    {
        if (_exiting) return;
        _exiting = true;
        _allowClose = true;
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        try
        {
            // Shutdown must never leave a hidden process holding the mutex if
            // a device driver or settings provider is slow to return.
            var save = _viewModel.SaveWindowPlacementAsync(bounds.Width, bounds.Height, bounds.Left, bounds.Top, WindowState == WindowState.Maximized);
            await Task.WhenAny(save, Task.Delay(TimeSpan.FromSeconds(2)));
            var dispose = _viewModel.DisposeAsync().AsTask();
            await Task.WhenAny(dispose, Task.Delay(TimeSpan.FromSeconds(2)));
        }
        finally
        {
            DisposeTrayIcon();
            Close();
            System.Windows.Application.Current.Shutdown();
        }
    }

    internal void DisposeTrayIcon()
    {
        if (_trayDisposed) return;
        _trayDisposed = true;
        try { _trayIcon.Visible = false; } catch { }
        try { _trayIcon.Dispose(); } catch { }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject)) return;

        if (e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        else
        {
            if (WindowState == WindowState.Maximized)
            {
                var pointer = e.GetPosition(this);
                var screenPointer = PointToScreen(pointer);
                var restoreWidth = RestoreBounds.Width;
                var horizontalRatio = ActualWidth <= 0 ? 0.5 : Math.Clamp(pointer.X / ActualWidth, 0.05, 0.95);
                WindowState = WindowState.Normal;
                Left = screenPointer.X - restoreWidth * horizontalRatio;
                Top = screenPointer.Y - pointer.Y;
            }

            DragMove();
        }
    }

    private void TitleBar_OnMouseRightButtonUp(object sender, MouseButtonEventArgs e) =>
        SystemCommands.ShowSystemMenu(this, PointToScreen(e.GetPosition(this)));

    private static bool IsInteractiveTitleBarSource(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is System.Windows.Controls.Primitives.ButtonBase or TabItem) return true;
            source = System.Windows.Media.VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private static CustomPopupPlacement[] StatusPopup_OnCustomPopupPlacement(System.Windows.Size popupSize, System.Windows.Size targetSize, System.Windows.Point offset)
    {
        var centeredX = (targetSize.Width - popupSize.Width) / 2;
        return [new CustomPopupPlacement(new System.Windows.Point(centeredX, targetSize.Height), PopupPrimaryAxis.Horizontal)];
    }

    private void UpdateWindowChromeMetrics()
    {
        var maximized = WindowState == WindowState.Maximized;
        WindowChrome.CornerRadius = new CornerRadius(0);
        WindowChrome.ResizeBorderThickness = maximized ? new Thickness(0) : new Thickness(4);
        WindowFrame.Margin = maximized ? new Thickness(4) : new Thickness(0);
        WindowFrame.CornerRadius = new CornerRadius(0);
    }

    private void UpdateMaximizeIcon() => MaximizeIcon.Icon = WindowState == WindowState.Maximized ? IconChar.Compress : IconChar.Expand;
}
