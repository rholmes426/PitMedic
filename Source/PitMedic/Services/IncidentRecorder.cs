using System.Globalization;
using System.Text;
using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class IncidentRecorder
{
    private readonly CrashClassifier _classifier = new();
    private readonly WindowsEventService _events = new();
    private readonly LogCollector _collector = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action<IncidentSummary>? IncidentCreated;

    public async Task<IncidentRecord?> RecordAsync(GameDefinition game, int pid, DateTimeOffset started, DateTimeOffset ended,
        int? exitCode, IReadOnlyList<TelemetrySample> telemetry, bool captureEveryExit,
        IReadOnlyList<LiveFaultEvidence>? liveFaults = null)
    {
        await Task.Delay(5500);

        var slug = game.DisplayName.Replace(" ", "_");
        var folder = Path.Combine(AppPaths.Incidents, $"{ended:yyyyMMdd_HHmmss}_{slug}");
        Directory.CreateDirectory(folder);

        var collected = _collector.Collect(game, started, ended, folder);
        var windowsEvents = _events.GetAround(ended, game.ExecutableName, TimeSpan.FromSeconds(45));
        var classification = _classifier.Classify(game, exitCode, windowsEvents, telemetry, collected, liveFaults);

        if (classification.Category == "Normal simulator exit"
            || (!captureEveryExit && classification.Category == "Unconfirmed simulator exit"))
        {
            try { Directory.Delete(folder, true); } catch { }
            AppLog.Write($"Ignored clean/ambiguous {game.DisplayName} exit: exitCode={(exitCode.HasValue ? $"0x{unchecked((uint)exitCode.Value):X8}" : "unknown")}, classification={classification.Category}");
            return null;
        }

        var repairPlan = RepairPlanner.Create(game, classification, collected, liveFaults);
        var record = new IncidentRecord
        {
            Game = game.DisplayName,
            Executable = game.ExecutableName,
            ProcessId = pid,
            SessionStarted = started,
            IncidentTime = ended,
            ExitCode = exitCode,
            Classification = classification,
            RecommendedRepair = repairPlan,
            IncidentFolder = folder
        };

        await File.WriteAllTextAsync(Path.Combine(folder, "incident.json"), JsonSerializer.Serialize(record, JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(folder, "telemetry.csv"), ToCsv(telemetry));
        await File.WriteAllTextAsync(Path.Combine(folder, "windows-events.txt"), FormatEvents(windowsEvents));
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"), FormatSummary(record, collected));

        AppLog.Write($"Recorded {game.DisplayName} exit as '{classification.Category}' ({classification.Confidence}%), repair={(repairPlan is null ? "none" : repairPlan.Id)}, folder={folder}");
        var summary = ToSummary(record, repairPlan);
        IncidentCreated?.Invoke(summary);
        return record;
    }

    public async Task<IncidentRecord?> RecordLiveFaultAsync(GameDefinition game, LiveFaultEvidence fault,
        IReadOnlyList<TelemetrySample> telemetry)
    {
        var now = DateTimeOffset.Now;
        var slug = game.DisplayName.Replace(" ", "_");
        var folder = Path.Combine(AppPaths.Incidents, $"{now:yyyyMMdd_HHmmss}_{slug}_Live");
        Directory.CreateDirectory(folder);

        try
        {
            var started = fault.Timestamp.AddMinutes(-2);
            var collected = _collector.Collect(game, started, now, folder);
            var windowsEvents = _events.GetAround(now, game.ExecutableName, TimeSpan.FromSeconds(60));
            var evidence = new List<string> { fault.ToEvidenceText() };
            evidence.AddRange(collected.CrashHints.Take(4));
            if (windowsEvents.Count > 0)
                evidence.Add($"Windows recorded {windowsEvents.Count} related event(s) near the detected simulator error.");

            var classification = new CrashClassification(
                fault.Category,
                90,
                $"{game.DisplayName} reported a recoverable software problem while PitMedic was passively monitoring its support logs/state: {fault.Message}",
                evidence.Distinct().ToArray());
            var repairPlan = RepairPlanner.Create(game, classification, collected, new[] { fault });
            var record = new IncidentRecord
            {
                Game = game.DisplayName,
                Executable = game.ExecutableName,
                ProcessId = 0,
                SessionStarted = started,
                IncidentTime = fault.Timestamp,
                ExitCode = null,
                Classification = classification,
                RecommendedRepair = repairPlan,
                IncidentFolder = folder
            };

            await File.WriteAllTextAsync(Path.Combine(folder, "incident.json"), JsonSerializer.Serialize(record, JsonOptions));
            await File.WriteAllTextAsync(Path.Combine(folder, "telemetry.csv"), ToCsv(telemetry));
            await File.WriteAllTextAsync(Path.Combine(folder, "windows-events.txt"), FormatEvents(windowsEvents));
            await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"), FormatSummary(record, collected));

            AppLog.Write($"Recorded live {game.DisplayName} fault as '{classification.Category}', repair={(repairPlan is null ? "none" : repairPlan.Id)}, folder={folder}");
            IncidentCreated?.Invoke(ToSummary(record, repairPlan));
            return record;
        }
        catch
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            throw;
        }
    }

    public async Task<IncidentSummary> CaptureSnapshotAsync(IReadOnlyList<TelemetrySample> telemetry)
    {
        var now = DateTimeOffset.Now;
        var folder = Path.Combine(AppPaths.Incidents, $"{now:yyyyMMdd_HHmmss}_ManualSnapshot");
        Directory.CreateDirectory(folder);
        var windowsEvents = _events.GetAround(now, string.Empty, TimeSpan.FromSeconds(30));
        await File.WriteAllTextAsync(Path.Combine(folder, "telemetry.csv"), ToCsv(telemetry));
        await File.WriteAllTextAsync(Path.Combine(folder, "windows-events.txt"), FormatEvents(windowsEvents));
        await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"), $"PitMedic manual diagnostic snapshot\r\nCaptured: {now:O}\r\n");
        var summary = new IncidentSummary(now, "System", "Manual diagnostic snapshot", 100, folder, Summary: "Manual diagnostic snapshot captured by an earlier PitMedic version.");
        IncidentCreated?.Invoke(summary);
        return summary;
    }

    public IncidentRecord? LoadRecord(string folder)
    {
        try
        {
            var json = Path.Combine(folder, "incident.json");
            if (!File.Exists(json)) return null;
            var record = JsonSerializer.Deserialize<IncidentRecord>(File.ReadAllText(json), JsonOptions);
            if (record is null) return null;
            if (string.IsNullOrWhiteSpace(record.IncidentFolder) || !Path.GetFullPath(record.IncidentFolder).Equals(Path.GetFullPath(folder), StringComparison.OrdinalIgnoreCase))
                record = record with { IncidentFolder = folder };
            var plan = RepairPlanner.TryCreateFromIncident(record);
            var changed = false;
            if (record.RecommendedRepair is null && plan is not null)
            {
                record = record with { RecommendedRepair = plan };
                changed = true;
            }
            if (plan is not null
                && !record.Classification.Category.Contains("content", StringComparison.OrdinalIgnoreCase)
                && record.Game.Equals("Le Mans Ultimate", StringComparison.OrdinalIgnoreCase))
            {
                var evidence = record.Classification.Evidence.ToList();
                evidence.Insert(0, "Stored LMU trace data identifies installed track/car content involved in a read or decompression failure.");
                record = record with
                {
                    Classification = new CrashClassification(
                        "LMU content read / decompression failure",
                        Math.Max(93, record.Classification.Confidence),
                        "Le Mans Ultimate could not read or decompress installed content. PitMedic has a targeted repair available.",
                        evidence.Distinct().ToArray())
                };
                changed = true;
            }
            if (changed)
            {
                try { File.WriteAllText(json, JsonSerializer.Serialize(record, JsonOptions)); } catch { }
            }
            return record;
        }
        catch { return null; }
    }

    public IReadOnlyList<IncidentSummary> LoadRecent(int count = 8)
    {
        if (!Directory.Exists(AppPaths.Incidents)) return Array.Empty<IncidentSummary>();
        var list = new List<IncidentSummary>();
        foreach (var folder in Directory.EnumerateDirectories(AppPaths.Incidents).OrderByDescending(x => x).Take(count * 4))
        {
            if (File.Exists(Path.Combine(folder, ".ignored"))) continue;
            var json = Path.Combine(folder, "incident.json");
            try
            {
                if (File.Exists(json))
                {
                    var record = LoadRecord(folder);
                    if (record is not null)
                    {
                        if (record.ExitCode == 0 && record.Classification.Category == "Unconfirmed simulator exit")
                            continue;
                        if (record.ExitCode == 0
                            && record.Classification.Category == "Simulator log indicates failure"
                            && record.Classification.Evidence.Any(e => e.Contains("'exception'", StringComparison.OrdinalIgnoreCase))
                            && !record.Classification.Evidence.Any(e => e.Contains("crash dump", StringComparison.OrdinalIgnoreCase)
                                || e.Contains("Windows Application Error", StringComparison.OrdinalIgnoreCase)))
                            continue;
                        list.Add(ToSummary(record, record.RecommendedRepair));
                    }
                }
                else
                {
                    var info = new DirectoryInfo(folder);
                    list.Add(new IncidentSummary(info.CreationTime, "System", "Manual diagnostic snapshot", 100, folder, Summary: "Manual diagnostic snapshot captured by an earlier PitMedic version."));
                }
            }
            catch { }
            if (list.Count >= count) break;
        }
        return list;
    }

    public IReadOnlyList<IncidentSummary> LoadHistory(int count = 500)
    {
        if (!Directory.Exists(AppPaths.Incidents)) return Array.Empty<IncidentSummary>();
        var list = new List<IncidentSummary>();
        foreach (var folder in Directory.EnumerateDirectories(AppPaths.Incidents).OrderByDescending(x => x))
        {
            try
            {
                var json = Path.Combine(folder, "incident.json");
                if (File.Exists(json))
                {
                    var record = LoadRecord(folder);
                    if (record is null) continue;
                    if (record.ExitCode == 0 && record.Classification.Category == "Unconfirmed simulator exit") continue;
                    list.Add(ToSummary(record, record.RecommendedRepair));
                }
                else
                {
                    var info = new DirectoryInfo(folder);
                    list.Add(new IncidentSummary(info.CreationTime, "System", "Manual diagnostic snapshot", 100, folder,
                        Summary: "Manual diagnostic snapshot captured by an earlier PitMedic version.",
                        IsDismissed: File.Exists(Path.Combine(folder, ".ignored"))));
                }
            }
            catch { }
            if (list.Count >= count) break;
        }
        return list;
    }

    public bool Ignore(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            var incidentsRoot = Path.GetFullPath(AppPaths.Incidents) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(incidentsRoot, StringComparison.OrdinalIgnoreCase)) return false;
            File.WriteAllText(Path.Combine(full, ".ignored"), $"Dismissed from Recent Issues: {DateTimeOffset.Now:O}\r\nEvidence retained on disk.\r\n");
            AppLog.Write($"Issue dismissed from recent list: {full}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not dismiss issue: {ex.Message}");
            return false;
        }
    }

    private static IncidentSummary ToSummary(IncidentRecord record, RepairPlan? plan)
    {
        var (resolved, resolutionText) = ReadResolution(record.IncidentFolder);
        return new IncidentSummary(record.IncidentTime, record.Game, record.Classification.Category, record.Classification.Confidence,
            record.IncidentFolder, plan is not null && !resolved, plan?.Title ?? string.Empty, plan?.EstimatedMinutes ?? 0, resolved, resolutionText,
            record.Classification.Summary, File.Exists(Path.Combine(record.IncidentFolder, ".ignored")), plan?.RequiresApproval ?? false);
    }

    private static (bool Resolved, string Text) ReadResolution(string folder)
    {
        try
        {
            var repairPath = Path.Combine(folder, "repair.json");
            if (!File.Exists(repairPath)) return (false, string.Empty);
            using var doc = JsonDocument.Parse(File.ReadAllText(repairPath));
            if (!doc.RootElement.TryGetProperty("state", out var state)) return (false, string.Empty);
            var complete = state.TryGetProperty("IsComplete", out var completeEl) && completeEl.GetBoolean();
            var success = state.TryGetProperty("Success", out var successEl) && successEl.GetBoolean();
            return complete && success ? (true, "Resolved") : (false, string.Empty);
        }
        catch { return (false, string.Empty); }
    }

    private static string ToCsv(IReadOnlyList<TelemetrySample> rows)
    {
        var sb = new StringBuilder("Timestamp,CpuTempC,CpuLoadPct,CpuClockMhz,CpuPowerW,GpuTempC,GpuHotspotC,GpuMemoryTempC,GpuLoadPct,GpuClockMhz,GpuPowerW,GpuFanRpm,GpuMemoryUsedMb,GpuMemoryTotalMb,MemoryLoadPct\r\n");
        foreach (var x in rows)
        {
            sb.Append(x.Timestamp.ToString("O", CultureInfo.InvariantCulture)).Append(',')
              .Append(F(x.CpuTempC)).Append(',').Append(F(x.CpuLoadPct)).Append(',').Append(F(x.CpuClockMhz)).Append(',').Append(F(x.CpuPowerW)).Append(',')
              .Append(F(x.GpuTempC)).Append(',').Append(F(x.GpuHotspotC)).Append(',').Append(F(x.GpuMemoryTempC)).Append(',').Append(F(x.GpuLoadPct)).Append(',')
              .Append(F(x.GpuClockMhz)).Append(',').Append(F(x.GpuPowerW)).Append(',').Append(F(x.GpuFanRpm)).Append(',')
              .Append(F(x.GpuMemoryUsedMb)).Append(',').Append(F(x.GpuMemoryTotalMb)).Append(',').Append(F(x.MemoryLoadPct)).Append("\r\n");
        }
        return sb.ToString();
        static string F(float? v) => v?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string FormatEvents(IEnumerable<WindowsEventEvidence> events)
    {
        var sb = new StringBuilder();
        foreach (var e in events)
        {
            sb.AppendLine($"[{e.TimeCreated:O}] {e.LogName} | {e.Provider} | Event {e.EventId} | {e.Level}");
            sb.AppendLine(e.Message);
            sb.AppendLine(new string('-', 88));
        }
        return sb.ToString();
    }

    private static string FormatSummary(IncidentRecord r, CollectedEvidence collected)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PITMEDIC FINDING / SESSION END");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine($"Game: {r.Game}");
        sb.AppendLine($"Issue: {r.IncidentTime:O}");
        sb.AppendLine($"Session started: {r.SessionStarted:O}");
        sb.AppendLine($"Process: {r.Executable} (PID {r.ProcessId})");
        sb.AppendLine($"Exit code: {(r.ExitCode.HasValue ? $"0x{unchecked((uint)r.ExitCode.Value):X8}" : "Unavailable")}");
        sb.AppendLine($"Classification: {r.Classification.Category}");
        sb.AppendLine($"Collected logs: {collected.LogFiles}");
        sb.AppendLine($"Collected dumps: {collected.DumpFiles}");
        sb.AppendLine();
        sb.AppendLine(r.Classification.Summary);
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        foreach (var item in r.Classification.Evidence) sb.AppendLine($"- {item}");
        if (r.RecommendedRepair is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Recommended repair:");
            sb.AppendLine($"- {r.RecommendedRepair.Title}");
            sb.AppendLine($"- Estimated time: {r.RecommendedRepair.EstimatedMinutes} minutes");
            foreach (var path in r.RecommendedRepair.AffectedContentRelativePaths) sb.AppendLine($"- Installed\\{path}");
        }

        return sb.ToString();
    }
}
