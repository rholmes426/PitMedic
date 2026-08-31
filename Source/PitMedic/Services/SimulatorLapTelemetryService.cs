using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Xml.Linq;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Reads only simulator-reported valid best-lap data and exact combination identity.
/// Adapters remain opt-in per simulator so PitMedic never labels an inferred combination as fact.
/// </summary>
public sealed class SimulatorLapTelemetryService : IDisposable
{
    private static readonly GameKind[] SupportedGames =
    [
        GameKind.LeMansUltimate,
        GameKind.IRacing,
        GameKind.AssettoCorsaEvo,
        GameKind.RaceRoom,
        GameKind.AssettoCorsaCompetizione,
        GameKind.Automobilista2
    ];

    private readonly Dictionary<GameKind, ILapAdapter> _adapters = new();

    public static bool SupportsBestLap(GameKind game) => SupportedGames.Contains(game);

    public IEnumerable<BestLapRecord> Poll(Func<GameKind, bool> isRunning)
    {
        foreach (var game in SupportedGames)
        {
            if (!isRunning(game)) continue;
            if (!_adapters.TryGetValue(game, out var adapter))
            {
                adapter = Create(game);
                _adapters.Add(game, adapter);
            }

            if (adapter.Poll() is { } lap) yield return lap;
        }
    }

    public BestLapRecord? Stop(GameKind game)
    {
        if (!_adapters.Remove(game, out var adapter)) return null;
        try
        {
            var lap = adapter.Poll();
            // LMU finalizes its official result XML during shutdown. Give that small, local-only
            // write a moment to land so the last completed lap is not lost with the process.
            if (game != GameKind.LeMansUltimate || lap is not null) return lap;
            for (var attempt = 0; attempt < 5; attempt++)
            {
                Thread.Sleep(200);
                if (adapter.Poll() is { } finalLap) return finalLap;
            }
            return null;
        }
        finally { adapter.Dispose(); }
    }

    public void Dispose()
    {
        foreach (var adapter in _adapters.Values) adapter.Dispose();
        _adapters.Clear();
    }

    private static ILapAdapter Create(GameKind game) => game switch
    {
        GameKind.LeMansUltimate => new LmuResultLapAdapter(),
        GameKind.IRacing => new IRacingLapAdapter(),
        GameKind.AssettoCorsaEvo => new AcEvoLapAdapter(),
        GameKind.RaceRoom => new RaceRoomLapAdapter(),
        GameKind.AssettoCorsaCompetizione => new AccLapAdapter(),
        GameKind.Automobilista2 => new Ams2LapAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };

    private interface ILapAdapter : IDisposable
    {
        BestLapRecord? Poll();
    }

    private abstract class MemoryMappedLapAdapter : ILapAdapter
    {
        private DateTimeOffset _nextOpenAttempt;
        private string _lastCombination = string.Empty;
        private double _lastReportedBest;

        protected MemoryMappedFile? OpenMap(string name)
        {
            if (DateTimeOffset.UtcNow < _nextOpenAttempt) return null;
            try { return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read); }
            catch
            {
                _nextOpenAttempt = DateTimeOffset.UtcNow.AddSeconds(10);
                return null;
            }
        }

        protected BestLapRecord? NewLap(GameKind game, string track, string layout, string car, double seconds)
        {
            track = HumanizeIdentifier(track);
            layout = HumanizeIdentifier(layout);
            car = HumanizeIdentifier(car);
            if (!double.IsFinite(seconds) || seconds is < 20 or > 1_800
                || string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(car)) return null;

            var combination = $"{track}|{layout}|{car}";
            if (!string.Equals(combination, _lastCombination, StringComparison.Ordinal))
            {
                _lastCombination = combination;
                _lastReportedBest = 0;
            }
            if (Math.Abs(seconds - _lastReportedBest) < 0.0005d) return null;
            _lastReportedBest = seconds;
            return new BestLapRecord(game, track, layout, car, seconds, DateTimeOffset.Now);
        }

        protected void ResetReportedLap()
        {
            _lastCombination = string.Empty;
            _lastReportedBest = 0;
        }

