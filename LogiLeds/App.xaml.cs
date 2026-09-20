using System.Threading;
using System.Windows;
using LogiLeds.Services;
using LogiLeds.ViewModels;

namespace LogiLeds;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeService.Initialize();
        _singleInstanceMutex = new Mutex(true, "Local\\LogiLeds.FH6.G29", out var createdNew);
        _ownsMutex = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show("LogiLeds is already running. Check the notification area.", "LogiLeds",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var receiver = new UdpTelemetryReceiver();
        var wheel = new LogitechWheelLedController();
        var service = new LedApplicationService(receiver, wheel, new SettingsStore());
        var window = new MainWindow(new MainViewModel(service));
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
