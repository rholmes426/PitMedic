using System.Text;
using Microsoft.Win32;
using LibreHardwareMonitor.Hardware;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private readonly UpdateVisitor _visitor = new();
    private readonly object _gate = new();

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true
        };
        _computer.Open();
    }

    public TelemetrySample Read(AppSettings settings)
    {
        lock (_gate)
        {
            try
            {
                _computer.Accept(_visitor);
                var all = Flatten(_computer.Hardware).ToList();
                var cpu = all.Where(h => h.HardwareType == HardwareType.Cpu).ToList();
                var gpu = all.Where(IsGpu).ToList();
                var memory = all.Where(h => h.HardwareType == HardwareType.Memory).ToList();

                return new TelemetrySample
                {
                    Timestamp = DateTimeOffset.Now,
                    CpuTempC = settings.MonitorCpuTemperature ? BestCpuTemp(cpu, all) : null,
                    CpuLoadPct = settings.MonitorCpuLoad ? (Named(cpu, SensorType.Load, "CPU Total") ?? Max(cpu, SensorType.Load)) : null,
                    CpuClockMhz = settings.MonitorCpuClock ? Max(cpu, SensorType.Clock) : null,
                    CpuPowerW = settings.MonitorCpuPower ? (Named(cpu, SensorType.Power, "CPU Package") ?? Max(cpu, SensorType.Power)) : null,
                    GpuTempC = settings.MonitorGpuTemperature ? (Named(gpu, SensorType.Temperature, "GPU Core") ?? Max(gpu, SensorType.Temperature)) : null,
                    GpuHotspotC = settings.MonitorGpuHotspot ? NameContains(gpu, SensorType.Temperature, "hot spot", "hotspot") : null,
                    GpuMemoryTempC = settings.MonitorGpuMemoryTemperature ? NameContains(gpu, SensorType.Temperature, "memory junction", "memory temperature", "gpu memory") : null,
                    GpuLoadPct = settings.MonitorGpuLoad ? (Named(gpu, SensorType.Load, "GPU Core") ?? Max(gpu, SensorType.Load)) : null,
                    GpuClockMhz = settings.MonitorGpuClock ? (Named(gpu, SensorType.Clock, "GPU Core") ?? Max(gpu, SensorType.Clock)) : null,
                    GpuPowerW = settings.MonitorGpuPower ? (NameContains(gpu, SensorType.Power, "gpu package", "gpu power", "total board") ?? Max(gpu, SensorType.Power)) : null,
                    GpuFanRpm = settings.MonitorGpuFan ? Max(gpu, SensorType.Fan) : null,
                    GpuMemoryUsedMb = settings.MonitorGpuMemory ? ReadGpuMemory(gpu, true) : null,
                    GpuMemoryTotalMb = settings.MonitorGpuMemory ? ReadGpuMemory(gpu, false) : null,
                    MemoryLoadPct = settings.MonitorSystemMemory ? (Named(memory, SensorType.Load, "Memory") ?? Max(memory, SensorType.Load)) : null
                };
            }
            catch (Exception ex)
            {
                AppLog.Write($"Hardware read failed: {ex.GetType().Name}: {ex.Message}");
                return new TelemetrySample { Timestamp = DateTimeOffset.Now };
            }
        }
    }

    public string WriteSensorReport()
    {
        lock (_gate)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PITMEDIC SENSOR REPORT");
            sb.AppendLine($"Generated: {DateTimeOffset.Now:O}");
            sb.AppendLine($"LibreHardwareMonitor: 0.9.6");
            sb.AppendLine($"PawnIO: {ReadPawnIoVersion() ?? "Not detected"}");
            sb.AppendLine();
            try
            {
                _computer.Accept(_visitor);
                foreach (var hardware in Flatten(_computer.Hardware))
                {
                    sb.AppendLine($"[{hardware.HardwareType}] {hardware.Name}");
                    foreach (var sensor in hardware.Sensors.OrderBy(s => s.SensorType).ThenBy(s => s.Name))
                        sb.AppendLine($"  {sensor.SensorType,-14} {sensor.Name,-38} = {(sensor.Value.HasValue ? sensor.Value.Value.ToString("0.###") : "N/A")}");
                    sb.AppendLine();
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Sensor enumeration failed: {ex}");
            }
            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(AppPaths.SensorReport, sb.ToString());
            return AppPaths.SensorReport;
        }
    }

    private static IEnumerable<IHardware> Flatten(IEnumerable<IHardware> hardware)
    {
        foreach (var item in hardware)
        {
            yield return item;
            foreach (var sub in Flatten(item.SubHardware))
                yield return sub;
        }
    }

    private static bool IsGpu(IHardware h) => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel;

    private static IEnumerable<ISensor> Sensors(IEnumerable<IHardware> hardware, SensorType type) =>
        hardware.SelectMany(h => h.Sensors).Where(s => s.SensorType == type && s.Value.HasValue);

    private static float? Named(IEnumerable<IHardware> hardware, SensorType type, string name) =>
        Sensors(hardware, type).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static float? NameContains(IEnumerable<IHardware> hardware, SensorType type, params string[] fragments)
    {
        var sensor = Sensors(hardware, type).FirstOrDefault(s => fragments.Any(f => s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)));
        return sensor?.Value;
    }

    private static float? Max(IEnumerable<IHardware> hardware, SensorType type)
    {
        var values = Sensors(hardware, type).Select(s => s.Value!.Value).ToArray();
        return values.Length == 0 ? null : values.Max();
    }

    private static float? BestCpuTemp(IEnumerable<IHardware> cpu, IEnumerable<IHardware> all)
    {
        var cpuTemps = Sensors(cpu, SensorType.Temperature).ToList();
        var preferredNames = new[] { "CPU Package", "Package", "Tctl/Tdie", "Tctl", "Tdie", "Core Max", "Core Average" };
        foreach (var preferred in preferredNames)
        {
            var sensor = cpuTemps.FirstOrDefault(s => s.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase))
                      ?? cpuTemps.FirstOrDefault(s => s.Name.Contains(preferred, StringComparison.OrdinalIgnoreCase));
            if (sensor?.Value is float value) return value;
        }
        if (cpuTemps.Count > 0) return cpuTemps.Max(s => s.Value!.Value);

        // Some boards expose CPU temperature through the Super I/O / motherboard tree instead of the CPU node.
        var fallback = Sensors(all, SensorType.Temperature)
            .Where(s => s.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)
                     || s.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => ScoreCpuTemperatureName(s.Name))
            .FirstOrDefault();
        return fallback?.Value;
    }

    private static int ScoreCpuTemperatureName(string name)
    {
        if (name.Contains("CPU Package", StringComparison.OrdinalIgnoreCase)) return 100;
        if (name.Contains("Tctl", StringComparison.OrdinalIgnoreCase) || name.Contains("Tdie", StringComparison.OrdinalIgnoreCase)) return 90;
        if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return 80;
        if (name.Contains("Package", StringComparison.OrdinalIgnoreCase)) return 60;
        return 0;
    }

    private static float? ReadGpuMemory(IEnumerable<IHardware> gpu, bool used)
    {
        var names = used
            ? new[] { "GPU Memory Used", "D3D Dedicated Memory Used", "Dedicated Memory Used" }
            : new[] { "GPU Memory Total", "D3D Dedicated Memory Total", "Dedicated Memory Total" };

        foreach (var type in new[] { SensorType.SmallData, SensorType.Data })
        {
            var sensor = Sensors(gpu, type).FirstOrDefault(s => names.Any(n => s.Name.Contains(n, StringComparison.OrdinalIgnoreCase)));
            if (sensor?.Value is float value)
                return type == SensorType.SmallData && value < 256 ? value * 1024f : value;
        }
        return null;
    }

    private static string? ReadPawnIoVersion()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                var version = key?.GetValue("DisplayVersion")?.ToString();
                if (!string.IsNullOrWhiteSpace(version)) return version;
            }
            catch { }
        }
        return null;
    }

    public void Dispose()
    {
        lock (_gate) _computer.Close();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var subHardware in hardware.SubHardware)
                subHardware.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
