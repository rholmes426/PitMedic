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

    public async Task<IncidentRecord?> RecordCompanionAsync(
        CompanionSoftwareDefinition software,
        int pid,
        DateTimeOffset started,
        DateTimeOffset ended,
        int? exitCode,
        string processPath,
        IReadOnlyList<TelemetrySample> telemetry)
    {
        await Task.Delay(5500);

        var slug = software.DisplayName.Replace(" ", "_");
        var folder = Path.Combine(AppPaths.Incidents, $"{ended:yyyyMMdd_HHmmss}_{slug}_{pid}");
        Directory.CreateDirectory(folder);

        try
        {
            var eventExecutable = string.IsNullOrWhiteSpace(processPath)
                ? software.ExecutableName
                : Path.GetFileName(processPath);
            if (string.IsNullOrWhiteSpace(eventExecutable)) eventExecutable = software.ExecutableName;
            var windowsEvents = _events.GetAround(ended, eventExecutable, TimeSpan.FromSeconds(45));
            var matchingEvent = windowsEvents.FirstOrDefault(e =>
                (e.Provider.Equals("Application Error", StringComparison.OrdinalIgnoreCase)
                    || e.Provider.Equals("Application Hang", StringComparison.OrdinalIgnoreCase)
                    || e.Provider.Equals("Windows Error Reporting", StringComparison.OrdinalIgnoreCase)
                    || e.EventId is 1000 or 1001 or 1002)
                && (e.Message.Contains(software.ExecutableName, StringComparison.OrdinalIgnoreCase)
                    || software.ProcessNames.Any(name => e.Message.Contains(name, StringComparison.OrdinalIgnoreCase))
                    || e.Message.Contains(software.DisplayName, StringComparison.OrdinalIgnoreCase)));
            var dumpFiles = CollectCompanionDumps(software, started, ended, folder);

            if (matchingEvent is null && dumpFiles == 0 && (!exitCode.HasValue || exitCode.Value == 0))
            {
                Directory.Delete(folder, true);
                AppLog.Write($"Ignored clean/ambiguous companion software exit for {software.DisplayName}: exitCode={(exitCode.HasValue ? $"0x{unchecked((uint)exitCode.Value):X8}" : "unknown")}");
                return null;
            }

            var evidence = BuildCompanionEvidence(software, exitCode, matchingEvent, dumpFiles, telemetry);
            var classification = matchingEvent is not null
                ? new CrashClassification(
                    "Companion software application fault",
                    92,
                    $"Windows recorded an application fault for {software.DisplayName}. PitMedic preserved the event, crash dumps, and surrounding hardware telemetry.",
                    evidence)
                : dumpFiles > 0
                    ? new CrashClassification(
                        "Companion software crash dump captured",
                        88,
                        $"{software.DisplayName} wrote a crash dump when it stopped. PitMedic preserved the dump and surrounding hardware telemetry.",
                        evidence)
                    : new CrashClassification(
                        "Companion software abnormal termination",
                        76,
                        $"{software.DisplayName} returned a non-zero exit code. PitMedic preserved the surrounding evidence for review.",
                        evidence);

            var repairPlan = RepairPlanner.CreateCompanion(software, processPath);
            var record = new IncidentRecord
            {
                Game = software.DisplayName,
                Executable = software.ExecutableName,
                ProcessPath = processPath,
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
            await File.WriteAllTextAsync(Path.Combine(folder, "summary.txt"), FormatCompanionSummary(record, dumpFiles));

            AppLog.Write($"Recorded {software.DisplayName} exit as '{classification.Category}' ({classification.Confidence}%), repair={(repairPlan is null ? "none" : repairPlan.Id)}, folder={folder}");
            var summary = ToSummary(record, repairPlan);
            IncidentCreated?.Invoke(summary);
            return record;
        }
        catch
        {
            try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { }
            throw;
        }
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
            if (IsAcknowledged(folder)) continue;
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
                        IsDismissed: IsAcknowledged(folder)));
                }
            }
            catch { }
            if (list.Count >= count) break;
        }
        return list;
    }

    public bool Acknowledge(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder);
            var incidentsRoot = Path.GetFullPath(AppPaths.Incidents) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(incidentsRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(full)) return false;
            File.WriteAllText(Path.Combine(full, ".acknowledged"),
                $"Acknowledged: {DateTimeOffset.Now:O}\r\nNo repair was requested.\r\nEvidence retained on disk.\r\n");
            AppLog.Write($"Finding acknowledged and cleared from active views: {full}");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Could not acknowledge finding: {ex.Message}");
            return false;
        }
    }

    private static bool IsAcknowledged(string folder)
        => File.Exists(Path.Combine(folder, ".acknowledged"))
            || File.Exists(Path.Combine(folder, ".ignored"));

    private static IncidentSummary ToSummary(IncidentRecord record, RepairPlan? plan)
    {
        var (resolved, resolutionText) = ReadResolution(record.IncidentFolder);
        return new IncidentSummary(record.IncidentTime, record.Game, record.Classification.Category, record.Classification.Confidence,
            record.IncidentFolder, plan is not null && !resolved, plan?.Title ?? string.Empty, plan?.EstimatedMinutes ?? 0, resolved, resolutionText,
            record.Classification.Summary, IsAcknowledged(record.IncidentFolder), plan?.RequiresApproval ?? false);
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

    private static IReadOnlyList<string> BuildCompanionEvidence(
        CompanionSoftwareDefinition software,
        int? exitCode,
        WindowsEventEvidence? matchingEvent,
        int dumpFiles,
        IReadOnlyList<TelemetrySample> telemetry)
    {
        var evidence = new List<string> { $"PitMedic identified the affected companion app as {software.DisplayName}." };
        if (matchingEvent is not null)
            evidence.Add($"Windows {matchingEvent.Provider} event {matchingEvent.EventId} matched {software.ExecutableName}.");
        if (dumpFiles > 0)
            evidence.Add($"PitMedic collected {dumpFiles} matching crash dump file(s).");
        if (exitCode is int code && code != 0)
            evidence.Add($"The process exited with non-zero code 0x{unchecked((uint)code):X8}.");

        var end = telemetry.Count > 0 ? telemetry.Max(x => x.Timestamp) : DateTimeOffset.Now;
        var recent = telemetry.Where(x => x.Timestamp >= end.AddMinutes(-1)).ToArray();
        var maxCpu = recent.Where(x => x.CpuTempC.HasValue).Select(x => x.CpuTempC!.Value).DefaultIfEmpty().Max();
        var maxGpu = recent.Where(x => x.GpuTempC.HasValue).Select(x => x.GpuTempC!.Value).DefaultIfEmpty().Max();
        if (maxCpu > 0) evidence.Add($"CPU peaked at {maxCpu:0}°C in the final minute.");
        if (maxGpu > 0) evidence.Add($"GPU core peaked at {maxGpu:0}°C in the final minute.");
        return evidence;
    }

    private static int CollectCompanionDumps(
        CompanionSoftwareDefinition software,
        DateTimeOffset started,
        DateTimeOffset ended,
        string incidentFolder)
    {
        var destination = Path.Combine(incidentFolder, "Dumps");
        var copied = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cutoff = started.AddSeconds(-5).UtcDateTime;
        var latest = ended.AddSeconds(75).UtcDateTime;
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps"),
            Path.GetTempPath()
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(root, "*.dmp", SearchOption.TopDirectoryOnly).ToArray(); }
            catch { continue; }

            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTimeUtc < cutoff || info.LastWriteTimeUtc > latest) continue;
                    if (!software.ProcessNames.Any(name =>
                        info.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                        && !info.Name.Contains(Path.GetFileNameWithoutExtension(software.ExecutableName), StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!seen.Add(info.FullName)) continue;

                    Directory.CreateDirectory(destination);
                    var target = Path.Combine(destination, info.Name);
                    if (File.Exists(target))
                        target = Path.Combine(destination, $"{Path.GetFileNameWithoutExtension(info.Name)}_{copied + 1}.dmp");
                    File.Copy(info.FullName, target, true);
                    copied++;
                }
                catch { }
            }
        }

        return copied;
    }

    private static string FormatCompanionSummary(IncidentRecord record, int dumpFiles)
    {
        var sb = new StringBuilder();
        sb.AppendLine("PITMEDIC COMPANION SOFTWARE FINDING");
        sb.AppendLine(new string('=', 64));
        sb.AppendLine($"Software: {record.Game}");
        sb.AppendLine($"Issue: {record.IncidentTime:O}");
        sb.AppendLine($"Process started: {record.SessionStarted:O}");
        sb.AppendLine($"Process: {record.Executable} (PID {record.ProcessId})");
        sb.AppendLine($"Exit code: {(record.ExitCode.HasValue ? $"0x{unchecked((uint)record.ExitCode.Value):X8}" : "Unavailable")}");
        sb.AppendLine($"Classification: {record.Classification.Category}");
        sb.AppendLine($"Collected dumps: {dumpFiles}");
        sb.AppendLine();
        sb.AppendLine(record.Classification.Summary);
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        foreach (var item in record.Classification.Evidence) sb.AppendLine($"- {item}");
        if (record.RecommendedRepair is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Recommended recovery:");
            sb.AppendLine($"- {record.RecommendedRepair.Title}");
            sb.AppendLine($"- Estimated time: {record.RecommendedRepair.EstimatedMinutes} minute(s)");
        }
        return sb.ToString();
    }
}
