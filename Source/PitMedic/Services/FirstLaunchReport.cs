using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PitMedic.Services;

// A one-time delivery receipt, deliberately separate from rotating activity tokens.
// Kept across upgrades and opt-out so neither can become another first launch.
public sealed class FirstLaunchReport
{
    private static readonly object Gate = new();
    private static readonly string StatePath = Path.Combine(AppPaths.Root, "first-launch-report.json");
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static void Initialize()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(StatePath)) return;
                var existing = File.Exists(AppPaths.SettingsFile) || File.Exists(AppPaths.AppLog)
                    || File.Exists(AppPaths.AnonymousUsageKeyFile) || File.Exists(AppPaths.StatsFile);
                Save(existing ? new State(null, null) : new State(new Payload(1,
                    Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
                    DateTimeOffset.UtcNow.ToString("yyyy-MM-dd"), AppInfo.Version,
                    AppInfo.ReleaseChannel, AnonymousUsageService.DetectInstallType()), null));
            }
            catch (Exception ex) { LogFailure(ex); }
        }
    }

    public void Discard()
    {
        lock (Gate)
        {
            try { Save(new State(null, null)); }
            catch (Exception ex) { LogFailure(ex); }
        }
    }

    public async Task SendIfDueAsync(HttpClient client, Uri endpoint, Func<bool> sharing, CancellationToken cancellationToken)
    {
        try
        {
            Payload payload;
            lock (Gate)
            {
                if (!sharing()) return;
                var state = File.Exists(StatePath)
                    ? JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath), Json) : null;
                if (state?.Pending is not { } pending) return;
                var now = DateTimeOffset.UtcNow;
                if (!DateTimeOffset.TryParse(pending.Day, out var day) || now.UtcDateTime.Date - day.UtcDateTime.Date > TimeSpan.FromDays(90))
                {
                    Save(new State(null, null));
                    return;
                }
                if (state.LastAttempt is { } last && now - last < TimeSpan.FromHours(1)) return;
                payload = pending;
                Save(state with { LastAttempt = now });
            }
            if (!sharing()) return;
            using var content = new StringContent(JsonSerializer.Serialize(payload, Json), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(endpoint, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return;
            lock (Gate) { Save(new State(null, null)); }
            AppLog.Write("Optional first-launch count accepted.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { LogFailure(ex); }
    }

    private static void Save(State state)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var temp = StatePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, StatePath, true);
    }

    private static void LogFailure(Exception ex) => AppLog.Write($"Optional first-launch report could not complete ({ex.GetType().Name}).");
    private sealed record State(Payload? Pending, DateTimeOffset? LastAttempt);
    private sealed record Payload(int Protocol, string EventToken, string Day, string AppVersion, string Channel, string InstallType);
}
