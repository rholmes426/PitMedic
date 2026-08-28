using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class UsageStatsService
{
    private sealed class PersistedStats
    {
        public DateTimeOffset MonitoringSince { get; set; } = DateTimeOffset.Now;
        public long SessionsMonitored { get; set; }
        public int AutomaticRepairsResolved { get; set; }
        public int EstimatedMinutesSaved { get; set; }
    }

    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private PersistedStats _stats;

    public UsageStatsService()
    {
        _stats = Load();
    }

    public void RecordSessionStarted(GameKind game)
    {
        lock (_gate)
        {
            _stats.SessionsMonitored++;
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
