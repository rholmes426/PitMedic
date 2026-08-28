namespace PitMedic.Models;

public sealed record TelemetrySample
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public float? CpuTempC { get; init; }
    public float? CpuLoadPct { get; init; }
    public float? CpuClockMhz { get; init; }
    public float? CpuPowerW { get; init; }
    public float? GpuTempC { get; init; }
    public float? GpuHotspotC { get; init; }
    public float? GpuMemoryTempC { get; init; }
    public float? GpuLoadPct { get; init; }
    public float? GpuClockMhz { get; init; }
    public float? GpuPowerW { get; init; }
    public float? GpuFanRpm { get; init; }
    public float? GpuMemoryUsedMb { get; init; }
    public float? GpuMemoryTotalMb { get; init; }
    public float? MemoryLoadPct { get; init; }
}
