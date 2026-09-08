namespace PitMedic.Models;

public enum SimulatorSessionOutcome
{
    NoErrorObserved,
    ErrorObserved,
    ReviewNeeded,
    ResultUnavailable
}

public sealed record SimulatorSessionSummary
{
    public GameKind Game { get; init; }
    public DateTimeOffset Started { get; init; }
    public DateTimeOffset Ended { get; init; }
    public SimulatorSessionOutcome Outcome { get; init; }

    public TimeSpan Duration => Ended > Started ? Ended - Started : TimeSpan.Zero;
}
