namespace PitMedic.Models;

public sealed record NetworkStackState
{
    public int ActiveInterfaces { get; init; }
    public int AddressedInterfaces { get; init; }
    public int GatewayInterfaces { get; init; }
    public int DnsInterfaces { get; init; }
    public int Ipv4LinkLocalOnlyInterfaces { get; init; }
    public bool ConfigurationIncomplete { get; init; }
}

public sealed record NetworkSessionReport
{
    public bool MonitoringEnabled { get; init; }
    public int Observations { get; init; }
    public int IncompleteObservations { get; init; }
    public int AddressLossSamples { get; init; }
    public int NoGatewaySamples { get; init; }
    public int NoDnsSamples { get; init; }
    public int Ipv4LinkLocalOnlySamples { get; init; }
    public int RoutedInterfaceDrops { get; init; }
    public int CounterIntervals { get; init; }
    public long LocalPacketErrors { get; init; }
    public long LocalPacketDiscards { get; init; }
    public DateTimeOffset? FirstSignAt { get; init; }
    public NetworkRecommendation Recommendations { get; init; }
}

[Flags]
public enum NetworkRecommendation
{
    None = 0, LinkChange = 1, NoAddress = 2, Ipv4LinkLocalOnly = 4,
    NoGateway = 8, NoDns = 16,
    LocalCounterIncrease = 32, // Legacy saved reports may have set this for discards alone.
    RepeatedPacketErrors = 64
}
