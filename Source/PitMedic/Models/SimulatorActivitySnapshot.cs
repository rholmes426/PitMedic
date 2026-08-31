namespace PitMedic.Models;

public sealed record SimulatorActivitySnapshot(
    GameKind Game,
    TimeSpan TimeMonitored,
    int CleanStreak,
    double? MilesMonitored,
    BestLapRecord? BestLap);
