using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed record AvailableUpdate(string Version, string Title, Uri DownloadUri, Uri ReleaseUri);

public sealed record UpdateCheckResult(string Message, AvailableUpdate? Update = null);

public sealed class UpdateService : IDisposable
{
    private const int MaximumManifestBytes = 16_384;
    private const int MaximumVersionLength = 32;
    private const int MaximumTitleLength = 100;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly SettingsService _settings;
    private readonly HttpClient _httpClient;
    private readonly Uri _manifestUri;
    private readonly SemaphoreSlim _checkGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _disposed;

    public event Action<AvailableUpdate>? UpdateAvailable;

    public UpdateService(SettingsService settings, HttpClient? httpClient = null, Uri? manifestUri = null)
    {
        _settings = settings;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _manifestUri = manifestUri ?? new Uri(AppInfo.UpdateManifestUrl);
        _settings.SettingsChanged += OnSettingsChanged;
    }

    public void Start()
    {
        if (_settings.Current.CheckForUpdates)
            _ = Task.Run(() => CheckAsync(force: false, _shutdown.Token));
    }

    public async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        if (_disposed) return new UpdateCheckResult("Update checking is unavailable because PitMedic is closing.");

        var gateHeld = false;
        try
        {
            await _checkGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            gateHeld = true;
            if (_disposed) return new UpdateCheckResult("Update checking is unavailable because PitMedic is closing.");
            if (!force && !_settings.Current.CheckForUpdates)
                return new UpdateCheckResult("Automatic update checks are off.");

            var utcDay = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var state = LoadState();
            if (!force && string.Equals(state.LastAttemptUtcDay, utcDay, StringComparison.Ordinal))
                return new UpdateCheckResult("PitMedic already checked for updates today.");

            SaveState(state with { LastAttemptUtcDay = utcDay });
            using var request = new HttpRequestMessage(HttpMethod.Get, _manifestUri);
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return new UpdateCheckResult($"The update service returned HTTP {(int)response.StatusCode}.");

            var declaredLength = response.Content.Headers.ContentLength;
            if (declaredLength > MaximumManifestBytes)
                return new UpdateCheckResult("The update response was larger than expected.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var memory = new MemoryStream();
            var buffer = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (memory.Length + read > MaximumManifestBytes)
                    return new UpdateCheckResult("The update response was larger than expected.");
                memory.Write(buffer, 0, read);
            }

            memory.Position = 0;
            var manifest = await JsonSerializer.DeserializeAsync<UpdateManifest>(memory, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (!TryValidateManifest(manifest, out var update, out var validationMessage))
                return new UpdateCheckResult(validationMessage);

            if (!Version.TryParse(AppInfo.Version, out var currentVersion)
                || !Version.TryParse(update.Version, out var availableVersion))
                return new UpdateCheckResult("The update version could not be compared.");

            if (availableVersion <= currentVersion)
                return new UpdateCheckResult($"PitMedic {AppInfo.Version} is up to date.");

            if (force || !string.Equals(state.DismissedVersion, update.Version, StringComparison.OrdinalIgnoreCase))
                UpdateAvailable?.Invoke(update);
            return new UpdateCheckResult($"PitMedic {update.Version} is available.", update);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult("The update check timed out.");
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult("The update check was canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Update check failed ({ex.GetType().Name}).");
            return new UpdateCheckResult("PitMedic could not reach the update service.");
        }
        finally
        {
            if (gateHeld) _checkGate.Release();
        }
    }

    public void Dismiss(string version)
    {
        var state = LoadState();
        SaveState(state with { DismissedVersion = version });
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        if (settings.CheckForUpdates)
            _ = Task.Run(() => CheckAsync(force: false, _shutdown.Token));
    }

    private static bool TryValidateManifest(
        UpdateManifest? manifest,
        out AvailableUpdate update,
        out string message)
    {
        update = null!;
        message = "The update response was invalid.";
        if (manifest is null || manifest.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(manifest.LatestVersion)
            || manifest.LatestVersion.Length > MaximumVersionLength
            || !Version.TryParse(manifest.LatestVersion, out var parsedVersion)
            || parsedVersion.Major < 0
            || parsedVersion.Minor < 0
            || parsedVersion.Build < 0
            || parsedVersion.Revision < 0
            || string.IsNullOrWhiteSpace(manifest.Title)
            || manifest.Title.Length > MaximumTitleLength
            || !TryTrustedGitHubUri(manifest.DownloadUrl, out var downloadUri)
            || !TryTrustedGitHubUri(manifest.ReleaseUrl, out var releaseUri))
            return false;

        update = new AvailableUpdate(manifest.LatestVersion, manifest.Title.Trim(), downloadUri, releaseUri);
        message = string.Empty;
        return true;
    }

    private static bool TryTrustedGitHubUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/rholmes426/PitMedic/releases/", StringComparison.Ordinal))
            return true;
        uri = null!;
        return false;
    }

    private static UpdateCheckState LoadState()
    {
        try
        {
            if (!File.Exists(AppPaths.UpdateCheckStateFile)) return new UpdateCheckState(null, null);
            return JsonSerializer.Deserialize<UpdateCheckState>(File.ReadAllText(AppPaths.UpdateCheckStateFile), JsonOptions)
                ?? new UpdateCheckState(null, null);
        }
        catch
        {
            return new UpdateCheckState(null, null);
        }
    }

    private static void SaveState(UpdateCheckState state)
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.UpdateCheckStateFile, JsonSerializer.Serialize(state, JsonOptions));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        _shutdown.Cancel();
        _httpClient.Dispose();
        _shutdown.Dispose();
    }

    private sealed record UpdateManifest(
        int SchemaVersion,
        string LatestVersion,
        string Title,
        string DownloadUrl,
        string ReleaseUrl);

    private sealed record UpdateCheckState(string? LastAttemptUtcDay, string? DismissedVersion);
}
