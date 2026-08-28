using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class RepairService : IDisposable
{
    private readonly object _gate = new();
    private readonly UsageStatsService? _usage;
    private CancellationTokenSource? _activeCts;
    private RepairStatus? _current;
    private DateTimeOffset _startedAt;
    private int _estimatedSeconds;
    private readonly bool _executeElevatedRepairsLocally;
    private readonly bool _persistStatus;
    private readonly Guid? _repairStatusId;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public event Action<RepairStatus>? StatusChanged;

    public RepairService(
        UsageStatsService? usage,
        bool executeElevatedRepairsLocally = false,
        bool persistStatus = true,
        Guid? repairStatusId = null)
    {
        _usage = usage;
        _executeElevatedRepairsLocally = executeElevatedRepairsLocally;
        _persistStatus = persistStatus;
        _repairStatusId = repairStatusId;
    }

    public RepairStatus? Current
    {
        get { lock (_gate) return _current; }
    }

    public bool Begin(IncidentRecord incident, RepairPlan plan, AppSettings settings, bool automatic = false)
    {
        lock (_gate)
        {
            if (_current?.IsActive == true) return false;
            _activeCts?.Dispose();
            _activeCts = new CancellationTokenSource();
            _startedAt = DateTimeOffset.Now;
            _estimatedSeconds = Math.Max(60, plan.EstimatedMinutes * 60);
            _current = new RepairStatus
            {
                RepairId = _repairStatusId ?? Guid.NewGuid(),
                IncidentFolder = incident.IncidentFolder,
                Title = plan.Title,
                Stage = "Preparing repair",
                Message = "Preparing the repair workspace and validating prerequisites...",
                Detail = "No game files have been changed yet.",
                StepNumber = 1,
                TotalSteps = 5,
                Percent = 2,
                EstimatedSecondsRemaining = _estimatedSeconds,
                StartedAt = _startedAt,
                IsActive = true
            };
        }
        Publish(Current!);
        _ = Task.Run(() =>
            ElevatedRepairPolicy.RequiresElevation(plan.Id) && !_executeElevatedRepairsLocally
                ? RunElevatedViaHelperAsync(incident, plan, settings, automatic, _activeCts!.Token)
                : RunAsync(incident, plan, settings, automatic, _activeCts!.Token));
        return true;
    }

    public void Cancel()
    {
        lock (_gate)
        {
            if (_current?.IsActive != true) return;
            try { _activeCts?.Cancel(); } catch { }
        }
    }

    private async Task RunElevatedViaHelperAsync(
        IncidentRecord incident,
        RepairPlan plan,
        AppSettings settings,
        bool automatic,
        CancellationToken token)
    {
        try
        {
            Update(
                incident,
                plan,
                3,
                "Administrator approval required",
                "Windows will ask for permission to run PitMedic's narrowly scoped repair helper.",
                $"Only the allowlisted repair '{plan.Id}' will run with administrator rights. Monitoring remains unelevated.",
                1,
                Math.Max(1, plan.Steps.Count),
                true,
                false,
                false,
                null);

            var final = await ElevatedRepairClient.RunAsync(
                incident,
                plan,
                settings,
                automatic,
                Current?.RepairId ?? Guid.NewGuid(),
                status =>
                {
                    PersistStatus(incident, plan, status);
                    AcceptExternalStatus(status);
                },
                token);
            if (final.Success) _usage?.RecordRepairCompleted(plan, automatic);
        }
        catch (OperationCanceledException ex)
        {
            AppLog.Write($"Elevated repair cancelled: {ex.Message}");
            Update(
                incident,
                plan,
                100,
                "Repair cancelled",
                ex.Message,
                "No additional elevated repair action will be started.",
                Math.Max(1, plan.Steps.Count),
                Math.Max(1, plan.Steps.Count),
                false,
                true,
                false,
                null);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Elevated repair failed: {ex}");
            Update(
                incident,
                plan,
                100,
                "Repair needs attention",
                $"The elevated repair helper could not complete: {ex.Message}",
                "PitMedic preserved the incident evidence. Reinstall PitMedic if the helper is missing or damaged.",
                Math.Max(1, plan.Steps.Count),
                Math.Max(1, plan.Steps.Count),
                false,
                true,
                false,
                null);
        }
    }

    private void AcceptExternalStatus(RepairStatus status)
    {
        lock (_gate) _current = status;
        Publish(status);
    }

    private async Task RunAsync(IncidentRecord incident, RepairPlan plan, AppSettings settings, bool automatic, CancellationToken token)
    {
        string? backupRoot = null;
        var backedUp = new List<(string Original, string Backup)>();
        IDisposable? steamUiSuppression = null;
        try
        {
            if (ElevatedRepairPolicy.RequiresElevation(plan.Id) && !IsAdministrator())
                throw new InvalidOperationException("The allowlisted repair helper is not running with administrator permissions.");

            if (!plan.Id.Equals("lmu-targeted-content-reacquire", StringComparison.OrdinalIgnoreCase))
            {
                var standardRepairId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");
                var repairStorage = _executeElevatedRepairsLocally ? AppPaths.ElevatedRepairs : AppPaths.Repairs;
                backupRoot = Path.Combine(repairStorage, "Backups", standardRepairId + "_" + SanitizeName(plan.Id));
                Directory.CreateDirectory(backupRoot);
                var completion = await RunStandardRepairAsync(incident, plan, backupRoot, token);
                if (!settings.KeepRepairBackups)
                {
                    try { Directory.Delete(backupRoot, true); backupRoot = null; } catch { }
                }
                _usage?.RecordRepairCompleted(plan, automatic);
                Update(incident, plan, 100, "Repair complete", completion, "The repair actions completed successfully. PitMedic retained the issue evidence and repair history for review.",
                    Math.Max(1, plan.Steps.Count), Math.Max(1, plan.Steps.Count), false, true, true, backupRoot);
                return;
            }

            if (IsLmuRunning())
                throw new InvalidOperationException("Le Mans Ultimate is still running. Close the simulator, then choose Repair again.");

            var lmuRoot = SteamLibraryLocator.FindLeMansUltimateRoot()
                ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located in the configured Steam libraries.");
            var installedRoot = Path.GetFullPath(Path.Combine(lmuRoot, "Installed")) + Path.DirectorySeparatorChar;
            var repairId = DateTimeOffset.Now.ToString("yyyyMMdd_HHmmss");

            // Recovery data belongs in PitMedic's own writable data folder rather than the Steam
            // installation. This avoids inheriting Steam/Program Files ACLs and makes rollback
            // independent of the game folder being renamed or temporarily locked.
            var repairStorage = _executeElevatedRepairsLocally ? AppPaths.ElevatedRepairs : AppPaths.Repairs;
            backupRoot = Path.Combine(repairStorage, "Backups", repairId);
            Directory.CreateDirectory(backupRoot);

            Update(incident, plan, 8, "Creating recovery point", "Creating a reversible recovery copy of the affected LMU content...", "Copying only the packages identified by the issue. Original game content remains in place during this step.", 1, 5, true, false, false, backupRoot);
            var backupIndex = 0;
            foreach (var relative in plan.AffectedContentRelativePaths)
            {
                token.ThrowIfCancellationRequested();
                backupIndex++;
                Update(incident, plan, Math.Min(16, 8 + backupIndex * 3), "Creating recovery point", "Copying affected content into PitMedic recovery storage...", $"Backing up Installed\\{relative} ({backupIndex} of {plan.AffectedContentRelativePaths.Count}).", 1, 5, true, false, false, backupRoot);
                var original = Path.GetFullPath(Path.Combine(installedRoot, relative));
                if (!original.StartsWith(installedRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Unsafe repair path was rejected: {relative}");
                if (!Directory.Exists(original))
                {
                    AppLog.Write($"Repair package already absent: {original}");
                    continue;
                }

                var backup = Path.Combine(backupRoot, relative);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.CreateDirectory(backup);

                AppLog.Write($"Repair backup copy start: {original} -> {backup}");
                await CopyDirectoryAsync(original, backup, token);
                backedUp.Add((original, backup));
                AppLog.Write($"Repair backup copy complete: {original} -> {backup}");
            }

            if (backedUp.Count == 0)
                throw new InvalidOperationException("The affected LMU content was not found. Nothing was changed.");

            Update(incident, plan, 18, "Isolating damaged content", "Recovery copy complete. Removing the affected installed files so Steam will reacquire clean copies...", "The recovery copy has been verified and is stored outside the Steam library.", 2, 5, true, false, false, backupRoot);
            var removeIndex = 0;
            foreach (var item in backedUp)
            {
                token.ThrowIfCancellationRequested();
                removeIndex++;
                Update(incident, plan, Math.Min(25, 18 + removeIndex * 3), "Isolating damaged content", "Removing the affected installed files so Steam will reacquire clean copies...", $"Removing package {removeIndex} of {backedUp.Count}: {Path.GetFileName(item.Original)}", 2, 5, true, false, false, backupRoot);
                await RemoveInstalledContentAsync(item.Original, token);
                AppLog.Write($"Repair removed installed content: {item.Original}");
            }

            Update(incident, plan, 27, "Starting Steam validation", "Affected content isolated. Asking Steam to validate LMU in the background...", "PitMedic requests silent/minimized Steam operation and continues monitoring the game files directly.", 3, 5, true, false, false, backupRoot);
            steamUiSuppression = await SteamClientService.StartValidationAsync(plan.SteamAppId, token);
            AppLog.Write($"Repair launched background Steam validation for app {plan.SteamAppId}.");
            Update(incident, plan, 34, "Reacquiring clean content", "Steam validation is running silently. PitMedic is watching for clean replacement content...", "Steam and Steam WebHelper windows are temporarily hidden while validation runs. PitMedic remains visible and owns the repair experience.", 4, 5, true, false, false, backupRoot);

            var started = DateTimeOffset.Now;
            var deadline = started.AddMinutes(35);
            var stablePasses = 0;
            while (DateTimeOffset.Now < deadline)
            {
                token.ThrowIfCancellationRequested();
                await Task.Delay(TimeSpan.FromSeconds(10), token);

                var restored = plan.AffectedContentRelativePaths.All(relative =>
                {
                    var path = Path.Combine(installedRoot, relative);
                    return Directory.Exists(path) && ContainsMasFile(path);
                });

                stablePasses = restored ? stablePasses + 1 : 0;
                var elapsed = (DateTimeOffset.Now - started).TotalMinutes;
                var pct = Math.Clamp(34 + (int)(elapsed / Math.Max(2, plan.EstimatedMinutes) * 48), 34, 88);
                var message = restored
                    ? "Replacement content detected. Waiting for Steam writes to settle..."
                    : "Steam is validating and reacquiring the affected LMU content...";
                Update(incident, plan, pct, restored ? "Verifying replacement files" : "Reacquiring clean content", message, restored ? "Replacement package files are present. Waiting for file sizes and timestamps to settle." : $"Waiting for {plan.AffectedContentRelativePaths.Count} affected package(s) to be restored.", restored ? 5 : 4, 5, true, false, false, backupRoot);

                if (stablePasses >= 3)
                {
                    if (!settings.KeepRepairBackups)
                    {
                        try { Directory.Delete(backupRoot, true); backupRoot = null; } catch { }
                    }
                    _usage?.RecordRepairCompleted(plan, automatic);
                    Update(incident, plan, 100, "Repair complete", "Repair completed. The affected LMU content has been restored and is ready to test.", "The issue has been marked resolved. PitMedic will keep the evidence and repair record for history.", 5, 5, false, true, true, backupRoot);
                    return;
                }
            }

            throw new TimeoutException("Steam validation did not restore the affected content within 35 minutes.");
        }
        catch (OperationCanceledException)
        {
            await TryRollbackAsync(backedUp, CancellationToken.None);
            Update(incident, plan, 100, "Repair cancelled", "Repair cancelled. PitMedic restored the backed-up content where possible.", "Review the repair record before retrying the simulator.", 5, 5, false, true, false, backupRoot);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Repair failed: {ex}");
            await TryRollbackAsync(backedUp, CancellationToken.None);
            var hint = ex is UnauthorizedAccessException
                ? " Windows denied an operation even though PitMedic is elevated."
                : string.Empty;
            var recoverySummary = backedUp.Count > 0
                ? " Changed LMU content was restored from the recovery copy where possible."
                : " PitMedic stopped the repair and preserved the detailed repair log and any recovery data.";
            Update(incident, plan, 100, "Repair needs attention", $"Repair could not complete: {ex.Message}{hint}{recoverySummary}", "Review the preserved repair log for the exact Windows/tool output before retrying.", 5, 5, false, true, false, backupRoot);
        }
        finally
        {
            steamUiSuppression?.Dispose();
        }
    }

    private async Task<string> RunStandardRepairAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        return plan.Id switch
        {
            "lmu-steam-verify" => await RepairLmuSteamVerifyAsync(incident, plan, backupRoot, token),
            "lmu-shader-cache" => await RepairLmuShaderCacheAsync(incident, plan, backupRoot, token),
            "lmu-reset-dx11-config" => await RepairLmuDx11ConfigAsync(incident, plan, backupRoot, token),
            "lmu-disable-plugins" => await RepairLmuPluginsAsync(incident, plan, backupRoot, token),
            "lmu-reinstall-eac" => await RepairLmuEacAsync(incident, plan, backupRoot, token),
            "lmu-quarantine-reshade" => await RepairLmuGraphicsHooksAsync(incident, plan, backupRoot, token),
            "lmu-sync-windows-time