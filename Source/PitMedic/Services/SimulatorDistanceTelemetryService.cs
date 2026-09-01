using System.IO.MemoryMappedFiles;
using System.Text;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Reads simulator-reported shared-memory telemetry and converts it to incremental distance.
/// Adapters are opened only while their simulator process is running and are disposed as soon
/// as that process stops. Nothing here estimates distance from process runtime.
/// </summary>
public sealed class SimulatorDistanceTelemetryService : IDisposable
{
    private static readonly GameKind[] SupportedGames =
    [
        GameKind.IRacing,
        GameKind.AssettoCorsaCompetizione,
        GameKind.RaceRoom,
        GameKind.Automobilista2
    ];

    private readonly Dictionary<GameKind, DistanceAdapter> _adapters = new();
    private readonly Dictionary<GameKind, DistanceTelemetryStatus> _lastStatuses = new();

    public event Action<DistanceTelemetryStatus>? StatusChanged;

    public static bool SupportsMileage(GameKind game) => SupportedGames.Contains(game);

    public IEnumerable<(GameKind Game, double Miles)> Poll(Func<GameKind, bool> isRunning)
    {
        foreach (var game in SupportedGames)
        {
            if (!isRunning(game))
            {
                var finalMiles = Stop(game);
                if (double.IsFinite(finalMiles) && finalMiles > 0)
                    yield return (game, finalMiles);
                continue;
            }

            if (!_adapters.TryGetValue(game, out var adapter))
            {
                adapter = Create(game);
                _adapters.Add(game, adapter);
            }

            var miles = adapter.PollMiles();
            PublishStatus(game, adapter);
            if (double.IsFinite(miles) && miles > 0)
                yield return (game, miles);
        }
    }

    public double Stop(GameKind game)
    {
        if (!_adapters.Remove(game, out var adapter)) return 0;
        try { return adapter.FlushMiles(); }
        finally
        {
            adapter.Dispose();
            _lastStatuses.Remove(game);
        }
    }

    public void Dispose()
    {
        foreach (var adapter in _adapters.Values) adapter.Dispose();
        _adapters.Clear();
        _lastStatuses.Clear();
    }

    private void PublishStatus(GameKind game, DistanceAdapter adapter)
    {
        if (game != GameKind.Automobilista2) return;
        var status = new DistanceTelemetryStatus(game, adapter.IsTelemetryAvailable, adapter.TelemetryMessage);
        if (_lastStatuses.TryGetValue(game, out var previous) && previous == status) return;
        _lastStatuses[game] = status;
        StatusChanged?.Invoke(status);
    }

    private static DistanceAdapter Create(GameKind game) => game switch
    {
        GameKind.IRacing => new IRacingDistanceAdapter(),
        GameKind.AssettoCorsaCompetizione => new AccDistanceAdapter(),
        GameKind.RaceRoom => new RaceRoomDistanceAdapter(),
        GameKind.Automobilista2 => new Ams2DistanceAdapter(),
        _ => throw new ArgumentOutOfRangeException(nameof(game))
    };

    private abstract class DistanceAdapter : IDisposable
    {
        private DateTimeOffset _nextOpenAttempt;

        protected MemoryMappedFile? OpenMap(string name)
        {
            if (DateTimeOffset.UtcNow < _nextOpenAttempt) return null;
            try
            {
                return MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
            }
            catch
            {
                // Shared memory may not be enabled yet even though the process is present.
                // Retry slowly so an unavailable integration remains effectively idle.
                _nextOpenAttempt = DateTimeOffset.UtcNow.AddSeconds(10);
                return null;
            }
        }

        public abstract double PollMiles();
        public virtual double FlushMiles() => 0;
        public abstract void Dispose();
        public virtual bool IsTelemetryAvailable => true;
        public virtual string TelemetryMessage => string.Empty;

        protected static double MetresToMiles(double metres) => metres / 1609.344d;
        protected static bool PlausibleSpeed(double metresPerSecond) =>
            double.IsFinite(metresPerSecond) && metresPerSecond is >= 0 and <= 160;
    }

    private sealed class IRacingDistanceAdapter : DistanceAdapter
    {
        private const int HeaderSize = 48;
        private const int VarHeaderSize = 144;
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private Dictionary<string, Variable>? _variables;
        private int _lastTick = int.MinValue;
        private double? _lastSessionTime;
        private double? _lastSpeed;

