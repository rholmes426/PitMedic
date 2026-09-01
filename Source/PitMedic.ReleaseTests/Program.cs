using PitMedic.Services;

var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
var day = "2026-09-01";
var retryDelay = TimeSpan.FromHours(1);

AssertFalse(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        day, "0.6.0.9|preview|installer", now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "A successful heartbeat with unchanged dimensions must remain limited to one per UTC day.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        day, "0.6.0.8|preview|installer", now.AddMinutes(-10).ToString("o"), "0.6.0.8|preview|installer", retryDelay),
    "An app-version change must send immediately even after a recent successful heartbeat.");

AssertFalse(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        null, null, now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "A failed heartbeat with unchanged dimensions must retain the one-hour retry throttle.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|portable",
        null, null, now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "An installation-type change must bypass an attempt made with older dimensions.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|stable|installer",
        day, "0.6.0.9|preview|installer", now.AddMinutes(-10).ToString("o"), null, retryDelay),
    "Legacy state without attempt dimensions must not suppress the first heartbeat after an update.");

var preservedIRacingSignature = IRacingRepairSignaturePolicy.FindKnowledgeSignature(
[
    "Windows Application Error event 1000 matched the issue window.",
    "Live iRacing error detected: Easy Anti-Cheat Error 73 [PitMedic diagnostic signature: eac-error-73]"
]);
AssertTrue(
    string.Equals(preservedIRacingSignature, "iracing-eac-error73", StringComparison.Ordinal),
    "Saved iRacing EAC evidence must reconstruct the narrow anti-cheat repair before a generic crash category.");

Console.WriteLine("PitMedic release policy tests passed.");

static void AssertTrue(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message) => AssertTrue(!value, message);
