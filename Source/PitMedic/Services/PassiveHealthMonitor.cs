using System.Diagnostics;
using PitMedic.Models;

namespace PitMedic.Services;

internal sealed class PassiveHealthMonitor
{
    private readonly Queue<NetworkStackSample> _networkWindow = new();
    private bool _enabled;
    private long? _lastNetwork;
    private long? _lastWindows;
    private bool _previousAudioStopped;
    private WindowsHealthSummary? _windows;
    private PassiveHealthStatus _status = new(false, null, null);
    public PassiveHealthStatus Status => Volatile.Read(ref _status);

    public bool Update(bool enabled, bool sessionRunning, GameWatchService games)
    {
        if (!enabled)
        {
            if (!_enabled) return false;
            _enabled = false;
            _networkWindow.Clear();
            _lastNetwork = _lastWindows = null;
            _previousAudioStopped = false;
            _windows = null;
            games.RecordNetworkSample(null);
            Volatile.Write(ref _status, new(false, null, null));
            return true;
        }

        _enabled = true;
        var tick = Stopwatch.GetTimestamp();
        var networkDue = Due(_lastNetwork, tick, sessionRunning ? 5 : 20);
        // Defer event-log reads and service queries while racing. Network reads remain passive.
        var windowsDue = !sessionRunning && Due(_lastWindows, tick, 60);
        if (!networkDue && !windowsDue) return false;

        if (networkDue)
        {
            _lastNetwork = tick;
            var sample = NetworkStackReader.Read();
            if (sessionRunning) games.RecordNetworkSample(sample);
            _networkWindow.Enqueue(sample);
            while (_networkWindow.Count > 4 || _networkWindow.Peek().Timestamp < sample.Timestamp.AddSeconds(-70))
                _networkWindow.Dequeue();
        }
        if (windowsDue)
        {
            var now = DateTimeOffset.Now;
            var audio = WindowsHealthReader.ReadAutomaticAudioServiceStopped();
            // A gap while racing is not a continuous confirmation that the service stayed stopped.
            var priorAudio = _previousAudioStopped && _lastWindows is long prior
                && Stopwatch.GetElapsedTime(prior, tick) <= TimeSpan.FromSeconds(90);
            _windows = WindowsHealthPolicy.Evaluate(WindowsHealthReader.ReadEvents(), audio, priorAudio, now);
            _previousAudioStopped = audio == true;
            _lastWindows = tick;
        }
        var currentNetwork = new SessionNetworkMonitor();
        foreach (var sample in _networkWindow)
            currentNetwork.Observe(sample, DateTimeOffset.MinValue);
        Volatile.Write(ref _status, new(true, currentNetwork.Complete(), _windows));
        return true;
    }

    private static bool Due(long? previous, long tick, int seconds) =>
        previous is not long last || Stopwatch.GetElapsedTime(last, tick) >= TimeSpan.FromSeconds(seconds);
}

internal sealed record PassiveHealthStatus(bool Enabled, NetworkSessionReport? Network, WindowsHealthSummary? Windows);
