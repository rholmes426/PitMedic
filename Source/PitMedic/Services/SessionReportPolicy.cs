using PitMedic.Models;

namespace PitMedic.Services;

internal static class SessionReportPolicy
{
    private const NetworkRecommendation ConfirmedNetworkEvidence = NetworkRecommendation.LinkChange
        | NetworkRecommendation.NoAddress | NetworkRecommendation.Ipv4LinkLocalOnly
        | NetworkRecommendation.NoGateway | NetworkRecommendation.NoDns | NetworkRecommendation.RepeatedPacketErrors;

    public static bool HasThermalFinding(SessionThermalReport? report) =>
        report is { MonitoringEnabled: true }
        && (report.Cpu.LimitedObservations > 0 || report.NvidiaGpu.LimitedObservations > 0);

    // Older summaries cannot distinguish a repeated packet error from repeated discards.
    // Keep independently confirmed link/configuration evidence when reading them.
    public static NetworkRecommendation NetworkFindings(NetworkSessionReport? report) =>
        report is { MonitoringEnabled: true } ? report.Recommendations & ConfirmedNetworkEvidence : NetworkRecommendation.None;
}
