namespace PitMedic.Models;

public sealed record KnowledgeEntry
{
    public string Id { get; init; } = string.Empty;
    public string Game { get; init; } = string.Empty;
    public string Issue { get; init; } = string.Empty;
    public string Detection { get; init; } = string.Empty;
    public string RepairStrategy { get; init; } = string.Empty;
    public string Safety { get; init; } = string.Empty;
    public IReadOnlyList<string> Signatures { get; init; } = Array.Empty<string>();
    public IReadOnlyList<RepairReference> References { get; init; } = Array.Empty<RepairReference>();
}
