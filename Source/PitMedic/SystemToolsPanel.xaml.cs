using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
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

            await Task.WhenAll(powerTask, startupTask, storageTask);

            PowerModeStatus.Text = $"Current: {powerTask.Result}";
            StartupAppsStatus.Text = startupTask.Result == 1
                ? "1 startup entry found"
                : $"{startupTask.Result} startup entries found";
            StorageStatus.Text = storageTask.Result;
            LastRefreshText.Text = $"Updated {DateTime.Now:h:mm tt}";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"System tools status refresh failed: {ex.GetType().Name}: {ex.Message}");
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

    private void PowerMode_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:powersleep", "Power settings");
    private void StartupApps_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:startupapps", "Startup Apps");
    private void Storage_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:storagesense", "Storage settings");
    private void GraphicsSettings_Click(object sender, RoutedEventArgs e) => OpenWindowsTarget("ms-settings:display-advancedgraphics", "Graphics settings");

    private void OpenWindowsTarget(string target, string friendlyName)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Could not open {friendlyName}: {ex.GetType().Name}: {ex.Message}");
            MessageBox.Show(Window.GetWindow(this),
                $"Windows could not open {friendlyName}.",
                "PitMedic",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

}
