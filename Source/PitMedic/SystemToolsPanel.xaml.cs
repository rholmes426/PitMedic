using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PitMedic;

public partial class SystemToolsPanel : UserControl
{
    private readonly DispatcherTimer _refreshTimer;
    private bool _refreshing;

    public SystemToolsPanel()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(60)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshStatusesAsync();

        Loaded += async (_, _) =>
        {
            _refreshTimer.Start();
            await RefreshStatusesAsync();
        };
        Unloaded += (_, _) => _refreshTimer.Stop();
    }

    private async Task RefreshStatusesAsync()
    {
        if (_refreshing) return;
        _refreshing = true;

        try
        {
            var powerTask = Task.Run(GetActivePowerPlan);
            var startupTask = Task.Run(GetStartupEntryCount);
            var storageTask = Task.Run(GetStorageFreeText);
            var gameModeTask = Task.Run(GetGameModeText);
            var rebootTask = Task.Run(GetWindowsUpdateState);
            var cpuTask = SampleCpuUsageAsync();

            await Task.WhenAll(powerTask, startupTask, storageTask, gameModeTask, rebootTask, cpuTask);

            PowerModeStatus.Text = $"Current: {powerTask.Result}";
            StartupAppsStatus.Text = startupTask.Result == 1
                ? "1 startup entry found"
                : $"{startupTask.Result} startup entries found";
            StorageStatus.Text = storageTask.Result;
            GameModeStatus.Text = gameModeTask.Result;
            WindowsUpdateStatus.Text = rebootTask.Result;

            var cpu = cpuTask.Result;
            var memory = GetMemoryLoadPercent();
            var loadLabel = cpu >= 35 || memory >= 85
                ? "High"
                : cpu >= 15 || memory >= 70
                    ? "Moderate"
                    : "Low";

            BackgroundLoadStatus.Text = loadLabel;
            BackgroundLoadDetail.Text = $"{cpu:0}% CPU · {memory:0}% memory in use. View Task Manager for the processes behind the load.";
            LastRefreshText.Text = $"Updated {DateTime.Now:h:mm tt}";
        }
        catch (Exception ex)
        {
            AppLog.Write($"System tools status refresh failed: {ex.GetType().Name}: {ex.Message}");
            LastRefreshText.Text = "Some Windows status checks are unavailable";
        }
        finally
        {
            _refreshing = false;
        }
    }

    private static string GetActivePowerPlan()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg.exe",
                Arguments = "/getactivescheme",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null) return "Windows managed";
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(2500);

            var open = output.LastIndexOf('(');
            var close = output.LastIndexOf(')');
            if (open >= 0 && close > open)
                return output[(open + 1)..close].Trim();
        }
        catch
        {
            // Fall back to a neutral status when powercfg is unavailable.
        }

        return "Windows managed";
    }

    private static int GetStartupEntryCount()
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddRegistryStartupEntries(RegistryHive.CurrentUser, RegistryView.Default,
            @"Software\Microsoft\Windows\CurrentVersion\Run", entries);
        AddRegistryStartupEntries(RegistryHive.LocalMachine, RegistryView.Registry64,
            @"Software\Microsoft\Windows\CurrentVersion\Run", entries);

        AddStartupFolderEntries(Environment.GetFolderPath(Environment.SpecialFolder.Startup), entries);
        AddStartupFolderEntries(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), entries);
        return entries.Count;
    }

    private static void AddRegistryStartupEntries(RegistryHive hive, RegistryView view, string path, ISet<string> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(path);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
                if (!string.IsNullOrWhiteSpace(name)) entries.Add(name);
        }
        catch
        {
            // Startup Apps still opens even when one registry view is unavailable.
        }
    }

    private static void AddStartupFolderEntries(string folder, ISet<string> entries)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            foreach (var file in Directory.EnumerateFiles(folder))
                entries.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch
        {
            // A protected startup folder should not block the rest of the panel.
        }
    }

    private static string GetStorageFreeText()
    {
        try
        {
            var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var root = Path.GetPathRoot(systemFolder);
            if (string.IsNullOrWhiteSpace(root)) return "Storage status unavailable";

            var drive = new DriveInfo(root);
            var freeGb = drive.AvailableFreeSpace / 1024d / 1024d / 1024d;
            return $"{freeGb:0} GB free on {drive.Name.TrimEnd('\\')}";
        }
        catch
        {
            return "Storage status unavailable";
        }
    }

    private static string GetGameModeText()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
            var value = key?.GetValue("AutoGameModeEnabled");
            if (value is int enabled) return enabled != 0 ? "On" : "Off";
        }
        catch
        {
            // Missing keys simply mean Windows owns the default.
        }

        return "Windows default";
    }

    private static string GetWindowsUpdateState()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            if (localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending") is not null)
                return "Restart pending";
            if (localMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired") is not null)
                return "Restart pending";

            using var sessionManager = localMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            if (sessionManager?.GetValue("PendingFileRenameOperations") is not null)
                return "Restart pending";
        }
        catch
        {
            return "Open update status";
        }

        return "No restart pending";
    }

    private static async Task<double> SampleCpuUsageAsync()
    {
        if (!GetSystemTimes(out var idleBefore, out var kernelBefore, out var userBefore)) return 0;
        await Task.Delay(450);
        if (!GetSystemTimes(out var idleAfter, out var kernelAfter, out var userAfter)) return 0;

        var idle = ToUInt64(idleAfter) - ToUInt64(idleBefore);
        var kernel = ToUInt64(kernelAfter) - ToUInt64(kernelBefore);
        var user = ToUInt64(userAfter) - ToUInt64(userBefore);
        var total = kernel + user;
        if (total == 0) return 0;

        return Math.Clamp((1d - (double)idle / total) * 100d, 0d, 100d);
    }

    private static double GetMemoryLoadPercent()
    {
        var memory = new MemoryStatusEx();
        return GlobalMemoryStatusEx(memory) ? memory.MemoryLoad : 0;
    }

    private void PowerMode_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:powersleep", "Power settings");
    private void StartupApps_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:startupapps", "Startup Apps");
    private void Storage_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:storagesense", "Storage settings");
    private void GameMode_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:gaming-gamemode", "Game Mode");
    private void GraphicsSettings_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:display-advancedgraphics", "Graphics settings");
    private void WindowsUpdate_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:windowsupdate", "Windows Update");
    private void TaskManager_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("taskmgr.exe", "Task Manager");
    private void BackgroundLoad_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("taskmgr.exe", "Task Manager");

    private void OpenWindowsTarget(string target, string friendlyName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not open {friendlyName}: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this),
                $"Windows could not open {friendlyName}.",
                "PitMedic",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    private static ulong ToUInt64(FileTime value) => ((ulong)value.High << 32) | value.Low;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatusEx
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatusEx>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);
}
