using System.Text.Json;

namespace PitMedic.Services;

internal sealed class ProtectedSensorClient : IDisposable
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReadInterval = TimeSpan.FromMilliseconds(500);
    private readonly object _gate = new();
    private readonly string _sensorPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PitMedic",
        "sensor.json");
    private SensorMessage? _latest;
    private DateTimeOffset _lastReadAttempt;
    private string _status = "Waiting for the installed sensor service";

    public void EnsureStarted() => Refresh();

    public bool TryGetRecent(out SensorMessage message)
    {
        Refresh();
        lock (_gate)
        {
            if (_latest is not null
                && string.IsNullOrWhiteSpace(_latest.Error)
                && DateTimeOffset.UtcNow - _latest.Timestamp <= FreshnessWindow)
            {
                message = _latest;
                return true;
            }
        }

        message = new SensorMessage();
        return false;
    }

    public string GetDiagnosticStatus()
    {
        Refresh(force: true);
        lock (_gate) return _status;
    }

    private void Refresh(bool force = false)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (!force && now - _lastReadAttempt < ReadInterval) return;
            _lastReadAttempt = now;

            if (!File.Exists(_sensorPath))
            {
                _latest = null;
                _status = "Not active; install PitMedic to enable protected CPU sensors";
                return;
            }

            try
            {
                using var stream = new FileStream(
                    _sensorPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                var message = JsonSerializer.Deserialize<SensorMessage>(stream);
                if (message is null)
                {
                    _latest = null;
                    _status = "Sensor service returned no data";
                    return;
                }

                _latest = message;
                if (now - message.Timestamp > FreshnessWindow)
                    _status = "Installed sensor service is not currently reporting";
                else if (!string.IsNullOrWhiteSpace(message.Error))
                    _status = $"Installed sensor service error: {message.Error}";
                else if (!message.CpuTempC.HasValue)
                    _status = "Installed sensor service is active; CPU temperature was not exposed by this system";
                else
                    _status = "Installed read-only sensor service active";
            }
            catch (IOException)
            {
                _status = "Waiting for the installed sensor service";
            }
            catch (UnauthorizedAccessException)
            {
                _latest = null;
                _status = "Installed sensor data could not be read";
            }
            catch (JsonException)
            {
                _status = "Waiting for a complete sensor sample";
            }
        }
    }

    public void Dispose()
    {
    }

    internal sealed record SensorMessage
    {
        public DateTimeOffset Timestamp { get; init; }
        public float? CpuTempC { get; init; }
        public float? CpuLoadPct { get; init; }
        public float? CpuClockMhz { get; init; }
        public float? CpuPowerW { get; init; }
        public string? Error { get; init; }
    }
}
