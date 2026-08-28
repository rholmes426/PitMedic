using PitMedic.Models;

namespace PitMedic.Services;

public sealed class TelemetryBuffer
{
    private readonly object _gate = new();
    private readonly Queue<TelemetrySample> _samples = new();
    private readonly int _maxCapacity;

    public TelemetryBuffer(int maxCapacity = 3600) => _maxCapacity = Math.Max(300, maxCapacity);

    public void Add(TelemetrySample sample)
    {
        lock (_gate)
        {
            _samples.Enqueue(sample);
            while (_samples.Count > _maxCapacity)
                _samples.Dequeue();
        }
    }

    public IReadOnlyList<TelemetrySample> Snapshot(int minutes = 10)
    {
        var cutoff = DateTimeOffset.Now.AddMinutes(-Math.Clamp(minutes, 1, 60));
        lock (_gate)
            return _samples.Where(s => s.Timestamp >= cutoff).ToArray();
    }
}
