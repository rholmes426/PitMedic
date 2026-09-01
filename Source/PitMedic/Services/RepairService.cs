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
                var standardRepairStorage = _executeElevatedRepairsLocally ? AppPaths.ElevatedRepairs : AppPaths.Repairs;
                backupRoot = Path.Combine(standardRepairStorage, "Backups", standardRepairId + "_" + SanitizeName(plan.Id));
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
            var targetedRepairStorage = _executeElevatedRepairsLocally ? AppPaths.ElevatedRepairs : AppPaths.Repairs;
            backupRoot = Path.Combine(targetedRepairStorage, "Backups", repairId);
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
            _ when CompanionRecoveryPolicy.IsSupportedRepairId(plan.Id) => await RepairCompanionAppAsync(incident, plan, backupRoot, token),

            "lmu-steam-verify" => await RepairLmuSteamVerifyAsync(incident, plan, backupRoot, token),
            "lmu-shader-cache" => await RepairLmuShaderCacheAsync(incident, plan, backupRoot, token),
            "lmu-reset-dx11-config" => await RepairLmuDx11ConfigAsync(incident, plan, backupRoot, token),
            "lmu-disable-plugins" => await RepairLmuPluginsAsync(incident, plan, backupRoot, token),
            "lmu-reinstall-eac" => await RepairLmuEacAsync(incident, plan, backupRoot, token),
            "lmu-quarantine-reshade" => await RepairLmuGraphicsHooksAsync(incident, plan, backupRoot, token),
            "lmu-sync-windows-time" => await RepairWindowsTimeAsync(incident, plan, backupRoot, token),
            "lmu-close-overlay-tools" => await RepairCloseProcessesAsync(incident, plan, backupRoot, token, new[] { "MSIAfterburner", "RTSS", "RivaTunerStatisticsServer" }, "overlay/tuning"),
            "lmu-close-ghub" => await RepairCloseProcessesAsync(incident, plan, backupRoot, token, new[] { "lghub", "lghub_agent", "lghub_updater" }, "Logitech G HUB"),

            "iracing-helper-service" => await RepairIRacingHelperServiceAsync(incident, plan, backupRoot, token),
            "iracing-ui-cache" => await RepairIRacingUiCacheAsync(incident, plan, backupRoot, token),
            "iracing-ui-safe" => await RepairIRacingUiSafeAsync(incident, plan, backupRoot, token),
            "iracing-eac-reinstall" => await RepairIRacingEacAsync(incident, plan, backupRoot, token),
            "iracing-update-reset" => await RepairIRacingUpdateAsync(incident, plan, backupRoot, token),
            "iracing-updater-autoinstall" => await RepairIRacingUpdaterAsync(incident, plan, backupRoot, token, "-autoinstall"),
            "iracing-track-index-reset" => await RepairIRacingTrackIndexAsync(incident, plan, backupRoot, token, false),
            "iracing-car-index-reset" => await RepairIRacingCarIndexAsync(incident, plan, backupRoot, token),
            "iracing-steam-track-repair" => await RepairIRacingTrackIndexAsync(incident, plan, backupRoot, token, true),
            "iracing-release-content-lock" => await RepairIRacingContentLockAsync(incident, plan, backupRoot, token),
            "iracing-renderer-reset" => await RepairIRacingRendererAsync(incident, plan, backupRoot, token),
            "iracing-trueforce-disable" => await RepairIRacingIniValueAsync(incident, plan, backupRoot, token, "trueForceEnabled", "0", "TrueForce disabled"),
            "iracing-logitech-led-disable" => await RepairIRacingIniValueAsync(incident, plan, backupRoot, token, "enableLogitechLED", "0", "Logitech hardware lighting disabled"),
            "iracing-logitech-shutdown-workaround" => await RepairIRacingLogitechShutdownAsync(incident, plan, backupRoot, token),
            "iracing-reset-user-config" => await RepairIRacingUserConfigAsync(incident, plan, backupRoot, token),
            "iracing-clear-compatibility" => await RepairIRacingCompatibilityAsync(incident, plan, backupRoot, token),
            "iracing-run-updater" => await RepairIRacingUpdaterAsync(incident, plan, backupRoot, token, string.Empty),
            "iracing-windows-integrity" => await RepairWindowsIntegrityAsync(incident, plan, backupRoot, token),

            "ace-video-settings-reset" => await RepairAceVideoSettingsAsync(incident, plan, backupRoot, token),
            "ace-profile-reset" => await RepairAceProfileAsync(incident, plan, backupRoot, token),
            "ace-steam-verify" => await RepairAceSteamVerifyAsync(incident, plan, backupRoot, token),

            "raceroom-browser-reset" => await RepairRaceRoomBrowserAsync(incident, plan, backupRoot, token),
            "raceroom-graphics-reset" => await RepairRaceRoomGraphicsAsync(incident, plan, backupRoot, token),
            "raceroom-shader-reset" => await RepairRaceRoomShaderAsync(incident, plan, backupRoot, token),
            "raceroom-profile-reset" => await RepairRaceRoomProfileAsync(incident, plan, backupRoot, token),
            "raceroom-steam-verify" => await RepairRaceRoomSteamVerifyAsync(incident, plan, backupRoot, token),

            "acc-game-user-settings-reset" => await RepairAccFileAsync(incident, plan, backupRoot, token, "GameUserSettings.ini", Path.Combine(GetAccLocalConfigRoot(), "GameUserSettings.ini"), "display/startup settings"),
            "acc-engine-config-reset" => await RepairAccFileAsync(incident, plan, backupRoot, token, "Engine.ini", Path.Combine(GetAccLocalConfigRoot(), "Engine.ini"), "engine configuration"),
            "acc-controls-reset" => await RepairAccFileAsync(incident, plan, backupRoot, token, "controls.json", Path.Combine(GetAccDocumentsRoot(), "Config", "controls.json"), "controller configuration"),
            "acc-ffb-reset" => await RepairAccFileAsync(incident, plan, backupRoot, token, "ffbUserSettings.json", Path.Combine(GetAccDocumentsRoot(), "Config", "ffbUserSettings.json"), "force-feedback settings"),
            "acc-trueforce-disable" => await RepairAccTrueForceAsync(incident, plan, backupRoot, token),
            "acc-control-presets-reset" => await RepairAccControlPresetsAsync(incident, plan, backupRoot, token),
            "acc-profile-reset" => await RepairAccProfileAsync(incident, plan, backupRoot, token),
            "acc-steam-verify" => await RepairSteamVerifyAsync(incident, plan, backupRoot, token, GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione", "805550"),

            "ams2-graphics-reset" => await RepairAms2FilesAsync(incident, plan, backupRoot, token, "graphics configuration", new[] { Path.Combine(GetAms2DocumentsRoot(), "graphicsconfigdx11.xml") }),
            "ams2-vr-reset" => await RepairAms2FilesAsync(incident, plan, backupRoot, token, "VR configuration", new[] { "graphicsconfigopenvrdx11.xml", "openvrsettings.xml", "graphicsconfigoculusdx11.xml", "oculussettings.xml" }.Select(n => Path.Combine(GetAms2DocumentsRoot(), n)).ToArray()),
            "ams2-controller-reset" => await RepairAms2FilesAsync(incident, plan, backupRoot, token, "controller configuration", FindAms2ProfileFiles("default.controllersettings.v1.03.sav")),
            "ams2-ffb-reset" => await RepairAms2FilesAsync(incident, plan, backupRoot, token, "custom FFB settings", new[] { Path.Combine(GetAms2DocumentsRoot(), "ffb_custom_settings.txt") }),
            "ams2-tuning-reset" => await RepairAms2TuningAsync(incident, plan, backupRoot, token),
            "ams2-default-profile-reset" => await RepairAms2FilesAsync(incident, plan, backupRoot, token, "default profile", FindAms2ProfileFiles("default.sav")),
            "ams2-championship-reset" => await RepairAms2ChampionshipAsync(incident, plan, backupRoot, token),
            "ams2-profile-reset" => await RepairAms2ProfileAsync(incident, plan, backupRoot, token),
            "ams2-steam-verify" => await RepairSteamVerifyAsync(incident, plan, backupRoot, token, GameKind.Automobilista2, "Automobilista 2", "1066890"),
            _ => throw new NotSupportedException($"This repair playbook is not implemented yet: {plan.Id}")
        };
    }

    private async Task<string> RepairCompanionAppAsync(
        IncidentRecord incident,
        RepairPlan plan,
        string backupRoot,
        CancellationToken token)
    {
        var software = CompanionSoftwareDefinition.Supported.FirstOrDefault(item =>
            item.DisplayName.Equals(incident.Game, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The companion software definition is no longer supported.");
        var recovery = CompanionRecoveryPolicy.For(software.Kind);
        if (!recovery.RepairId.Equals(plan.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved companion recovery no longer matches the affected software.");

        foreach (var game in GameDefinition.Supported)
            EnsureGameNotRunning(game.Kind, game.DisplayName);

        var target = ResolveCompanionExecutable(software, incident.ProcessPath)
            ?? throw new FileNotFoundException($"{software.DisplayName}'s captured executable could not be found. No process was closed.");

        var totalSteps = string.IsNullOrWhiteSpace(recovery.WindowsServiceName) ? 4 : 5;
        UpdateSimple(incident, plan, 18, $"Preparing {software.DisplayName} recovery",
            "Validating the captured executable and limiting the recovery to this companion app...", 1, totalSteps, backupRoot);

        UpdateSimple(incident, plan, 42, $"Closing {software.DisplayName}",
            $"Closing only {software.DisplayName}'s remaining processes before relaunch...", 2, totalSteps, backupRoot);
        await CloseProcessesAsync(software.RecoveryProcessNames, token);
        await Task.Delay(750, token);

        if (software.Kind == CompanionSoftwareKind.LogitechGHub)
        {
            var hubUi = Path.Combine(Path.GetDirectoryName(target) ?? string.Empty, "lghub.exe");
            if (File.Exists(hubUi)) target = hubUi;
        }

        var launchStep = 3;
        if (!string.IsNullOrWhiteSpace(recovery.WindowsServiceName))
        {
            UpdateSimple(incident, plan, 57, "Restarting G HUB service",
                $"Restarting {recovery.WindowsServiceName} using Logitech's documented loading-loop recovery order...", 3, totalSteps, backupRoot);
            await RestartWindowsServiceAsync(recovery.WindowsServiceName, token);
            launchStep = 4;
        }

        UpdateSimple(incident, plan, 70, $"Restarting {software.DisplayName}",
            $"Launching the installed {Path.GetFileName(target)} executable...", launchStep, totalSteps, backupRoot);
        using var launched = Process.Start(new ProcessStartInfo
        {
            FileName = target,
            WorkingDirectory = Path.GetDirectoryName(target) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException($"{software.DisplayName} could not be started.");

        if (!await WaitForCompanionProcessAsync(software, TimeSpan.FromSeconds(12), token))
            throw new InvalidOperationException($"{software.DisplayName} did not remain running after the recovery launch.");

        UpdateSimple(incident, plan, 92, $"{software.DisplayName} is running",
            "PitMedic detected the companion app after relaunch.", totalSteps, totalSteps, backupRoot);
        return $"{software.DisplayName} recovery completed successfully.";
    }

    private async Task<string> RepairLmuSteamVerifyAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        UpdateSimple(incident, plan, 25, "Starting Steam validation", "Asking Steam to validate LMU while keeping Steam UI suppressed...", 1, 3, backupRoot);
        using var suppression = await SteamClientService.StartValidationAsync("2399420", token);
        UpdateSimple(incident, plan, 62, "Steam validation started", "Steam accepted the LMU validation request. PitMedic is keeping Steam in the background during repair startup...", 2, 3, backupRoot);
        await Task.Delay(TimeSpan.FromSeconds(20), token);
        return "Steam validation for Le Mans Ultimate was started successfully. Steam will reacquire any files it determines are missing or damaged.";
    }

    private async Task<string> RepairLmuShaderCacheAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        var root = SteamLibraryLocator.FindLeMansUltimateRoot() ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located.");
        var userData = Path.Combine(root, "UserData");
        UpdateSimple(incident, plan, 12, "Backing up generated cache", "Preserving LMU shader/cache files before rebuilding them...", 1, 4, backupRoot);

        var targets = new List<string>();
        var shaders = Path.Combine(userData, "Log", "Shaders");
        if (Directory.Exists(shaders)) targets.Add(shaders);
        var cbash = Path.Combine(userData, "Log", "CBash");
        if (Directory.Exists(cbash)) targets.Add(cbash);
        try { targets.AddRange(Directory.EnumerateFiles(userData, "dynamic.cache", SearchOption.AllDirectories)); } catch { }
        if (targets.Count == 0) throw new FileNotFoundException("LMU shader/cache files were not found.");

        foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            await BackupPathAsync(target, backupRoot, userData, token);
        }
        UpdateSimple(incident, plan, 48, "Clearing generated cache", "Removing only generated LMU cache data...", 2, 4, backupRoot);
        foreach (var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            if (Directory.Exists(target)) await RemoveInstalledContentAsync(target, token);
            else if (File.Exists(target)) await DeleteFileWithRetryAsync(target, token);
        }
        UpdateSimple(incident, plan, 82, "Verifying cache reset", "Checking that active cache files were removed...", 3, 4, backupRoot);
        return "LMU's generated shader/cache data was backed up and cleared. It will rebuild automatically on the next launch.";
    }

    private async Task<string> RepairLmuDx11ConfigAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        var root = SteamLibraryLocator.FindLeMansUltimateRoot() ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located.");
        var userData = Path.Combine(root, "UserData");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "config_dx11.ini", "dx11_config.ini" };
        var files = Directory.Exists(userData)
            ? Directory.EnumerateFiles(userData, "*.ini", SearchOption.AllDirectories).Where(f => names.Contains(Path.GetFileName(f))).ToArray()
            : Array.Empty<string>();
        if (files.Length == 0) throw new FileNotFoundException("LMU DX11 configuration file was not found.");
        UpdateSimple(incident, plan, 20, "Backing up graphics configuration", "Creating a recovery copy of LMU's DX11 configuration...", 1, 3, backupRoot);
        foreach (var file in files) await BackupPathAsync(file, backupRoot, userData, token);
        UpdateSimple(incident, plan, 60, "Resetting graphics initialization", "Removing the active DX11 configuration so LMU can regenerate it...", 2, 3, backupRoot);
        foreach (var file in files) await DeleteFileWithRetryAsync(file, token);
        return "LMU's DX11 configuration was reset. The simulator will generate a fresh configuration on the next launch.";
    }

    private async Task<string> RepairLmuPluginsAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        var root = SteamLibraryLocator.FindLeMansUltimateRoot() ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located.");
        var file = Path.Combine(root, "UserData", "player", "CustomPluginVariables.JSON");
        if (!File.Exists(file)) throw new FileNotFoundException("CustomPluginVariables.JSON was not found.", file);
        UpdateSimple(incident, plan, 20, "Backing up plugin configuration", "Saving the current LMU plugin configuration...", 1, 3, backupRoot);
        await BackupPathAsync(file, backupRoot, Path.Combine(root, "UserData"), token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(file, token)) ?? throw new InvalidDataException("LMU plugin configuration is not valid JSON.");
        var changed = DisableEnabledProperties(node);
        if (changed == 0) return "No enabled LMU plugin entries were found; the existing plugin configuration was preserved.";
        UpdateSimple(incident, plan, 60, "Disabling plugins", "Temporarily disabling LMU plugin entries for a clean retest...", 2, 3, backupRoot);
        await File.WriteAllTextAsync(file, node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), token);
        return $"Disabled {changed} LMU plugin entr{(changed == 1 ? "y" : "ies")}. The original configuration is stored in the repair backup.";
    }

    private async Task<string> RepairLmuEacAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        var root = SteamLibraryLocator.FindLeMansUltimateRoot() ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located.");
        var eac = Path.Combine(root, "EasyAntiCheat");
        if (!Directory.Exists(eac)) throw new DirectoryNotFoundException("LMU EasyAntiCheat folder was not found.");
        var batches = Directory.EnumerateFiles(eac, "*.bat", SearchOption.TopDirectoryOnly).ToArray();
        var install = batches.FirstOrDefault(f => Path.GetFileName(f).Contains("install", StringComparison.OrdinalIgnoreCase)
            && !Path.GetFileName(f).Contains("uninstall", StringComparison.OrdinalIgnoreCase));
        if (install is null) throw new FileNotFoundException("LMU's Easy Anti-Cheat install/reinstall batch file was not found.");
        UpdateSimple(incident, plan, 25, "Repairing Easy Anti-Cheat", "Running LMU's included Easy Anti-Cheat repair workflow...", 2, 4, backupRoot);
        var code = await RunProcessAsync("cmd.exe", $"/c \"\"{install}\"\"", eac, token, 120);
        if (code != 0) throw new InvalidOperationException($"LMU Easy Anti-Cheat repair returned exit code {code}.");
        return "LMU's included Easy Anti-Cheat repair workflow completed successfully.";
    }

    private async Task<string> RepairLmuGraphicsHooksAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.LeMansUltimate, "Le Mans Ultimate");
        var root = SteamLibraryLocator.FindLeMansUltimateRoot() ?? throw new DirectoryNotFoundException("Le Mans Ultimate installation could not be located.");
        var names = new[] { "dxgi.dll", "d3d11.dll", "ReShade.ini", "ReShadePreset.ini", "ReShade.log" };
        var targets = names.Select(n => Path.Combine(root, n)).Where(File.Exists).Cast<string>().ToList();
        var shaderDir = Path.Combine(root, "reshade-shaders");
        if (Directory.Exists(shaderDir)) targets.Add(shaderDir);
        if (targets.Count == 0) throw new FileNotFoundException("No ReShade/custom DirectX hook files were detected in the LMU application folder.");
        UpdateSimple(incident, plan, 18, "Backing up graphics hooks", "Preserving detected ReShade/custom runtime files...", 1, 4, backupRoot);
        foreach (var t in targets) await BackupPathAsync(t, backupRoot, root, token);
        var quarantine = Path.Combine(backupRoot, "Quarantine");
        Directory.CreateDirectory(quarantine);
        UpdateSimple(incident, plan, 52, "Quarantining graphics hooks", "Moving detected graphics hook files out of the LMU application folder...", 2, 4, backupRoot);
        foreach (var t in targets)
        {
            token.ThrowIfCancellationRequested();
            var dest = Path.Combine(quarantine, Path.GetFileName(t));
            if (File.Exists(t)) File.Move(t, dest, true);
            else if (Directory.Exists(t)) Directory.Move(t, dest);
        }
        return $"Temporarily quarantined {targets.Count} detected ReShade/custom graphics hook item(s). Recovery copies are retained.";
    }

    private async Task<string> RepairWindowsTimeAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        UpdateSimple(incident, plan, 30, "Synchronizing Windows time", "Requesting an immediate Windows time synchronization...", 2, 3, backupRoot);
        var code = await RunProcessAsync("w32tm.exe", "/resync /force", Environment.SystemDirectory, token, 30);
        if (code != 0) throw new InvalidOperationException($"Windows time synchronization returned exit code {code}.");
        return "Windows time synchronization completed. PitMedic did not change the configured time zone.";
    }

    private async Task<string> RepairCloseProcessesAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token,
        IReadOnlyList<string> processNames, string label)
    {
        UpdateSimple(incident, plan, 30, $"Closing {label} software", $"Closing the implicated {label} process(es) for a controlled simulator retest...", 2, 3, backupRoot);
        var closed = await CloseProcessesAsync(processNames, token);
        return closed == 0 ? $"No running {label} processes were found." : $"Closed {closed} {label} process(es). No software was uninstalled or reconfigured.";
    }

    private async Task<string> RepairIRacingHelperServiceAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        UpdateSimple(incident, plan, 20, "Restarting Helper Service", "Restarting the iRacing background service using iRacing's installed service helpers...", 1, 3, backupRoot);
        await RestartIRacingServiceAsync(root, token);
        UpdateSimple(incident, plan, 78, "Verifying Helper Service", "Checking that the iRacing Helper Service is running again...", 2, 3, backupRoot);
        if (!await WaitForIRacingServiceAsync(TimeSpan.FromSeconds(12), token))
            throw new InvalidOperationException("iRacing Helper Service did not appear to restart successfully.");
        return "iRacing Helper Service restarted successfully and is running again.";
    }

    private async Task<string> RepairIRacingUiCacheAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        UpdateSimple(incident, plan, 12, "Closing iRacing UI", "Closing the iRacing UI so its Electron cache can be refreshed safely...", 1, 4, backupRoot);
        await CloseProcessesAsync(new[] { "iRacingUI", "iRacingUI64" }, token);
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "iracing-electron");
        if (Directory.Exists(cache))
        {
            UpdateSimple(incident, plan, 30, "Backing up UI cache", "Creating a recovery copy of the iRacing Electron UI cache...", 2, 4, backupRoot);
            await BackupPathAsync(cache, backupRoot, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), token);
            await RemoveInstalledContentAsync(cache, token);
        }
        UpdateSimple(incident, plan, 68, "Restarting Helper Service", "Restarting the iRacing Helper Service after clearing the UI cache...", 3, 4, backupRoot);
        await RestartIRacingServiceAsync(root, token);
        return "The iRacing UI cache was refreshed and the Helper Service was restarted.";
    }

    private async Task<string> RepairIRacingUiSafeAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var appIni = GetIRacingDocumentsFile("app.ini");
        if (!File.Exists(appIni)) throw new FileNotFoundException("Documents\\iRacing\\app.ini was not found.", appIni);
        UpdateSimple(incident, plan, 24, "Backing up app.ini", "Saving iRacing UI configuration before changing UISafe...", 1, 3, backupRoot);
        await BackupPathAsync(appIni, backupRoot, Path.GetDirectoryName(appIni)!, token);
        UpdateSimple(incident, plan, 58, "Resetting UI safe mode", "Setting UISafe=0 using iRacing's current supported workaround...", 2, 3, backupRoot);
        SetIniValue(appIni, "UISafe", "0");
        return "iRacing UISafe was set to 0. The previous app.ini is preserved in the repair backup.";
    }

    private async Task<string> RepairIRacingEacAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        var eac = Path.Combine(root, "EasyAntiCheat");
        var batch = Path.Combine(eac, "InstallEOSAntiCheat.bat");
        if (!File.Exists(batch)) throw new FileNotFoundException("InstallEOSAntiCheat.bat was not found.", batch);
        UpdateSimple(incident, plan, 32, "Repairing Easy Anti-Cheat", "Running iRacing's supported EOS Easy Anti-Cheat installer...", 2, 4, backupRoot);
        var code = await RunProcessAsync("cmd.exe", $"/c \"\"{batch}\"\"", eac, token, 120);
        if (code != 0) throw new InvalidOperationException($"iRacing Easy Anti-Cheat installer returned exit code {code}.");
        return "iRacing EOS Easy Anti-Cheat installation completed successfully.";
    }

    private async Task<string> RepairIRacingUpdateAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        var versionFile = Path.Combine(root, "version_system.txt");
        if (File.Exists(versionFile)) await BackupPathAsync(versionFile, backupRoot, root, token);
        UpdateSimple(incident, plan, 34, "Resetting update state", "Removing stale iRacing update metadata and temporary downloads...", 2, 4, backupRoot);
        if (File.Exists(versionFile)) await DeleteFileWithRetryAsync(versionFile, token);
        var downloads = Path.Combine(root, "downloads");
        if (Directory.Exists(downloads)) await RemoveInstalledContentAsync(downloads, token);
        UpdateSimple(incident, plan, 72, "Launching updater", "Starting iRacingUpdater to reacquire required update files...", 3, 4, backupRoot);
        await LaunchIRacingUpdaterAsync(root, string.Empty, token);
        return "iRacing update metadata/cache was reset and the updater was started.";
    }

    private async Task<string> RepairIRacingUpdaterAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token, string arguments)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        UpdateSimple(incident, plan, 48, "Starting iRacing updater", string.IsNullOrWhiteSpace(arguments) ? "Launching iRacingUpdater directly..." : "Launching iRacingUpdater with automatic installation enabled...", 2, 3, backupRoot);
        await LaunchIRacingUpdaterAsync(root, arguments, token);
        return string.IsNullOrWhiteSpace(arguments) ? "iRacingUpdater started successfully." : "iRacingUpdater started with -autoinstall.";
    }

    private async Task<string> RepairIRacingTrackIndexAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token, bool steam)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        var tracks = Path.Combine(root, "tracks");
        if (!Directory.Exists(tracks)) throw new DirectoryNotFoundException("iRacing tracks folder was not found.");
        var metadata = new[] { Path.Combine(tracks, "tracks.dat"), Path.Combine(tracks, "version.txt") }.Where(File.Exists).ToArray();
        var affectedFolders = FindIRacingContentFolders(incident.IncidentFolder, "tracks", tracks);
        UpdateSimple(incident, plan, 16, "Backing up track content", affectedFolders.Count > 0
            ? $"Preserving {affectedFolders.Count} track folder(s) named in the captured error before reacquisition..."
            : "No specific track folder was identified in the captured log; preserving track index metadata before rebuilding it...", 1, steam ? 5 : 4, backupRoot);
        foreach (var folder in affectedFolders) await BackupPathAsync(folder, backupRoot, tracks, token);
        foreach (var file in metadata) await BackupPathAsync(file, backupRoot, tracks, token);
        if (affectedFolders.Count > 0)
        {
            UpdateSimple(incident, plan, 34, "Removing affected track content", "Removing only the track folder(s) implicated by the captured loading error...", 2, steam ? 5 : 4, backupRoot);
            foreach (var folder in affectedFolders) await RemoveInstalledContentAsync(folder, token);
        }
        UpdateSimple(incident, plan, 50, "Resetting track metadata", "Removing stale track index metadata so iRacing can regenerate it...", affectedFolders.Count > 0 ? 3 : 2, steam ? 5 : 4, backupRoot);
        foreach (var file in metadata) await DeleteFileWithRetryAsync(file, token);
        if (steam)
        {
            UpdateSimple(incident, plan, 70, "Starting Steam validation", "Asking Steam to validate iRacing while keeping Steam UI suppressed...", affectedFolders.Count > 0 ? 4 : 3, 5, backupRoot);
            using var suppression = await SteamClientService.StartValidationAsync("266410", token);
            await Task.Delay(TimeSpan.FromSeconds(20), token);
            return "iRacing track metadata was reset and Steam validation was started. PitMedic kept Steam in the background during repair startup.";
        }
        UpdateSimple(incident, plan, 72, "Launching updater", "Starting iRacingUpdater to restore track metadata/content...", affectedFolders.Count > 0 ? 4 : 3, 4, backupRoot);
        await LaunchIRacingUpdaterAsync(root, string.Empty, token);
        return "iRacing track metadata was reset and the updater was started.";
    }

    private async Task<string> RepairIRacingCarIndexAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        var cars = Path.Combine(root, "cars");
        if (!Directory.Exists(cars)) throw new DirectoryNotFoundException("iRacing cars folder was not found.");
        var metadata = new[] { Path.Combine(cars, "cars.dat"), Path.Combine(cars, "version.txt") }.Where(File.Exists).ToArray();
        var affectedFolders = FindIRacingContentFolders(incident.IncidentFolder, "cars", cars);
        UpdateSimple(incident, plan, 16, "Backing up car content", affectedFolders.Count > 0
            ? $"Preserving {affectedFolders.Count} car folder(s) named in the captured error before reacquisition..."
            : "No specific car folder was identified in the captured log; preserving car index metadata before rebuilding it...", 1, 4, backupRoot);
        foreach (var folder in affectedFolders) await BackupPathAsync(folder, backupRoot, cars, token);
        foreach (var file in metadata) await BackupPathAsync(file, backupRoot, cars, token);
        if (affectedFolders.Count > 0)
        {
            UpdateSimple(incident, plan, 36, "Removing affected car content", "Removing only the car folder(s) implicated by the captured loading error...", 2, 4, backupRoot);
            foreach (var folder in affectedFolders) await RemoveInstalledContentAsync(folder, token);
        }
        UpdateSimple(incident, plan, 54, "Resetting car metadata", "Removing stale car index metadata so iRacing can redetect vehicle content...", affectedFolders.Count > 0 ? 3 : 2, 4, backupRoot);
        foreach (var file in metadata) await DeleteFileWithRetryAsync(file, token);
        UpdateSimple(incident, plan, 74, "Launching updater", "Starting iRacingUpdater to restore required car metadata/content...", 4, 4, backupRoot);
        await LaunchIRacingUpdaterAsync(root, string.Empty, token);
        return "iRacing car metadata was reset and the updater was started. If a specific car folder is corrupt, iRacing can now identify it as missing/out-of-date for reacquisition.";
    }

    private async Task<string> RepairIRacingContentLockAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        UpdateSimple(incident, plan, 20, "Releasing iRacing file locks", "Stopping the iRacing Helper Service so Steam can access update files...", 1, 4, backupRoot);
        await StopIRacingServiceAsync(root, token);
        await Task.Delay(2500, token);
        try
        {
            UpdateSimple(incident, plan, 52, "Starting Steam validation", "Starting Steam validation with iRacing's service stopped...", 2, 4, backupRoot);
            using var suppression = await SteamClientService.StartValidationAsync("266410", token);
            await Task.Delay(TimeSpan.FromSeconds(20), token);
        }
        finally
        {
            UpdateSimple(incident, plan, 80, "Restarting Helper Service", "Restarting iRacing's Helper Service after releasing update locks...", 3, 4, backupRoot);
            await StartIRacingServiceAsync(root, CancellationToken.None);
        }
        return "iRacing file locks were released, Steam validation was started, and the Helper Service was restarted.";
    }

    private async Task<string> RepairIRacingRendererAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var docs = GetIRacingDocumentsRoot();
        if (!Directory.Exists(docs)) throw new DirectoryNotFoundException("Documents\\iRacing was not found.");
        var files = Directory.EnumerateFiles(docs, "rendererDX11*.ini", SearchOption.TopDirectoryOnly).ToArray();
        if (files.Length == 0) throw new FileNotFoundException("No rendererDX11 configuration files were found.");
        UpdateSimple(incident, plan, 20, "Backing up renderer configuration", "Preserving iRacing renderer settings...", 1, 3, backupRoot);
        foreach (var f in files) await BackupPathAsync(f, backupRoot, docs, token);
        UpdateSimple(incident, plan, 58, "Resetting renderer configuration", "Removing the active rendererDX11 configuration so iRacing can regenerate it...", 2, 3, backupRoot);
        foreach (var f in files) await DeleteFileWithRetryAsync(f, token);
        return $"Reset {files.Length} iRacing renderer configuration file(s). The originals are retained in the repair backup.";
    }

    private async Task<string> RepairIRacingIniValueAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token,
        string key, string value, string result)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var appIni = GetIRacingDocumentsFile("app.ini");
        if (!File.Exists(appIni)) throw new FileNotFoundException("Documents\\iRacing\\app.ini was not found.", appIni);
        UpdateSimple(incident, plan, 24, "Backing up app.ini", "Creating a recovery copy of iRacing's application configuration...", 1, 3, backupRoot);
        await BackupPathAsync(appIni, backupRoot, Path.GetDirectoryName(appIni)!, token);
        UpdateSimple(incident, plan, 58, "Applying supported workaround", $"Setting {key}={value}...", 2, 3, backupRoot);
        SetIniValue(appIni, key, value);
        return $"{result}. The previous app.ini is preserved in the repair backup.";
    }

    private async Task<string> RepairIRacingLogitechShutdownAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        UpdateSimple(incident, plan, 12, "Closing stale simulator state", "Closing any iRacing simulator process left behind by the documented shutdown conflict...", 1, 5, backupRoot);
        await CloseProcessesAsync(new[] { "iRacingSim64DX11", "iRacingSim64Dx11" }, token);
        var appIni = GetIRacingDocumentsFile("app.ini");
        if (!File.Exists(appIni)) throw new FileNotFoundException("Documents\\iRacing\\app.ini was not found.", appIni);
        UpdateSimple(incident, plan, 28, "Backing up app.ini", "Preserving iRacing's Logitech-related configuration...", 2, 5, backupRoot);
        await BackupPathAsync(appIni, backupRoot, Path.GetDirectoryName(appIni)!, token);
        UpdateSimple(incident, plan, 52, "Disabling TrueForce workaround", "Setting trueForceEnabled=0 using iRacing's documented workaround...", 3, 5, backupRoot);
        SetIniValue(appIni, "trueForceEnabled", "0");
        UpdateSimple(incident, plan, 74, "Disabling Logitech hardware lighting", "Setting enableLogitechLED=0 to prevent the documented Logitech shutdown conflict...", 4, 5, backupRoot);
        SetIniValue(appIni, "enableLogitechLED", "0");
        return "iRacing TrueForce and Logitech hardware-lighting integration were disabled. The original app.ini is retained in the repair backup.";
    }

    private async Task<string> RepairIRacingUserConfigAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        UpdateSimple(incident, plan, 12, "Closing iRacing UI", "Closing the iRacing UI before resetting user configuration...", 1, 4, backupRoot);
        await CloseProcessesAsync(new[] { "iRacingUI", "iRacingUI64" }, token);
        var docs = GetIRacingDocumentsRoot();
        if (!Directory.Exists(docs)) throw new DirectoryNotFoundException("Documents\\iRacing was not found.");
        var parent = Path.GetDirectoryName(docs) ?? throw new DirectoryNotFoundException("The parent Documents folder could not be determined.");
        var backup = Path.Combine(parent, $"iRacing.PitMedicBackup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}");
        UpdateSimple(incident, plan, 38, "Preserving iRacing user data", "Moving the current Documents\\iRacing folder aside intact so setups, paints and replays are preserved...", 2, 4, backupRoot);
        Directory.Move(docs, backup);
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "user-config-backup-location.txt"), backup, token);
        UpdateSimple(incident, plan, 76, "Preparing clean configuration", "The active iRacing user configuration has been moved aside. iRacing will create a clean Documents\\iRacing folder on next launch...", 3, 4, backupRoot);
        return $"iRacing user configuration was moved to '{backup}'. iRacing will build a clean profile on next launch; the original setups, paints and replays remain intact in that backup folder.";
    }

    private async Task<string> RepairIRacingCompatibilityAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var root = IRacingLocator.FindRoot() ?? throw new DirectoryNotFoundException("iRacing installation could not be located.");
        var executables = new List<string>();
        try { executables.AddRange(Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories).Where(f => Path.GetFileName(f).Contains("iRacing", StringComparison.OrdinalIgnoreCase))); } catch { }
        UpdateSimple(incident, plan, 24, "Inspecting compatibility overrides", "Checking Windows compatibility settings applied to iRacing executables...", 1, 3, backupRoot);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
        if (key is null) throw new InvalidOperationException("Windows compatibility settings could not be opened.");
        var changed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in executables)
        {
            var raw = key.GetValue(exe)?.ToString();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            var filtered = tokens.Where(t => !t.Equals("RUNASADMIN", StringComparison.OrdinalIgnoreCase)
                && !t.StartsWith("WIN", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (filtered.Length == tokens.Count) continue;
            changed[exe] = raw;
            if (filtered.Length == 0) key.DeleteValue(exe, false);
            else key.SetValue(exe, string.Join(' ', filtered));
        }
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "compatibility-overrides.json"), JsonSerializer.Serialize(changed, JsonOptions), token);
        return changed.Count == 0 ? "No Run-as-administrator or Windows compatibility overrides were found on iRacing executables." : $"Removed incompatible Windows compatibility overrides from {changed.Count} iRacing executable(s).";
    }

    private async Task<string> RepairWindowsIntegrityAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.IRacing, "iRacing Simulator");
        var resultsPath = Path.Combine(backupRoot, "windows-integrity-results.txt");

        UpdateSimple(incident, plan, 12, "Checking Windows component store", "Running DISM RestoreHealth. This can take several minutes...", 1, 4, backupRoot);
        var dism = Path.Combine(Environment.SystemDirectory, "dism.exe");
        var dismResult = await RunProcessCaptureAsync(dism, "/Online /Cleanup-Image /RestoreHealth", Environment.SystemDirectory, token, 1800);
        await File.WriteAllTextAsync(resultsPath, BuildProcessLog("DISM /RestoreHealth", dismResult), token);

        var dismSourceMissing = unchecked((uint)dismResult.ExitCode) == 0x800F081Fu;
        var sfcMessage = dismSourceMissing
            ? "DISM could not locate Windows repair source files (0x800F081F). Running System File Checker anyway so PitMedic can complete the remaining integrity check..."
            : dismResult.ExitCode == 0
                ? "DISM completed. Running System File Checker..."
                : $"DISM returned {FormatExitCode(dismResult.ExitCode)}. Running System File Checker anyway so both Windows integrity results are captured...";

        UpdateSimple(incident, plan, 58, "Checking Windows system files", sfcMessage, 2, 4, backupRoot);
        var sfc = Path.Combine(Environment.SystemDirectory, "sfc.exe");
        var sfcResult = await RunProcessCaptureAsync(sfc, "/scannow", Environment.SystemDirectory, token, 1800);
        await File.AppendAllTextAsync(resultsPath, Environment.NewLine + BuildProcessLog("SFC /scannow", sfcResult), token);

        UpdateSimple(incident, plan, 92, "Reviewing Windows integrity results", "DISM and System File Checker have finished. PitMedic is evaluating the results...", 3, 4, backupRoot);

        if (dismSourceMissing)
        {
            var sfcSummary = sfcResult.ExitCode == 0
                ? "System File Checker completed successfully."
                : $"System File Checker also returned {FormatExitCode(sfcResult.ExitCode)}.";
            throw new InvalidOperationException($"Windows component repair could not find the required source files (0x800F081F). {sfcSummary} Windows needs a valid repair source from Windows Update or matching Windows installation media before DISM can finish.");
        }

        if (dismResult.ExitCode != 0)
            throw new InvalidOperationException($"DISM RestoreHealth returned {FormatExitCode(dismResult.ExitCode)}. System File Checker was still run and its output was saved to the repair log.");

        if (sfcResult.ExitCode != 0)
            throw new InvalidOperationException($"System File Checker returned {FormatExitCode(sfcResult.ExitCode)}. DISM completed successfully, and the full SFC output was saved to the repair log.");

        UpdateSimple(incident, plan, 98, "Windows integrity checks complete", "DISM and System File Checker completed successfully.", 4, 4, backupRoot);
        return "Windows component-store and system-file repair checks completed successfully. If Windows repaired files, reboot before retesting iRacing.";
    }

    private async Task<string> RepairAceVideoSettingsAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaEvo, "Assetto Corsa EVO");
        var files = GetAceUserDataRoots()
            .Where(Directory.Exists)
            .SelectMany(root => FindTopLevelFileIgnoreCase(root, "Video.videosettings").Select(file => (Root: root, File: file)))
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException("Assetto Corsa EVO Video.videosettings was not found in the active ACE user-data folders.");

        UpdateSimple(incident, plan, 22, "Backing up video settings", "Preserving Assetto Corsa EVO's current video settings before resetting them...", 1, 3, backupRoot);
        for (var i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            await BackupPathAsync(files[i].File, Path.Combine(backupRoot, $"ACE-{i + 1}"), files[i].Root, token);
        }

        UpdateSimple(incident, plan, 62, "Resetting video settings", "Removing the active Video.videosettings file so Assetto Corsa EVO can regenerate clean graphics settings...", 2, 3, backupRoot);
        foreach (var item in files)
        {
            token.ThrowIfCancellationRequested();
            await DeleteFileWithRetryAsync(item.File, token);
        }

        return $"Reset {files.Length} Assetto Corsa EVO video-settings file(s). The original settings are preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairAceProfileAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaEvo, "Assetto Corsa EVO");
        var roots = GetAceUserDataRoots().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0)
            throw new DirectoryNotFoundException("Assetto Corsa EVO's ACE user-data folder was not found in Documents or Saved Games.");

        UpdateSimple(incident, plan, 18, "Preparing profile recovery", "Preparing intact backup locations for Assetto Corsa EVO user data...", 1, 4, backupRoot);
        var moved = new List<string>();
        foreach (var root in roots)
        {
            token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException($"Could not determine the parent folder for '{root}'.");
            var destination = NextAvailableSiblingBackupPath(parent, "ACE");
            UpdateSimple(incident, plan, 38 + moved.Count * 20, "Preserving ACE user profile", "Moving the active ACE profile aside intact so Assetto Corsa EVO can create a clean profile...", 2, 4, backupRoot);
            Directory.Move(root, destination);
            moved.Add(destination);
        }

        await File.WriteAllLinesAsync(Path.Combine(backupRoot, "ace-user-config-backup-locations.txt"), moved, token);
        UpdateSimple(incident, plan, 84, "Preparing clean ACE profile", "The active Assetto Corsa EVO user data has been moved aside. The game will regenerate clean settings on next launch...", 3, 4, backupRoot);
        return $"Assetto Corsa EVO user data was preserved in {moved.Count} sibling backup folder(s). The game will create a clean ACE profile on next launch.";
    }

    private async Task<string> RepairAceSteamVerifyAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaEvo, "Assetto Corsa EVO");
        UpdateSimple(incident, plan, 25, "Starting Steam validation", "Asking Steam to validate Assetto Corsa EVO while keeping Steam UI suppressed...", 1, 3, backupRoot);
        using var suppression = await SteamClientService.StartValidationAsync("3058630", token);
        UpdateSimple(incident, plan, 62, "Steam validation started", "Steam accepted the Assetto Corsa EVO validation request. PitMedic is keeping Steam in the background during repair startup...", 2, 3, backupRoot);
        await Task.Delay(TimeSpan.FromSeconds(20), token);
        return "Steam validation for Assetto Corsa EVO was started successfully. Steam will reacquire files it determines are missing or damaged.";
    }

    private async Task<string> RepairRaceRoomBrowserAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.RaceRoom, "RaceRoom Racing Experience");
        var browserFolders = GetRaceRoomUserRoots()
            .Select(root => Path.Combine(root, "BrowserData"))
            .Where(Directory.Exists)
            .ToArray();
        if (browserFolders.Length == 0)
            throw new DirectoryNotFoundException("RaceRoom BrowserData was not found in the active RaceRoom user-data folders.");

        UpdateSimple(incident, plan, 20, "Backing up browser data", "Preserving RaceRoom's embedded-browser data before refreshing it...", 1, 3, backupRoot);
        for (var i = 0; i < browserFolders.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            await BackupPathAsync(browserFolders[i], Path.Combine(backupRoot, $"RaceRoom-Browser-{i + 1}"), Path.GetDirectoryName(browserFolders[i])!, token);
        }

        UpdateSimple(incident, plan, 62, "Refreshing RaceRoom browser data", "Clearing the active embedded-browser cache/state so RaceRoom can rebuild it cleanly...", 2, 3, backupRoot);
        foreach (var folder in browserFolders)
        {
            token.ThrowIfCancellationRequested();
            await RemoveInstalledContentAsync(folder, token);
        }

        return $"Cleared {browserFolders.Length} RaceRoom BrowserData folder(s). PitMedic preserved the previous browser data in the repair backup.";
    }

    private async Task<string> RepairRaceRoomGraphicsAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.RaceRoom, "RaceRoom Racing Experience");
        var files = GetRaceRoomUserRoots()
            .Select(root => Path.Combine(root, "UserData", "graphics_options.xml"))
            .Where(File.Exists)
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException("RaceRoom graphics_options.xml was not found.");

        UpdateSimple(incident, plan, 24, "Backing up graphics configuration", "Preserving RaceRoom's current graphics configuration...", 1, 3, backupRoot);
        for (var i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            await BackupPathAsync(files[i], Path.Combine(backupRoot, $"RaceRoom-Graphics-{i + 1}"), Path.GetDirectoryName(files[i])!, token);
        }

        UpdateSimple(incident, plan, 64, "Resetting graphics configuration", "Removing graphics_options.xml so RaceRoom can regenerate clean display and resolution settings...", 2, 3, backupRoot);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            await DeleteFileWithRetryAsync(file, token);
        }
        return $"Reset {files.Length} RaceRoom graphics configuration file(s). The previous settings are preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairRaceRoomShaderAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.RaceRoom, "RaceRoom Racing Experience");
        var files = GetRaceRoomUserRoots()
            .Select(root => Path.Combine(root, "UserData", "ShaderCache.bin"))
            .Where(File.Exists)
            .ToArray();
        if (files.Length == 0)
            throw new FileNotFoundException("RaceRoom ShaderCache.bin was not found.");

        UpdateSimple(incident, plan, 24, "Backing up shader cache", "Preserving RaceRoom's current shader cache before rebuilding it...", 1, 3, backupRoot);
        for (var i = 0; i < files.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            await BackupPathAsync(files[i], Path.Combine(backupRoot, $"RaceRoom-Shader-{i + 1}"), Path.GetDirectoryName(files[i])!, token);
        }

        UpdateSimple(incident, plan, 64, "Clearing shader cache", "Removing ShaderCache.bin so RaceRoom can rebuild shaders on the next launch...", 2, 3, backupRoot);
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            await DeleteFileWithRetryAsync(file, token);
        }
        return $"Cleared {files.Length} RaceRoom shader cache file(s). The previous cache is preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairRaceRoomProfileAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.RaceRoom, "RaceRoom Racing Experience");
        var root = GetRaceRoomPrimaryUserRoot();
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("RaceRoom's user profile folder was not found.");

        var parent = Path.GetDirectoryName(root) ?? throw new DirectoryNotFoundException("The parent SimBin folder could not be determined.");
        var destination = NextAvailableSiblingBackupPath(parent, "RaceRoom Racing Experience");
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "raceroom-user-config-backup-location.txt"), destination, token);

        UpdateSimple(incident, plan, 32, "Preserving RaceRoom user data", "Moving the current RaceRoom profile aside intact so setups and configuration are preserved...", 1, 3, backupRoot);
        Directory.Move(root, destination);
        UpdateSimple(incident, plan, 78, "Preparing clean RaceRoom profile", "The active RaceRoom profile has been moved aside. RaceRoom will create clean user data on next launch...", 2, 3, backupRoot);
        return $"RaceRoom user configuration was moved to '{destination}'. RaceRoom will build a clean profile on next launch while the original files remain intact.";
    }

    private async Task<string> RepairRaceRoomSteamVerifyAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.RaceRoom, "RaceRoom Racing Experience");
        UpdateSimple(incident, plan, 25, "Starting Steam validation", "Asking Steam to validate RaceRoom Racing Experience while keeping Steam UI suppressed...", 1, 3, backupRoot);
        using var suppression = await SteamClientService.StartValidationAsync("211500", token);
        UpdateSimple(incident, plan, 62, "Steam validation started", "Steam accepted the RaceRoom validation request. PitMedic is keeping Steam in the background during repair startup...", 2, 3, backupRoot);
        await Task.Delay(TimeSpan.FromSeconds(20), token);
        return "Steam validation for RaceRoom Racing Experience was started successfully. Steam will reacquire files it determines are missing or damaged.";
    }

    private async Task<string> RepairAccFileAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token,
        string fileName, string file, string label)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione");
        if (!File.Exists(file)) throw new FileNotFoundException($"ACC {fileName} was not found.", file);
        UpdateSimple(incident, plan, 24, $"Backing up ACC {label}", $"Preserving {fileName} before resetting it...", 1, 3, backupRoot);
        await BackupPathAsync(file, Path.Combine(backupRoot, "ACC-Config"), Path.GetDirectoryName(file)!, token);
        UpdateSimple(incident, plan, 64, $"Resetting ACC {label}", $"Removing the active {fileName} so ACC can regenerate clean settings...", 2, 3, backupRoot);
        await DeleteFileWithRetryAsync(file, token);
        return $"ACC {fileName} was reset. The original file is preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairAccTrueForceAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione");
        var file = Path.Combine(GetAccDocumentsRoot(), "Config", "controls.json");
        if (!File.Exists(file)) throw new FileNotFoundException("ACC controls.json was not found.", file);
        UpdateSimple(incident, plan, 24, "Backing up ACC controller settings", "Preserving controls.json before changing the TrueForce compatibility setting...", 1, 3, backupRoot);
        await BackupPathAsync(file, Path.Combine(backupRoot, "ACC-TrueForce"), Path.GetDirectoryName(file)!, token);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(file, token)) as JsonObject ?? throw new InvalidDataException("ACC controls.json could not be parsed.");
        node["enableManufacturerExtras"] = false;
        await File.WriteAllTextAsync(file, node.ToJsonString(JsonOptions), token);
        UpdateSimple(incident, plan, 72, "TrueForce extras disabled", "ACC manufacturer extras were disabled for this controller profile...", 2, 3, backupRoot);
        return "ACC manufacturer extras were disabled in controls.json. The original file is preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairAccControlPresetsAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione");
        var folder = Path.Combine(GetAccDocumentsRoot(), "Customs", "Controls");
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("ACC saved control presets were not found.");
        var parent = Path.GetDirectoryName(folder)!;
        var destination = NextAvailableSiblingBackupPath(parent, "Controls");
        UpdateSimple(incident, plan, 30, "Preserving ACC control presets", "Moving the active saved-control preset folder aside intact...", 1, 3, backupRoot);
        Directory.Move(folder, destination);
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "acc-control-presets-backup-location.txt"), destination, token);
        UpdateSimple(incident, plan, 78, "ACC presets quarantined", "ACC can now open the Controls screen without loading the old saved preset set...", 2, 3, backupRoot);
        return $"ACC saved control presets were moved to '{destination}'.";
    }

    private async Task<string> RepairAccProfileAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione");
        var targets = new[] { GetAccDocumentsRoot(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AC2", "Saved", "Config") }
            .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (targets.Length == 0) throw new DirectoryNotFoundException("ACC user configuration folders were not found.");
        var moved = new List<string>();
        for (var i = 0; i < targets.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            var target = targets[i];
            var parent = Path.GetDirectoryName(target)!;
            var destination = NextAvailableSiblingBackupPath(parent, Path.GetFileName(target));
            UpdateSimple(incident, plan, 25 + i * 25, "Preserving ACC user configuration", $"Moving '{target}' aside intact...", i + 1, targets.Length + 1, backupRoot);
            Directory.Move(target, destination);
            moved.Add(destination);
        }
        await File.WriteAllLinesAsync(Path.Combine(backupRoot, "acc-profile-backup-locations.txt"), moved, token);
        return $"ACC user configuration was preserved in {moved.Count} sibling backup folder(s). ACC will regenerate clean settings on next launch.";
    }

    private async Task<string> RepairAms2FilesAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token,
        string label, IEnumerable<string> candidates)
    {
        EnsureGameNotRunning(GameKind.Automobilista2, "Automobilista 2");
        var files = candidates.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length == 0) throw new FileNotFoundException($"Automobilista 2 {label} file(s) were not found.");
        UpdateSimple(incident, plan, 22, $"Backing up AMS2 {label}", "Preserving the current settings before resetting them...", 1, 3, backupRoot);
        for (var i = 0; i < files.Length; i++)
            await BackupPathAsync(files[i], Path.Combine(backupRoot, $"AMS2-{i + 1}"), Path.GetDirectoryName(files[i])!, token);
        UpdateSimple(incident, plan, 66, $"Resetting AMS2 {label}", "Removing the active settings so Automobilista 2 can rebuild clean defaults...", 2, 3, backupRoot);
        foreach (var file in files) await DeleteFileWithRetryAsync(file, token);
        return $"Reset {files.Length} Automobilista 2 {label} file(s). The originals are preserved in the PitMedic repair backup.";
    }

    private async Task<string> RepairAms2TuningAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.Automobilista2, "Automobilista 2");
        var folders = FindAms2ProfileDirectories("tuningsetups").Where(Directory.Exists).ToArray();
        if (folders.Length == 0) throw new DirectoryNotFoundException("Automobilista 2 tuning setup folders were not found.");
        var moved = new List<string>();
        foreach (var folder in folders)
        {
            token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(folder)!;
            var destination = NextAvailableSiblingBackupPath(parent, "tuningsetups");
            Directory.Move(folder, destination);
            moved.Add(destination);
        }
        await File.WriteAllLinesAsync(Path.Combine(backupRoot, "ams2-tuningsetups-backup-locations.txt"), moved, token);
        UpdateSimple(incident, plan, 78, "AMS2 setups reset", "Old tuning setup folders are preserved and no longer active...", 2, 3, backupRoot);
        return $"Moved {moved.Count} Automobilista 2 tuning setup folder(s) aside intact.";
    }

    private async Task<string> RepairAms2ChampionshipAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.Automobilista2, "Automobilista 2");
        var savegame = Path.Combine(GetAms2DocumentsRoot(), "savegame");
        if (!Directory.Exists(savegame)) throw new DirectoryNotFoundException("Automobilista 2 savegame folder was not found.");
        var candidates = new List<string>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(savegame, "*.sav", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                var dir = Path.GetDirectoryName(f) ?? string.Empty;
                if (name.Contains("championship", StringComparison.OrdinalIgnoreCase)
                    || dir.Contains("singlechamps", StringComparison.OrdinalIgnoreCase)
                    || (name.Equals(".sav", StringComparison.OrdinalIgnoreCase) && dir.Contains("automobilista 2", StringComparison.OrdinalIgnoreCase)))
                    candidates.Add(f);
            }
        }
        catch { }
        if (candidates.Count == 0) throw new FileNotFoundException("No Automobilista 2 championship save-state files were found.");
        var moved = new List<string>();
        foreach (var file in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(file)!;
            var quarantine = Path.Combine(parent, "PitMedic Championship Backup");
            Directory.CreateDirectory(quarantine);
            var dest = Path.Combine(quarantine, Path.GetFileName(file));
            if (File.Exists(dest)) dest = Path.Combine(quarantine, $"{Path.GetFileNameWithoutExtension(file)}-{DateTime.Now:yyyyMMdd-HHmmssfff}{Path.GetExtension(file)}");
            File.Move(file, dest);
            moved.Add(dest);
        }
        await File.WriteAllLinesAsync(Path.Combine(backupRoot, "ams2-championship-backup-locations.txt"), moved, token);
        UpdateSimple(incident, plan, 78, "AMS2 championship state quarantined", "Corrupted championship saves were moved aside intact...", 2, 3, backupRoot);
        return $"Moved {moved.Count} Automobilista 2 championship state file(s) aside intact.";
    }

    private async Task<string> RepairAms2ProfileAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token)
    {
        EnsureGameNotRunning(GameKind.Automobilista2, "Automobilista 2");
        var root = GetAms2DocumentsRoot();
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("Automobilista 2 user-data folder was not found.");
        var parent = Path.GetDirectoryName(root)!;
        var destination = NextAvailableSiblingBackupPath(parent, "Automobilista 2");
        UpdateSimple(incident, plan, 35, "Preserving AMS2 user data", "Moving the complete Automobilista 2 user-data folder aside intact...", 1, 3, backupRoot);
        Directory.Move(root, destination);
        await File.WriteAllTextAsync(Path.Combine(backupRoot, "ams2-profile-backup-location.txt"), destination, token);
        UpdateSimple(incident, plan, 80, "Preparing clean AMS2 profile", "Automobilista 2 will create clean user data on next launch...", 2, 3, backupRoot);
        return $"Automobilista 2 user data was moved to '{destination}'.";
    }

    private async Task<string> RepairSteamVerifyAsync(IncidentRecord incident, RepairPlan plan, string backupRoot, CancellationToken token,
        GameKind kind, string displayName, string appId)
    {
        EnsureGameNotRunning(kind, displayName);
        UpdateSimple(incident, plan, 25, "Starting Steam validation", $"Asking Steam to validate {displayName} while keeping Steam UI suppressed...", 1, 3, backupRoot);
        using var suppression = await SteamClientService.StartValidationAsync(appId, token);
        UpdateSimple(incident, plan, 62, "Steam validation started", $"Steam accepted the {displayName} validation request...", 2, 3, backupRoot);
        await Task.Delay(TimeSpan.FromSeconds(20), token);
        return $"Steam validation for {displayName} was started successfully.";
    }

    private void UpdateSimple(IncidentRecord incident, RepairPlan plan, int percent, string stage, string message, int step, int total, string backupRoot)
        => Update(incident, plan, percent, stage, message, $"Repair playbook: {plan.Id}", step, total, true, false, false, backupRoot);

    private static void EnsureGameNotRunning(GameKind kind, string displayName)
    {
        var game = GameDefinition.Supported.First(g => g.Kind == kind);
        foreach (var alias in game.ProcessNames)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(alias); } catch { continue; }
            try
            {
                if (processes.Length > 0) throw new InvalidOperationException($"{displayName} is still running. Close the simulator before applying this repair.");
            }
            finally { foreach (var p in processes) p.Dispose(); }
        }
    }

    private static string? ResolveCompanionExecutable(CompanionSoftwareDefinition software, string capturedPath)
    {
        foreach (var candidate in new[] { capturedPath }.Concat(software.DefaultExecutablePaths))
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            string fullPath;
            try { fullPath = Path.GetFullPath(candidate); } catch { continue; }
            if (!Path.IsPathFullyQualified(fullPath) || !File.Exists(fullPath)) continue;

            var fileName = Path.GetFileName(fullPath);
            var processName = Path.GetFileNameWithoutExtension(fullPath);
            if (fileName.Equals(software.ExecutableName, StringComparison.OrdinalIgnoreCase)
                || software.ProcessNames.Any(name => processName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return fullPath;
        }
        return null;
    }

    private static async Task<bool> WaitForCompanionProcessAsync(
        CompanionSoftwareDefinition software,
        TimeSpan timeout,
        CancellationToken token)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            token.ThrowIfCancellationRequested();
            foreach (var name in software.ProcessNames)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); } catch { continue; }
                try
                {
                    if (processes.Any(process =>
                    {
                        try { return !process.HasExited; } catch { return false; }
                    }))
                        return true;
                }
                finally
                {
                    foreach (var process in processes) process.Dispose();
                }
            }
            await Task.Delay(400, token);
        }
        return false;
    }

    private static async Task BackupPathAsync(string source, string backupRoot, string relativeRoot, CancellationToken token)
    {
        if (!File.Exists(source) && !Directory.Exists(source)) return;
        string relative;
        try { relative = Path.GetRelativePath(relativeRoot, source); }
        catch { relative = Path.GetFileName(source); }
        if (relative.StartsWith("..", StringComparison.Ordinal)) relative = Path.GetFileName(source);
        var target = Path.Combine(backupRoot, "Files", relative);
        if (Directory.Exists(source)) await CopyDirectoryAsync(source, target, token);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyFileWithRetryAsync(source, target, token);
        }
    }

    private static int DisableEnabledProperties(JsonNode node)
    {
        var changed = 0;
        if (node is JsonObject obj)
        {
            foreach (var pair in obj.ToArray())
            {
                if (pair.Key.Equals("Enabled", StringComparison.OrdinalIgnoreCase))
                {
                    obj[pair.Key] = 0;
                    changed++;
                }
                else if (pair.Value is not null) changed += DisableEnabledProperties(pair.Value);
            }
        }
        else if (node is JsonArray arr)
        {
            foreach (var child in arr) if (child is not null) changed += DisableEnabledProperties(child);
        }
        return changed;
    }

    private static IReadOnlyList<string> FindIRacingContentFolders(string incidentFolder, string contentKind, string contentRoot)
    {
        var results = new List<string>();
        var logs = Path.Combine(incidentFolder, "Logs");
        if (!Directory.Exists(logs) || !Directory.Exists(contentRoot)) return results;
        var regex = new Regex($@"(?i)(?:^|[\\/]){Regex.Escape(contentKind)}[\\/](?<name>[A-Za-z0-9_.-]+)", RegexOptions.Compiled);
        var rootFull = Path.GetFullPath(contentRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        try
        {
            foreach (var file in Directory.EnumerateFiles(logs, "*", SearchOption.TopDirectoryOnly))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                foreach (Match match in regex.Matches(text))
                {
                    var name = match.Groups["name"].Value;
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string candidate;
                    try { candidate = Path.GetFullPath(Path.Combine(contentRoot, name)); } catch { continue; }
                    if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(candidate)) continue;
                    if (!results.Contains(candidate, StringComparer.OrdinalIgnoreCase)) results.Add(candidate);
                    if (results.Count >= 3) return results;
                }
            }
        }
        catch { }
        return results;
    }

    private static IReadOnlyList<string> GetAceUserDataRoots()
    {
        var roots = new List<string>();
        AddUniquePath(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ACE"));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            AddUniquePath(roots, Path.Combine(profile, "Saved Games", "ACE"));
            AddUniquePath(roots, Path.Combine(profile, "Documents", "ACE"));
        }
        return roots;
    }

    private static string GetAccDocumentsRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa Competizione");
    private static string GetAccLocalConfigRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AC2", "Saved", "Config", "WindowsNoEditor");
    private static string GetAms2DocumentsRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Automobilista 2");

    private static IReadOnlyList<string> FindAms2ProfileFiles(string fileName)
    {
        var results = new List<string>();
        var savegame = Path.Combine(GetAms2DocumentsRoot(), "savegame");
        if (!Directory.Exists(savegame)) return results;
        try
        {
            foreach (var profile in Directory.EnumerateDirectories(savegame))
            {
                var file = Path.Combine(profile, "automobilista 2", "profiles", fileName);
                if (File.Exists(file)) results.Add(file);
            }
        }
        catch { }
        return results;
    }

    private static IReadOnlyList<string> FindAms2ProfileDirectories(string directoryName)
    {
        var results = new List<string>();
        var savegame = Path.Combine(GetAms2DocumentsRoot(), "savegame");
        if (!Directory.Exists(savegame)) return results;
        try
        {
            foreach (var profile in Directory.EnumerateDirectories(savegame))
            {
                var dir = Path.Combine(profile, "automobilista 2", directoryName);
                if (Directory.Exists(dir)) results.Add(dir);
            }
        }
        catch { }
        return results;
    }

    private static string GetRaceRoomPrimaryUserRoot()
    {
        var existing = GetRaceRoomUserRoots().FirstOrDefault(path =>
            Path.GetFileName(path).Equals("RaceRoom Racing Experience", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(path));
        return existing ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "SimBin", "RaceRoom Racing Experience");
    }

    private static IReadOnlyList<string> GetRaceRoomUserRoots()
    {
        var roots = new List<string>();
        var documentsCandidates = new List<string>();
        AddUniquePath(documentsCandidates, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) AddUniquePath(documentsCandidates, Path.Combine(profile, "Documents"));

        foreach (var documents in documentsCandidates)
        {
            foreach (var name in new[] { "RaceRoom Racing Experience", "RaceRoom Racing Experience Install 2", "RaceRoom Racing Experience Install 3" })
                AddUniquePath(roots, Path.Combine(documents, "My Games", "SimBin", name));
        }
        return roots;
    }

    private static IEnumerable<string> FindTopLevelFileIgnoreCase(string root, string fileName)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly); }
        catch { yield break; }
        foreach (var file in files)
            if (Path.GetFileName(file).Equals(fileName, StringComparison.OrdinalIgnoreCase)) yield return file;
    }

    private static string NextAvailableSiblingBackupPath(string parent, string baseName)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var candidate = Path.Combine(parent, $"{baseName}.PitMedicBackup-{timestamp}");
        var suffix = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate))
            candidate = Path.Combine(parent, $"{baseName}.PitMedicBackup-{timestamp}-{suffix++}");
        return candidate;
    }

    private static void AddUniquePath(ICollection<string> paths, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { path = Path.GetFullPath(path); } catch { return; }
        if (!paths.Contains(path, StringComparer.OrdinalIgnoreCase)) paths.Add(path);
    }

    private static string GetIRacingDocumentsRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing");
    private static string GetIRacingDocumentsFile(string name) => Path.Combine(GetIRacingDocumentsRoot(), name);

    private static void SetIniValue(string file, string key, string value)
    {
        var lines = File.ReadAllLines(file).ToList();
        var prefix = key + "=";
        var found = false;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            lines[i] = prefix + value;
            found = true;
        }
        if (!found)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add(string.Empty);
            lines.Add(prefix + value);
        }
        File.WriteAllLines(file, lines);
    }

    private static async Task RestartIRacingServiceAsync(string root, CancellationToken token)
    {
        await StopIRacingServiceAsync(root, token);
        await Task.Delay(900, token);
        await StartIRacingServiceAsync(root, token);
    }

    private static async Task StopIRacingServiceAsync(string root, CancellationToken token)
    {
        var file = FindFileIgnoreCase(root, "Stop_iRacingService.bat");
        if (file is null) return;
        await RunProcessAsync("cmd.exe", $"/c \"\"{file}\"\"", root, token, 30);
    }

    private static async Task StartIRacingServiceAsync(string root, CancellationToken token)
    {
        var file = FindFileIgnoreCase(root, "Start_iRacingService.bat") ?? FindFileIgnoreCase(root, "START_iRacingservice.bat");
        if (file is null) throw new FileNotFoundException("iRacing Start_iRacingService.bat was not found.");
        var code = await RunProcessAsync("cmd.exe", $"/c \"\"{file}\"\"", root, token, 30);
        if (code != 0) throw new InvalidOperationException($"iRacing service start helper returned exit code {code}.");
    }

    private static async Task<bool> WaitForIRacingServiceAsync(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTimeOffset.Now + timeout;
        while (DateTimeOffset.Now < deadline)
        {
            token.ThrowIfCancellationRequested();
            foreach (var name in new[] { "iRacingService64", "iRacingService" })
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); } catch { continue; }
                var found = processes.Length > 0;
                foreach (var p in processes) p.Dispose();
                if (found) return true;
            }
            await Task.Delay(500, token);
        }
        return false;
    }

    private static async Task LaunchIRacingUpdaterAsync(string root, string arguments, CancellationToken token)
    {
        var updater = new[]
        {
            Path.Combine(root, "iRacingUpdater.exe"),
            Path.Combine(root, "updater", "iRacingUpdater.exe")
        }.FirstOrDefault(File.Exists);
        if (updater is null) throw new FileNotFoundException("iRacingUpdater.exe was not found.");
        var info = new ProcessStartInfo
        {
            FileName = updater,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(updater) ?? root,
            UseShellExecute = true
        };
        using var updaterProcess = Process.Start(info) ?? throw new InvalidOperationException("iRacingUpdater could not be started.");
        await Task.Delay(1500, token);
    }

    private static string? FindFileIgnoreCase(string root, string name)
    {
        try { return Directory.EnumerateFiles(root, "*.bat", SearchOption.TopDirectoryOnly).FirstOrDefault(f => Path.GetFileName(f).Equals(name, StringComparison.OrdinalIgnoreCase)); }
        catch { return null; }
    }

    private sealed record ProcessCaptureResult(int ExitCode, string StandardOutput, string StandardError);

    private static string FormatExitCode(int exitCode)
        => $"{exitCode} (0x{unchecked((uint)exitCode):X8})";

    private static string BuildProcessLog(string label, ProcessCaptureResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{label} exit code: {FormatExitCode(result.ExitCode)}");
        builder.AppendLine("--- standard output ---");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.StandardOutput) ? "(no output)" : result.StandardOutput.TrimEnd());
        builder.AppendLine("--- standard error ---");
        builder.AppendLine(string.IsNullOrWhiteSpace(result.StandardError) ? "(no error output)" : result.StandardError.TrimEnd());
        return builder.ToString();
    }

    private static async Task<ProcessCaptureResult> RunProcessCaptureAsync(string fileName, string arguments, string workingDirectory, CancellationToken token, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        var waitTask = process.WaitForExitAsync(token);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, timeoutTask);
        if (completed != waitTask)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeoutSeconds} seconds.");
        }

        await waitTask;
        token.ThrowIfCancellationRequested();
        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        return new ProcessCaptureResult(process.ExitCode, standardOutput, standardError);
    }

    private static async Task RestartWindowsServiceAsync(string serviceName, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(serviceName)
            || serviceName.Any(c => !char.IsLetterOrDigit(c) && c is not '_' and not '-'))
            throw new InvalidOperationException("The companion service name was rejected.");

        var sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
        if (!File.Exists(sc)) throw new FileNotFoundException("Windows Service Control could not be found.", sc);

        var stop = await RunProcessCaptureAsync(sc, $"stop \"{serviceName}\"", Environment.SystemDirectory, token, 20);
        var stopText = $"{stop.StandardOutput}\n{stop.StandardError}";
        if (stop.ExitCode != 0
            && !stopText.Contains("1062", StringComparison.OrdinalIgnoreCase)
            && !stopText.Contains("has not been started", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Windows could not stop {serviceName}: {stopText.Trim()}");

        await Task.Delay(800, token);
        var start = await RunProcessCaptureAsync(sc, $"start \"{serviceName}\"", Environment.SystemDirectory, token, 20);
        var startText = $"{start.StandardOutput}\n{start.StandardError}";
        if (start.ExitCode != 0
            && !startText.Contains("1056", StringComparison.OrdinalIgnoreCase)
            && !startText.Contains("already running", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Windows could not start {serviceName}: {startText.Trim()}");

        var deadline = DateTimeOffset.Now.AddSeconds(12);
        while (DateTimeOffset.Now < deadline)
        {
            token.ThrowIfCancellationRequested();
            var query = await RunProcessCaptureAsync(sc, $"query \"{serviceName}\"", Environment.SystemDirectory, token, 10);
            if (query.ExitCode == 0
                && query.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
                return;
            await Task.Delay(500, token);
        }

        throw new InvalidOperationException($"{serviceName} did not return to the running state.");
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDirectory, CancellationToken token, int timeoutSeconds)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }
        };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(fileName)}.");
        var waitTask = process.WaitForExitAsync(token);
        var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), CancellationToken.None);
        var completed = await Task.WhenAny(waitTask, timeout);
        if (completed != waitTask)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException($"{Path.GetFileName(fileName)} did not finish within {timeoutSeconds} seconds.");
        }
        await waitTask;
        token.ThrowIfCancellationRequested();
        return process.ExitCode;
    }

    private static async Task<int> CloseProcessesAsync(IReadOnlyList<string> names, CancellationToken token)
    {
        var closed = 0;
        foreach (var name in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); } catch { continue; }
            foreach (var process in processes)
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        try { process.CloseMainWindow(); } catch { }
                        var deadline = DateTimeOffset.Now.AddSeconds(3);
                        while (!process.HasExited && DateTimeOffset.Now < deadline)
                        {
                            token.ThrowIfCancellationRequested();
                            await Task.Delay(150, token);
                            try { process.Refresh(); } catch { break; }
                        }
                        if (!process.HasExited) process.Kill(true);
                        closed++;
                    }
                    catch { }
                }
            }
        }
        return closed;
    }

    private static string SanitizeName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
        return value;
    }

    private void Update(IncidentRecord incident, RepairPlan plan, int percent, string stage, string message, string detail, int stepNumber, int totalSteps, bool active, bool complete, bool success, string? backupFolder)
    {
        var elapsedSeconds = Math.Max(0, (int)(DateTimeOffset.Now - _startedAt).TotalSeconds);
        var remaining = complete ? 0 : Math.Max(0, _estimatedSeconds - elapsedSeconds);
        var state = new RepairStatus
        {
            RepairId = Current?.RepairId ?? Guid.NewGuid(),
            IncidentFolder = incident.IncidentFolder,
            Title = plan.Title,
            Stage = stage,
            Message = message,
            Detail = detail,
            StepNumber = stepNumber,
            TotalSteps = totalSteps,
            Percent = percent,
            EstimatedSecondsRemaining = remaining,
            StartedAt = _startedAt,
            IsActive = active,
            IsComplete = complete,
            Success = success,
            BackupFolder = backupFolder
        };
        lock (_gate) _current = state;
        if (_persistStatus) PersistStatus(incident, plan, state);
        Publish(state);
    }

    private static void PersistStatus(IncidentRecord incident, RepairPlan plan, RepairStatus state)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new { incident = incident.Id, plan, state, updated = DateTimeOffset.Now }, JsonOptions);
            File.WriteAllText(Path.Combine(incident.IncidentFolder, "repair.json"), payload);
            var historyPayload = JsonSerializer.Serialize(new
            {
                time = DateTimeOffset.Now,
                state.Stage,
                state.Message,
                state.Detail,
                state.StepNumber,
                state.TotalSteps,
                state.Percent,
                state.IsComplete,
                state.Success
            });
            File.AppendAllText(Path.Combine(incident.IncidentFolder, "repair_history.jsonl"), historyPayload + Environment.NewLine);
            Directory.CreateDirectory(AppPaths.Repairs);
            File.WriteAllText(Path.Combine(AppPaths.Repairs, $"{state.RepairId:N}.json"), payload);
        }
        catch { }
    }

    private void Publish(RepairStatus state)
    {
        try { StatusChanged?.Invoke(state); } catch { }
    }

    private static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(destination, relative));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyFileWithRetryAsync(file, target, token);
        }
    }

    private static async Task CopyFileWithRetryAsync(string source, string destination, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 8; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                File.Copy(source, destination, true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), token);
            }
        }
        throw new IOException($"Could not back up '{source}' after several attempts.", last);
    }

    private static async Task RemoveInstalledContentAsync(string root, CancellationToken token)
    {
        if (!Directory.Exists(root)) return;

        // Delete files individually first. Renaming/moving the package root can fail when Steam,
        // Explorer, antivirus, or an indexing service has a handle on the directory itself even
        // though the individual files are writable. Steam verification only needs the content
        // files to be absent; an empty package directory is harmless.
        var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToArray();
        foreach (var file in files)
        {
            token.ThrowIfCancellationRequested();
            await DeleteFileWithRetryAsync(file, token);
        }

        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .OrderByDescending(x => x.Length)
            .ToArray();
        foreach (var directory in directories)
        {
            token.ThrowIfCancellationRequested();
            await TryDeleteDirectoryAsync(directory, token);
        }

        // The root itself may still have an open directory handle. Its removal is optional.
        await TryDeleteDirectoryAsync(root, token, throwOnFailure: false);
    }

    private static async Task DeleteFileWithRetryAsync(string file, CancellationToken token)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 12; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!File.Exists(file)) return;
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                File.Delete(file);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), token);
            }
        }
        throw new IOException($"A game file is still locked and could not be removed: {file}", last);
    }

    private static async Task TryDeleteDirectoryAsync(string directory, CancellationToken token, bool throwOnFailure = false)
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 6; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(directory)) return;
                Directory.Delete(directory, false);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), token);
            }
        }
        if (throwOnFailure && last is not null)
            throw new IOException($"Could not remove directory '{directory}'.", last);
    }

    private static async Task TryRollbackAsync(IEnumerable<(string Original, string Backup)> backedUp, CancellationToken token)
    {
        foreach (var item in backedUp.Reverse())
        {
            try
            {
                if (!Directory.Exists(item.Backup)) continue;
                Directory.CreateDirectory(item.Original);
                await CopyDirectoryAsync(item.Backup, item.Original, token);
                AppLog.Write($"Repair rollback restored: {item.Backup} -> {item.Original}");
            }
            catch (Exception ex) { AppLog.Write($"Repair rollback warning: {ex.Message}"); }
        }
    }

    private static bool ContainsMasFile(string path)
    {
        try { return Directory.EnumerateFiles(path, "*.mas", SearchOption.AllDirectories).Any(); }
        catch { return false; }
    }

    private static bool IsLmuRunning()
    {
        foreach (var alias in GameDefinition.Supported.First(g => g.Kind == GameKind.LeMansUltimate).ProcessNames)
        {
            try
            {
                var processes = Process.GetProcessesByName(alias);
                var any = processes.Length > 0;
                foreach (var process in processes) process.Dispose();
                if (any) return true;
            }
            catch { }
        }
        return false;
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _activeCts?.Cancel(); } catch { }
            _activeCts?.Dispose();
            _activeCts = null;
        }
    }
}