        public override double PollMiles()
        {
            if (!EnsureOpen()) return 0;
            try
            {
                var bufferCount = _view!.ReadInt32(32);
                if (bufferCount is < 1 or > 4) return 0;

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

                if (newestTick == _lastTick || dataOffset <= 0) return 0;
                _lastTick = newestTick;

                if (!TryReadDouble("SessionTime", dataOffset, out var sessionTime)
                    || !TryReadNumber("Speed", dataOffset, out var speed))
                    return 0;

                var onTrack = !TryReadBool("IsOnTrack", dataOffset, out var isOnTrack) || isOnTrack;
                var replay = TryReadBool("IsReplayPlaying", dataOffset, out var isReplay) && isReplay;
                var metres = Integrate(sessionTime, speed, onTrack && !replay);
                return MetresToMiles(metres);
            }
            catch
            {
                Reset();
                return 0;
            }
        }

        private bool EnsureOpen()
        {
            if (_view is not null && _variables is not null) return true;
            _map = OpenMap("Local\\IRSDKMemMapFileName");
            if (_map is null) return false;
            try
            {
                _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                var variableCount = _view.ReadInt32(24);
                var variableOffset = _view.ReadInt32(28);
                if (variableCount is < 1 or > 4096 || variableOffset < HeaderSize)
                    throw new InvalidDataException("Unexpected iRacing telemetry header.");

                var variables = new Dictionary<string, Variable>(StringComparer.Ordinal);
                for (var i = 0; i < variableCount; i++)
                {
                    var offset = variableOffset + i * VarHeaderSize;
                    var type = _view.ReadInt32(offset);
                    var dataOffset = _view.ReadInt32(offset + 4);
                    var name = ReadAscii(_view, offset + 16, 32);
                    if (!string.IsNullOrWhiteSpace(name)) variables[name] = new Variable(type, dataOffset);
                }

                _variables = variables;
                return true;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        private double Integrate(double sessionTime, double speed, bool onTrack)
        {
            var metres = 0d;
            if (_lastSessionTime is double previousTime && _lastSpeed is double previousSpeed)
            {
                var elapsed = sessionTime - previousTime;
                if (onTrack && elapsed is > 0 and <= 5 && PlausibleSpeed(speed) && PlausibleSpeed(previousSpeed))
                    metres = (speed + previousSpeed) * 0.5d * elapsed;
            }

            _lastSessionTime = sessionTime;
            _lastSpeed = speed;
            return metres;
        }

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

        private bool TryReadDouble(string name, int baseOffset, out double value) =>
            TryReadNumber(name, baseOffset, out value);

        private bool TryReadBool(string name, int baseOffset, out bool value)
        {
            value = false;
            if (_variables is null || !_variables.TryGetValue(name, out var variable)) return false;
            value = _view!.ReadByte(baseOffset + variable.Offset) != 0;
            return true;
        }

        private static string ReadAscii(MemoryMappedViewAccessor view, long offset, int count)
        {
            var bytes = new byte[count];
            view.ReadArray(offset, bytes, 0, bytes.Length);
            var terminator = Array.IndexOf(bytes, (byte)0);
            return Encoding.ASCII.GetString(bytes, 0, terminator < 0 ? bytes.Length : terminator);
        }

        private void Reset()
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
            _variables = null;
            _lastTick = int.MinValue;
            _lastSessionTime = null;
            _lastSpeed = null;
        }

        public override void Dispose() => Reset();
        private readonly record struct Variable(int Type, int Offset);
    }

    private sealed class AccDistanceAdapter : DistanceAdapter
    {
        private MemoryMappedFile? _physicsMap;
        private MemoryMappedFile? _graphicsMap;
        private MemoryMappedViewAccessor? _physics;
        private MemoryMappedViewAccessor? _graphics;
        private int _lastPacket = int.MinValue;
        private long? _lastTimestamp;
        private double? _lastSpeed;

        public override double PollMiles()
        {
            if (!EnsureOpen()) return 0;
            try
            {
                var packet = _physics!.ReadInt32(0);
                if (packet == _lastPacket) return 0;
                _lastPacket = packet;
                var speed = _physics.ReadSingle(28) / 3.6d;
                var isLive = _graphics!.ReadInt32(4) == 2;
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                var metres = Integrate(now, speed, isLive);
                return MetresToMiles(metres);
            }
            catch
            {
                Reset();
                return 0;
            }
        }

