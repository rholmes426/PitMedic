using PitMedic.Models;

namespace PitMedic.Services;

public static class RepairPlanner
{
    public static RepairPlan? Create(GameDefinition game, CrashClassification classification, CollectedEvidence collected,
        IReadOnlyList<LiveFaultEvidence>? liveFaults = null)
    {
        if (game.Kind == GameKind.LeMansUltimate)
        {
            if (collected.AffectedInstalledContent.Count > 0
                && (classification.Category.Contains("content", StringComparison.OrdinalIgnoreCase)
                    || collected.CrashHints.Any(h => h.Contains("decompress", StringComparison.OrdinalIgnoreCase))))
                return BuildLmuContentRepair(collected.AffectedInstalledContent);

            // Prefer official, symptom-specific LMU repairs before community-only conflict checks.
            // A memory-allocation signature is intentionally diagnostic-only because changing the
            // Windows page file is a global/reboot-sensitive operation.
            var signatures = collected.RepairSignatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (signatures.Contains("lmu-memory-allocation")) return null;
            foreach (var signature in new[]
            {
                "lmu-content-corruption", "lmu-shader-cache", "lmu-startup-config",
                "lmu-plugin-conflict", "lmu-eac", "lmu-reshade-runtime",
                "lmu-online-clock-eac", "lmu-overlay-conflict", "lmu-ghub-conflict"
            })
            {
                if (!signatures.Contains(signature)) continue;
                var plan = PlanForSignature(signature);
                if (plan is not null) return plan;
            }
            return null;
        }

        if (game.Kind == GameKind.IRacing)
        {
            foreach (var fault in liveFaults ?? Array.Empty<LiveFaultEvidence>())
            {
                var plan = PlanForSignature(IRacingRepairSignaturePolicy.MapDiagnosticSignature(fault.SignatureId));
                if (plan is not null) return plan;
            }

            return PlanForCategory(classification.Category);
        }

        if (game.Kind == GameKind.AssettoCorsaEvo)
        {
            var signatures = collected.RepairSignatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var signature in new[] { "ace-steam-content", "ace-video-settings", "ace-user-profile" })
            {
                if (!signatures.Contains(signature)) continue;
                var plan = PlanForSignature(signature);
                if (plan is not null) return plan;
            }
            if (classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase)
                || classification.Category.Contains("abnormal process termination", StringComparison.OrdinalIgnoreCase)
                || classification.Category.Contains("simulator log indicates failure", StringComparison.OrdinalIgnoreCase))
                return PlanForSignature("ace-video-settings");
            return null;
        }

        if (game.Kind == GameKind.RaceRoom)
        {
            var signatures = collected.RepairSignatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var signature in new[] { "raceroom-browser-cache", "raceroom-shader-cache", "raceroom-graphics-config", "raceroom-steam-content", "raceroom-user-config" })
            {
                if (!signatures.Contains(signature)) continue;
                var plan = PlanForSignature(signature);
                if (plan is not null) return plan;
            }
            if (classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase)
                || classification.Category.Contains("abnormal process termination", StringComparison.OrdinalIgnoreCase))
                return PlanForSignature("raceroom-graphics-config");
            return null;
        }

        if (game.Kind == GameKind.AssettoCorsaCompetizione)
        {
            foreach (var fault in liveFaults ?? Array.Empty<LiveFaultEvidence>())
            {
                var livePlan = PlanForSignature(fault.SignatureId);
                if (livePlan is not null) return livePlan;
            }
            var signatures = collected.RepairSignatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var signature in new[] { "acc-control-presets", "acc-trueforce", "acc-controls", "acc-ffb", "acc-engine-config", "acc-game-user-settings", "acc-steam-content", "acc-user-profile" })
            {
                if (!signatures.Contains(signature)) continue;
                var plan = PlanForSignature(signature);
                if (plan is not null) return plan;
            }
            if (classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase)
                || classification.Category.Contains("abnormal process termination", StringComparison.OrdinalIgnoreCase))
                return PlanForSignature("acc-game-user-settings");
            return null;
        }

        if (game.Kind == GameKind.Automobilista2)
        {
            var signatures = collected.RepairSignatureIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var signature in new[] { "ams2-championship-state", "ams2-controller-config", "ams2-ffb-custom", "ams2-tuning-setups", "ams2-vr-config", "ams2-graphics-config", "ams2-default-profile", "ams2-steam-content", "ams2-user-profile" })
            {
                if (!signatures.Contains(signature)) continue;
                var plan = PlanForSignature(signature);
                if (plan is not null) return plan;
            }
            if (classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase)
                || classification.Category.Contains("abnormal process termination", StringComparison.OrdinalIgnoreCase))
                return PlanForSignature("ams2-graphics-config");
            return null;
        }

        return null;
    }

    public static RepairPlan? TryCreateFromIncident(IncidentRecord record)
    {
        if (record.RecommendedRepair is not null
            && !record.RecommendedRepair.Id.Equals("companion-app-restart", StringComparison.OrdinalIgnoreCase))
            return record.RecommendedRepair;

        if (record.Game.Equals("Le Mans Ultimate", StringComparison.OrdinalIgnoreCase))
        {
            var affected = LogCollector.FindAffectedContentInIncidentFolder(record.IncidentFolder);
            if (affected.Count > 0) return BuildLmuContentRepair(affected);
            return PlanFromEvidence("Le Mans Ultimate", record.Classification.Evidence);
        }

        if (record.Game.Equals("iRacing", StringComparison.OrdinalIgnoreCase))
            return PlanFromEvidence("iRacing", record.Classification.Evidence)
                ?? PlanForCategory(record.Classification.Category);

        if (record.Game.Equals("Assetto Corsa EVO", StringComparison.OrdinalIgnoreCase))
            return PlanFromEvidence("Assetto Corsa EVO", record.Classification.Evidence)
                ?? (record.Classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase) ? PlanForSignature("ace-video-settings") : null);

        if (record.Game.Equals("RaceRoom Racing Experience", StringComparison.OrdinalIgnoreCase))
            return PlanFromEvidence("RaceRoom Racing Experience", record.Classification.Evidence)
                ?? (record.Classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase) ? PlanForSignature("raceroom-graphics-config") : null);

        if (record.Game.Equals("Assetto Corsa Competizione", StringComparison.OrdinalIgnoreCase))
            return PlanFromEvidence("Assetto Corsa Competizione", record.Classification.Evidence)
                ?? (record.Classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase) ? PlanForSignature("acc-game-user-settings") : null);

        if (record.Game.Equals("Automobilista 2", StringComparison.OrdinalIgnoreCase))
            return PlanFromEvidence("Automobilista 2", record.Classification.Evidence)
                ?? (record.Classification.Category.Contains("application fault", StringComparison.OrdinalIgnoreCase) ? PlanForSignature("ams2-graphics-config") : null);

        var companion = CompanionSoftwareDefinition.Supported.FirstOrDefault(software =>
            record.Game.Equals(software.DisplayName, StringComparison.OrdinalIgnoreCase));
        if (companion is not null)
            return CreateCompanion(companion, record.ProcessPath);

        return null;
    }

    public static RepairPlan? CreateCompanion(CompanionSoftwareDefinition software, string processPath)
    {
        var executable = ValidCompanionExecutable(software, processPath)
            ?? software.DefaultExecutablePaths.FirstOrDefault(ValidCompanionPath);
        if (string.IsNullOrWhiteSpace(executable)) return null;

        var recovery = CompanionRecoveryPolicy.For(software.Kind);

        return new RepairPlan
        {
            Id = recovery.RepairId,
            Title = recovery.Title,
            Summary = recovery.Summary,
            Game = software.DisplayName,
            Safety = RepairSafety.Reversible,
            EstimatedMinutes = 1,
            RequiresApproval = true,
            Steps = recovery.Steps,
            References = CompanionSoftwareKnowledgeBase.ReferencesFor(software.Kind)
        };
    }

    private static string? ValidCompanionExecutable(CompanionSoftwareDefinition software, string path)
    {
        if (!ValidCompanionPath(path)) return null;
        var fileName = Path.GetFileName(path);
        var processName = Path.GetFileNameWithoutExtension(path);
        return fileName.Equals(software.ExecutableName, StringComparison.OrdinalIgnoreCase)
            || software.ProcessNames.Any(name => processName.Equals(name, StringComparison.OrdinalIgnoreCase))
            ? Path.GetFullPath(path)
            : null;
    }

    private static bool ValidCompanionPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return Path.IsPathFullyQualified(path) && File.Exists(Path.GetFullPath(path)); }
        catch { return false; }
    }

    private static RepairPlan? PlanFromEvidence(string game, IEnumerable<string> evidence)
    {
        if (game.Equals("iRacing", StringComparison.OrdinalIgnoreCase))
        {
            var preservedSignature = IRacingRepairSignaturePolicy.FindKnowledgeSignature(evidence);
            var preservedPlan = PlanForSignature(preservedSignature ?? string.Empty);
            if (preservedPlan is not null) return preservedPlan;
        }

        var text = string.Join("\n", evidence);
        foreach (var entry in RepairKnowledgeBase.Entries.Where(x => x.Game.Equals(game, StringComparison.OrdinalIgnoreCase)))
        {
            if (entry.Signatures.Any(sig => text.Contains(sig, StringComparison.OrdinalIgnoreCase)))
            {
                var plan = PlanForSignature(entry.Id);
                if (plan is not null) return plan;
            }
        }
        return null;
    }

    private static RepairPlan? PlanForCategory(string category)
    {
        var c = category.ToLowerInvariant();
        if (c.Contains("helper service")) return PlanForSignature("iracing-helper-service");
        if (c.Contains("ui startup")) return PlanForSignature("iracing-ui-cache");
        if (c.Contains("anti-cheat")) return PlanForSignature("iracing-eac-error73");
        if (c.Contains("update verification")) return PlanForSignature("iracing-update-verification");
        if (c.Contains("track content") && c.Contains("steam")) return PlanForSignature("iracing-loading-error-49");
        if (c.Contains("track content")) return PlanForSignature("iracing-track-corruption");
        if (c.Contains("car content")) return PlanForSignature("iracing-car-corruption");
        if (c.Contains("already-running")) return PlanForSignature("iracing-trueforce-stale-state");
        if (c.Contains("permission/compatibility") || c.Contains("compatibility-mode")) return PlanForSignature("iracing-compatibility-flags");
        if (c.Contains("digital signature")) return PlanForSignature("iracing-run-updater");
        if (c.Contains("updater waiting")) return PlanForSignature("iracing-updater-autoinstall");
        if (c.Contains("renderer configuration")) return PlanForSignature("iracing-renderer-config");
        if (c.Contains("application fault") || c.Contains("abnormal process termination")) return PlanForSignature("iracing-windows-integrity");
        return null;
    }

    private static RepairPlan? PlanForSignature(string signature) => signature switch
    {
        "lmu-content-corruption" => Simple("lmu-steam-verify", "Validate LMU game content",
            "PitMedic could not safely isolate a specific damaged package, so it will ask Steam to validate LMU and reacquire any missing/corrupt files.",
            "Le Mans Ultimate", RepairSafety.Reversible, 8, true,
            new[] { "Confirm LMU is closed", "Start Steam validation", "Keep Steam in the background", "Record validation launch" }, "2399420"),

        "lmu-shader-cache" => Simple("lmu-shader-cache", "Rebuild LMU shader cache",
            "PitMedic will back up and clear LMU's generated shader/cache files so the simulator can rebuild them cleanly on the next launch.",
            "Le Mans Ultimate", RepairSafety.Automatic, 1, false,
            new[] { "Confirm LMU is closed", "Back up generated shader/cache files", "Clear Shaders, CBash and dynamic.cache", "Verify the cache paths are clean" }),

        "lmu-startup-config" => Simple("lmu-reset-dx11-config", "Reset LMU graphics initialization",
            "PitMedic will back up and remove LMU's DX11 configuration file so it is regenerated on the next launch.",
            "Le Mans Ultimate", RepairSafety.Reversible, 1, false,
            new[] { "Confirm LMU is closed", "Back up Config_DX11/config_dx11.ini", "Remove the active DX11 config", "Allow LMU to regenerate it on next launch" }),

        "lmu-plugin-conflict" => Simple("lmu-disable-plugins", "Temporarily disable LMU plugins",
            "PitMedic will back up CustomPluginVariables.JSON and set installed plugin entries to disabled for a controlled retest.",
            "Le Mans Ultimate", RepairSafety.Reversible, 1, false,
            new[] { "Confirm LMU is closed", "Back up plugin configuration", "Disable plugin entries", "Preserve the backup for rollback" }),

        "lmu-eac" => Simple("lmu-reinstall-eac", "Repair LMU Easy Anti-Cheat",
            "PitMedic will run LMU's supported Easy Anti-Cheat reinstall workflow and record the result.",
            "Le Mans Ultimate", RepairSafety.Significant, 2, true,
            new[] { "Confirm LMU is closed", "Locate LMU EasyAntiCheat support files", "Run the supported reinstall/install batch", "Verify the installer completed" }),

        "lmu-reshade-runtime" => Simple("lmu-quarantine-reshade", "Disable unsupported LMU graphics hooks",
            "PitMedic will back up and temporarily move detected ReShade/custom DirectX hook files out of the LMU root for a clean launch test.",
            "Le Mans Ultimate", RepairSafety.Significant, 1, true,
            new[] { "Confirm LMU is closed", "Identify known ReShade/custom hook files", "Back them up", "Move them to PitMedic quarantine", "Retest LMU" }),

        "lmu-overlay-conflict" => Simple("lmu-close-overlay-tools", "Close conflicting overlay/tuning tools",
            "PitMedic will close detected MSI Afterburner/RivaTuner processes for a controlled LMU retest. It will not uninstall them or change overclock settings.",
            "Le Mans Ultimate", RepairSafety.Significant, 1, true,
            new[] { "Confirm LMU is closed", "Close implicated overlay/tuning processes", "Retest LMU" }),

        "lmu-ghub-conflict" => Simple("lmu-close-ghub", "Close Logitech G HUB for retest",
            "PitMedic will close Logitech G HUB processes for a controlled LMU launch test without uninstalling or changing device settings.",
            "Le Mans Ultimate", RepairSafety.Significant, 1, true,
            new[] { "Confirm LMU is closed", "Close G HUB processes", "Retest LMU" }),

        "lmu-online-clock-eac" => Simple("lmu-sync-windows-time", "Synchronize Windows time",
            "PitMedic will request an immediate Windows time synchronization, a supported LMU online-connection fix. It will not change your time zone.",
            "Le Mans Ultimate", RepairSafety.Automatic, 1, false,
            new[] { "Synchronize Windows time", "Confirm the time service accepted the request" }),

        "iracing-helper-service" => Simple("iracing-helper-service", "Restart iRacing Helper Service",
            "PitMedic will restart the iRacing background service and verify that it is running again.",
            "iRacing", RepairSafety.Automatic, 1, false,
            new[] { "Locate iRacing", "Restart iRacing Helper Service", "Verify service/process state" }),

        "iracing-ui-cache" => Simple("iracing-ui-cache", "Refresh iRacing UI cache",
            "PitMedic will preserve a recovery copy of the Electron UI cache, clear the active cache, and restart the iRacing Helper Service.",
            "iRacing", RepairSafety.Reversible, 1, false,
            new[] { "Close iRacing UI if needed", "Back up iracing-electron cache", "Clear the active cache", "Restart iRacing Helper Service" }),

        "iracing-ui-safe" => Simple("iracing-ui-safe", "Reset iRacing UI safe mode",
            "PitMedic will back up Documents\\iRacing\\app.ini and set UISafe=0, matching iRacing's current support guidance.",
            "iRacing", RepairSafety.Reversible, 1, false,
            new[] { "Back up app.ini", "Set UISafe to 0", "Verify the setting was saved" }),

        "iracing-eac-error73" => Simple("iracing-eac-reinstall", "Repair iRacing Easy Anti-Cheat",
            "PitMedic will run iRacing's supported InstallEOSAntiCheat.bat repair workflow and verify the command completes.",
            "iRacing", RepairSafety.Significant, 2, true,
            new[] { "Confirm simulator is closed", "Locate EasyAntiCheat\\InstallEOSAntiCheat.bat", "Run EOS anti-cheat installer", "Record completion" }),

        "iracing-update-verification" => Simple("iracing-update-reset", "Reset iRacing update cache",
            "PitMedic will back up version_system.txt, clear the downloads cache, then launch the iRacing updater so required files can be reacquired.",
            "iRacing", RepairSafety.Reversible, 4, true,
            new[] { "Back up version_system.txt", "Clear downloads cache", "Launch iRacing updater", "Wait for update activity" }),

        "iracing-updater-autoinstall" => Simple("iracing-updater-autoinstall", "Resume iRacing updater installation",
            "PitMedic will launch iRacingUpdater with the supported -autoinstall option to install already-downloaded update files.",
            "iRacing", RepairSafety.Reversible, 2, false,
            new[] { "Locate iRacingUpdater", "Run -autoinstall", "Wait for updater to initialize" }),

        "iracing-track-corruption" or "iracing-track-loading-errors" => Simple("iracing-track-index-reset", "Repair iRacing track content index",
            "PitMedic will back up track index metadata, remove the active tracks.dat/version.txt metadata, and launch the updater so track content is revalidated.",
            "iRacing", RepairSafety.Reversible, 4, true,
            new[] { "Back up track metadata", "Reset tracks.dat/version.txt", "Launch updater", "Allow iRacing to reacquire track metadata/content" }),

        "iracing-car-corruption" or "iracing-car-loading-errors" => Simple("iracing-car-index-reset", "Repair iRacing car content index",
            "PitMedic will back up cars.dat/version.txt and reset the active car-content index so iRacing can redetect and redownload damaged vehicle data.",
            "iRacing", RepairSafety.Reversible, 4, true,
            new[] { "Back up car metadata", "Reset cars.dat/version.txt", "Launch updater", "Allow iRacing to reacquire required car content" }),

        "iracing-loading-error-49" => Simple("iracing-steam-track-repair", "Repair iRacing Steam track content",
            "PitMedic will back up and reset track metadata, then ask Steam to validate iRacing so clean track files are restored.",
            "iRacing", RepairSafety.Reversible, 6, true, new[] { "Back up track metadata", "Reset tracks.dat", "Start Steam validation", "Monitor validation" }, "266410"),

        "iracing-content-file-locked" => Simple("iracing-release-content-lock", "Release iRacing update file lock",
            "PitMedic will stop the iRacing Helper Service, wait for file handles to release, and start Steam validation. The service is restarted afterward.",
            "iRacing", RepairSafety.Reversible, 4, true, new[] { "Stop iRacing Helper Service", "Wait for locks to release", "Start Steam validation", "Restart Helper Service" }, "266410"),

        "iracing-renderer-config" => Simple("iracing-renderer-reset", "Reset iRacing renderer configuration",
            "PitMedic will back up rendererDX11*.ini files and remove the active copies so iRacing regenerates graphics configuration.",
            "iRacing", RepairSafety.Reversible, 1, false,
            new[] { "Back up renderer configuration", "Remove rendererDX11*.ini", "Allow iRacing to regenerate graphics settings" }),

        "iracing-trueforce-stale-state" => Simple("iracing-logitech-shutdown-workaround", "Repair iRacing Logitech shutdown conflict",
            "PitMedic will back up app.ini and disable the two iRacing Logitech features that official support has documented as causes of stale/already-running simulator states: TrueForce and Logitech hardware lighting.",
            "iRacing", RepairSafety.Significant, 1, true,
            new[] { "Close a stale iRacing simulator process if present", "Back up app.ini", "Set trueForceEnabled to 0", "Set enableLogitechLED to 0", "Verify both settings" }),

        "iracing-loading-error-3" => Simple("iracing-reset-user-config", "Reset iRacing user configuration",
            "Loading Error 3 can be caused by damaged user configuration. PitMedic will preserve the entire Documents\\iRacing folder and let iRacing create a clean configuration on next launch. Setups, replays and paints remain in the backup.",
            "iRacing", RepairSafety.Significant, 3, true,
            new[] { "Confirm iRacing is closed", "Preserve Documents\\iRacing", "Move the active configuration aside", "Allow iRacing to create a clean profile" }),

        "iracing-compatibility-flags" => Simple("iracing-clear-compatibility", "Reset iRacing compatibility flags",
            "PitMedic will back up Windows compatibility-layer values for iRacing executables and remove Run-as-admin/compatibility overrides that can prevent launch.",
            "iRacing", RepairSafety.Reversible, 1, false,
            new[] { "Locate iRacing executables", "Back up compatibility values", "Remove incompatible overrides", "Verify the registry values" }),

        "iracing-run-updater" or "iracing-digital-signature" => Simple("iracing-run-updater", "Run iRacing updater repair",
            "PitMedic will launch iRacingUpdater directly, matching iRacing's supported recovery step for updater/digital-signature failures.",
            "iRacing", RepairSafety.Automatic, 1, false,
            new[] { "Locate iRacingUpdater.exe", "Launch updater", "Verify updater starts" }),

        "iracing-windows-integrity" => Simple("iracing-windows-integrity", "Check and repair Windows system files",
            "For repeated iRacing access-violation/application faults, PitMedic can run Microsoft's DISM image repair followed by System File Checker. This is system-wide and can take a while, so approval is always required.",
            "iRacing", RepairSafety.Significant, 20, true,
            new[] { "Run DISM /RestoreHealth", "Run sfc /scannow", "Record the results", "Recommend a reboot if Windows repairs files" }),

        "ace-video-settings" => Simple("ace-video-settings-reset", "Reset Assetto Corsa EVO video settings",
            "PitMedic will back up Video.videosettings and remove the active copy so Assetto Corsa EVO can regenerate clean graphics settings without wiping controls or the rest of the profile.",
            "Assetto Corsa EVO", RepairSafety.Reversible, 1, false,
            new[] { "Confirm Assetto Corsa EVO is closed", "Back up Video.videosettings", "Remove the active video settings file", "Allow the game to regenerate graphics settings" }),

        "ace-user-profile" => Simple("ace-profile-reset", "Reset Assetto Corsa EVO user profile",
            "PitMedic will preserve the ACE user-data folder and move the active profile aside so Assetto Corsa EVO can create a clean profile on the next launch.",
            "Assetto Corsa EVO", RepairSafety.Significant, 3, true,
            new[] { "Confirm Assetto Corsa EVO is closed", "Preserve ACE user data", "Move the active profile aside", "Allow Assetto Corsa EVO to create a clean profile" }),

        "ace-steam-content" => Simple("ace-steam-verify", "Validate Assetto Corsa EVO game files",
            "PitMedic will ask Steam to verify Assetto Corsa EVO and reacquire files that are missing or damaged.",
            "Assetto Corsa EVO", RepairSafety.Reversible, 8, true,
            new[] { "Confirm Assetto Corsa EVO is closed", "Start Steam validation", "Keep Steam in the background", "Record validation launch" }, "3058630"),

        "raceroom-browser-cache" => Simple("raceroom-browser-reset", "Refresh RaceRoom browser cache",
            "PitMedic will preserve RaceRoom BrowserData and clear the active browser cache/state so the embedded UI can rebuild cleanly.",
            "RaceRoom Racing Experience", RepairSafety.Reversible, 1, false,
            new[] { "Confirm RaceRoom is closed", "Back up BrowserData", "Clear active browser cache/state", "Allow RaceRoom to rebuild browser data" }),

        "raceroom-graphics-config" => Simple("raceroom-graphics-reset", "Reset RaceRoom graphics configuration",
            "PitMedic will back up graphics_options.xml and remove the active copy so RaceRoom can regenerate display and resolution settings.",
            "RaceRoom Racing Experience", RepairSafety.Reversible, 1, false,
            new[] { "Confirm RaceRoom is closed", "Back up graphics_options.xml", "Remove the active graphics configuration", "Allow RaceRoom to regenerate graphics settings" }),

        "raceroom-shader-cache" => Simple("raceroom-shader-reset", "Rebuild RaceRoom shader cache",
            "PitMedic will preserve ShaderCache.bin and remove the active cache so RaceRoom can rebuild shaders on the next launch.",
            "RaceRoom Racing Experience", RepairSafety.Reversible, 1, false,
            new[] { "Confirm RaceRoom is closed", "Back up ShaderCache.bin", "Remove the active shader cache", "Allow RaceRoom to rebuild shaders" }),

        "raceroom-user-config" => Simple("raceroom-profile-reset", "Reset RaceRoom user configuration",
            "PitMedic will preserve the RaceRoom Racing Experience user folder and move the active copy aside so RaceRoom can generate a clean configuration.",
            "RaceRoom Racing Experience", RepairSafety.Significant, 3, true,
            new[] { "Confirm RaceRoom is closed", "Preserve RaceRoom user data", "Move the active profile aside", "Allow RaceRoom to create a clean profile" }),

        "raceroom-steam-content" => Simple("raceroom-steam-verify", "Validate RaceRoom game files",
            "PitMedic will ask Steam to verify RaceRoom Racing Experience and reacquire files that are missing or damaged.",
            "RaceRoom Racing Experience", RepairSafety.Reversible, 8, true,
            new[] { "Confirm RaceRoom is closed", "Start Steam validation", "Keep Steam in the background", "Record validation launch" }, "211500"),

        "acc-game-user-settings" => Simple("acc-game-user-settings-reset", "Reset ACC display/startup settings",
            "PitMedic will back up and remove GameUserSettings.ini so ACC can regenerate a clean display/startup configuration.",
            "Assetto Corsa Competizione", RepairSafety.Reversible, 1, false, new[] { "Confirm ACC is closed", "Back up GameUserSettings.ini", "Remove active settings", "Allow ACC to regenerate settings" }),
        "acc-engine-config" => Simple("acc-engine-config-reset", "Reset ACC engine configuration",
            "PitMedic will back up and remove Engine.ini so ACC can regenerate a clean Unreal Engine configuration.",
            "Assetto Corsa Competizione", RepairSafety.Reversible, 1, false, new[] { "Confirm ACC is closed", "Back up Engine.ini", "Remove active engine configuration", "Retest ACC" }),
        "acc-controls" => Simple("acc-controls-reset", "Reset ACC controller configuration",
            "PitMedic will preserve controls.json and remove the active copy so wheel/controller bindings can be rebuilt cleanly.",
            "Assetto Corsa Competizione", RepairSafety.Significant, 1, true, new[] { "Confirm ACC is closed", "Back up controls.json", "Remove active controller config", "Remap controls after launch" }),
        "acc-ffb" => Simple("acc-ffb-reset", "Reset ACC force-feedback settings",
            "PitMedic will preserve ffbUserSettings.json and remove the active copy so ACC can regenerate force-feedback settings.",
            "Assetto Corsa Competizione", RepairSafety.Significant, 1, true, new[] { "Confirm ACC is closed", "Back up FFB settings", "Remove active FFB config", "Retest force feedback" }),
        "acc-trueforce" => Simple("acc-trueforce-disable", "Disable ACC Logitech TrueForce extras",
            "PitMedic will back up controls.json and disable ACC manufacturer extras when the captured failure points to the Logitech TrueForce integration.",
            "Assetto Corsa Competizione", RepairSafety.Significant, 1, true, new[] { "Confirm ACC is closed", "Back up controls.json", "Set enableManufacturerExtras to false", "Retest ACC" }),
        "acc-control-presets" => Simple("acc-control-presets-reset", "Quarantine ACC control presets",
            "PitMedic will preserve the saved Customs\\Controls presets and move the active folder aside when an old preset is crashing the Controls menu.",
            "Assetto Corsa Competizione", RepairSafety.Significant, 1, true, new[] { "Confirm ACC is closed", "Back up saved control presets", "Move active preset folder aside", "Retest Controls menu" }),
        "acc-user-profile" => Simple("acc-profile-reset", "Reset ACC user configuration",
            "PitMedic will preserve ACC's Documents configuration and LocalAppData Saved\\Config, then move the active copies aside so clean settings are generated.",
            "Assetto Corsa Competizione", RepairSafety.Significant, 3, true, new[] { "Confirm ACC is closed", "Preserve user configuration", "Move active config aside", "Allow ACC to generate clean settings" }),
        "acc-steam-content" => Simple("acc-steam-verify", "Validate ACC game files",
            "PitMedic will ask Steam to verify Assetto Corsa Competizione and reacquire missing or damaged files.",
            "Assetto Corsa Competizione", RepairSafety.Reversible, 8, true, new[] { "Confirm ACC is closed", "Start Steam validation", "Keep Steam in the background", "Record validation launch" }, "805550"),

        "ams2-graphics-config" => Simple("ams2-graphics-reset", "Reset AMS2 graphics configuration",
            "PitMedic will preserve graphicsconfigdx11.xml and remove the active copy so Automobilista 2 can regenerate display settings.",
            "Automobilista 2", RepairSafety.Reversible, 1, false, new[] { "Confirm AMS2 is closed", "Back up graphics configuration", "Remove active graphicsconfigdx11.xml", "Allow AMS2 to regenerate it" }),
        "ams2-vr-config" => Simple("ams2-vr-reset", "Reset AMS2 VR configuration",
            "PitMedic will preserve OpenVR/Oculus graphics and VR settings XML files and remove the active copies so VR configuration can be regenerated.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Back up VR XML settings", "Remove active VR settings", "Retest in VR" }),
        "ams2-controller-config" => Simple("ams2-controller-reset", "Reset AMS2 controller configuration",
            "PitMedic will preserve default.controllersettings.v1.03.sav and remove the active copy so wheel/controller bindings can be rebuilt.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Back up controller profile", "Remove active controller settings", "Remap controls after launch" }),
        "ams2-ffb-custom" => Simple("ams2-ffb-reset", "Reset AMS2 custom FFB file",
            "PitMedic will preserve ffb_custom_settings.txt and remove the active copy so a clean/default custom FFB state can be recreated.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Back up custom FFB file", "Remove active custom FFB file", "Retest FFB" }),
        "ams2-tuning-setups" => Simple("ams2-tuning-reset", "Reset incompatible AMS2 car setups",
            "PitMedic will preserve tuning setup folders and move the active copies aside when older setups are incompatible with physics updates.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Back up tuning setups", "Move stale setup data aside", "Retest affected cars" }),
        "ams2-championship-state" => Simple("ams2-championship-reset", "Quarantine corrupted AMS2 championship state",
            "PitMedic will preserve championship save files and move the active championship state aside so Automobilista 2 can rebuild it without deleting other profile data.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Preserve championship saves", "Move active championship state aside", "Retest Championship mode" }),
        "ams2-default-profile" => Simple("ams2-default-profile-reset", "Reset AMS2 default profile settings",
            "PitMedic will preserve default.sav and remove the active copy so Automobilista 2 can rebuild the default profile state.",
            "Automobilista 2", RepairSafety.Significant, 1, true, new[] { "Confirm AMS2 is closed", "Back up default.sav", "Remove active default profile file", "Retest AMS2" }),
        "ams2-user-profile" => Simple("ams2-profile-reset", "Reset AMS2 user profile",
            "PitMedic will preserve the complete Documents\\Automobilista 2 folder and move the active copy aside so the simulator can create clean user data.",
            "Automobilista 2", RepairSafety.Significant, 3, true, new[] { "Confirm AMS2 is closed", "Preserve user data", "Move active profile aside", "Allow AMS2 to create clean settings" }),
        "ams2-steam-content" => Simple("ams2-steam-verify", "Validate AMS2 game files",
            "PitMedic will ask Steam to verify Automobilista 2 and reacquire missing or damaged files.",
            "Automobilista 2", RepairSafety.Reversible, 8, true, new[] { "Confirm AMS2 is closed", "Start Steam validation", "Keep Steam in the background", "Record validation launch" }, "1066890"),

        _ => null
    };

    private static RepairPlan Simple(string id, string title, string summary, string game, RepairSafety safety,
        int minutes, bool approval, IReadOnlyList<string> steps, string steamAppId = "") => new()
    {
        Id = id,
        Title = title,
        Summary = summary,
        Game = game,
        Safety = safety,
        EstimatedMinutes = minutes,
        RequiresApproval = approval || minutes > 2,
        Steps = steps,
        SteamAppId = steamAppId,
        References = RepairKnowledgeBase.ReferencesForPlan(id)
    };

    private static RepairPlan? BuildLmuContentRepair(IReadOnlyList<string> affected)
    {
        var normalized = affected
            .Select(x => x.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar))
            .Where(IsSafeRelativeContentPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalized.Length == 0) return null;

        var estimate = Math.Clamp(6 + Math.Max(0, normalized.Length - 1) * 2, 6, 12);
        return new RepairPlan
        {
            Id = "lmu-targeted-content-reacquire",
            Title = "Replace damaged LMU content",
            Summary = "PitMedic will back up only the affected track/car packages, remove the damaged installed copies, then start Steam validation so clean copies are reacquired.",
            Game = "Le Mans Ultimate",
            Safety = RepairSafety.Reversible,
            EstimatedMinutes = estimate,
            RequiresApproval = true,
            SteamAppId = "2399420",
            AffectedContentRelativePaths = normalized,
            Steps = new[]
            {
                "Confirm Le Mans Ultimate is closed",
                "Copy affected content into a PitMedic recovery backup",
                "Remove the damaged installed content using a lock-tolerant cleanup",
                "Start Steam validation for Le Mans Ultimate",
                "Monitor until the affected content has been restored",
                "Keep the backup available for rollback"
            },
            References = RepairKnowledgeBase.ReferencesForPlan("lmu-targeted-content-reacquire")
        };
    }

    private static bool IsSafeRelativeContentPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal)) return false;
        var parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && (parts[0].Equals("Locations", StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("Vehicles", StringComparison.OrdinalIgnoreCase))
            && parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}
