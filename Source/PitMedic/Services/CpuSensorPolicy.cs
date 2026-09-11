namespace PitMedic.Services;

internal static class CpuSensorPolicy
{
    // Zero is a common unavailable sentinel in the CPU driver path. Do not
    // convert it to 32 F or let it suppress the protected-service fallback.
    public static float? PositiveReading(float? value) =>
        value is float reading && float.IsFinite(reading) && reading > 0 ? reading : null;

    public static float? PreferValid(float? primary, float? fallback) =>
        PositiveReading(primary) ?? PositiveReading(fallback);
}
