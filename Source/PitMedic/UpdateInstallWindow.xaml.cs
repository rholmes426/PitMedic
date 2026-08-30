using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using PitMedic.Services;

namespace PitMedic;

public partial class UpdateInstallWindow : Window
{
    private const long MaximumInstallerBytes = 300L * 1024 * 1024;
    private const int MaximumManifestBytes = 16_384;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly AvailableUpdate _update;
    private readonly CancellationTokenSource _cancel = new();
    private string? _installerPath;
    private bool _installStarted;

    public UpdateInstallWindow(AvailableUpdate update)
    {
        InitializeComponent();
        _update = update;
        TitleText.Text = "Downloading update";
        VersionText.Text = $"PitMedic {update.Version}";

        Loaded += async (_, _) => await PrepareUpdateAsync();
        Closed += (_, _) =>
        {
            if (!_installStarted) _cancel.Cancel();
            _cancel.Dispose();
        };
    }

    private async Task PrepareUpdateAsync()
    {
        try
        {
            StatusText.Text = "Checking published fingerprint…";
            DetailText.Text = "PitMedic is confirming that the package and SHA-256 fingerprint belong to the version you selected.";
            Progress.IsIndeterminate = true;

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            var manifest = await LoadManifestAsync(client, _cancel.Token);
            ValidateManifest(manifest);

            var updateDirectory = Path.Combine(AppPaths.Root, "Updates");
            Directory.CreateDirectory(updateDirectory);
            DeleteOldUpdateFiles(updateDirectory);

            var finalPath = Path.Combine(updateDirectory, $"PitMedic-Setup-x64-{SafeVersion(_update.Version)}.exe");
            var tempPath = finalPath + ".download";
            if (File.Exists(tempPath)) File.Delete(tempPath);

            StatusText.Text = "Downloading inside PitMedic…";
            DetailText.Text = "The update is being saved to PitMedic's private update folder. Your browser and Downloads folder are not used.";
            Progress.IsIndeterminate = false;
            Progress.Value = 0;

            var actualHash = await DownloadAndHashAsync(client, _update.DownloadUri, tempPath, _cancel.Token);
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(actualHash),
                    Convert.FromHexString(manifest.Sha256)))
            {
                File.Delete(tempPath);
                throw new InvalidDataException("The downloaded installer did not match the published SHA-256 fingerprint.");
            }

            File.Move(tempPath, finalPath, true);
            _installerPath = finalPath;

