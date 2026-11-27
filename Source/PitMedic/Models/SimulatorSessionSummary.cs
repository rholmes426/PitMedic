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
    public SessionThermalReport? ThermalReport { get; init; }

    public TimeSpan Duration => Ended > Started ? Ended - Started : TimeSpan.Zero;
}

public sealed record SessionThermalReport
{
    public bool MonitoringEnabled { get; init; }
    public ThermalComponentReport Cpu { get; init; } = new();
    public ThermalComponentReport NvidiaGpu { get; init; } = new();
    public float? MinimumCpuHeadroomC { get; init; }
}

public sealed record ThermalComponentReport
{
    public int Observations { get; init; }
    public int LimitedObservations { get; init; }
    public int Episodes { get; init; }
    public double ObservedSeconds { get; init; }
}
