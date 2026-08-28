namespace PitMedic.Models;

public sealed record ElevatedRepairRequest
{
    public const int CurrentProtocolVersion = 1;

    public int ProtocolVersion { get; init; } = CurrentProtocolVersion;
    public Guid RequestId { get; init; }
    public Guid RepairStatusId { get; init; }
    public int ParentProcessId { get; init; }
    public string StatusPipeName { get; init; } = string.Empty;
    public string IncidentFolder { get; init; } = string.Empty;
    public string RepairId { get; init; } = string.Empty;
    public bool Automatic { get; init; }
    public bool KeepRepairBackups { get; init; } = true;
}
