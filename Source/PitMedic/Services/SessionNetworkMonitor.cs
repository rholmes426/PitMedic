using PitMedic.Models;

namespace PitMedic.Services;

internal sealed class SessionNetworkMonitor
{
    private static readonly TimeSpan MaximumComparisonGap = TimeSpan.FromSeconds(35);
    private NetworkStackSample? _previous;
    private NetworkSessionReport _report = new();
    private NetworkRecommendation _previousConfigurationIssues;
    private bool _previousCounterIncrease;

    public void Pause()
    {
        _previous = null;
        _previousConfigurationIssues = NetworkRecommendation.None;
        _previousCounterIncrease = false;
    }

    public void Observe(NetworkStackSample sample, DateTimeOffset sessionStarted)
    {
        if (sample.Timestamp < sessionStarted || (_previous is not null && sample.Timestamp <= _previous.Timestamp)) return;
        _report = _report with { MonitoringEnabled = true };
        var state = sample.State;
        if (state is null || sample.Interfaces is null)
        {
            Pause();
            _report = _report with { IncompleteObservations = _report.IncompleteObservations + 1 };
            return;
        }

        var addressMissing = !state.ConfigurationIncomplete && state.AddressedInterfaces == 0;
        var gatewayMissing = !state.ConfigurationIncomplete && state.AddressedInterfaces > 0 && state.GatewayInterfaces == 0;
        var dnsMissing = !state.ConfigurationIncomplete && state.GatewayInterfaces > 0 && state.DnsInterfaces == 0;
        var ipv4LinkLocalOnly = addressMissing && state.Ipv4LinkLocalOnlyInterfaces > 0;
        var drops = 0;
        var comparable = false;
        long errors = 0, discards = 0;
        var nearby = _previous is not null && sample.Timestamp - _previous.Timestamp <= MaximumComparisonGap;
        if (nearby && _previous?.Interfaces is { } previous)
        {
            var nowById = sample.Interfaces.ToLookup(i => i.Id, StringComparer.OrdinalIgnoreCase);
            foreach (var old in previous.Where(i => i.GatewayConfigured && !string.IsNullOrEmpty(i.Id)))
            {
                var now = nowById[old.Id].FirstOrDefault();
                if (now is null || (!now.IsUp && now.StatusKnown))
                {
                    drops++;
                    continue;
                }
                if (now.GatewayConfigured && now.Counters is PacketCounters current && old.Counters is PacketCounters prior
                    && current.TryGetDeltas(prior, out var errorDelta, out var discardDelta))
                {
                    comparable = true;
                    errors = PacketCounters.Add(errors, errorDelta);
                    discards = PacketCounters.Add(discards, discardDelta);
                }
            }
        }
        var configIssues = NetworkRecommendation.None;
        if (addressMissing) configIssues |= ipv4LinkLocalOnly ? NetworkRecommendation.Ipv4LinkLocalOnly : NetworkRecommendation.NoAddress;
        if (gatewayMissing) configIssues |= NetworkRecommendation.NoGateway;
        if (dnsMissing) configIssues |= NetworkRecommendation.NoDns;
        var recommendations = nearby ? configIssues & _previousConfigurationIssues : NetworkRecommendation.None;
        if (drops > 0) recommendations |= NetworkRecommendation.LinkChange;
        var counterIncrease = errors > 0 || discards > 0;
        if (counterIncrease && nearby && _previousCounterIncrease) recommendations |= NetworkRecommendation.LocalCounterIncrease;
        _previous = sample;
        _previousConfigurationIssues = configIssues;
        _previousCounterIncrease = counterIncrease;
        _report = _report with
        {
            Observations = _report.Observations + 1,
            IncompleteObservations = _report.IncompleteObservations + (state.ConfigurationIncomplete ? 1 : 0),
            AddressLossSamples = _report.AddressLossSamples + (addressMissing ? 1 : 0),
            NoGatewaySamples = _report.NoGatewaySamples + (gatewayMissing ? 1 : 0),
            NoDnsSamples = _report.NoDnsSamples + (dnsMissing ? 1 : 0),
            Ipv4LinkLocalOnlySamples = _report.Ipv4LinkLocalOnlySamples + (ipv4LinkLocalOnly ? 1 : 0),
            RoutedInterfaceDrops = _report.RoutedInterfaceDrops + drops,
            CounterIntervals = _report.CounterIntervals + (comparable ? 1 : 0),
            LocalPacketErrors = PacketCounters.Add(_report.LocalPacketErrors, errors),
            LocalPacketDiscards = PacketCounters.Add(_report.LocalPacketDiscards, discards),
            FirstSignAt = _report.FirstSignAt ?? (configIssues != NetworkRecommendation.None
                || drops > 0 || counterIncrease ? sample.Timestamp : null),
            Recommendations = _report.Recommendations | recommendations
        };
    }

    public NetworkSessionReport Complete() => _report;
}
