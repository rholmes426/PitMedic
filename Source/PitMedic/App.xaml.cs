using System.Windows;
using PitMedic.Services;

namespace PitMedic;

public partial class App : System.Windows.Application
{
    private TrayIconService? _tray;
    private MonitoringCoordinator? _monitoring;
    private MainWindow? _mainWindow;
    private SettingsService? _settings;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.EnsureCreated();
        _settings = new SettingsService();
        _settings.RefreshStartupRegistration();

        _monitoring = new MonitoringCoordinator(_settings);
        _mainWindow = new MainWindow(_monitoring);
        _tray = new TrayIconService(_mainWindow, ExitApplication);

        var startupLaunch = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        if (!_settings.Current.LaunchMinimized && !startupLaunch)
            _mainWindow.Show();
        _monitoring.Start();
        AppLog.Write("PitMedic v0.4.4.0 started on .NET 10. Monitoring runs unelevated; allowlisted system repairs use the one-shot repair helper.");
    }

    private void ExitApplication()
    {
        if (_monitoring?.CurrentRepair?.IsActive == true)
        {
            _mainWindow?.Show();
            if (_mainWindow is not null && _mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
            MessageBox.Show("PitMedic is currently repairing simulator content. Keep PitMedic running until the repair finishes so it can monitor the Steam validation and safely roll back if needed.",
                "Repair in progress", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _tray?.Dispose();
        _monitoring?.Dispose();
        _mainWindow?.AllowCloseAndClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _monitoring?.Dispose();
        AppLog.Write("PitMedic exited.");
        base.OnExit(e);
    }
}