            Progress.Value = 100;
            ProgressText.Text = "Verified";
            TitleText.Text = "Ready to install";
            StatusText.Text = "Download verified successfully";
            DetailText.Text = "PitMedic checked the complete installer against the published SHA-256 fingerprint. Click Install now when you are ready.";
            SafetyText.Text = $"Verified SHA-256: {manifest.Sha256.ToLowerInvariant()}";
            InstallButton.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
            if (IsLoaded) Close();
        }
        catch (Exception ex)
        {
            AppLog.Write($"In-app update preparation failed ({ex.GetType().Name}): {ex.Message}");
            Progress.IsIndeterminate = false;
            Progress.Value = 0;
            TitleText.Text = "Update needs attention";
            StatusText.Text = "PitMedic blocked this update";
            DetailText.Text = ex is InvalidDataException
                ? ex.Message
                : "PitMedic could not securely download and verify the update. Nothing was installed.";
            SafetyBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "WarnSoftBrush");
            SafetyBorder.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "WarnBorderBrush");
            SafetyText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "WarnTextBrush");
            SafetyText.Text = "No installer was run. You can close this window and try again later.";
            InstallButton.IsEnabled = false;
            CancelButton.Content = "Close";
        }
    }

    private async Task<UpdateInstallManifest> LoadManifestAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AppInfo.UpdateManifestUrl);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        if (response.Content.Headers.ContentLength is long length && length > MaximumManifestBytes)
            throw new InvalidDataException("The published update manifest was larger than expected.");

        var bytes = await ReadBoundedAsync(await response.Content.ReadAsStreamAsync(cancellationToken), MaximumManifestBytes, cancellationToken);
        return JsonSerializer.Deserialize<UpdateInstallManifest>(bytes, JsonOptions)
            ?? throw new InvalidDataException("The published update manifest could not be read.");
    }

    private void ValidateManifest(UpdateInstallManifest manifest)
    {
        if (manifest.SchemaVersion != 1
            || !string.Equals(manifest.LatestVersion, _update.Version, StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(manifest.DownloadUrl, UriKind.Absolute, out var downloadUri)
            || downloadUri != _update.DownloadUri
            || !IsTrustedPitMedicReleaseUri(downloadUri)
            || string.IsNullOrWhiteSpace(manifest.Sha256)
            || manifest.Sha256.Length != 64
            || !manifest.Sha256.All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("The published update information did not pass PitMedic verification.");
        }
    }

    private async Task<string> DownloadAndHashAsync(HttpClient client, Uri uri, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        if (total is > MaximumInstallerBytes)
            throw new InvalidDataException("The published installer was larger than PitMedic allows.");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long received = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            received += read;
            if (received > MaximumInstallerBytes)
                throw new InvalidDataException("The published installer was larger than PitMedic allows.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            hash.AppendData(buffer, 0, read);

            if (total is > 0)
            {
                var percent = Math.Clamp(received * 100d / total.Value, 0d, 100d);
                Progress.Value = percent;
                ProgressText.Text = $"{percent:0}% · {received / 1024d / 1024d:0.0} MB of {total.Value / 1024d / 1024d:0.0} MB";
            }
            else
            {
                ProgressText.Text = $"{received / 1024d / 1024d:0.0} MB downloaded";
            }
        }

        await output.FlushAsync(cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream input, int maximumBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            if (memory.Length + read > maximumBytes)
                throw new InvalidDataException("The published update manifest was larger than expected.");
            memory.Write(buffer, 0, read);
        }
        return memory.ToArray();
    }

    private static bool IsTrustedPitMedicReleaseUri(Uri uri) =>
        uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.StartsWith("/rholmes426/PitMedic/releases/", StringComparison.Ordinal);

    private static string SafeVersion(string version) =>
        string.Concat(version.Where(ch => char.IsDigit(ch) || ch == '.'));

    private static void DeleteOldUpdateFiles(string directory)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "PitMedic-Setup-x64-*"))
            {
                if (File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-14))
                    File.Delete(file);
            }
        }
        catch
        {
            // Old update cleanup is best effort and never blocks a new update.
        }
    }

    private void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_installerPath is null || !File.Exists(_installerPath)) return;

        try
        {
            _installStarted = true;
            InstallButton.IsEnabled = false;
            CancelButton.IsEnabled = false;
            TitleText.Text = "Starting installer";
            StatusText.Text = "Windows may ask for administrator approval";
            DetailText.Text = "After approval, PitMedic will close, install the verified update silently, and relaunch automatically.";
            Progress.IsIndeterminate = true;
            ProgressText.Text = "";

            AppLog.Write($"Starting verified PitMedic {_update.Version} installer with Windows administrator approval.");
            var installerProcess = Process.Start(new ProcessStartInfo
            {
                FileName = _installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTPITMEDIC=1",
                WorkingDirectory = Path.GetDirectoryName(_installerPath)!,
                UseShellExecute = true,
                Verb = "runas"
            });
            if (installerProcess is null)
                throw new InvalidOperationException("Windows did not return an installer process.");

            AppLog.Write($"Verified PitMedic {_update.Version} installer started; closing the current app instance.");
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            _installStarted = false;
            AppLog.Write($"Could not start verified update installer ({ex.GetType().Name}): {ex.Message}");
            TitleText.Text = "Update not started";
            StatusText.Text = "Windows did not start the installer";
            DetailText.Text = "Nothing was changed. You can try Install now again.";
            Progress.IsIndeterminate = false;
            Progress.Value = 100;
            ProgressText.Text = "Verified installer retained";
            InstallButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_installStarted) _cancel.Cancel();
        Close();
    }

    private sealed record UpdateInstallManifest(
        int SchemaVersion,
        string LatestVersion,
        string Title,
        string DownloadUrl,
        string ReleaseUrl,
        string Sha256);
}
