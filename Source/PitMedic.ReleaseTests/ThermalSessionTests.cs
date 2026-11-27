using System.Text.Json;
using PitMedic.Models;
using PitMedic.Services;

internal static class ThermalSessionTests
{
    public static void Run()
    {
        Assert(CpuSensorPolicy.ThermalHeadroom(new (string, float?)[] { ("CPU Package", 0), ("Core Temp", 100) }) == null,
            "An ordinary temperature is not a thermal-limit signal.");
        Assert(CpuSensorPolicy.ThermalHeadroom(new (string, float?)[] { ("CPU Core #1 Distance to TjMax", 11), ("CPU Core #2 Distance to TjMax", 0), ("CPU Core #3 Distance to TjMax", -1) }) == 0,
            "Zero thermal headroom is valid and the smallest valid per-core reading wins.");
        Assert(CpuSensorPolicy.ThermalHeadroom(new (string, float?)[] { ("CPU Core #1 Distance to TjMax", float.NaN), ("CPU Core #2 Distance to TjMax", 140) }) == null,
            "Malformed DTS readings remain unknown.");
        Assert(SessionThermalMonitor.NvidiaThermalLimit(0x60, 0x20) == true &&
               SessionThermalMonitor.NvidiaThermalLimit(0x60, 0x40) == true &&
               SessionThermalMonitor.NvidiaThermalLimit(0x60, 0x8 | 0x4 | 0x80) == false &&
               SessionThermalMonitor.NvidiaThermalLimit(0x4, 0x4) == null,
            "Only supported NVIDIA thermal slowdown reasons count.");

        var started = DateTimeOffset.UtcNow;
        var monitor = new SessionThermalMonitor();
        monitor.Observe(new TelemetrySample { Timestamp = started.AddSeconds(-1), CpuThermalHeadroomC = 0,
            CpuThermalReadingAt = started.AddSeconds(-1), NvidiaGpuThermalLimited = true }, started, true);
        monitor.Observe(Sample(1, 0, true), started, true);
        monitor.Observe(Sample(2, 0, true, cpuTime: 1), started, true);
        monitor.Observe(Sample(3, 0, true), started, true);
        monitor.Observe(Sample(4, null, null), started, true);
        monitor.Observe(Sample(20, 0, true), started, true);
        monitor.Observe(Sample(22, 20, false), started, true);
        monitor.Observe(Sample(24, 0, true), started, false);
        monitor.Observe(Sample(26, 0, true, cpuTime: -1), started, true);

        var report = monitor.Complete();
        Assert(report.MonitoringEnabled && report.Cpu.Observations == 4 && report.Cpu.LimitedObservations == 3 &&
               report.Cpu.Episodes == 2 && report.Cpu.ObservedSeconds == 2 && report.MinimumCpuHeadroomC == 0,
            "CPU summaries exclude stale, duplicate and disabled samples; gaps do not extend limited intervals.");
        Assert(report.NvidiaGpu.Observations == 6 && report.NvidiaGpu.LimitedObservations == 5 &&
               report.NvidiaGpu.Episodes == 3 && report.NvidiaGpu.ObservedSeconds == 2,
            "GPU summaries independently record actual reported flags and break intervals at gaps.");
        var unsupported = new SessionThermalMonitor();
        unsupported.Observe(Sample(1, null, null), started, true);
        Assert(unsupported.Complete().MonitoringEnabled && unsupported.Complete().Cpu.Observations == 0 &&
               unsupported.Complete().NvidiaGpu.Observations == 0, "Unavailable telemetry must not appear healthy.");

        var folder = Path.Combine(Path.GetTempPath(), "pitmedic-thermal-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "sessions.json");
            var session = new SimulatorSessionSummary { Game = GameKind.IRacing, Started = started,
                Ended = started.AddSeconds(30), Outcome = SimulatorSessionOutcome.NoErrorObserved, ThermalReport = report };
            var store = new SimulatorSessionStore(path);
            store.Save(session);
            store.Save(session with { Ended = started.AddSeconds(20), ThermalReport = null });
            Assert(new SimulatorSessionStore(path).Get(GameKind.IRacing)?.ThermalReport == report,
                "The thermal report survives reload and a late completion cannot overwrite a newer session.");
            File.WriteAllText(path, JsonSerializer.Serialize(new Dictionary<GameKind, object> { [GameKind.IRacing] =
                new { Game = GameKind.IRacing, Started = started, Ended = started.AddSeconds(30), Outcome = SimulatorSessionOutcome.NoErrorObserved } }));
            Assert(new SimulatorSessionStore(path).Get(GameKind.IRacing)?.ThermalReport is null,
                "Historical sessions load without inventing thermal data.");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("Session thermal monitoring tests passed.");

        TelemetrySample Sample(int seconds, float? headroom, bool? gpu, int? cpuTime = null) => new()
        {
            Timestamp = started.AddSeconds(seconds),
            CpuThermalHeadroomC = headroom,
            CpuThermalReadingAt = headroom.HasValue ? started.AddSeconds(cpuTime ?? seconds) : null,
            NvidiaGpuThermalLimited = gpu
        };
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
