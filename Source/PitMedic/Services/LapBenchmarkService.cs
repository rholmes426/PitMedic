using System.Net.Http;
using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class LapBenchmarkService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly Uri? _endpoint;
    private readonly Dictionary<string, LapBenchmark> _cache = new(StringComparer.Ordinal);

    public LapBenchmarkService(HttpClient? httpClient = null, Uri? endpoint = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        _endpoint = endpoint ?? ParseEndpoint();
    }

    public async Task<LapBenchmark> FindAsync(BestLapRecord lap, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(lap.CombinationKey, out var cached)) return cached;
        if (_endpoint is null) return Unavailable();

        try
        {
            var builder = new UriBuilder(_endpoint);
            builder.Query = string.Join('&', new Dictionary<string, string>
            {
                ["sim"] = GameDefinition.Supported.First(game => game.Kind == lap.Game).DisplayName,
                ["track"] = lap.Track,
                ["layout"] = lap.Layout,
                ["car"] = lap.Car
            }.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

            using var response = await _httpClient.GetAsync(builder.Uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return Unavailable();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var benchmark = await JsonSerializer.DeserializeAsync<LapBenchmark>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false) ?? Unavailable();
            _cache[lap.CombinationKey] = benchmark;
            return benchmark;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Lap benchmark lookup failed ({ex.GetType().Name}).");
            return Unavailable();
        }
    }

    private static LapBenchmark Unavailable() =>
        new(false, null, string.Empty, string.Empty, null, string.Empty, DateTimeOffset.UtcNow);

    private static Uri? ParseEndpoint() =>
        Uri.TryCreate(AppInfo.LapBenchmarkEndpoint, UriKind.Absolute, out var uri)
        && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            ? uri
            : null;

    public void Dispose() => _httpClient.Dispose();
}
