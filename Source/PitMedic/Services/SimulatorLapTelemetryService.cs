using System.IO.MemoryMappedFiles;
using System.Text;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Reads only simulator-reported valid best-lap data and exact combination identity.
/// Adapters remain opt-in per simulator so PitMedic never labels an inferred combination as fact.
/// </summary>
public sealed class SimulatorLapTelemetryService : IDisposable
{
    private readonly IRacingLapAdapter _iRacing = new();

    public IEnumerable<BestLapRecord> Poll(Func<GameKind, bool> isRunning)
    {
        if (!isRunning(GameKind.IRacing))
        {
            _iRacing.Stop();
            yield break;
        }

        if (_iRacing.Poll() is { } lap)
            yield return lap;
    }

    public void Stop(GameKind game)
    {
        if (game == GameKind.IRacing) _iRacing.Stop();
    }

    public void Dispose() => _iRacing.Dispose();

    private sealed class IRacingLapAdapter : IDisposable
    {
        private const int HeaderSize = 48;
        private const int VariableHeaderSize = 144;
        private const int MaximumSessionInfoBytes = 1_048_576;
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private Dictionary<string, Variable>? _variables;
        private DateTimeOffset _nextOpenAttempt;
        private int _lastTick = int.MinValue;
        private int _lastSessionInfoUpdate = int.MinValue;
        private string _track = string.Empty;
        private string _layout = string.Empty;
        private string _car = string.Empty;
        private double _lastReportedBest;

        public BestLapRecord? Poll()
        {
            if (!EnsureOpen()) return null;
            try
            {
                RefreshSessionIdentity();
                var bufferCount = _view!.ReadInt32(32);
                if (bufferCount is < 1 or > 4) return null;

                var newestTick = int.MinValue;
                var dataOffset = 0;
                for (var i = 0; i < bufferCount; i++)
                {
                    var header = HeaderSize + i * 16;
                    var tick = _view.ReadInt32(header);
                    if (tick <= newestTick) continue;
                    newestTick = tick;
                    dataOffset = _view.ReadInt32(header + 4);
                }

                if (newestTick == _lastTick || dataOffset <= 0) return null;
                _lastTick = newestTick;
                if (!TryReadNumber("LapBestLapTime", dataOffset, out var best)
                    || best is < 20 or > 1_800
                    || string.IsNullOrWhiteSpace(_track)
                    || string.IsNullOrWhiteSpace(_car)
                    || Math.Abs(best - _lastReportedBest) < 0.0005d)
                    return null;

                _lastReportedBest = best;
                return new BestLapRecord(
                    GameKind.IRacing,
                    _track,
                    _layout,
                    _car,
                    best,
                    DateTimeOffset.Now);
            }
            catch
            {
                Reset();
                return null;
            }
        }

        private bool EnsureOpen()
        {
            if (_view is not null && _variables is not null) return true;
            if (DateTimeOffset.UtcNow < _nextOpenAttempt) return false;
            try
            {
                _map = MemoryMappedFile.OpenExisting("Local\\IRSDKMemMapFileName", MemoryMappedFileRights.Read);
                _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                var variableCount = _view.ReadInt32(24);
                var variableOffset = _view.ReadInt32(28);
                if (variableCount is < 1 or > 4_096 || variableOffset < HeaderSize)
                    throw new InvalidDataException("Unexpected iRacing telemetry header.");

                var variables = new Dictionary<string, Variable>(StringComparer.Ordinal);
                for (var i = 0; i < variableCount; i++)
                {
                    var offset = variableOffset + i * VariableHeaderSize;
                    var type = _view.ReadInt32(offset);
                    var dataOffset = _view.ReadInt32(offset + 4);
                    var name = ReadAscii(offset + 16, 32);
                    if (!string.IsNullOrWhiteSpace(name)) variables[name] = new Variable(type, dataOffset);
                }

                _variables = variables;
                RefreshSessionIdentity(force: true);
                return true;
            }
            catch
            {
                Reset();
                _nextOpenAttempt = DateTimeOffset.UtcNow.AddSeconds(10);
                return false;
            }
        }

