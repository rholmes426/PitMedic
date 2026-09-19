using PitMedic.Services;
using System.Net;
using System.Text.Json.Nodes;

void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reset() { if (Directory.Exists(AppPaths.Root)) Directory.Delete(AppPaths.Root, true); Directory.CreateDirectory(AppPaths.Root); }
var firstPath = Path.Combine(AppPaths.Root, "first-launch-report.json");
try {
    Reset();
    FirstLaunchReport.Initialize();
    var original = File.ReadAllText(firstPath);
    FirstLaunchReport.Initialize();
    Check(original == File.ReadAllText(firstPath), "Upgrade/restart must preserve pending receipt");
    var handler = new CaptureHandler();
    using var client = new HttpClient(handler);
    var report = new FirstLaunchReport();
    var endpoint = new Uri("https://example.invalid/v1/first-launch");
    await report.SendIfDueAsync(client, endpoint, () => false, default);
    Check(handler.Count == 0, "Opted-out users must send nothing");
    handler.Status = HttpStatusCode.ServiceUnavailable;
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    var failedBody = handler.LastBody;
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    Check(handler.Count == 1, "Failed first launch must respect retry delay");
    var state = JsonNode.Parse(File.ReadAllText(firstPath))!;
    state["lastAttempt"] = DateTimeOffset.UtcNow.AddHours(-2).ToString("O");
    File.WriteAllText(firstPath, state.ToJsonString());
    handler.Status = HttpStatusCode.Accepted;
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    Check(handler.Count == 2 && failedBody == handler.LastBody, "Retry must use identical receipt and original dimensions");
    FirstLaunchReport.Initialize();
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    Check(handler.Count == 2, "Acknowledged report must not repeat after restart");
    Reset(); File.WriteAllText(AppPaths.SettingsFile, "{}"); FirstLaunchReport.Initialize();
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    Check(handler.Count == 2, "Existing profiles must not count as new installs");
    Reset(); FirstLaunchReport.Initialize(); report.Discard(); FirstLaunchReport.Initialize();
    await report.SendIfDueAsync(client, endpoint, () => true, default);
    Check(handler.Count == 2, "Opt-out then re-enable must not create another first launch");

    Reset(); File.WriteAllText(AppPaths.SettingsFile, "{}"); FirstLaunchReport.Initialize();
    var clock = new TestClock();
    var settings = new SettingsService(); settings.Current.ShareAnonymousUsage = true;
    var activeHandler = new CaptureHandler();
    using (var usage = new AnonymousUsageService(settings, new HttpClient(activeHandler), timeProvider: clock, checkInterval: TimeSpan.FromMilliseconds(20))) {
        usage.Start(); usage.Start();
        await WaitFor(() => activeHandler.Count >= 1);
        await Task.Delay(80);
        Check(activeHandler.Count == 1, "Repeated timer checks must not duplicate today's count");
        clock.Now = clock.Now.AddDays(1);
        await WaitFor(() => activeHandler.Count >= 2);
        activeHandler.Status = HttpStatusCode.ServiceUnavailable;
        clock.Now = clock.Now.AddDays(1);
        await WaitFor(() => activeHandler.Count >= 3);
        await Task.Delay(80);
        Check(activeHandler.Count == 3, "Timer failures must be throttled");
        activeHandler.Status = HttpStatusCode.Accepted;
        clock.Now = clock.Now.AddHours(2);
        await WaitFor(() => activeHandler.Count >= 4);
        settings.Set(false);
        clock.Now = clock.Now.AddDays(1);
        await Task.Delay(100);
        Check(activeHandler.Count == 4, "Timer must respect opt-out");
    }
    Console.WriteLine("Usage delivery regressions passed: timer rollover/retry, consent, migration, stable receipts and acknowledgement.");
} finally { if (Directory.Exists(AppPaths.Root)) Directory.Delete(AppPaths.Root, true); }

static async Task WaitFor(Func<bool> condition) {
    for (var i=0; i<100 && !condition(); i++) await Task.Delay(20);
    if (!condition()) throw new Exception("Timed out waiting for scheduled usage count");
    await Task.Delay(30); // Let successful acknowledgement state finish writing.
}
sealed class CaptureHandler : HttpMessageHandler {
    public int Count;
    public string? LastBody;
    public HttpStatusCode Status = HttpStatusCode.Accepted;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        LastBody = await request.Content!.ReadAsStringAsync(ct);
        Interlocked.Increment(ref Count);
        return new HttpResponseMessage(Status);
    }
}
sealed class TestClock : TimeProvider {
    public DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
