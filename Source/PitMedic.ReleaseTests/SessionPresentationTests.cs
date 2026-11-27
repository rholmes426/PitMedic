using PitMedic.Models;
using PitMedic.Services;

static class SessionPresentationTests
{
    public static void Run()
    {
        Check(!SessionReportPolicy.HasThermalFinding(null), "Old sessions without evidence remain quiet.");
        Check(!SessionReportPolicy.HasThermalFinding(new()
        {
            MonitoringEnabled = true,
            Cpu = new() { Observations = 1360 },
            NvidiaGpu = new() { Observations = 1360 }
        }), "Normal thermal readings do not open a report.");
        Check(SessionReportPolicy.HasThermalFinding(new()
        {
            MonitoringEnabled = true, Cpu = new() { LimitedObservations = 1 }
        }), "A CPU thermal limit opens a report even without GPU evidence.");
        Check(SessionReportPolicy.HasThermalFinding(new()
        {
            MonitoringEnabled = true, NvidiaGpu = new() { LimitedObservations = 1 }
        }), "A GPU thermal limit opens a report even without CPU evidence.");

        Check(SessionReportPolicy.NetworkFindings(null) == NetworkRecommendation.None,
            "Missing network evidence does not open a report.");
        var oldDiscardOnly = new NetworkSessionReport
        {
            MonitoringEnabled = true, Observations = 341, CounterIntervals = 340,
            LocalPacketDiscards = 6454, Recommendations = NetworkRecommendation.LocalCounterIncrease
        };
        Check(SessionReportPolicy.NetworkFindings(oldDiscardOnly) == NetworkRecommendation.None,
            "A saved legacy discard-only warning no longer opens a report.");
        Check(SessionReportPolicy.NetworkFindings(oldDiscardOnly with
        {
            Recommendations = oldDiscardOnly.Recommendations | NetworkRecommendation.LinkChange
        }) == NetworkRecommendation.LinkChange, "Keep independent evidence from a legacy report.");
        Check(SessionReportPolicy.NetworkFindings(new()
        {
            MonitoringEnabled = true,
            Recommendations = NetworkRecommendation.NoDns | NetworkRecommendation.RepeatedPacketErrors
        }) == (NetworkRecommendation.NoDns | NetworkRecommendation.RepeatedPacketErrors),
            "Show current configuration and repeated packet-error findings.");

        var stats = ThermalTraceStatistics.Calculate(
        [
            new() { CpuTempC = 40, GpuTempC = 54 },
            new() { CpuTempC = 90 },
            new() { CpuTempC = 50, GpuTempC = 66 },
            new() { CpuTempC = float.NaN, GpuTempC = float.PositiveInfinity },
            new()
        ]);
        Check(stats.Cpu == new TemperatureStatistics(40, 90, 60), "Average valid samples; keep a brief peak.");
        Check(stats.Gpu == new TemperatureStatistics(54, 66, 60), "Calculate each sensor independently.");
        var one = ThermalTraceStatistics.Calculate([new() { CpuTempC = 45, GpuTempC = float.NegativeInfinity }]);
        Check(one.Cpu == new TemperatureStatistics(45, 45, 45) && one.Gpu is null,
            "A single reading has a min, max and average. An unavailable sensor has no statistics.");
        Check(ThermalTraceStatistics.Calculate([]) == (null, null), "An empty trace has no temperature statistics.");

        Console.WriteLine("Quiet session reports and thermal statistics fixtures passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
