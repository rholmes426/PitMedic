using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>Accumulates sensor evidence across the entire tracked simulator session.</summary>
internal sealed class SessionThermalMonitor
{
    private readonly Component _cpu = new();
    private readonly Component _gpu = new();
    private bool _monitoringEnabled;
    private float? _minimumCpuHeadroom;

    public void Observe(TelemetrySample sample, DateTimeOffset sessionStarted, bool enabled)
    {
        _monitoringEnabled |= enabled;
        if (!enabled || sample.Timestamp < sessionStarted)
        {
            _cpu.Interrupt();
            _gpu.Interrupt();
            return;
        }

        var distance = sample.CpuThermalHeadroomC;
        var cpuTime = sample.CpuThermalReadingAt;
        if (distance is float headroom && float.IsFinite(headroom) && headroom is >= 0 and <= 127
            && cpuTime is DateTimeOffset measured && measured >= sessionStarted && measured <= sample.Timestamp)
        {
            _cpu.Observe(measured, headroom == 0);
            _minimumCpuHeadroom = _minimumCpuHeadroom.HasValue ? Math.Min(_minimumCpuHeadroom.Value, headroom) : headroom;
        }
        else _cpu.Interrupt();

        if (sample.NvidiaGpuThermalLimited is bool gpuLimited)
            _gpu.Observe(sample.Timestamp, gpuLimited);
        else _gpu.Interrupt();
    }

    public SessionThermalReport Complete() => new()
    {
        MonitoringEnabled = _monitoringEnabled,
        Cpu = _cpu.Report,
        NvidiaGpu = _gpu.Report,
        MinimumCpuHeadroomC = _minimumCpuHeadroom
    };

    // NVIDIA defines these two masks for thermal slowdown. Power caps, idle,
    // generic hardware slowdown and power-brake reasons are deliberately excluded.
    public const ulong NvidiaThermalReasonMask = 0x20UL | 0x40UL;

    public static bool? NvidiaThermalLimit(ulong supported, ulong active) =>
        (supported & NvidiaThermalReasonMask) == 0 ? null : (active & supported & NvidiaThermalReasonMask) != 0;

    private sealed class Component
    {
        private static readonly TimeSpan MaximumSampleGap = TimeSpan.FromSeconds(12);
        private DateTimeOffset? _lastObserved;
        private bool _lastLimited;
        private int _observations;
        private int _limited;
        private int _episodes;
        private double _seconds;

        public void Interrupt() => _lastLimited = false;

        public void Observe(DateTimeOffset timestamp, bool limited)
        {
            if (_lastObserved is DateTimeOffset last && timestamp == last) return;
            var gap = _lastObserved is DateTimeOffset previous ? timestamp - previous : TimeSpan.MaxValue;
            var continuous = gap > TimeSpan.Zero && gap <= MaximumSampleGap && _lastLimited;
            _observations++;
            if (limited)
            {
                _limited++;
                if (continuous) _seconds += gap.TotalSeconds;
                else _episodes++;
            }
            _lastLimited = limited;
            _lastObserved = timestamp;
        }

        public ThermalComponentReport Report => new()
        {
            Observations = _observations,
            LimitedObservations = _limited,
            Episodes = _episodes,
            ObservedSeconds = _seconds
        };
    }
}