        private void RefreshSessionIdentity(bool force = false)
        {
            var update = _view!.ReadInt32(12);
            if (!force && update == _lastSessionInfoUpdate) return;
            _lastSessionInfoUpdate = update;
            var length = _view.ReadInt32(16);
            var offset = _view.ReadInt32(20);
            if (length is <= 0 or > MaximumSessionInfoBytes || offset < HeaderSize) return;

            var bytes = new byte[length];
            _view.ReadArray(offset, bytes, 0, bytes.Length);
            var sessionInfo = Encoding.UTF8.GetString(bytes).TrimEnd('\0');
            _track = ReadYamlValue(sessionInfo, "TrackDisplayName")
                ?? ReadYamlValue(sessionInfo, "TrackName")
                ?? string.Empty;
            _layout = ReadYamlValue(sessionInfo, "TrackConfigName") ?? string.Empty;

            var driverCarIndex = ReadYamlInt(sessionInfo, "DriverCarIdx");
            _car = driverCarIndex.HasValue
                ? ReadDriverValue(sessionInfo, driverCarIndex.Value, "CarScreenName")
                    ?? ReadDriverValue(sessionInfo, driverCarIndex.Value, "CarPath")
                    ?? string.Empty
                : string.Empty;
        }

        internal static string? ReadYamlValue(string yaml, string key)
        {
            var prefix = key + ":";
            foreach (var rawLine in yaml.Split('\n'))
            {
                var line = rawLine.Trim();
                if (!line.StartsWith(prefix, StringComparison.Ordinal)) continue;
                return CleanYamlValue(line[prefix.Length..]);
            }
            return null;
        }

        private static int? ReadYamlInt(string yaml, string key) =>
            int.TryParse(ReadYamlValue(yaml, key), out var value) ? value : null;

        internal static string? ReadDriverValue(string yaml, int carIndex, string key)
        {
            var lines = yaml.Split('\n');
            var inDriver = false;
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.StartsWith("- CarIdx:", StringComparison.Ordinal))
                {
                    inDriver = int.TryParse(CleanYamlValue(line["- CarIdx:".Length..]), out var current)
                        && current == carIndex;
                    continue;
                }

                if (!inDriver || !line.StartsWith(key + ":", StringComparison.Ordinal)) continue;
                return CleanYamlValue(line[(key.Length + 1)..]);
            }
            return null;
        }

        private static string CleanYamlValue(string value) =>
            value.Trim().Trim('"', '\'').Replace("\\\"", "\"");

        private bool TryReadNumber(string name, int baseOffset, out double value)
        {
            value = 0;
            if (_variables is null || !_variables.TryGetValue(name, out var variable)) return false;
            var offset = baseOffset + variable.Offset;
            value = variable.Type switch
            {
                2 or 3 => _view!.ReadInt32(offset),
                4 => _view!.ReadSingle(offset),
                5 => _view!.ReadDouble(offset),
                _ => double.NaN
            };
            return double.IsFinite(value);
        }

        private string ReadAscii(long offset, int count)
        {
            var bytes = new byte[count];
            _view!.ReadArray(offset, bytes, 0, bytes.Length);
            var terminator = Array.IndexOf(bytes, (byte)0);
            return Encoding.ASCII.GetString(bytes, 0, terminator < 0 ? bytes.Length : terminator);
        }

        public void Stop() => Reset();

        private void Reset()
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
            _variables = null;
            _lastTick = int.MinValue;
            _lastSessionInfoUpdate = int.MinValue;
            _track = string.Empty;
            _layout = string.Empty;
            _car = string.Empty;
            _lastReportedBest = 0;
        }

        public void Dispose() => Reset();
        private readonly record struct Variable(int Type, int Offset);
    }
}
