using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class AnonymousUsageService : IDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan FailedAttemptRetryDelay = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Uri? _endpoint;
    private bool _disposed;

    public AnonymousUsageService(SettingsService settings, HttpClient? httpClient = null, Uri? endpoint = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _endpoint = endpoint ?? ParseConfiguredEndpoint();
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public bool IsEndpointConfigured => _endpoint is not null;

    public void Start()
    {
        if (_settings.Current.ShareAnonymousUsage == true)
            _ = Task.Run(() => SendIfDueAsync(_shutdown.Token));
    }

    public string GetStatusText()
    {
        if (_settings.Current.ShareAnonymousUsage != true)
            return "Off — PitMedic sends no app-usage data.";
        if (_endpoint is null)
            return "Enabled, but the anonymous usage service is not configured in this build.";

        var state = LoadState();
        return state?.LastSuccessfulUtcDay is { Length: > 0 } day
            ? $"On — last anonymous count sent {day} UTC."
            : state?.LastAttemptUtcDay is { Length: > 0 } attemptDay
                ? $"On — PitMedic attempted today's count on {attemptDay} UTC and will retry later if it was not accepted."
                : "On — the first anonymous count will be sent when the service is reachable.";
    }

    public string BuildDataPreview()
    {
        var preview = new
        {
            protocol = ProtocolVersion,
            dailyToken = "<anonymous token that changes every UTC day>",
            monthlyToken = "<anonymous token that changes every UTC month>",
            appVersion = AppInfo.Version,
            channel = AppInfo.ReleaseChannel,
            installType = DetectInstallType()
        };
        return JsonSerializer.Serialize(preview, JsonOptions);
    }

    public async Task SendIfDueAsync(CancellationToken cancellationToken)
    {
        if (_disposed || _endpoint is null || _settings.Current.ShareAnonymousUsage != true) return;

        var gateHeld = false;
        try
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            if (_disposed || _settings.Current.ShareAnonymousUsage != true) return;

            var utcNow = DateTimeOffset.UtcNow;
            var utcDay = utcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var appVersion = AppInfo.Version;
            var channel = AppInfo.ReleaseChannel;
            var installType = DetectInstallType();
            var usageDimensions = $"{appVersion}|{channel}|{installType}";
            var state = LoadState();
            if (!AnonymousUsageThrottlePolicy.ShouldSend(
                    utcNow,
                    utcDay,
                    usageDimensions,
                    state?.LastSuccessfulUtcDay,
                    state?.LastSuccessfulDimensions,
                    state?.LastAttemptUtc,
                    state?.LastAttemptDimensions,
                    FailedAttemptRetryDelay))
                return;

            // Store only local send timing. A failed request may retry after a quiet one-hour delay
            // when the dimensions are unchanged; a version/channel/install change may send at once.
            SaveState(new UsageState(
                utcDay,
                utcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                state?.LastSuccessfulUtcDay,
                state?.LastSuccessfulDimensions,
                usageDimensions));

            var secret = LoadOrCreateSecret();
            var payload = new UsagePayload(
                ProtocolVersion,
                CreateRotatingToken(secret, $"pitmedic:day:{utcDay}"),
                CreateRotatingToken(secret, $"pitmedic:month:{utcNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)}"),
                appVersion,
                channel,
                installType);

            // The Worker uses a strict six-field camelCase allowlist. Use the exact same serializer
            // settings as the user-visible data preview so previewed and transmitted fields cannot drift.
            using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await _httpClient.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Write($"Anonymous usage count was not accepted (HTTP {(int)response.StatusCode}); PitMedic will retry later.");
                return;
            }

            if (_settings.Current.ShareAnonymousUsage != true)
            {
                DeleteLocalIdentity();
                return;
            }

            SaveState(new UsageState(
                utcDay,
                utcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                utcDay,
                usageDimensions,
                usageDimensions));
            AppLog.Write("Anonymous usage count sent successfully.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            AppLog.Write("Anonymous usage count timed out; PitMedic will try again later.");
        }
        catch (OperationCanceledException)
        {
            // Normal application shutdown.
        }
        catch (Exception ex)
        {
            // Do not log request content, tokens, URLs, or local environment details.
            AppLog.Write($"Anonymous usage count failed ({ex.GetType().Name}); PitMedic will try again later.");
        }
        finally
        {
            if (gateHeld) _sendGate.Release();
        }
    }

    public void DeleteLocalIdentity()
    {
        DeleteIfPresent(AppPaths.AnonymousUsageKeyFile);
        DeleteIfPresent(AppPaths.AnonymousUsageStateFile);
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        if (settings.ShareAnonymousUsage == true)
            _ = Task.Run(() => SendIfDueAsync(_shutdown.Token));
        else
            DeleteLocalIdentity();
    }

    private static byte[] LoadOrCreateSecret()
    {
        Directory.CreateDirectory(AppPaths.Root);
        if (File.Exists(AppPaths.AnonymousUsageKeyFile))
        {
            try
            {
                var existing = Convert.FromBase64String(File.ReadAllText(AppPaths.AnonymousUsageKeyFile));
                if (existing.Length == 32) return existing;
            }
            catch { }
        }

        var secret = RandomNumberGenerator.GetBytes(32);
        var temp = AppPaths.AnonymousUsageKeyFile + ".tmp";
        File.WriteAllText(temp, Convert.ToBase64String(secret));
        File.Move(temp, AppPaths.AnonymousUsageKeyFile, true);
        return secret;
    }

    private static string CreateRotatingToken(byte[] secret, string period)
    {
        var digest = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(period));
        return Convert.ToHexStringLower(digest);
    }

    private static string DetectInstallType()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var executable = Environment.ProcessPath ?? AppContext.BaseDirectory;
        return !string.IsNullOrWhiteSpace(programFiles)
            && executable.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)
            ? "installer"
            : "portable";
    }

    private static UsageState? LoadState()
    {
        try
        {
            if (!File.Exists(AppPaths.AnonymousUsageStateFile)) return null;
            return JsonSerializer.Deserialize<UsageState>(File.ReadAllText(AppPaths.AnonymousUsageStateFile), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState(UsageState state)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.AnonymousUsageStateFile, JsonSerializer.Serialize(state, JsonOptions));
    }

    private static void DeleteIfPresent(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not remove anonymous usage state ({ex.GetType().Name}).");
        }
    }

    private static Uri? ParseConfiguredEndpoint() =>
        Uri.TryCreate(AppInfo.AnonymousUsageEndpoint, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        _shutdown.Cancel();
        _httpClient.Dispose();
        _shutdown.Dispose();
    }

    private sealed record UsagePayload(
        int Protocol,
        string DailyToken,
        string MonthlyToken,
        string AppVersion,
        string Channel,
        string InstallType);

    private sealed record UsageState(
        string? LastAttemptUtcDay,
        string? LastAttemptUtc,
        string? LastSuccessfulUtcDay,
        string? LastSuccessfulDimensions = null,
        string? LastAttemptDimensions = null);
}
