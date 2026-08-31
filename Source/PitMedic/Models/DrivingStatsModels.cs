namespace PitMedic.Models;

public sealed record BestLapRecord(
    GameKind Game,
    string Track,
    string Layout,
    string Car,
    double LapSeconds,
    DateTimeOffset RecordedAt)
{
    public string CombinationKey => string.Join("|",
        Game,
        Normalize(Track),
        Normalize(Layout),
        Normalize(Car));

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToLowerInvariant().Split(
            [' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}

public sealed record LapBenchmark(
    bool Available,
    double? LapSeconds,
    string SourceKind,
    string SourceName,
    string? SourceUrl,
    string Confidence,
    DateTimeOffset? CheckedAt);
