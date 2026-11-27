namespace PitMedic.Services;

internal static class CpuSensorPolicy
{
    // Zero is a common unavailable sentinel in the CPU driver path. Do not
    // convert it to 32 F or let it suppress the protected-service fallback.
    public static float? PositiveReading(float? value) =>
        value is float reading && float.IsFinite(reading) && reading > 0 ? reading : null;

    public static float? PreferValid(float? primary, float? fallback) =>
        PositiveReading(primary) ?? PositiveReading(fallback);

    public static bool IsThermalHeadroomName(string name) =>
        name.EndsWith(" Distance to TjMax", StringComparison.OrdinalIgnoreCase);

    // Unlike absolute temperature, zero is meaningful for the Intel DTS distance
    // to TjMax. Only accept this specifically named relative sensor.
    public static float? ThermalHeadroom(IEnumerable<(string Name, float? Value)> readings)
    {
        float? minimum = null;
        foreach (var (name, value) in readings)
        {
            if (!IsThermalHeadroomName(name)
                || value is not float distance || !float.IsFinite(distance) || distance is < 0 or > 127)
                continue;
            minimum = minimum.HasValue ? Math.Min(minimum.Value, distance) : distance;
        }
        return minimum;
    }
}
