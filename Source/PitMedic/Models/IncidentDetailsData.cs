namespace PitMedic.Models;

public sealed record IncidentRepairAction
{
    public DateTimeOffset? Timestamp { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}

public sealed record IncidentDetailsData
{
    public IncidentRecord Incident { get; init; } = new();
    public RepairPlan? RepairPlan { get; init; }
    public bool IsResolved { get; init; }
    public bool RepairAttempted { get; init; }
    public bool RepairInProgress { get; init; }
    public bool RepairCancelled { get; init; }
    public bool RepairFailed { get; init; }
    public string PlainLanguageExplanation { get; init; } = string.Empty;
    public string OutcomeHeadline { get; init; } = string.Empty;
    public string ResolutionSummary { get; init; } = string.Empty;
    public string NextStep { get; init; } = string.Empty;
    public string BackupFolder { get; init; } = string.Empty;
    public DateTimeOffset? RepairStarted { get; init; }
    public DateTimeOffset? RepairUpdated { get; init; }
    public IReadOnlyList<string> ResolutionActions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IncidentRepairAction> RepairActivity { get; init; } = Array.Empty<IncidentRepairAction>();
    public IReadOnlyList<RepairReference> References { get; init; } = Array.Empty<RepairReference>();
}
