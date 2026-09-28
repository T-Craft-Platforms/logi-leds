using System.Windows;
using LogiWheelForge.Services;
using LogiWheelForge.ViewModels;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace LogiWheelForge;

public partial class App : Application
{
    private bool _ownsMutex;
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize();
        _singleInstanceMutex = new Mutex(true, "Local\\LogiWheelForge.FH6.G29", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            MessageBox.Show("LogiWheel Forge is already running. Check the notification area.", "LogiWheel Forge",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var receiver = new UdpTelemetryReceiver();
        var wheel = new LogitechWheelLedController();
        var service = new LedIndicatorService(receiver, wheel, new SettingsStore());
        var mapper = new InputMapperService(wheel.AvailableDefinitions);
        var selection = new WheelSelectionService(wheel.AvailableDefinitions);
        var window = new MainWindow(new MainViewModel(service, mapper, selection));
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window) window.DisposeTrayIcon();
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