        public abstract BestLapRecord? Poll();
        public abstract void Dispose();
    }

    private sealed class AccLapAdapter : MemoryMappedLapAdapter
    {
        private MemoryMappedFile? _graphicsMap;
        private MemoryMappedFile? _staticMap;
        private MemoryMappedViewAccessor? _graphics;
        private MemoryMappedViewAccessor? _static;
        private int _lastPacket = int.MinValue;

        public override BestLapRecord? Poll()
        {
            if (!EnsureOpen()) return null;
            try
            {
                var packet = _graphics!.ReadInt32(0);
                if (packet == _lastPacket) return null;
                _lastPacket = packet;
                if (_graphics.ReadInt32(4) != 2 || _graphics.ReadInt32(132) <= 0) return null;
                return NewLap(
                    GameKind.AssettoCorsaCompetizione,
                    ReadUtf16(_static!, 134, 33),
                    ReadUtf16(_static!, 524, 33),
                    ReadUtf16(_static!, 68, 33),
                    _graphics.ReadInt32(148) / 1_000d);
            }
            catch { Reset(); return null; }
        }

        private bool EnsureOpen()
        {
            if (_graphics is not null && _static is not null) return true;
            _graphicsMap = OpenMap("Local\\acpmf_graphics");
            _staticMap = OpenMap("Local\\acpmf_static");
            if (_graphicsMap is null || _staticMap is null) { Reset(); return false; }
            _graphics = _graphicsMap.CreateViewAccessor(0, 160, MemoryMappedFileAccess.Read);
            _static = _staticMap.CreateViewAccessor(0, 600, MemoryMappedFileAccess.Read);
            return true;
        }

        private void Reset()
        {
            _graphics?.Dispose(); _static?.Dispose(); _graphicsMap?.Dispose(); _staticMap?.Dispose();
            _graphics = null; _static = null; _graphicsMap = null; _staticMap = null;
            _lastPacket = int.MinValue; ResetReportedLap();
        }

        public override void Dispose() => Reset();
    }

    private sealed class Ams2LapAdapter : MemoryMappedLapAdapter
    {
        private const long ViewSize = 6724;
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;

        public override BestLapRecord? Poll()
        {
            if (!EnsureOpen()) return null;
            try
            {
                if (_view!.ReadUInt32(0) < 14 || _view.ReadUInt32(8) != 2) return null;
                return NewLap(
                    GameKind.Automobilista2,
                    ReadAscii(_view, 6576, 64),
                    ReadAscii(_view, 6640, 64),
                    ReadAscii(_view, 6444, 64),
                    _view.ReadSingle(6716));
            }
            catch { Reset(); return null; }
        }

        private bool EnsureOpen()
        {
            if (_view is not null) return true;
            _map = OpenMap("$pcars2$");
            if (_map is null) return false;
            _view = _map.CreateViewAccessor(0, ViewSize, MemoryMappedFileAccess.Read);
            return true;
        }

        private void Reset()
        {
            _view?.Dispose(); _map?.Dispose(); _view = null; _map = null; ResetReportedLap();
        }

        public override void Dispose() => Reset();
    }

    private sealed class RaceRoomLapAdapter : MemoryMappedLapAdapter
    {
        private const long ViewSize = 1272;
        private static readonly IReadOnlyDictionary<string, string> Cars = LoadRaceRoomCars();
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;

        public override BestLapRecord? Poll()
        {
            if (!EnsureOpen()) return null;
            try
            {
                if (_view!.ReadInt32(0) != 3) return null;
                var modelId = _view.ReadInt32(1268);
                if (!Cars.TryGetValue(modelId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var car))
                    return null;
                return NewLap(
                    GameKind.RaceRoom,
                    ReadAscii(_view, 600, 64),
                    ReadAscii(_view, 664, 64),
                    car,
                    _view.ReadDouble(1068));
            }
            catch { Reset(); return null; }
        }

        private bool EnsureOpen()
        {
            if (_view is not null) return true;
            _map = OpenMap("$R3E");
            if (_map is null) return false;
            _view = _map.CreateViewAccessor(0, ViewSize, MemoryMappedFileAccess.Read);
            return true;
        }

        private static IReadOnlyDictionary<string, string> LoadRaceRoomCars()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("PitMedic.Assets.RaceRoomCars.json");
                return stream is null
                    ? new Dictionary<string, string>()
                    : JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                        ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }

        private void Reset()
        {
            _view?.Dispose(); _map?.Dispose(); _view = null; _map = null; ResetReportedLap();
        }

        public override void Dispose() => Reset();
    }

    private sealed class AcEvoLapAdapter : MemoryMappedLapAdapter
    {
        // Kunos ACE SharedFileOut v1, pack=4. The public identity fields are
        // deliberately read from the new ACE maps instead of assuming ACC layout compatibility.
        private const long GraphicsViewSize = 3124;
        private const long StaticViewSize = 208;
        private MemoryMappedFile? _graphicsMap;
        private MemoryMappedFile? _staticMap;
        private MemoryMappedViewAccessor? _graphics;
        private MemoryMappedViewAccessor? _static;
        private int _lastPacket = int.MinValue;

        public override BestLapRecord? Poll()
        {
            if (!EnsureOpen()) return null;
            try
            {
                var packet = _graphics!.ReadInt32(0);
                if (packet == _lastPacket) return null;
                _lastPacket = packet;
                if (_graphics.ReadInt32(2384) <= 0) return null;
                return NewLap(
                    GameKind.AssettoCorsaEvo,
                    ReadAscii(_static!, 136, 33),
                    ReadAscii(_static!, 169, 33),
                    ReadAscii(_graphics, 3086, 33),
                    _graphics.ReadInt32(2400) / 1_000d);
            }
            catch { Reset(); return null; }
        }

        private bool EnsureOpen()
        {
            if (_graphics is not null && _static is not null) return true;
            _graphicsMap = OpenMap("Local\\acevo_pmf_graphics");
            _staticMap = OpenMap("Local\\acevo_pmf_static");
            if (_graphicsMap is null || _staticMap is null) { Reset(); return false; }
            _graphics = _graphicsMap.CreateViewAccessor(0, GraphicsViewSize, MemoryMappedFileAccess.Read);
            _static = _staticMap.CreateViewAccessor(0, StaticViewSize, MemoryMappedFileAccess.Read);
            return true;
        }

        private void Reset()
        {
            _graphics?.Dispose(); _static?.Dispose(); _graphicsMap?.Dispose(); _staticMap?.Dispose();
            _graphics = null; _static = null; _graphicsMap = null; _staticMap = null;
            _lastPacket = int.MinValue; ResetReportedLap();
        }

        public override void Dispose() => Reset();
    }

    private sealed class LmuResultLapAdapter : ILapAdapter
    {
        private readonly DateTimeOffset _startedAt = DateTimeOffset.Now.AddSeconds(-10);
        private readonly string? _resultsFolder;
        private readonly string? _playerName;
        private string _lastResult = string.Empty;
        private long _lastLength;
        private double _lastReportedBest;

        public LmuResultLapAdapter()
        {
            var root = SteamLibraryLocator.FindLeMansUltimateRoot();
            _resultsFolder = root is null ? null : Path.Combine(root, "UserData", "Log", "Results");
            _playerName = root is null ? null : ReadPlayerName(Path.Combine(root, "UserData", "player", "Settings.JSON"));
        }

        public BestLapRecord? Poll()
        {
            if (string.IsNullOrWhiteSpace(_resultsFolder) || string.IsNullOrWhiteSpace(_playerName)
                || !Directory.Exists(_resultsFolder)) return null;
            try
            {
                var file = new DirectoryInfo(_resultsFolder).EnumerateFiles("*.xml")
                    .Where(item => item.LastWriteTimeUtc >= _startedAt.UtcDateTime)
                    .OrderByDescending(item => item.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (file is null || (file.FullName == _lastResult && file.Length == _lastLength)) return null;
                _lastResult = file.FullName;
                _lastLength = file.Length;

                using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var doc = XDocument.Load(stream);
                var raceResults = doc.Root?.Element("RaceResults");
                if (raceResults is null) return null;
                var driver = raceResults.Descendants("Driver").FirstOrDefault(item =>
                    string.Equals(item.Element("Name")?.Value.Trim(), _playerName, StringComparison.OrdinalIgnoreCase));
                if (driver is null || !double.TryParse(driver.Element("BestLapTime")?.Value,
                    System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var best)
                    || best is < 20 or > 1_800 || Math.Abs(best - _lastReportedBest) < 0.0005d) return null;

                var track = raceResults.Element("TrackVenue")?.Value.Trim()
                    ?? raceResults.Element("TrackCourse")?.Value.Trim() ?? string.Empty;
                var course = raceResults.Element("TrackCourse")?.Value.Trim() ?? string.Empty;
                var layout = string.Equals(track, course, StringComparison.OrdinalIgnoreCase) ? string.Empty : course;
                var car = driver.Element("CarType")?.Value.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(track) || string.IsNullOrWhiteSpace(car)) return null;
                _lastReportedBest = best;
                return new BestLapRecord(GameKind.LeMansUltimate, track, layout, car, best, DateTimeOffset.Now);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (System.Xml.XmlException) { return null; }
        }

        private static string? ReadPlayerName(string path)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
                return doc.RootElement.TryGetProperty("Player Name", out var name) ? name.GetString()?.Trim() : null;
            }
            catch { return null; }
        }

        public void Dispose() { }
    }

    private static string ReadAscii(MemoryMappedViewAccessor view, long offset, int count)
    {
        var bytes = new byte[count];
        view.ReadArray(offset, bytes, 0, bytes.Length);
        var terminator = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, terminator < 0 ? bytes.Length : terminator).Trim();
    }

    private static string ReadUtf16(MemoryMappedViewAccessor view, long offset, int characterCount)
    {
        var bytes = new byte[characterCount * 2];
        view.ReadArray(offset, bytes, 0, bytes.Length);
        var value = Encoding.Unicode.GetString(bytes);
        var terminator = value.IndexOf('\0');
        return (terminator < 0 ? value : value[..terminator]).Trim();
    }

    internal static string HumanizeIdentifier(string value)
    {
        value = value.Trim().Replace('_', ' ').Replace('-', ' ');
        if (value.StartsWith("ks ", StringComparison.OrdinalIgnoreCase)) value = value[3..];
        if (value.Length == 0) return value;
        var acronyms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "acc", "amg", "bmw", "evo", "f1", "gt2", "gt3", "gt4", "gte", "lmp2", "lmp3", "lmh", "lmgt3", "rcf", "v8", "v10", "v12"
        };
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(word =>
            acronyms.Contains(word) ? word.ToUpperInvariant()
            : char.ToUpperInvariant(word[0]) + word[1..]));
    }

    private sealed class IRacingLapAdapter : ILapAdapter
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
