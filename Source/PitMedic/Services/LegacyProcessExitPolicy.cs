using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public static class LegacyProcessExitPolicy
{
    public static bool IsUnverifiedRunningStatus(IncidentRecord record)
        => !record.ProcessExitConfirmed && record.ExitCode == 259;

    public static bool IsUnverifiedRunningExit(IncidentRecord record)
        => IsUnverifiedRunningStatus(record)
            && record.Classification.Category == "Abnormal process termination";

    public static (string Game, int Pid, DateTimeOffset Started) SessionKey(IncidentRecord record)
        => (record.Game, record.ProcessId, record.SessionStarted);

    public static HashSet<(string Game, int Pid, DateTimeOffset Started)> FindCorroboratedSessions(string root)
    {
        var sessions = new HashSet<(string, int, DateTimeOffset)>();
        if (!Directory.Exists(root)) return sessions;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                var record = JsonSerializer.Deserialize<IncidentRecord>(File.ReadAllText(Path.Combine(folder, "incident.json")));
                if (record is null || record.ProcessId <= 0 || record.SessionStarted == default) continue;
                record = record with { IncidentFolder = folder };
                if (IsFalseRunningExit(record)) sessions.Add(SessionKey(record));
            }
            catch { } // Preserve unreadable captures.
        }
        return sessions;
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
