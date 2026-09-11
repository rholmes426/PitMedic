using System.Diagnostics;

namespace PitMedic.Services;

/// <summary>Recognizes updater activity without treating an ordinary open UI as an update.</summary>
public sealed class IRacingUpdateState
{
    private DateTimeOffset? _lastActive;
    public bool Observe(bool updaterActive, DateTimeOffset now)
    {
        if (updaterActive) _lastActive = now;
        return updaterActive || (_lastActive.HasValue && now - _lastActive.Value < TimeSpan.FromSeconds(30));
    }
}

public static class IRacingUpdateGuard
{
    private static readonly object Gate = new();
    private static readonly IRacingUpdateState State = new();

    public const string WaitMessage = "iRacing is updating or has just finished updating. Let the updater finish, then wait 30 seconds before retrying a repair if the problem remains.";

    public static bool IsBusy()
    {
        lock (Gate) return State.Observe(DetectUpdater(), DateTimeOffset.UtcNow);
    }

    public static bool IsUpdateProcess(string processName, string windowTitle) =>
        processName.Equals("iRacingUpdater", StringComparison.OrdinalIgnoreCase)
        || processName.Equals("iRacingUpdater64", StringComparison.OrdinalIgnoreCase)
        || ((processName.Equals("iRacingUI", StringComparison.OrdinalIgnoreCase)
                || processName.Equals("iRacingUI64", StringComparison.OrdinalIgnoreCase))
            && windowTitle.Trim().Equals("Updating", StringComparison.OrdinalIgnoreCase));

    public static bool SuppressDiagnostic(string signature, bool updating) => updating && signature is
        "verification-failure" or "helper-service" or "waiting-service" or "digital-signature"
        or "content-file-locked" or "missing-file-privileges" or "could-not-find-sim"
        or "ui-welcome" or "ui-render-failure";

    public static bool BlocksRepair(string repairId, bool updating) =>
        updating && repairId.StartsWith("iracing-", StringComparison.OrdinalIgnoreCase);

    public static void EnsureIdle()
    {
        if (IsBusy()) throw new InvalidOperationException(WaitMessage);
    }

    private static bool DetectUpdater()
    {
        foreach (var name in new[] { "iRacingUpdater", "iRacingUpdater64", "iRacingUI", "iRacingUI64" })
        {
            Process[] processes;
            // Do not permit repairs when the process snapshot cannot be obtained.
            try { processes = Process.GetProcessesByName(name); }
            catch { return true; }
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        if (IsUpdateProcess(name, name.StartsWith("iRacingUpdater", StringComparison.OrdinalIgnoreCase)
                                ? string.Empty : process.MainWindowTitle)) return true;
                    }
                    catch (InvalidOperationException) { } // Process exited during the snapshot.
                    catch { return true; }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }
}
