using PitMedic.Models;
using PitMedic.Services;
using System.Text.Json;

static class PassiveHealthTests
{
    public static void Run()
    {
        var start = new DateTimeOffset(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
        NetworkStackSample Sample(int seconds, params NetworkInterfaceReading[] interfaces) => new(start.AddSeconds(seconds), interfaces);
        NetworkInterfaceReading Online(long errors = 0, long discards = 0) =>
            new("do-not-persist-this-adapter-id", true, true, true, true, true, true,
                Counters: new PacketCounters(errors, 0, discards, 0));
        var monitor = new SessionNetworkMonitor();
        monitor.Observe(Sample(-5, Online()), start);
        monitor.Observe(Sample(1, Online(8, 10)), start);
        monitor.Observe(Sample(6, Online(9, 12)), start);
        monitor.Observe(Sample(11, Online(10, 15)), start);
        // Counter reset then a missing sample should never be counted as a spike or a drop.
        monitor.Observe(Sample(16, Online(0, 0)), start);
        monitor.Observe(new(start.AddSeconds(21), null), start);
        monitor.Observe(Sample(26, Online(100, 100)), start);
        monitor.Observe(Sample(31, Online() with { IsUp = false }), start);
        var linkLocal = new NetworkInterfaceReading("other", true, true, true, Ipv4LinkLocalOnly: true);
        monitor.Observe(Sample(36, linkLocal), start);
        monitor.Observe(Sample(41, linkLocal), start);
        var report = monitor.Complete();
        Check(report is { Observations: 8, IncompleteObservations: 1, LocalPacketErrors: 2, LocalPacketDiscards: 5,
            CounterIntervals: 2, RoutedInterfaceDrops: 1, Ipv4LinkLocalOnlySamples: 2 }, "Session counters and availability.");
        var expected = NetworkRecommendation.LinkChange | NetworkRecommendation.Ipv4LinkLocalOnly | NetworkRecommendation.LocalCounterIncrease;
        Check(report.Recommendations == expected && report.FirstSignAt == start.AddSeconds(6), "Exact session findings and first sign.");
        var sessionJson = JsonSerializer.Serialize(new SimulatorSessionSummary { NetworkReport = report });
        Check(!sessionJson.Contains("do-not-persist") && JsonSerializer.Deserialize<SimulatorSessionSummary>(sessionJson)?.NetworkReport == report,
            "Only aggregate network evidence is persisted; summaries round trip.");
        Check(JsonSerializer.Deserialize<SimulatorSessionSummary>("{}")?.NetworkReport is null, "Old session JSON is compatible.");

        var uncertain = new SessionNetworkMonitor();
        uncertain.Observe(Sample(1, Online()), start);
        uncertain.Observe(Sample(6, Online() with { IsUp = false, StatusKnown = false }), start);
        uncertain.Observe(Sample(11, Online() with { HasDns = false }), start);
        uncertain.Pause();
        uncertain.Observe(Sample(16, Online() with { HasDns = false }), start);
        uncertain.Observe(Sample(21, Online()), start);
        Check(uncertain.Complete().Recommendations == NetworkRecommendation.None, "Do not infer faults from unknown status or isolated DNS samples.");

        var missingConfig = new SessionNetworkMonitor();
        missingConfig.Observe(Sample(1, Online() with { HasDns = false }), start);
        missingConfig.Observe(Sample(6, Online() with { HasDns = false }), start);
        missingConfig.Observe(Sample(11, Online() with { HasGateway = false }), start);
        missingConfig.Observe(Sample(16, Online() with { HasGateway = false }), start);
        Check(missingConfig.Complete().Recommendations == (NetworkRecommendation.NoDns | NetworkRecommendation.NoGateway), "Match sustained IP configuration findings specifically.");

        WindowsHealthEvent Event(string provider, int id, int level = 2, int minutesAgo = 1) =>
            new(provider, id, level, start.AddMinutes(-minutesAgo));
        var benign = WindowsHealthPolicy.Evaluate(
        [
            Event("unrelated", 55), Event("Microsoft-Windows-WHEA-Logger", 17, 3),
            Event("Microsoft-Windows-DNS-Client", 1014), Event("Disk", 153),
            Event("Microsoft-Windows-WindowsUpdateClient", 20, minutesAgo: 121),
            Event("Display", 4101, minutesAgo: -1)
        ], true, false, start);
        Check(benign.Findings.Count == 0, "Ignore stale, future, corrected, unattributed and one-off findings.");

        var actionable = WindowsHealthPolicy.Evaluate(
        [
            Event("Ntfs", 55), Event("Disk", 153), Event("Disk", 153, minutesAgo: 2),
            Event("Microsoft-Windows-WHEA-Logger", 18), Event("Display", 4101),
            Event("Microsoft-Windows-WindowsUpdateClient", 20),
            Event("Microsoft-Windows-DNS-Client", 1014), Event("Microsoft-Windows-DNS-Client", 1014, minutesAgo: 2)
        ], true, true, start);
        Check(actionable.Findings.Select(f => f.Issue).ToHashSet().SetEquals(Enum.GetValues<WindowsHealthIssue>()),
            "Every actionable finding is mapped to specific guidance.");
        Check(actionable.Findings.Single(f => f.Issue == WindowsHealthIssue.AudioServiceStopped).Guidance.Contains("approve"),
            "Audio recovery remains approval-gated.");
        var unavailable = WindowsHealthPolicy.Evaluate(null, null, true, start);
        Check(!unavailable.LogAvailable && !unavailable.AudioStatusAvailable && unavailable.Findings.Count == 0,
            "Unavailable Windows evidence is not a healthy or failed result.");
        Check(WindowsHealthPolicy.Queries.Count() == 6, "Bounded, fixed, provider-scoped queries.");
        Console.WriteLine("Passive Windows and network health fixtures passed.");
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
    }
}
