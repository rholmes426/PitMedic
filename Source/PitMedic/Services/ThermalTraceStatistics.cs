using PitMedic.Models;

namespace PitMedic.Services;

internal readonly record struct TemperatureStatistics(float Minimum, float Maximum, float Average);

internal static class ThermalTraceStatistics
{
    // Samples are prefiltered to the graph's visible time range, before graphical downsampling.
    public static (TemperatureStatistics? Cpu, TemperatureStatistics? Gpu) Calculate(IReadOnlyList<TelemetrySample> samples)
    {
        var cpu = new Accumulator();
        var gpu = new Accumulator();
        for (var i = 0; i < samples.Count; i++)
        {
            cpu.Add(samples[i].CpuTempC);
            gpu.Add(samples[i].GpuTempC);
        }
        return (cpu.Result, gpu.Result);
    }

    private struct Accumulator
    {
        private int _count;
        private double _sum;
        private float _minimum, _maximum;

        public void Add(float? reading)
        {
            if (reading is not float temperature || !float.IsFinite(temperature)) return;
            _minimum = _count == 0 ? temperature : Math.Min(_minimum, temperature);
            _maximum = _count == 0 ? temperature : Math.Max(_maximum, temperature);
            _count++;
            _sum += temperature;
        }

        public readonly TemperatureStatistics? Result =>
            _count == 0 ? null : new TemperatureStatistics(_minimum, _maximum, (float)(_sum / _count));
    }
}
