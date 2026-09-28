using System.Diagnostics;
using System.Text.Json;
using PitMedic.Models;
using PitMedic.Services;

internal static class ProcessExitTests
{
    public static void Run()
    {
        // All supported sims use the same policy. Preserve actual faults, missing
        // evidence, and newly confirmed genuine exit code 259.
        foreach (var game in GameDefinition.Supported)
        {
            var record = new IncidentRecord
            {
                Game = game.DisplayName, ExitCode = 259,
                Classification = new("Abnormal process termination", 72, "Non-zero code", [])
            };
            var blocked = $"Repair could not complete: {game.DisplayName} is still running. Close the simulator before applying this repair.";
            Check(LegacyProcessExitPolicy.IsFalseRunningExit(record, blocked), "Corroborated legacy false exit must be filtered for " + game.DisplayName);
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record with { ProcessExitConfirmed = true }, blocked), "A confirmed real 259 exit must survive.");
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record with { ExitCode = unchecked((int)0xC0000005) }, blocked), "Real crash codes must survive.");
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record with { Classification = new("Application fault", 88, "Windows evidence", []) }, blocked), "Independent Windows faults must survive.");
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record, null), "Missing corroboration must not discard evidence.");
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record, "A different application is still running."), "Unrelated repair evidence must not match.");
        }

        var root = Path.Combine(Path.GetTempPath(), "PitMedic-exit-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var record = new IncidentRecord { Game = "Assetto Corsa EVO", ExitCode = 259, IncidentFolder = root,
                Classification = new("Abnormal process termination", 72, "", []) };
            File.WriteAllText(Path.Combine(root, "repair.json"), JsonSerializer.Serialize(new
            {
                state = new { IsComplete = true, Success = false, Message = "Assetto Corsa EVO is still running." }
            }));
            Check(LegacyProcessExitPolicy.IsFalseRunningExit(record), "Saved blocked repair must corroborate the false exit.");
            // Many duplicates can be created without their own repair status when the
            // repair worker is already busy. Corroborate by exact process/session identity.
            var sessionRoot = Path.Combine(root, "incidents");
            var first = Path.Combine(sessionRoot, "first");
            Directory.CreateDirectory(first);
            var sessionRecord = record with { ProcessId = 1234, SessionStarted = DateTimeOffset.UtcNow, IncidentFolder = first };
            File.WriteAllText(Path.Combine(first, "incident.json"), JsonSerializer.Serialize(sessionRecord));
            File.Copy(Path.Combine(root, "repair.json"), Path.Combine(first, "repair.json"));
            var sessions = LegacyProcessExitPolicy.FindCorroboratedSessions(sessionRoot);
            Check(sessions.Contains(LegacyProcessExitPolicy.SessionKey(sessionRecord)), "Corroboration must clear siblings from the same false-exit loop.");
            Check(!sessions.Contains(LegacyProcessExitPolicy.SessionKey(sessionRecord with { ProcessId = 4567 })), "Different processes must not be suppressed.");
            Check(!sessions.Contains(LegacyProcessExitPolicy.SessionKey(sessionRecord with { SessionStarted = sessionRecord.SessionStarted.AddHours(1) })), "Reused PIDs in later sessions must not be suppressed.");
            File.WriteAllText(Path.Combine(root, "repair.json"), "incomplete json");
            Check(!LegacyProcessExitPolicy.IsFalseRunningExit(record), "Corrupt metadata must preserve the finding.");
            Check(!LegacyProcessExitPolicy.IsManualSnapshot(root), "An interrupted incident is not a manual snapshot.");
            var manual = Path.Combine(root, "20260928_120000_ManualSnapshot");
            Directory.CreateDirectory(manual);
            File.WriteAllText(Path.Combine(manual, "summary.txt"), "PitMedic manual diagnostic snapshot");
            Check(LegacyProcessExitPolicy.IsManualSnapshot(manual), "Explicit manual snapshots remain browsable.");
        }
        finally { Directory.Delete(root, true); }

        if (OperatingSystem.IsWindows())
        {
            // Exercise the real Windows handles, including a genuine 259 exit. stdin
            // keeps each child alive until the test has sampled it repeatedly.
            foreach (var expectedCode in new[] { 0, 17, 259 })
            {
                using var child = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /q /c \"set /p input= & exit {expectedCode}\"")
                { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true })!;
                using var monitor = new ProcessExitMonitor(child);
                try
                {
                    for (var i = 0; i < 1500; i++)
                        Check(!monitor.TryGetExitCode(out _), "Live process must not produce any exit during repeated polling.");
                    child.StandardInput.WriteLine("continue");
                    child.StandardInput.Flush();
                    Check(child.WaitForExit(10000), "Synthetic child must terminate.");
                    Check(monitor.TryGetExitCode(out var code) && code == expectedCode, "Confirmed termination must preserve the true exit code.");
                }
                finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
            }
            using var current = Process.GetCurrentProcess();
            using var liveMonitor = new ProcessExitMonitor(current);
            current.Dispose();
            Check(!liveMonitor.TryGetExitCode(out _), "Disposed .NET process state must not become a crash; the independent handle remains valid.");
        }
        Console.WriteLine("Process-exit and legacy-finding regression tests passed.");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
