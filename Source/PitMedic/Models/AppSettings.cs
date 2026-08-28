namespace PitMedic.Models;

public sealed class AppSettings
{
    public bool MonitorLeMansUltimate { get; set; } = true;
    public bool MonitorIRacing { get; set; } = true;
    public bool MonitorAssettoCorsaEvo { get; set; } = true;
    public bool MonitorRaceRoom { get; set; } = true;
    public bool MonitorAssettoCorsaCompetizione { get; set; } = true;
    public bool MonitorAutomobilista2 { get; set; } = true;

    public bool MonitorCpuTemperature { get; set; } = true;
    public bool MonitorCpuLoad { get; set; } = true;
    public bool MonitorCpuClock { get; set; } = true;
    public bool MonitorCpuPower { get; set; } = true;
    public bool MonitorGpuTemperature { get; set; } = true;
    public bool MonitorGpuHotspot { get; set; } = true;
    public bool MonitorGpuMemoryTemperature { get; set; } = true;
    public bool MonitorGpuLoad { get; set; } = true;
    public bool MonitorGpuClock { get; set; } = true;
    public bool MonitorGpuPower { get; set; } = true;
    public bool MonitorGpuFan { get; set; } = true;
    public bool MonitorGpuMemory { get; set; } = true;
    public bool MonitorSystemMemory { get; set; } = true;

    public int SamplingSeconds { get; set; } = 1;
    public int BufferMinutes { get; set; } = 10;
    public int ThermalTraceMinutes { get; set; } = 10;
    public bool CaptureEveryGameExit { get; set; } = false;
    public bool StartWithWindows { get; set; } = true;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool LaunchMinimized { get; set; } = false;
    public bool UseFahrenheit { get; set; } = false;

    public bool AutoRunSafeRepairs { get; set; } = true;
    public bool AlwaysAskBeforeRepair { get; set; } = false;
    public bool KeepRepairBackups { get; set; } = true;

}
