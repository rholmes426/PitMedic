using System.Threading;
using System.Windows;
using PitMedic.Services;

namespace PitMedic;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = "PitMedic-E805E797-5FEF-4D91-8B72-0E20C53D2E09";
    private const string MaintenanceShutdownEventName = "PitMedic-MaintenanceShutdown-E805E797-5FEF-4D91-8B72-0E20C53D2E09";
    private TrayIconService? _tray;
    private MonitoringCoordinator? _monitoring;
    private MainWindow? _mainWindow;
    private SettingsService? _settings;
    private AnonymousUsageService? _anonymousUsage;
    private UpdateService? _updates;
    private Mutex? _instanceMutex;
    private bool _ownsInstanceMutex;
    private EventWaitHandle? _maintenanceShutdownEvent;
    private RegisteredWaitHandle? _maintenanceShutdownRegistration;
    private bool _anonymousUsagePromptScheduled;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Any(a => a.Equals("--shutdown-for-maintenance", StringComparison.OrdinalIgnoreCase)))
        {
            RequestMaintenanceShutdown();
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("PitMedic is already running. Open it from the notification area near the Windows clock.",
                "PitMedic is already running", MessageBoxButton.OK, MessageBoxImage.Information);
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }
        _ownsInstanceMutex = true;

        _maintenanceShutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, MaintenanceShutdownEventName);
        _maintenanceShutdownRegistration = ThreadPool.RegisterWaitForSingleObject(
            _maintenanceShutdownEvent,
            (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        AppPaths.EnsureCreated();
        _settings = new SettingsService();
        _settings.RefreshStartupRegistration();

        _monitoring = new MonitoringCoordinator(_settings);
        _anonymousUsage = new AnonymousUsageService(_settings);
        _updates = new UpdateService(_settings);
        _mainWindow = new MainWindow(_monitoring, _anonymousUsage, _updates);
        _mainWindow.ContentRendered += MainWindow_ContentRendered;
        _tray = new TrayIconService(_mainWindow, ExitApplication);

        var startupLaunch = e.Args.Any(a => a.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        // Normal launches, including the installer's post-install launch, are always visible.
        // Only the explicit Windows-startup path begins quietly in the notification area.
        if (!startupLaunch)
            _mainWindow.Show();
        _monitoring.Start();
        _anonymousUsage.Start();
        _updates.Start();
        AppLog.Write($"PitMedic v{AppInfo.Version} started on .NET 10. Monitoring runs unelevated; protected CPU telemetry uses the installed read-only service and allowlisted protected repairs use the one-shot repair helper.");
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        if (_anonymousUsagePromptScheduled || _settings?.Current.ShareAnonymousUsage is not null
            || _mainWindow is null || _anonymousUsage is null) return;

        _anonymousUsagePromptScheduled = true;
        var settingsService = _settings!;
        var anonymousUsage = _anonymousUsage!;
        var mainWindow = _mainWindow!;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (settingsService.Current.ShareAnonymousUsage is not null) return;
            var prompt = new AnonymousUsageConsentWindow(anonymousUsage) { Owner = mainWindow };
            var settings = settingsService.Current;
            settings.ShareAnonymousUsage = prompt.ShowDialog() == true;
            settingsService.Save(settings);
        }));
    }

    private static void RequestMaintenanceShutdown()
    {
        try
        {
            using var shutdownEvent = EventWaitHandle.OpenExisting(MaintenanceShutdownEventName);
            shutdownEvent.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return;
        }

        try
        {
            using var runningInstance = Mutex.OpenExisting(InstanceMutexName);
            try
            {
                if (runningInstance.WaitOne(TimeSpan.FromSeconds(15)))
                    runningInstance.ReleaseMutex();
                else
                    Environment.ExitCode = 2;
            }
            catch (AbandonedMutexException)
            {
                // The monitored process ended without releasing the mutex; maintenance may continue.
            }
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The app exited between signaling the event and opening the mutex.
        }
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
        _anonymousUsage?.Dispose();
        _updates?.Dispose();
        _maintenanceShutdownRegistration?.Unregister(null);
        _maintenanceShutdownRegistration = null;
        _maintenanceShutdownEvent?.Dispose();
        _maintenanceShutdownEvent = null;
        if (_ownsInstanceMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); } catch { }
            _ownsInstanceMutex = false;
        }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        AppLog.Write("PitMedic exited.");
        base.OnExit(e);
    }
}