        private bool EnsureOpen()
        {
            if (_physics is not null && _graphics is not null) return true;
            _physicsMap = OpenMap("Local\\acpmf_physics");
            _graphicsMap = OpenMap("Local\\acpmf_graphics");
            if (_physicsMap is null || _graphicsMap is null)
            {
                Reset();
                return false;
            }
            _physics = _physicsMap.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read);
            _graphics = _graphicsMap.CreateViewAccessor(0, 16, MemoryMappedFileAccess.Read);
            return true;
        }

        private double Integrate(long timestamp, double speed, bool isLive)
        {
            var metres = 0d;
            if (_lastTimestamp is long previousTimestamp && _lastSpeed is double previousSpeed)
            {
                var elapsed = (timestamp - previousTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency;
                if (isLive && elapsed is > 0 and <= 5 && PlausibleSpeed(speed) && PlausibleSpeed(previousSpeed))
                    metres = (speed + previousSpeed) * 0.5d * elapsed;
            }
            _lastTimestamp = timestamp;
            _lastSpeed = speed;
            return metres;
        }

        private void Reset()
        {
            _physics?.Dispose();
            _graphics?.Dispose();
            _physicsMap?.Dispose();
            _graphicsMap?.Dispose();
            _physics = null;
            _graphics = null;
            _physicsMap = null;
            _graphicsMap = null;
            _lastPacket = int.MinValue;
            _lastTimestamp = null;
            _lastSpeed = null;
        }

        public override void Dispose() => Reset();
    }

    private sealed class RaceRoomDistanceAdapter : DistanceAdapter
    {
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private double? _lastSimulationTime;
        private double? _lastSpeed;

        public override double PollMiles()
        {
            if (!EnsureOpen()) return 0;
            try
            {
                if (_view!.ReadInt32(0) != 3) return 0;
                var driving = _view.ReadInt32(20) == 0
                    && _view.ReadInt32(24) == 0
                    && _view.ReadInt32(28) == 0
                    && _view.ReadInt32(36) == 0;
                var simulationTime = _view.ReadDouble(48);
                var x = _view.ReadDouble(80);
                var y = _view.ReadDouble(88);
                var z = _view.ReadDouble(96);
                var speed = Math.Sqrt(x * x + y * y + z * z);
                var metres = Integrate(simulationTime, speed, driving);
                return MetresToMiles(metres);
            }
            catch
            {
                Reset();
                return 0;
            }
        }

        private bool EnsureOpen()
        {
            if (_view is not null) return true;
            _map = OpenMap("$R3E");
            if (_map is null) return false;
            _view = _map.CreateViewAccessor(0, 128, MemoryMappedFileAccess.Read);
            return true;
        }

        private double Integrate(double simulationTime, double speed, bool driving)
        {
            var metres = 0d;
            if (_lastSimulationTime is double previousTime && _lastSpeed is double previousSpeed)
            {
                var elapsed = simulationTime - previousTime;
                if (driving && elapsed is > 0 and <= 5 && PlausibleSpeed(speed) && PlausibleSpeed(previousSpeed))
                    metres = (speed + previousSpeed) * 0.5d * elapsed;
            }
            _lastSimulationTime = simulationTime;
            _lastSpeed = speed;
            return metres;
        }

        private void Reset()
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
            _lastSimulationTime = null;
            _lastSpeed = null;
        }

        public override void Dispose() => Reset();
    }

    private sealed class Ams2DistanceAdapter : DistanceAdapter
    {
        // Offsets are from Reiza's v14 $pcars2$ shared-memory structure (Pack=4/8).
        private const long SpeedMetresPerSecondOffset = 6848;
        private const long OdometerKmOffset = 6884;
        private const double OdometerStallFallbackSeconds = 4;
        private const string SharedMemoryGuidance =
            "AMS2 shared memory is unavailable. In AMS2, set Shared Memory to Project CARS 2, then restart the session.";
        private MemoryMappedFile? _map;
        private MemoryMappedViewAccessor? _view;
        private double? _lastOdometerKm;
        private long? _lastTimestamp;
        private double? _lastSpeed;
        private double _pendingSpeedMetres;
        private double _odometerStallSeconds;
        private bool _usingSpeedFallback;
        private bool _lastDriving;

