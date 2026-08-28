namespace PitMedic.Models;

public sealed record LiveFaultEvidence(
    DateTimeOffset Timestamp,
    string SignatureId,
    string Category,
    string Message,
    string SourceFile,
    GameKind? Game = null)
{
    public string ToEvidenceText()
    {
        var game = Game.HasValue
            ? GameDefinition.Supported.FirstOrDefault(g => g.Kind == Game.Value)?.DisplayName ?? Game.Value.ToString()
            : "Simulator";
        return $"Live {game} error detected at {Timestamp:h:mm:ss tt}: {Category} — {Message}";
    }
}
