using System.Diagnostics;
using System.Windows;
using PitMedic.Models;
using PitMedic.Services;

namespace PitMedic;

public partial class SettingsWindow : Window
{
    private readonly MonitoringCoordinator _monitoring;
    private readonly SettingsService _settings;

    public SettingsWindow(MonitoringCoordinator monitoring)
    {
        InitializeComponent();
        _monitoring = monitoring;
        _settings = monitoring.Settings;
        LoadSettings(_settings.Current);
    }

    private void LoadSettings(AppSettings s)
    {
        MonitorLmu.IsChecked = s.MonitorLeMansUltimate;
        MonitorIRacing.IsChecked = s.MonitorIRacing;
        MonitorAce.IsChecked = s.MonitorAssettoCorsaEvo;
        MonitorRaceRoom.IsChecked = s.MonitorRaceRoom;
        MonitorAcc.IsChecked = s.MonitorAssettoCorsaCompetizione;
        MonitorAms2.IsChecked = s.MonitorAutomobilista2;
        CpuTemp.IsChecked = s.MonitorCpuTemperature;
        CpuLoad.IsChecked = s.MonitorCpuLoad;
        CpuClock.IsChecked = s.MonitorCpuClock;
        CpuPower.IsChecked = s.MonitorCpuPower;
        GpuTemp.IsChecked = s.MonitorGpuTemperature;
        GpuHotspot.IsChecked = s.MonitorGpuHotspot;
        GpuMemTemp.IsChecked = s.MonitorGpuMemoryTemperature;
        GpuLoad.IsChecked = s.MonitorGpuLoad;
        GpuClock.IsChecked = s.MonitorGpuClock;
        GpuPower.IsChecked = s.MonitorGpuPower;
        GpuFan.IsChecked = s.MonitorGpuFan;
        GpuMemory.IsChecked = s.MonitorGpuMemory;
        SystemMemory.IsChecked = s.MonitorSystemMemory;
        CaptureEveryExit.IsChecked = s.CaptureEveryGameExit;
        StartWithWindows.IsChecked = s.StartWithWindows;
        MinimizeToTray.IsChecked = s.MinimizeToTrayOnClose;
        LaunchMinimized.IsChecked = s.LaunchMinimized;
        UseFahrenheit.IsChecked = s.UseFahrenheit;
        AutoRunSafeRepairs.IsChecked = s.AutoRunSafeRepairs;
        AlwaysAskBeforeRepair.IsChecked = s.AlwaysAskBeforeRepair;
        KeepRepairBackups.IsChecked = s.KeepRepairBackups;
    }

    private AppSettings ReadSettings()
    {
        return new AppSettings
        {
            MonitorLeMansUltimate = MonitorLmu.IsChecked == true,
            MonitorIRacing = MonitorIRacing.IsChecked == true,
            MonitorAssettoCorsaEvo = MonitorAce.IsChecked == true,
            MonitorRaceRoom = MonitorRaceRoom.IsChecked == true,
            MonitorAssettoCorsaCompetizione = MonitorAcc.IsChecked == true,
            MonitorAutomobilista2 = MonitorAms2.IsChecked == true,
            MonitorCpuTemperature = CpuTemp.IsChecked == true,
            MonitorCpuLoad = CpuLoad.IsChecked == true,
            MonitorCpuClock = CpuClock.IsChecked == true,
            MonitorCpuPower = CpuPower.IsChecked == true,
            MonitorGpuTemperature = GpuTemp.IsChecked == true,
            MonitorGpuHotspot = GpuHotspot.IsChecked == true,
            MonitorGpuMemoryTemperature = GpuMemTemp.IsChecked == true,
            MonitorGpuLoad = GpuLoad.IsChecked == true,
            MonitorGpuClock = GpuClock.IsChecked == true,
            MonitorGpuPower = GpuPower.IsChecked == true,
            MonitorGpuFan = GpuFan.IsChecked == true,
            MonitorGpuMemory = GpuMemory.IsChecked == true,
            MonitorSystemMemory = SystemMemory.IsChecked == true,
            SamplingSeconds = _settings.Current.SamplingSeconds,
            BufferMinutes = _settings.Current.BufferMinutes,
            ThermalTraceMinutes = _settings.Current.ThermalTraceMinutes,
            CaptureEveryGameExit = CaptureEveryExit.IsChecked == true,
            StartWithWindows = StartWithWindows.IsChecked == true,
            MinimizeToTrayOnClose = MinimizeToTray.IsChecked == true,
            LaunchMinimized = LaunchMinimized.IsChecked == true,
            UseFahrenheit = UseFahrenheit.IsChecked == true,
            AutoRunSafeRepairs = AutoRunSafeRepairs.IsChecked == true,
            AlwaysAskBeforeRepair = AlwaysAskBeforeRepair.IsChecked == true,
            KeepRepairBackups = KeepRepairBackups.IsChecked == true
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var value = ReadSettings();
        _settings.Save(value);
        DialogResult = true;
        Close();
    }

    private void Defaults_Click(object sender, RoutedEventArgs e) => LoadSettings(new AppSettings());
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SensorReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = _monitoring.WriteSensorReport();
            SensorReportStatus.Text = $"Saved to {path}";
            Process.Start(new ProcessStartInfo("notepad.exe", path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SensorReportStatus.Text = $"Could not create sensor report: {ex.Message}";
        }
    }


}
