namespace PitMedic.Services;

public static class AnonymousUsageThrottlePolicy
{
    public static bool ShouldSend(
        DateTimeOffset utcNow,
        string utcDay,
        string usageDimensions,
        string? lastSuccessfulUtcDay,
        string? lastSuccessfulDimensions,
        string? lastAttemptUtc,
        string? lastAttemptDimensions,
        TimeSpan retryDelay)
    {
        if (string.Equals(lastSuccessfulUtcDay, utcDay, StringComparison.Ordinal)
            && string.Equals(lastSuccessfulDimensions, usageDimensions, StringComparison.Ordinal))
            return false;

        if (!string.Equals(lastAttemptDimensions, usageDimensions, StringComparison.Ordinal))
            return true;

        return string.IsNullOrWhiteSpace(lastAttemptUtc)
            || !DateTimeOffset.TryParse(
                lastAttemptUtc,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind,
                out var parsedAttempt)
            || utcNow - parsedAttempt >= retryDelay;
    }
}
