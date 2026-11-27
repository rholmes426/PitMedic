using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using PitMedic.Models;

namespace PitMedic.Services;

internal static class NetworkStackReader
{
    // Windows adapter metadata and counters only: no network traffic or commands.
    public static NetworkStackSample Read()
    {
        var timestamp = DateTimeOffset.Now;
        var interfaces = new List<NetworkInterfaceReading>();
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var status = adapter.OperationalStatus;
                var isUp = status == OperationalStatus.Up;
                var knownStatus = isUp || status is OperationalStatus.Down or OperationalStatus.LowerLayerDown
                    or OperationalStatus.NotPresent or OperationalStatus.Dormant;
                var read = new NetworkInterfaceReading(adapter.Id, isUp, knownStatus);
                if (isUp) read = ReadActiveAdapter(adapter, read);
                interfaces.Add(read);
            }
            return new(timestamp, interfaces);
        }
        catch (Exception ex) when (IsUnavailable(ex))
        {
            return new(timestamp, null);
        }
    }

    private static NetworkInterfaceReading ReadActiveAdapter(NetworkInterface adapter, NetworkInterfaceReading read)
    {
        try
        {
            var properties = adapter.GetIPProperties();
            var addresses = properties.UnicastAddresses.Where(a => IsConfigured(a.Address)
                && a.DuplicateAddressDetectionState is not (DuplicateAddressDetectionState.Tentative
                    or DuplicateAddressDetectionState.Duplicate or DuplicateAddressDetectionState.Invalid));
            var hasAddress = false;
            var hasIpv4LinkLocal = false;
            foreach (var entry in addresses)
            {
                if (IsIpv4LinkLocal(entry.Address)) hasIpv4LinkLocal = true;
                else if (!entry.Address.IsIPv6LinkLocal) hasAddress = true;
            }

            read = read with
            {
                ConfigurationKnown = true,
                HasAddress = hasAddress,
                HasGateway = properties.GatewayAddresses.Any(g => IsConfigured(g.Address)),
                HasDns = properties.DnsAddresses.Any(a => IsConfigured(a) || IPAddress.IsLoopback(a)),
                Ipv4LinkLocalOnly = hasIpv4LinkLocal && !hasAddress
            };
        }
        catch (Exception ex) when (IsUnavailable(ex)) { }

        if (read is { HasAddress: true, HasGateway: true })
        {
            try
            {
                var stats = adapter.GetIPStatistics();
                var counters = new PacketCounters(stats.IncomingPacketsWithErrors, stats.OutgoingPacketsWithErrors,
                    stats.IncomingPacketsDiscarded, stats.OutgoingPacketsDiscarded);
                if (counters.IsValid) read = read with { Counters = counters };
            }
            catch (Exception ex) when (IsUnavailable(ex)) { }
        }
        return read;
    }

    private static bool IsConfigured(IPAddress address) =>
        IsUnicast(address)
        && !IPAddress.IsLoopback(address) && !address.IsIPv6Multicast;

    private static bool IsUnicast(IPAddress address) =>
        !address.Equals(IPAddress.Any) && !address.Equals(IPAddress.IPv6Any);

    private static bool IsIpv4LinkLocal(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        Span<byte> bytes = stackalloc byte[4];
        return address.TryWriteBytes(bytes, out _) && bytes[0] == 169 && bytes[1] == 254;
    }

    private static bool IsUnavailable(Exception error) =>
        error is NetworkInformationException or SocketException or NotSupportedException or UnauthorizedAccessException
            or InvalidOperationException;
}

internal sealed record NetworkStackSample(DateTimeOffset Timestamp, IReadOnlyList<NetworkInterfaceReading>? Interfaces)
{
    public NetworkStackState? State => Interfaces is null ? null : new()
    {
        ActiveInterfaces = Interfaces.Count(i => i.IsUp),
        AddressedInterfaces = Interfaces.Count(i => i.IsUp && i.HasAddress),
        GatewayInterfaces = Interfaces.Count(i => i.IsUp && i.HasAddress && i.HasGateway),
        DnsInterfaces = Interfaces.Count(i => i.IsUp && i.HasDns),
        Ipv4LinkLocalOnlyInterfaces = Interfaces.Count(i => i.IsUp && i.Ipv4LinkLocalOnly),
        ConfigurationIncomplete = Interfaces.Any(i => !i.StatusKnown || (i.IsUp && !i.ConfigurationKnown))
    };
}

internal sealed record NetworkInterfaceReading(string Id, bool IsUp, bool StatusKnown,
    bool ConfigurationKnown = false, bool HasAddress = false, bool HasGateway = false,
    bool HasDns = false, bool Ipv4LinkLocalOnly = false, PacketCounters? Counters = null)
{
    public bool GatewayConfigured => IsUp && HasAddress && HasGateway;
}

internal readonly record struct PacketCounters(long RxErrors, long TxErrors, long RxDiscards, long TxDiscards)
{
    public bool IsValid => RxErrors >= 0 && TxErrors >= 0 && RxDiscards >= 0 && TxDiscards >= 0;

    public bool TryGetDeltas(PacketCounters previous, out long errors, out long discards)
    {
        errors = discards = 0;
        if (!IsValid || !previous.IsValid || RxErrors < previous.RxErrors || TxErrors < previous.TxErrors
            || RxDiscards < previous.RxDiscards || TxDiscards < previous.TxDiscards) return false;
        errors = Add(RxErrors - previous.RxErrors, TxErrors - previous.TxErrors);
        discards = Add(RxDiscards - previous.RxDiscards, TxDiscards - previous.TxDiscards);
        return true;
    }

    public static long Add(long a, long b) => a > long.MaxValue - b ? long.MaxValue : a + b;
}