        public override bool IsTelemetryAvailable => _view is not null;
        public override string TelemetryMessage => IsTelemetryAvailable ? string.Empty : SharedMemoryGuidance;

        public override double PollMiles()
        {
            if (!EnsureOpen()) return 0;
            try
            {
                var timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                var version = _view!.ReadUInt32(0);
                var gameState = _view.ReadUInt32(8);
                var speed = _view.ReadSingle(SpeedMetresPerSecondOffset);
                var odometerKm = _view.ReadSingle(OdometerKmOffset);
                var driving = version >= 8 && gameState == 2 && PlausibleSpeed(speed);
                var (speedMetres, elapsedSeconds) = IntegrateSpeed(timestamp, speed, driving);
                _lastDriving = driving;

                if (!driving)
                {
                    _lastOdometerKm = odometerKm >= 0 ? odometerKm : null;
                    _pendingSpeedMetres = 0;
                    _odometerStallSeconds = 0;
                    _usingSpeedFallback = false;
                    return 0;
                }

                var validOdometer = double.IsFinite(odometerKm) && odometerKm >= 0;
                if (_lastOdometerKm is null)
                {
                    _lastOdometerKm = validOdometer ? odometerKm : null;
                    if (validOdometer) return 0;
                }

                var kilometres = validOdometer && _lastOdometerKm is double previous
                    ? odometerKm - previous
                    : double.NaN;
                if (validOdometer) _lastOdometerKm = odometerKm;

                if (kilometres is > 0 and < 20)
                {
                    // Once speed integration has been emitted, the next odometer movement is only
                    // a new baseline. Emitting both would count the same stretch twice.
                    if (_usingSpeedFallback)
                    {
                        _usingSpeedFallback = false;
                        _pendingSpeedMetres = 0;
                        _odometerStallSeconds = 0;
                        return 0;
                    }

                    _pendingSpeedMetres = 0;
                    _odometerStallSeconds = 0;
                    return kilometres * 0.621371192237334d;
                }

                _pendingSpeedMetres += speedMetres;
                _odometerStallSeconds += elapsedSeconds;
                if (!validOdometer || kilometres < 0 || kilometres >= 20
                    || _usingSpeedFallback || _odometerStallSeconds >= OdometerStallFallbackSeconds)
                {
                    _usingSpeedFallback = true;
                    var fallbackMetres = _pendingSpeedMetres;
                    _pendingSpeedMetres = 0;
                    return MetresToMiles(fallbackMetres);
                }

                return 0;
            }
            catch
            {
                Reset();
                return 0;
            }
        }

        public override double FlushMiles()
        {
            var finalMetres = _pendingSpeedMetres;
            _pendingSpeedMetres = 0;
            if (_lastDriving && _lastTimestamp is long previousTimestamp
                && _lastSpeed is double previousSpeed && PlausibleSpeed(previousSpeed))
            {
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(
                    previousTimestamp, System.Diagnostics.Stopwatch.GetTimestamp()).TotalSeconds;
                if (elapsed is > 0 and <= 5)
                    finalMetres += previousSpeed * elapsed;
            }

            return MetresToMiles(finalMetres);
        }

        private bool EnsureOpen()
        {
            if (_view is not null) return true;
            _map = OpenMap("$pcars2$");
            if (_map is null) return false;
            _view = _map.CreateViewAccessor(0, OdometerKmOffset + sizeof(float), MemoryMappedFileAccess.Read);
            return true;
        }

        private (double Metres, double ElapsedSeconds) IntegrateSpeed(long timestamp, double speed, bool driving)
        {
            var metres = 0d;
            var elapsed = 0d;
            if (_lastTimestamp is long previousTimestamp && _lastSpeed is double previousSpeed)
            {
                elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
                if (driving && elapsed is > 0 and <= 5 && PlausibleSpeed(previousSpeed))
                    metres = (speed + previousSpeed) * 0.5d * elapsed;
            }

            _lastTimestamp = timestamp;
            _lastSpeed = speed;
            return (metres, elapsed is > 0 and <= 5 ? elapsed : 0);
        }

        private void Reset()
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
            _lastOdometerKm = null;
            _lastTimestamp = null;
            _lastSpeed = null;
            _pendingSpeedMetres = 0;
            _odometerStallSeconds = 0;
            _usingSpeedFallback = false;
            _lastDriving = false;
        }

        public override void Dispose() => Reset();
    }
}
