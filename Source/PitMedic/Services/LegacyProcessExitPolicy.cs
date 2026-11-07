using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public static class LegacyProcessExitPolicy
{
    public static bool IsUnverifiedRunningStatus(IncidentRecord record)
        => !record.ProcessExitConfirmed && record.ExitCode == 259;

    public const string RepeatedExitCategory = "Repeated simulator exit report";

    public static bool IsUnverifiedRunningExit(IncidentRecord record)
        => IsUnverifiedRunningStatus(record) && IsGeneric259Exit(record);

    public static bool IsGeneric259Exit(IncidentRecord record)
        => record.ExitCode == 259 && record.Classification.Category == "Abnormal process termination";

    public static IncidentRecord AsRepeatedExitReview(IncidentRecord record)
        => record with
        {
            ProcessExitConfirmed = false,
            RecommendedRepair = null,
            Classification = record.Classification with
            {
                Category = RepeatedExitCategory,
                Summary = "PitMedic recorded the same simulator process exit repeatedly. The original evidence is retained for review."
            }
        };

    public static (string Game, int Pid, DateTimeOffset Started) SessionKey(IncidentRecord record)
        => (record.Game, record.ProcessId, record.SessionStarted);

    public static (
        HashSet<(string Game, int Pid, DateTimeOffset Started)> LegacyFalse,
        HashSet<(string Game, int Pid, DateTimeOffset Started)> Repeated)
        FindExitAnomalies(string root)
    {
        var legacyFalse = new HashSet<(string, int, DateTimeOffset)>();
        var seen = new HashSet<(string, int, DateTimeOffset)>();
        var repeated = new HashSet<(string, int, DateTimeOffset)>();
        if (!Directory.Exists(root)) return (legacyFalse, repeated);
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                var record = JsonSerializer.Deserialize<IncidentRecord>(File.ReadAllText(Path.Combine(folder, "incident.json")));
                if (record is null || record.ProcessId <= 0 || record.SessionStarted == default || !IsGeneric259Exit(record))
                    continue;
                var key = SessionKey(record);
                if (!seen.Add(key)) repeated.Add(key);
                if (!record.ProcessExitConfirmed && IsFalseRunningExit(record with { IncidentFolder = folder }))
                    legacyFalse.Add(key);
            }
            catch { } // Preserve unreadable captures.
        }
        return (legacyFalse, repeated);
    }

    public static bool IsFalseRunningExit(IncidentRecord record, string? repairMessage)
        => IsUnverifiedRunningExit(record)
            && !string.IsNullOrWhiteSpace(record.Game)
            && (repairMessage?.Contains(record.Game + " is still running.", StringComparison.OrdinalIgnoreCase) ?? false);

    public static bool IsFalseRunningExit(IncidentRecord record)
    {
        if (record.ProcessExitConfirmed || record.ExitCode != 259) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(record.IncidentFolder, "repair.json")));
            var state = doc.RootElement.GetProperty("state");
            if (!state.GetProperty("IsComplete").GetBoolean() || state.GetProperty("Success").GetBoolean()) return false;
            return IsFalseRunningExit(record, state.GetProperty("Message").GetString());
        }
        catch { return false; } // Missing/corrupt evidence must never discard a finding.
    }

    public static bool IsManualSnapshot(string folder)
        => Path.GetFileName(folder).EndsWith("_ManualSnapshot", StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(folder, "summary.txt"));
}
