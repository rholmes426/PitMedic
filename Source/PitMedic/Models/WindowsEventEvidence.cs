namespace PitMedic.Models;

public sealed record WindowsEventEvidence(
    string LogName,
    DateTimeOffset? TimeCreated,
    string Provider,
    int EventId,
    string Level,
    string Message);
