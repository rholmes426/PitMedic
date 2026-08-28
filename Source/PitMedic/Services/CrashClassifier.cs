using PitMedic.Models;

namespace PitMedic.Services;

public sealed class CrashClassifier
{
    public CrashClassification Classify(GameDefinition game, int? exitCode, IReadOnlyList<WindowsEventEvidence> events,
        IReadOnlyList<TelemetrySample> telemetry, CollectedEvidence collected,
        IReadOnlyList<LiveFaultEvidence>? liveFaults = null)
    {
        var evidence = new List<string>();
        var end = telemetry.Count > 0 ? telemetry.Max(t => t.Timestamp) : DateTimeOffset.Now;
        var recent = telemetry.Where(t => t.Timestamp >= end.AddMinutes(-1)).ToArray();
        var maxCpu = recent.Where(x => x.CpuTempC.HasValue).Select(x => x.CpuTempC!.Value).DefaultIfEmpty().Max();
        var maxGpu = recent.Where(x => x.GpuTempC.HasValue).Select(x => x.GpuTempC!.Value).DefaultIfEmpty().Max();
        var maxHotspot = recent.Where(x => x.GpuHotspotC.HasValue).Select(x => x.GpuHotspotC!.Value).DefaultIfEmpty().Max();

        if (maxCpu > 0) evidence.Add($"CPU peaked at {maxCpu:0}°C in the final minute.");
        if (maxGpu > 0) evidence.Add($"GPU core peaked at {maxGpu:0}°C in the final minute.");
        if (maxHotspot > 0) evidence.Add($"GPU hotspot peaked at {maxHotspot:0}°C in the final minute.");
        if (collected.DumpFiles > 0) evidence.Add($"PitMedic collected {collected.DumpFiles} crash dump file(s) written near the exit.");
        foreach (var hint in collected.CrashHints) evidence.Add(hint);
        foreach (var content in collected.AffectedInstalledContent)
            evidence.Add($"LMU reported a content-read failure involving Installed\\{content}.");

        var whea = events.FirstOrDefault(e => e.Provider.Contains("WHEA", StringComparison.OrdinalIgnoreCase));
        if (whea is not null)
        {
            evidence.Insert(0, $"Windows recorded {whea.Provider} event {whea.EventId} near the failure.");
            return new CrashClassification("Hardware stability event", 92,
                "A Windows hardware error was recorded at approximately the same time as the simulator failure.", evidence);
        }

        var display = events.FirstOrDefault(e => e.Provider.Equals("Display", StringComparison.OrdinalIgnoreCase)
            || e.Provider.Contains("nvlddmkm", StringComparison.OrdinalIgnoreCase)
            || e.Provider.Contains("amdwdd", StringComparison.OrdinalIgnoreCase)
            || e.EventId == 4101);
        if (display is not null)
        {
            evidence.Insert(0, $"Windows recorded graphics/display event {display.EventId} from {display.Provider}.");
            return new CrashClassification("Graphics driver / GPU event", 88,
                "A graphics-driver event occurred close to the simulator failure.", evidence);
        }

        // LMU content archive failures are highly actionable. Prioritize this diagnosis over a
        // generic Application Error / access violation when the trace identifies an affected package.
        if (collected.AffectedInstalledContent.Count > 0)
        {
            evidence.Insert(0, $"LMU identified {collected.AffectedInstalledContent.Count} installed content package(s) involved in the read/decompression failure.");
            return new CrashClassification("LMU content read / decompression failure", 95,
                "Le Mans Ultimate could not read or decompress installed track/car content. PitMedic can replace only the affected content and ask Steam to reacquire clean copies.", evidence);
        }

        var appError = events.FirstOrDefault(e =>
            (e.Provider.Equals("Application Error", StringComparison.OrdinalIgnoreCase)
                || e.Provider.Equals("Windows Error Reporting", StringComparison.OrdinalIgnoreCase)
                || e.EventId is 1000 or 1001)
            && (e.Message.Contains(game.ExecutableName, StringComparison.OrdinalIgnoreCase)
                || game.ProcessNames.Any(name => e.Message.Contains(name, StringComparison.OrdinalIgnoreCase))
                || e.Message.Contains(game.DisplayName, StringComparison.OrdinalIgnoreCase)));
        if (appError is not null)
        {
            evidence.Insert(0, $"Windows Application Error event {appError.EventId} matched the issue window.");
            return new CrashClassification("Application fault", 88,
                "Windows recorded an application fault for the simulator.", evidence);
        }

        if (collected.DumpFiles > 0)
        {
            evidence.Insert(0, "A new crash dump was found close to the simulator exit.");
            return new CrashClassification("Application crash dump captured", 86,
                "A crash dump strongly suggests the simulator terminated unexpectedly.", evidence);
        }

        if (liveFaults is { Count: > 0 })
        {
            foreach (var fault in liveFaults.OrderBy(x => x.Timestamp))
                evidence.Insert(0, fault.ToEvidenceText());
            var lead = liveFaults[0];
            if (exitCode == 0)
            {
                return new CrashClassification(lead.Category, 90,
                    $"iRacing reported {lead.Category.ToLowerInvariant()} while the simulator was still running. The user then exited normally, so PitMedic preserved the session as a diagnostic issue.", evidence);
            }
            return new CrashClassification(lead.Category, 92,
                $"iRacing reported {lead.Category.ToLowerInvariant()} before the simulator process ended.", evidence);
        }

        if (exitCode == 0 && collected.CleanExitDetected)
        {
            foreach (var hint in collected.CleanExitHints.Take(3)) evidence.Insert(0, hint);
            return new CrashClassification("Normal simulator exit", 100,
                "The simulator completed its normal shutdown sequence and exited successfully.", evidence);
        }

        if (exitCode == 0 && collected.CrashHints.Count == 0)
        {
            return new CrashClassification("Normal simulator exit", 100,
                "The simulator closed normally and no crash evidence was found for this session.", evidence);
        }

        if (maxCpu >= 97 || maxGpu >= 92 || maxHotspot >= 108)
        {
            evidence.Insert(0, "A high thermal reading was observed immediately before the failure.");
            return new CrashClassification("Thermal condition correlated", 74,
                "Temperatures were unusually high near the failure. This is correlation, not proof of thermal causation.", evidence);
        }

        if (exitCode is int code && code != 0)
        {
            evidence.Insert(0, $"The process exited with non-zero code 0x{unchecked((uint)code):X8}.");
            return new CrashClassification("Abnormal process termination", 72,
                "The simulator returned a non-zero process exit code, but stronger Windows evidence was not available.", evidence);
        }

        if (collected.CrashHints.Count > 0)
        {
            evidence.Insert(0, "Simulator logs contain one or more strong failure markers from this session.");
            return new CrashClassification("Simulator log indicates failure", 70,
                "The process exit itself was not conclusive, but current-session simulator logs contain strong failure indicators.", evidence);
        }

        return new CrashClassification("Unconfirmed simulator exit", 40,
            "The simulator process ended without strong Windows crash evidence. PitMedic preserved the session so a real crash is not lost.", evidence);
    }
}
