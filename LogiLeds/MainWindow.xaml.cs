using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using LogiLeds.ViewModels;
using Application = System.Windows.Application;
using Icon = System.Drawing.Icon;

namespace LogiLeds;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreferenceAttribute = 33;
    private const int DwmDoNotRound = 1;
    private readonly ToolStripMenuItem _startStopMenuItem;
    private readonly NotifyIcon _trayIcon;
    private readonly MainViewModel _viewModel;
    private bool _allowClose, _shownTrayHint, _exiting, _trayDisposed, _startupComplete;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        UpdateWindowChromeMetrics();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ExitRequested += async (_, _) => await ExitAsync();
        _viewModel.PropertyChanged += OnMainViewModelPropertyChanged;

        _startStopMenuItem = new ToolStripMenuItem("Stop control");
        _startStopMenuItem.Click += (_, _) => _viewModel.Settings.StartStopCommand.Execute(null);
        var openItem = new ToolStripMenuItem("Open LogiLeds");
        openItem.Click += (_, _) => RestoreWindow();
        var exitItem = new ToolStripMenuItem("Exit LogiLeds");
        exitItem.Click += async (_, _) => await ExitAsync();
        var menu = new ContextMenuStrip();
        menu.Items.Add(openItem);
        menu.Items.Add(_startStopMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) => _startStopMenuItem.Text = _viewModel.IsRunning ? "Stop control" : "Start control";
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LogiLeds.ico");
        var icon = File.Exists(iconPath) ? new Icon(iconPath) : null;
        _trayIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application, Text = "LogiLeds", Visible = false, ContextMenuStrip = menu
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
        if (e.PropertyName == nameof(MainViewModel.CloseToTray)) UpdateTrayIconVisibility();
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
        _trayIcon.ShowBalloonTip(2200, "LogiLeds is running", "Open or exit from the notification area.",
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