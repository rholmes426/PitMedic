namespace PitMedic.Models;

public sealed record DistanceTelemetryStatus(
    GameKind Game,
    bool IsAvailable,
    string Message);
