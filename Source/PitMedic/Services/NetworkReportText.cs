using PitMedic.Models;

namespace PitMedic.Services;

internal static class NetworkReportText
{
    public static IEnumerable<string> Recommendations(NetworkRecommendation evidence)
    {
        if (evidence.HasFlag(NetworkRecommendation.LinkChange))
            yield return "A previously connected adapter went down or disappeared. If that was unexpected, check the Ethernet cable, Wi-Fi roaming, or VPN reconnect history.";
        if (evidence.HasFlag(NetworkRecommendation.Ipv4LinkLocalOnly))
            yield return "Windows repeatedly saw only a self-assigned IPv4 address (169.254.x.x). Check the router or DHCP service, then reconnect to the intended network when safe.";
        if (evidence.HasFlag(NetworkRecommendation.NoAddress))
            yield return "Windows repeatedly saw no usable IP address. If this PC should be online, verify that Wi-Fi is connected or Ethernet is plugged in and the adapter obtains an address.";
        if (evidence.HasFlag(NetworkRecommendation.NoGateway))
            yield return "Windows repeatedly saw no configured gateway on an addressed adapter. Verify the intended router, DHCP, or VPN settings if this network is supposed to reach the internet.";
        if (evidence.HasFlag(NetworkRecommendation.NoDns))
            yield return "Windows repeatedly saw no configured DNS resolver. Check the active connection's DNS configuration and any VPN or managed network policy before changing it.";
        if (evidence.HasFlag(NetworkRecommendation.LocalCounterIncrease))
            yield return "Local adapter error or discard counters rose repeatedly. Check for Wi-Fi interference or a bad cable, and compare with another adapter or device.";
    }

    public static string SessionSummary(NetworkSessionReport report)
    {
        if (report.Observations == 0)
            return report.MonitoringEnabled ? "Windows network evidence was unavailable." : "No Windows network samples were recorded.";
        return $"Samples: {report.Observations:N0} read, {report.IncompleteObservations:N0} incomplete or unavailable. "
            + $"Link drops: {report.RoutedInterfaceDrops:N0}. Samples without an address / gateway / DNS: "
            + $"{report.AddressLossSamples:N0} / {report.NoGatewaySamples:N0} / {report.NoDnsSamples:N0}. "
            + (report.CounterIntervals > 0
                ? $"Local adapter counters: {report.LocalPacketErrors:N0} errors and {report.LocalPacketDiscards:N0} discards across {report.CounterIntervals:N0} comparable intervals."
                : "Comparable packet counters were unavailable.");
    }
}
