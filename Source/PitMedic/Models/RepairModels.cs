namespace PitMedic.Models;

public enum RepairSafety
{
    Automatic,
    Reversible,
    Significant
}

public sealed record RepairReference
{
    public string Title { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
    public bool IsOfficial { get; init; }
}

public sealed record RepairPlan
{
    public string Id { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string Game { get; init; } = string.Empty;
    public RepairSafety Safety { get; init; } = RepairSafety.Reversible;
    public int EstimatedMinutes { get; init; }
    public bool RequiresApproval { get; init; }
    public string SteamAppId { get; init; } = string.Empty;
    public IReadOnlyList<string> AffectedContentRelativePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RepairReference> References { get; init; } = Array.Empty<RepairReference>();
}

public sealed record RepairStatus
{
    public Guid RepairId { get; init; } = Guid.NewGuid();
    public string IncidentFolder { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public int StepNumber { get; init; }
    public int TotalSteps { get; init; }
    public int Percent { get; init; }
    public int EstimatedSecondsRemaining { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public bool IsActive { get; init; }
    public bool IsComplete { get; init; }
    public bool Success { get; init; }
    public string? BackupFolder { get; init; }
}
