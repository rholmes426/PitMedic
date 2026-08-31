using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class UsageStatsService
{
    private sealed class PersistedGameStats
    {
        public double MonitoredSeconds { get; set; }
        public int CleanStreak { get; set; }
        public double MilesMonitored { get; set; }
        public bool MileageAvailable { get; set; }
        public BestLapRecord? LastSessionBestLap { get; set; }
        public string? LastBestLapKey { get; set; }
        public Dictionary<string, BestLapRecord> BestLaps { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PersistedStats
    {
        public DateTimeOffset MonitoringSince { get; set; } = DateTimeOffset.Now;
        public long SessionsMonitored { get; set; }
        public int AutomaticRepairsResolved { get; set; }
        public int EstimatedMinutesSaved { get; set; }
        public Dictionary<string, PersistedGameStats> Games { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly Dictionary<GameKind, DateTimeOffset> _activeSessions = new();
    private readonly Dictionary<GameKind, BestLapRecord> _activeSessionBestLaps = new();
    private PersistedStats _stats;

    public UsageStatsService()
    {
        _stats = Load();
    }

    public void RecordSessionStarted(GameKind game)
    {
        lock (_gate)
        {
            if (_activeSessions.ContainsKey(game)) return;
            _activeSessions[game] = DateTimeOffset.Now;
            _activeSessionBestLaps.Remove(game);
            _stats.SessionsMonitored++;
            var gameStats = GetGameStats(game);
            gameStats.LastSessionBestLap = null;
            gameStats.LastBestLapKey = null;
            gameStats.BestLaps.Clear();
            Save();
        }
    }

    public void RecordSessionEnded(GameKind game, DateTimeOffset ended, bool clean)
    {
        lock (_gate)
        {
            if (!_activeSessions.Remove(game, out var started)) return;
            var gameStats = GetGameStats(game);
            gameStats.MonitoredSeconds += Math.Max(0, (ended - started).TotalSeconds);
            gameStats.CleanStreak = clean ? gameStats.CleanStreak + 1 : 0;
            if (_activeSessionBestLaps.Remove(game, out var sessionBest))
                gameStats.LastSessionBestLap = sessionBest;
            Save();
        }
    }

    public void RecordFinding(GameKind game)
    {
        lock (_gate)
        {
            GetGameStats(game).CleanStreak = 0;
            Save();
        }
    }

    // Simulator-specific telemetry adapters can call this only after they have measured actual
    // on-track distance. Until then, the UI deliberately marks mileage unavailable instead of
    // presenting an estimated value as fact.
    public void RecordMiles(GameKind game, double miles, bool persist = true)
    {
        if (!double.IsFinite(miles) || miles < 0) return;
        lock (_gate)
        {
            var gameStats = GetGameStats(game);
            gameStats.MileageAvailable = true;
            gameStats.MilesMonitored += miles;
            if (persist) Save();
        }
    }

    public void RecordBestLap(BestLapRecord lap, bool persist = true)
    {
        if (!double.IsFinite(lap.LapSeconds) || lap.LapSeconds is < 20 or > 1_800
            || string.IsNullOrWhiteSpace(lap.Track) || string.IsNullOrWhiteSpace(lap.Car))
            return;

        lock (_gate)
        {
            var gameStats = GetGameStats(lap.Game);
            if (!_activeSessions.ContainsKey(lap.Game)) return;
            if (!_activeSessionBestLaps.TryGetValue(lap.Game, out var existing)
                || !string.Equals(lap.CombinationKey, existing.CombinationKey, StringComparison.Ordinal)
                || lap.LapSeconds < existing.LapSeconds)
            {
                _activeSessionBestLaps[lap.Game] = lap;
                gameStats.LastSessionBestLap = lap;
                if (persist) Save();
            }
        }
    }

    public void Flush()
    {
        lock (_gate) Save();
    }

    public SimulatorActivitySnapshot SimulatorSnapshot(GameKind game)
    {
        lock (_gate)
        {
            var gameStats = GetGameStats(game);
            var seconds = gameStats.MonitoredSeconds;
            if (_activeSessions.TryGetValue(game, out var started))
                seconds += Math.Max(0, (DateTimeOffset.Now - started).TotalSeconds);
            gameStats.BestLaps ??= new Dictionary<string, BestLapRecord>(StringComparer.Ordinal);
            var legacyBestLap = gameStats.LastBestLapKey is { Length: > 0 } key
                && gameStats.BestLaps.TryGetValue(key, out var lap)
                    ? lap
                    : gameStats.BestLaps.Values.OrderByDescending(item => item.RecordedAt).FirstOrDefault();
            var bestLap = _activeSessionBestLaps.TryGetValue(game, out var activeBestLap)
                ? activeBestLap
                : gameStats.LastSessionBestLap ?? legacyBestLap;
            return new SimulatorActivitySnapshot(
                game,
                TimeSpan.FromSeconds(seconds),
                gameStats.CleanStreak,
                gameStats.MileageAvailable ? gameStats.MilesMonitored : null,
                bestLap);
        }
    }

    public void StopMonitoring()
    {
        lock (_gate)
        {
            var stopped = DateTimeOffset.Now;
            foreach (var (game, started) in _activeSessions)
                GetGameStats(game).MonitoredSeconds += Math.Max(0, (stopped - started).TotalSeconds);
            _activeSessions.Clear();
            _activeSessionBestLaps.Clear();
            Save();
        }
    }

    public void RecordRepairCompleted(RepairPlan plan, bool automatic)
    {
        lock (_gate)
        {
            if (automatic) _stats.AutomaticRepairsResolved++;
            // Conservative value metric: a successful automated repair is credited with at least
            // ten minutes of manual troubleshooting avoided, or the repair's own estimate if longer.
            _stats.EstimatedMinutesSaved += Math.Max(10, plan.EstimatedMinutes);
            Save();
        }
    }

    public CapabilitiesSnapshot Snapshot()
    {
        PersistedStats copy;
        lock (_gate)
        {
            copy = new PersistedStats
            {
                MonitoringSince = _stats.MonitoringSince,
                SessionsMonitored = _stats.SessionsMonitored,
                AutomaticRepairsResolved = _stats.AutomaticRepairsResolved,
                EstimatedMinutesSaved = _stats.EstimatedMinutesSaved
            };
        }

        var issues = 0;
        DateTimeOffset? lastIssue = null;
        try
        {
            foreach (var file in Directory.EnumerateFiles(AppPaths.Incidents, "incident.json", SearchOption.AllDirectories))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("Game", out var gameEl) && gameEl.GetString()?.Equals("System", StringComparison.OrdinalIgnoreCase) == true)
                        continue;
                    issues++;
                    if (root.TryGetProperty("IncidentTime", out var incidentEl) && incidentEl.TryGetDateTimeOffset(out var incidentTime))
                    {
                        if (!lastIssue.HasValue || incidentTime > lastIssue.Value) lastIssue = incidentTime;
                    }
                }
                catch { }
            }
        }
        catch { }

        var repairs = 0;
        DateTimeOffset? lastRepair = null;
        try
        {
            if (Directory.Exists(AppPaths.Repairs))
            {
                foreach (var file in Directory.EnumerateFiles(AppPaths.Repairs, "*.json", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        var root = doc.RootElement;
                        if (root.TryGetProperty("state", out var state)
                            && state.TryGetProperty("IsComplete", out var completeEl)
                            && completeEl.GetBoolean())
                            repairs++;
                        if (root.TryGetProperty("updated", out var updatedEl) && updatedEl.TryGetDateTimeOffset(out var updated))
                        {
                            if (!lastRepair.HasValue || updated > lastRepair.Value) lastRepair = updated;
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        return new CapabilitiesSnapshot
        {
            AutomatedFixesAvailable = RepairCapabilityCatalog.AutomatedFixCount,
            SessionsMonitored = copy.SessionsMonitored,
            IssuesDetected = issues,
            IssuesResolvedAutomatically = copy.AutomaticRepairsResolved,
            RepairsPerformed = repairs,
            EstimatedMinutesSaved = copy.EstimatedMinutesSaved,
            MonitoringSince = copy.MonitoringSince,
            LastIssue = lastIssue,
            LastRepair = lastRepair
        };
    }

    private PersistedStats Load()
    {
        try
        {
            if (File.Exists(AppPaths.StatsFile))
                return JsonSerializer.Deserialize<PersistedStats>(File.ReadAllText(AppPaths.StatsFile), _json) ?? new PersistedStats();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not load usage stats: {ex.Message}");
        }
        return new PersistedStats();
    }

    private PersistedGameStats GetGameStats(GameKind game)
    {
        var key = game.ToString();
        if (!_stats.Games.TryGetValue(key, out var gameStats))
        {
            gameStats = new PersistedGameStats();
            _stats.Games[key] = gameStats;
        }
        gameStats.BestLaps ??= new Dictionary<string, BestLapRecord>(StringComparer.Ordinal);
        return gameStats;
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var temp = AppPaths.StatsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_stats, _json));
            File.Move(temp, AppPaths.StatsFile, true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not save usage stats: {ex.Message}");
        }
    }
}
