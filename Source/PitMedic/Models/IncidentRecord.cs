namespace PitMedic.Models;

public sealed record CrashClassification(
    string Category,
    int Confidence,
    string Summary,
    IReadOnlyList<string> Evidence);

public sealed record IncidentRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Game { get; init; } = string.Empty;
    public string Executable { get; init; } = string.Empty;
    public string ProcessPath { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public DateTimeOffset SessionStarted { get; init; }
    public DateTimeOffset IncidentTime { get; init; }
    public int? ExitCode { get; init; }
    public CrashClassification Classification { get; init; } = new("Unknown", 0, "No classification", Array.Empty<string>());
    public RepairPlan? RecommendedRepair { get; init; }
    public string IncidentFolder { get; init; } = string.Empty;
}

public sealed record IncidentSummary(
    DateTimeOffset Timestamp,
    string Game,
    string Category,
    int Confidence,
    string Folder,
    bool RepairAvailable = false,
    string RepairTitle = "",
    int EstimatedMinutes = 0,
    bool IsResolved = false,
    string ResolutionText = "",
    string Summary = "",
    bool IsDismissed = false,
    bool RequiresRepairApproval = false)
{
    public string ActionLabel => IsResolved ? "Details" : RepairAvailable ? "Repair" : "Open";
    public string StatusLabel => IsResolved ? (string.IsNullOrWhiteSpace(ResolutionText) ? "RESOLVED" : ResolutionText.ToUpperInvariant()) : string.Empty;
    public string HistoryStatus => IsResolved ? "Resolved" : IsDismissed ? "Acknowledged" : RepairAvailable ? "Repair available" : "Captured";
}
