namespace PitMedic.Models;

public sealed record RepairCapability(
    string Id,
    string Title,
    string Game,
    string Category);

public sealed record CapabilitiesSnapshot
{
    public int AutomatedFixesAvailable { get; init; }
    public long SessionsMonitored { get; init; }
    public int IssuesDetected { get; init; }
    public int IssuesResolvedAutomatically { get; init; }
    public int RepairsPerformed { get; init; }
    public int EstimatedMinutesSaved { get; init; }
    public DateTimeOffset MonitoringSince { get; init; }
    public DateTimeOffset? LastIssue { get; init; }
    public DateTimeOffset? LastRepair { get; init; }
}
