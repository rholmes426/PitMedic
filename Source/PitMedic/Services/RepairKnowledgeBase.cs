using PitMedic.Models;

namespace PitMedic.Services;

public static class RepairKnowledgeBase
{
    private static RepairReference Ref(string title, string source, string url, string note, bool official = true) => new()
    {
        Title = title,
        Source = source,
        Url = url,
        Note = note,
        IsOfficial = official
    };

    public static IReadOnlyList<KnowledgeEntry> Entries { get; } = new[]
    {
        new KnowledgeEntry
        {
            Id = "lmu-content-corruption",
            Game = "Le Mans Ultimate",
            Issue = "Installed track or vehicle content cannot be read/decompressed",
            Detection = "MAS decompression, missing mesh/scene, or affected Installed\\Locations / Installed\\Vehicles package references near the failure.",
            RepairStrategy = "Back up only affected content, remove the installed copy, then use Steam validation to reacquire clean content.",
            Safety = "Reversible / approval required when expected over two minutes",
            Signatures = new[] { "Error decompressing file", "Error loading mesh file", "Error initializing scene file", "CUBE error loading scene file" },
            References = new[]
            {
                Ref("Common Fixes", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes", "Official guidance starts with Steam file verification for game issues."),
                Ref("Game Launching Error - targeted track replacement", "Le Mans Ultimate Community", "https://community.lemansultimate.com/index.php?threads/crash-game-launching-error.3785/", "Studio 397 community moderator guidance recommends deleting affected track content then verifying files."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-shader-cache",
            Game = "Le Mans Ultimate",
            Issue = "Stale/corrupt shader cache or loading failure",
            Detection = "Loading failures around track/car initialization without package corruption; repeated shader/cache errors or known 48% load behavior.",
            RepairStrategy = "Back up then clear UserData\\Log\\Shaders\\dynamic.cache; optionally clear vendor DX shader cache only when the evidence supports it.",
            Safety = "Automatic/reversible for LMU dynamic.cache; vendor cache requires confirmation",
            Signatures = new[] { "shader", "dynamic.cache", "48%" },
            References = new[]
            {
                Ref("Common Fixes", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes", "Official LMU guidance recommends clearing dynamic.cache for crashes."),
                Ref("48% loading fixes", "Le Mans Ultimate Community", "https://community.lemansultimate.com/index.php?threads/i-cant-connect-to-any-multiplayer-server.3023/", "Community moderator guidance also lists dynamic.cache and NVIDIA DX cache cleanup."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-startup-config",
            Game = "Le Mans Ultimate",
            Issue = "Startup / black-screen / early initialization failure",
            Detection = "LMU exits during early configuration or CEF/video initialization before a session begins.",
            RepairStrategy = "Back up and regenerate Config_DX11/dx11 configuration; if evidence points to CEF, test supported CEF mode values with rollback.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "CEF", "Config_DX11", "dx11_config", "black screen" },
            References = new[]
            {
                Ref("Common Fixes", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes", "Official guidance recommends deleting the DX11 config on launch crashes."),
                Ref("Instant crash after start", "Le Mans Ultimate Community", "https://community.lemansultimate.com/index.php?threads/crash-the-game-instantly-crashes-after-start.8664/", "Moderator guidance includes DX11 config, CEF mode and sound config recovery."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-plugin-conflict",
            Game = "Le Mans Ultimate",
            Issue = "Third-party plugin conflict",
            Detection = "Crash occurs with plugin-related trace evidence or after enabling a known plugin; no stronger hardware/content fault is present.",
            RepairStrategy = "Back up CustomPluginVariables.JSON and temporarily disable plugin entries, then retest.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "CustomPluginVariables", "plugin" },
            References = new[]
            {
                Ref("Common Fixes", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes", "Official guidance says some plugins cause crashes and describes disabling them."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-overlay-conflict",
            Game = "Le Mans Ultimate",
            Issue = "Overlay / hook / tuning software conflict",
            Detection = "LMU loading crash while MSI Afterburner / RivaTuner or similar hook software is running, especially when no stronger content fault exists.",
            RepairStrategy = "Offer to close only the implicated background process for the next LMU launch; do not uninstall or modify overclock settings automatically.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "MSIAfterburner", "RTSS", "RivaTuner" },
            References = new[]
            {
                Ref("Known Issues report", "LMU Reddit community", "https://www.reddit.com/r/LeMansUltimateWEC/comments/1avgqin/", "Early known-issues guidance reported MSI Afterburner/RivaTuner performance problems.", false),
                Ref("LMU crash after 100% loading", "LMU Reddit community", "https://www.reddit.com/r/LeMans_Ultimate/comments/1ryal47/lmu_crashing_after_100_loading/", "A 2026 user report identified Afterburner/RivaTuner as the cause of a loading crash.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-ghub-conflict",
            Game = "Le Mans Ultimate",
            Issue = "Logitech G Hub compatibility/version issue",
            Detection = "LMU launch failure with G Hub running and no stronger signature; only suggest when installed version/state is known.",
            RepairStrategy = "Offer to close G Hub for a controlled retest or direct the user to update it; never silently update third-party software.",
            Safety = "Ask first / community-derived",
            Signatures = new[] { "lghub", "Logitech G HUB" },
            References = new[]
            {
                Ref("Black-screen crash solved by G Hub update", "LMU Reddit community", "https://www.reddit.com/r/LeMansUltimateWEC/comments/1vdpwc2/cant_fix_crashing_issue/", "Recent community report says an outdated G Hub installation caused LMU startup crashes.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-memory-allocation",
            Game = "Le Mans Ultimate",
            Issue = "Virtual memory / page-file allocation failure",
            Detection = "Trace contains 'Failed to allocate a section of memory' or Windows commit pressure is exhausted near the failure.",
            RepairStrategy = "Measure installed RAM and current Windows page-file/commit configuration. Offer a page-file correction only with explicit approval because it changes a Windows system setting and may require restart.",
            Safety = "Significant / always ask",
            Signatures = new[] { "Failed to allocate a section of memory", "out of memory", "allocation" },
            References = new[]
            {
                Ref("Known Issues and Common Fixes", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/", "Current official guidance says a too-small Windows page file can cause LMU memory allocation failures, particularly on systems with less RAM."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-eac",
            Game = "Le Mans Ultimate",
            Issue = "Easy Anti-Cheat installation/startup failure",
            Detection = "LMU trace or launch state reports Easy Anti-Cheat/EAC missing, failed, or not installed.",
            RepairStrategy = "Run LMU's included EasyAntiCheat install/reinstall batch file while the simulator is closed.",
            Safety = "Significant / ask first",
            Signatures = new[] { "Easy Anti-Cheat", "EasyAntiCheat", "EAC", "not installed" },
            References = new[]
            {
                Ref("How do I uninstall or reinstall Easy Anti-Cheat?", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/14524562770447-How-do-I-uninstall-or-reinstall-Easy-Anti-Cheat", "Official LMU support says EAC can be reinstalled using the batch files included in the game's EasyAntiCheat folder."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-reshade-runtime",
            Game = "Le Mans Ultimate",
            Issue = "Unsupported ReShade/custom runtime prevents launch",
            Detection = "Early launch failure or 0xc000007b plus ReShade/custom graphics runtime files in the LMU application directory.",
            RepairStrategy = "Create a recovery backup and temporarily disable only the detected ReShade/custom runtime files, then retest. Never delete them without a recovery copy.",
            Safety = "Reversible / ask first",
            Signatures = new[] { "0xc000007b", "ReShade", "dxgi.dll", "d3d11.dll" },
            References = new[]
            {
                Ref("Known Issues and Advice", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/", "Current official guidance states ReShade is unsupported and can prevent LMU from launching; 0xc000007b can be caused by incompatible local runtime files."),
            }
        },
        new KnowledgeEntry
        {
            Id = "lmu-online-clock-eac",
            Game = "Le Mans Ultimate",
            Issue = "Unable to join online sessions",
            Detection = "RaceControl connection/join failure without local loading crash; compare Windows time settings and EAC launch state.",
            RepairStrategy = "Validate automatic Windows time/time-zone and detect EAC launch path; targeted content verification is a later step.",
            Safety = "Diagnostic first; changes require confirmation",
            Signatures = new[] { "join", "RaceControl", "Easy Anti-Cheat", "EAC" },
            References = new[]
            {
                Ref("Connection Issues", "Le Mans Ultimate Support", "https://guide.lemansultimate.com/hc/en-gb/articles/13192678574095-Connection-Issues", "Official guidance identifies incorrect Windows date/time as a common connection cause and describes file/EAC checks."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-helper-service",
            Game = "iRacing",
            Issue = "iRacing Helper Service stopped / UI cannot launch",
            Detection = "iRacing.com Helper Service absent or stopped while UI launch fails or reports Not In Service.",
            RepairStrategy = "Restart the iRacing service using the installed service/start-stop helpers, then verify service state.",
            Safety = "Automatic / short",
            Signatures = new[] { "Not In Service", "iRacing.com Helper Service", "iRacingService" },
            References = new[]
            {
                Ref("System Not in Service", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000133527-what-does-system-not-in-service-indicate-", "Official support says the helper service should run automatically and can be started manually."),
                Ref("Quick Troubleshooting Launching the iRacing UI", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000162469", "Official first step is restarting the iRacing service."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-ui-cache",
            Game = "iRacing",
            Issue = "iRacing UI black/white screen, frozen UI, or stale Electron cache",
            Detection = "UI process remains alive but fails to render/respond; logs point to Electron/UI state rather than simulator crash.",
            RepairStrategy = "Back up metadata if useful, close the UI, clear %APPDATA%\\iracing-electron, then relaunch.",
            Safety = "Automatic/reversible enough for cache data; short",
            Signatures = new[] { "iracing-electron", "white screen", "black screen", "UI cache" },
            References = new[]
            {
                Ref("Quick Troubleshooting Launching the iRacing UI", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000162469", "Official support recommends deleting the local iracing-electron cache after restarting the service."),
                Ref("UI freeze after update", "iRacing Reddit community", "https://www.reddit.com/r/iRacing/comments/1u22vpd/game_crashing_on_settings_or_quitting/", "Users reported UI-cache cleanup and graphics settings as workarounds during a 2026 regression.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-ui-safe",
            Game = "iRacing",
            Issue = "iRacing UI stuck on Welcome to iRacing",
            Detection = "UI startup stuck/crashing with the supported UISafe setting present in app.ini.",
            RepairStrategy = "Back up app.ini and change UISafe from 1 to 0, then relaunch; restore if ineffective.",
            Safety = "Reversible / short",
            Signatures = new[] { "Welcome to iRacing", "UISafe" },
            References = new[]
            {
                Ref("iRacing UI crashing or stuck on Welcome to iRacing", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000178873-iracing-ui-crashing-or-stuck-on-welcome-to-iracing-", "Official 2026 support guidance specifies UISafe=0 in Documents\\iRacing\\app.ini."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-eac-error73",
            Game = "iRacing",
            Issue = "Easy Anti-Cheat Error 73 / anti-cheat installation failure",
            Detection = "Error 73 or explicit EAC/EOS anti-cheat failure near simulator startup.",
            RepairStrategy = "Run the supported EOS Easy Anti-Cheat uninstall/reinstall/repair workflow, then verify the sim installation path is unique.",
            Safety = "Significant / always ask",
            Signatures = new[] { "Error 73", "Easy Anti-Cheat", "EOS" },
            References = new[]
            {
                Ref("Error 73 Fatal Error Detected", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000173542-error-73-fatal-error-detected", "Official guidance says to reinstall the EOS version of EAC and check for multiple iRacing installs."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-update-verification",
            Game = "iRacing",
            Issue = "Update verification failure",
            Detection = "Updater reports verification failure or repeatedly requests the same update.",
            RepairStrategy = "Rename version_system.txt, retry update; if needed clear the downloads folder. CDN/network changes are advisory rather than automatic.",
            Safety = "Reversible / one-click for local files",
            Signatures = new[] { "Verification Failure", "version_system.txt", "downloads" },
            References = new[]
            {
                Ref("Verification Failure Error while Updating", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000155062-verification-failure-error-while-updating", "Official support documents renaming version_system.txt and clearing downloads."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-content-corruption",
            Game = "iRacing",
            Issue = "Corrupt/missing vehicle content",
            Detection = "Specific car data fails in the UI/3D viewer while other content works.",
            RepairStrategy = "Back up then remove only the affected car content and let iRacing redownload it; broader cars.dat reset only when multiple vehicles are affected.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "car data", "3D model", "cars.dat" },
            References = new[]
            {
                Ref("Quick Troubleshooting Guide Customizing Vehicles", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000162487-quick-troubleshooting-guide-customizing-vehicles", "Official support recommends redownloading a specific car folder when its data is corrupt."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-could-not-find-sim",
            Game = "iRacing",
            Issue = "Could not find sim",
            Detection = "UI reports Could not find sim; correlate VR/OpenXR Toolkit, duplicate installations, OneDrive Documents path, and Windows UTF-8 beta setting.",
            RepairStrategy = "Detect the conflicting condition first; offer only targeted corrective actions. Do not broadly move Documents or uninstall software without approval.",
            Safety = "Diagnostic / ask first",
            Signatures = new[] { "Could not find sim", "OpenXR Toolkit", "OneDrive" },
            References = new[]
            {
                Ref("Could not find sim error message", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000168994--could-not-find-sim-error-message", "Official support lists OpenXR Toolkit, duplicate installs, OneDrive Documents and Windows UTF-8 beta mode as known causes."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-content-file-locked",
            Game = "iRacing",
            Issue = "Steam Content File Locked during iRacing update",
            Detection = "Steam reports Content File Locked while updating iRacing and the Helper Service is running.",
            RepairStrategy = "Stop the iRacing Helper Service to release file handles, begin Steam validation/update work, then restart the service.",
            Safety = "Reversible / approval required because validation may exceed two minutes",
            Signatures = new[] { "Content File Locked", "Helper Service" },
            References = new[]
            {
                Ref("Steam accounts getting Content File Locked error", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000167301-steam-accounts-getting-content-file-locked-error", "Official support's first action is stopping the iRacing Helper Service before retrying the Steam update."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-missing-file-privileges",
            Game = "iRacing",
            Issue = "Steam Missing File Privileges during an iRacing update",
            Detection = "Steam reports Missing File Privileges for iRacing while the iRacing Helper Service may still hold installed files open.",
            RepairStrategy = "Stop the iRacing Helper Service, wait for its file handles to close, start Steam validation/update work, and restart the service afterward.",
            Safety = "Reversible / approval required because Steam validation may exceed two minutes",
            Signatures = new[] { "Missing File Privileges", "missing-file-privileges", "Helper Service" },
            References = new[]
            {
                Ref("Steam accounts getting Content File Locked error", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000167301-steam-accounts-getting-content-file-locked-error", "iRacing officially documents stopping its Helper Service when that process prevents Steam from updating installed files."),
                Ref("Steam Missing file privileges", "iRacing Reddit community", "https://www.reddit.com/r/iRacing/comments/12domdi/steam_missing_file_privileges/", "Multiple iRacing users confirmed across later updates that closing the iRacing Helper process resolved this specific Steam error.", false),
            }
        },

        new KnowledgeEntry
        {
            Id = "iracing-track-loading-errors",
            Game = "iRacing",
            Issue = "Loading Error 49 / 61 / 62 / 72 and damaged track content",
            Detection = "iRacing reports a numbered track loading error or a track-specific content failure during sim launch.",
            RepairStrategy = "Back up and reset track index metadata. For Steam Error 49, start Steam validation; for direct installs, launch iRacingUpdater. A future targeted-car/track parser can narrow deletion to the exact folder when the error provides it.",
            Safety = "Reversible; Steam validation may require approval because it can exceed two minutes",
            Signatures = new[] { "Loading Error 49", "Loading Error 61", "Loading Error 62", "Loading Error 72", "tracks.dat", "version.txt" },
            References = new[]
            {
                Ref("Loading Error 61", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000133597-loading-error-61", "Official guidance identifies damaged track files and recommends redownloading the affected track, then resetting tracks.dat/version.txt if needed."),
                Ref("Loading Error 62", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000159866-loading-error-62", "Official guidance uses the same targeted track redownload and metadata reset workflow."),
                Ref("Loading Error 49 for Steam accounts", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000173881-loading-error-49-for-steam-accounts", "Official Steam guidance recommends file validation and deleting tracks.dat."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-car-loading-errors",
            Game = "iRacing",
            Issue = "Loading Error 22 / 71 and damaged car content",
            Detection = "iRacing reports Loading Error 22/71 or car-data corruption while other content remains available.",
            RepairStrategy = "Prefer targeted removal of the affected vehicle folder when identifiable. If several cars are implicated, reset cars.dat/version.txt and let iRacing redownload content.",
            Safety = "Reversible / one-click when the affected content can be identified",
            Signatures = new[] { "Loading Error 22", "Loading Error 71", "cars.dat", "car data" },
            References = new[]
            {
                Ref("Loading Error 22", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000133596-loading-error-22", "Official guidance says the error usually indicates a bad car file and recommends redownloading the affected car, then resetting cars.dat/version.txt if necessary."),
                Ref("Loading Error 71", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000133592-loading-error-71", "Official guidance identifies out-of-date/corrupt car content and describes removing affected car data and car metadata."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-loading-error-3",
            Game = "iRacing",
            Issue = "Loading Error 3 / damaged user configuration",
            Detection = "iRacing reports Loading Error 3 after simpler car/track causes have not produced a more specific signature.",
            RepairStrategy = "With approval, preserve the entire Documents\\iRacing folder and move the active profile aside so iRacing can generate a clean configuration. Setups, replays and paints remain in the backup.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "Loading Error 3", "Documents\\iRacing", "iRacing.old" },
            References = new[]
            {
                Ref("Loading Error 3", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000154681-loading-error-3", "Official support lists damaged car/track data and a damaged Documents\\iRacing profile as possible causes; its final configuration-reset step is to rename the current Documents\\iRacing folder so a clean one is generated."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-trueforce-stale-state",
            Game = "iRacing",
            Issue = "iRacing is already running / simulator failed to close",
            Detection = "iRacing reports the simulator is already running or a stale simulator process remains after exit.",
            RepairStrategy = "Back up app.ini and disable both TrueForce and Logitech hardware-lighting integrations, matching iRacing's documented workarounds for simulator shutdown/stale-process conflicts.",
            Safety = "Reversible / ask first because a stale simulator process may be closed",
            Signatures = new[] { "iRacing is already running", "sim is already running", "trueForceEnabled", "enableLogitechLED", "Please close the simulator" },
            References = new[]
            {
                Ref("iRacing is already running message", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000163228--iracing-is-already-running-message", "Official support documents a Logitech G HUB TrueForce conflict and the trueForceEnabled=0 workaround."),
                Ref("Please close the simulator if you wish to launch a new session", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000149330-please-close-the-simulator-if-you-wish-to-launch-a-new-session", "Official support also documents Logitech hardware lighting as a shutdown conflict and recommends enableLogitechLED=0."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-compatibility-flags",
            Game = "iRacing",
            Issue = "Failed CreateProcessAsUser / incompatible Windows compatibility override",
            Detection = "Launch log reports CreateProcessAsUser failure or Windows compatibility mode/admin override is applied to iRacing executables.",
            RepairStrategy = "Back up AppCompat registry values and remove only Run-as-administrator/legacy Windows compatibility tokens for iRacing executables.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "CreateProcessAsUser", "compatibility mode", "RUNASADMIN" },
            References = new[]
            {
                Ref("Failed CreateProcessAsUser error", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000162005-failed-createprocessasuser-error", "Official guidance recommends removing the Run as administrator compatibility setting from iRacingSim64DX11.exe."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-updater-autoinstall",
            Game = "iRacing",
            Issue = "Updater stuck waiting for iRacingService",
            Detection = "Updater log/UI reports Waiting for iRacingService while downloaded update files are ready to install.",
            RepairStrategy = "Launch iRacingUpdater with the supported -autoinstall parameter; if that fails, clear the downloads cache through the separate update-reset playbook.",
            Safety = "Automatic / short",
            Signatures = new[] { "Waiting for iRacingService", "-autoinstall" },
            References = new[]
            {
                Ref("Waiting for iRacingService when Downloading Updates", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000176612-waiting-for-iracingservice-when-downloading-updates", "Official support documents running iRacingUpdater -autoinstall or clearing the downloads folder."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-digital-signature",
            Game = "iRacing",
            Issue = "Digital Signature Failed during update/startup",
            Detection = "iRacing reports Digital Signature Failed while the UI/updater is involved.",
            RepairStrategy = "Close the affected UI/updater state if necessary and launch iRacingUpdater.exe directly.",
            Safety = "Automatic / short",
            Signatures = new[] { "Digital Signature Failed", "iRacingUpdater.exe" },
            References = new[]
            {
                Ref("Digital Signature Check", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000141082-digital-signature-check", "Official support says to close the UI/updater and launch iRacingUpdater.exe directly for Digital Signature Failed."),
            }
        },
        new KnowledgeEntry
        {
            Id = "iracing-windows-integrity",
            Game = "iRacing",
            Issue = "Repeated 0xc0000005 / Windows application access-violation failure",
            Detection = "Windows/iRacing records a repeatable application fault or 0xc0000005 without a more specific content, driver, thermal, or service signature.",
            RepairStrategy = "With explicit approval, run DISM /RestoreHealth followed by sfc /scannow. Driver reinstall, RAM testing, and full iRacing reinstall remain separate/manual because they are broader actions.",
            Safety = "Significant / always ask / often over two minutes",
            Signatures = new[] { "0xc0000005", "Access Violation", "Application fault" },
            References = new[]
            {
                Ref("iRacingSim64DX11.exe Application Error 0xc0000005", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000178953-iracingsim64dx11-exe-application-error-0xc0000005-", "Current official guidance starts with Windows updates, DISM /RestoreHealth and sfc /scannow, then recommends RAM/graphics-driver checks and reinstall if needed."),
            }
        },

        new KnowledgeEntry
        {
            Id = "iracing-renderer-config",
            Game = "iRacing",
            Issue = "Graphics configuration / renderer settings corruption",
            Detection = "Graphics Config crashes or rendererDX11 settings are implicated without stronger GPU driver evidence.",
            RepairStrategy = "Back up rendererDX11*.ini, regenerate graphics config, then compare/restore user monitor settings if needed.",
            Safety = "Reversible / one-click",
            Signatures = new[] { "rendererDX11", "Graphics Config" },
            References = new[]
            {
                Ref("Graphics Config crash discussion", "iRacing Reddit community", "https://www.reddit.com/r/iRacing/comments/1lb3obu/", "Community troubleshooting commonly regenerates rendererDX11 files; treat as corroboration until a matching official article is available.", false),
                Ref("Crash report collection", "iRacing Support", "https://support.iracing.com/support/solutions/articles/31000133598-how-to-get-a-crash-report-to-submit-to-iracing", "Official support confirms Documents\\iRacing crash logs and Windows Application events are key evidence for simulator crashes."),
            }
        },
        new KnowledgeEntry
        {
            Id = "ace-video-settings",
            Game = "Assetto Corsa EVO",
            Issue = "Broken video settings / graphics startup state",
            Detection = "Assetto Corsa EVO exits during startup or logs point to video/graphics initialization. A Video.videosettings file exists in the ACE user-data folder.",
            RepairStrategy = "Back up and remove only Video.videosettings so the simulator can regenerate graphics settings without wiping controls and other user data.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "Video.videosettings", "video settings", "graphics startup" },
            References = new[]
            {
                Ref("TrueForce / video settings workaround", "Assetto Corsa EVO Steam Community", "https://steamcommunity.com/app/3058630/discussions/0/695372460980302280/", "Community reports repeatedly identify Video.videosettings as a recoverable source of startup, graphics and TrueForce state problems. PitMedic backs the file up before resetting it.", false),
                Ref("0.7 update issues - video.videosettings reset", "Assetto Corsa EVO Steam Community", "https://steamcommunity.com/app/3058630/discussions/0/756142145462660478/", "Users report that deleting the video settings file restores resolution/performance after updates.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ace-user-profile",
            Game = "Assetto Corsa EVO",
            Issue = "Corrupt ACE user profile / launch state",
            Detection = "Repeated early launch crash or black/white startup failure without a stronger hardware/driver signature after a targeted video reset is not enough.",
            RepairStrategy = "Preserve the ACE user-data folder, move the active copy aside, and allow Assetto Corsa EVO to build a clean profile on the next launch.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "ACE folder", "Saved Games\\ACE", "Documents\\ACE", "profile corruption" },
            References = new[]
            {
                Ref("0.8 crash at launch - Kunos support response", "Assetto Corsa EVO Steam Community", "https://steamcommunity.com/app/3058630/discussions/0/576046907555585146/", "The accepted post reports Kunos support instructed the user to remove Saved Games\\ACE and Documents\\ACE so the game could regenerate its user data.", false),
                Ref("Game does not start", "Assetto Corsa EVO Steam Community", "https://steamcommunity.com/app/3058630/discussions/0/756142145462660478/", "A large launch-troubleshooting thread contains repeated successful ACE-folder resets; PitMedic uses a reversible move instead of permanent deletion.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ace-steam-content",
            Game = "Assetto Corsa EVO",
            Issue = "Missing or damaged Steam game files",
            Detection = "Assetto Corsa EVO logs or Windows evidence indicate missing/corrupt local game files or a failed asset load without a more specific repair signature.",
            RepairStrategy = "Ask Steam to verify Assetto Corsa EVO and reacquire missing or damaged files.",
            Safety = "Reversible / approval required when expected over two minutes",
            Signatures = new[] { "missing file", "corrupt file", "failed to load" },
            References = new[]
            {
                Ref("Verify Integrity of Game Files", "Steam Support", "https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB", "Valve documents file verification for missing content and crashing games."),
            }
        },
        new KnowledgeEntry
        {
            Id = "raceroom-browser-cache",
            Game = "RaceRoom Racing Experience",
            Issue = "Browser cache / Error 503 / UI loading problem",
            Detection = "RaceRoom reports Error 503, CEF/browser loading errors, or the browser UI fails while the game process remains otherwise functional.",
            RepairStrategy = "Back up and clear BrowserData cache/state so RaceRoom can rebuild its embedded browser data.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "503", "BrowserData", "CEF", "browser cache" },
            References = new[]
            {
                Ref("ERROR 503. How to fix?", "RaceRoom Steam Community", "https://steamcommunity.com/app/211500/discussions/1/587308261888346216/", "Recent RaceRoom discussion identifies Documents\\My Games\\SimBin\\RaceRoom Racing Experience\\BrowserData as the browser cache location and reports cache clearing or file verification as successful fixes.", false),
                Ref("dash.exe overlay / BrowserData", "KW Studios Forum", "https://forum.kw-studios.com/index.php?threads%2Fdash-exe-overlay.20491%2F=", "KW Studios community support also points to the RaceRoom BrowserData folder when troubleshooting browser/HUD state.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "raceroom-graphics-config",
            Game = "RaceRoom Racing Experience",
            Issue = "Broken graphics / resolution configuration",
            Detection = "RaceRoom cannot display correctly, minimizes on launch, shows black output after a GPU/display change, or graphics_options.xml is implicated.",
            RepairStrategy = "Back up and remove UserData\\graphics_options.xml so RaceRoom creates a fresh display configuration.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "graphics_options.xml", "resolution", "refresh rate", "black screen" },
            References = new[]
            {
                Ref("Black screen with AMD GPU", "RaceRoom Steam Community", "https://steamcommunity.com/app/211500/discussions/1/601901942630817124/", "A RaceRoom moderator recommends deleting graphics_options.xml after a GPU change when entering a race produces a black screen.", false),
                Ref("Graphic Resolution - settings off screen", "KW Studios Forum", "https://forum.kw-studios.com/index.php?threads%2Fgraphic-resolution-cannot-change-settings-off-the-screen.19714%2F=", "Community support recommends renaming graphics_options.xml to reset graphics settings.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "raceroom-shader-cache",
            Game = "RaceRoom Racing Experience",
            Issue = "Shader cache corruption",
            Detection = "RaceRoom logs point to shader compilation/cache errors or graphics behavior persists despite normal configuration.",
            RepairStrategy = "Back up and remove UserData\\ShaderCache.bin so the simulator rebuilds shader data.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "ShaderCache.bin", "shader cache", "shader" },
            References = new[]
            {
                Ref("Resolved - Shader caching problems?!", "KW Studios Forum", "https://forum.kw-studios.com/index.php?threads%2Fshader-caching-problems.20904%2F=", "Current community support discusses removing ShaderCache.bin from RaceRoom UserData when troubleshooting shader caching problems.", false),
                Ref("Video driver crashes during gameplay", "RaceRoom Steam Community", "https://steamcommunity.com/app/211500/discussions/1/864977564134310697/", "RaceRoom users report rebuilding ShaderCache.bin as a successful graphics-crash recovery step.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "raceroom-user-config",
            Game = "RaceRoom Racing Experience",
            Issue = "Damaged RaceRoom user configuration",
            Detection = "Repeated startup/loading failure remains after targeted cache/config repairs and evidence points to UserData configuration corruption.",
            RepairStrategy = "Preserve the complete RaceRoom Racing Experience user folder and move the active copy aside so the game can generate a clean profile.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "UserData", "configuration corruption", "RaceRoom Racing Experience.old" },
            References = new[]
            {
                Ref("game crashes, can someone help me?", "RaceRoom Steam Community", "https://forum.kw-studios.com/index.php?threads%2Fcontrol-settings-are-missing-and-game-keeps-going-to-black-screen.19776%2F=", "A RaceRoom moderator recommends renaming the RaceRoom user folder to reset all settings; the reporter later confirmed the game worked. PitMedic preserves the folder as a recovery backup.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "raceroom-steam-content",
            Game = "RaceRoom Racing Experience",
            Issue = "Missing or damaged Steam game files",
            Detection = "RaceRoom logs or Windows evidence indicate missing/corrupt local content or verification-related failures.",
            RepairStrategy = "Ask Steam to verify RaceRoom Racing Experience and reacquire missing or damaged files.",
            Safety = "Reversible / approval required when expected over two minutes",
            Signatures = new[] { "missing file", "corrupt file", "verify file integrity" },
            References = new[]
            {
                Ref("Verify Integrity of Game Files", "Steam Support", "https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB", "Valve documents file verification for missing content and crashing games."),
                Ref("ERROR 503. How to fix?", "RaceRoom Steam Community", "https://steamcommunity.com/app/211500/discussions/1/587308261888346216/", "Recent RaceRoom users also report Steam file verification resolving the issue.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-game-user-settings",
            Game = "Assetto Corsa Competizione",
            Issue = "Broken display or startup settings",
            Detection = "ACC log evidence names GameUserSettings.ini, resolution, or fullscreen configuration near a startup/display failure.",
            RepairStrategy = "Back up and remove only GameUserSettings.ini so ACC can regenerate clean display and startup settings.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "GameUserSettings.ini", "resolution", "fullscreen" },
            References = new[]
            {
                Ref("Assetto Corsa Competizione support forum", "Kunos Simulazioni", "https://www.assettocorsa.net/forum/index.php?forums/acc-troubleshooting.79/", "Kunos directs ACC troubleshooting through simulator logs and configuration evidence before broader recovery actions."),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-engine-config",
            Game = "Assetto Corsa Competizione",
            Issue = "Unreal Engine or graphics configuration failure",
            Detection = "ACC logs identify Engine.ini, DXGI device loss, a rendering-thread exception, or a GPU crash without a stronger content signature.",
            RepairStrategy = "Back up and remove Engine.ini so ACC can regenerate a clean Unreal Engine configuration. PitMedic does not change graphics drivers automatically.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "Engine.ini", "DXGI_ERROR", "D3D device lost", "rendering thread exception" },
            References = new[]
            {
                Ref("Assetto Corsa Competizione support forum", "Kunos Simulazioni", "https://www.assettocorsa.net/forum/index.php?forums/acc-troubleshooting.79/", "Kunos troubleshooting requests the ACC logs and crash evidence needed to distinguish configuration failures from driver or hardware faults."),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-controls",
            Game = "Assetto Corsa Competizione",
            Issue = "Damaged controller or wheel configuration",
            Detection = "ACC reports invalid controller, DirectInput, wheel, or controls.json state around a Controls-menu or input failure.",
            RepairStrategy = "Preserve controls.json, remove the active copy, and allow ACC to create a clean controller configuration. The user must remap controls afterward.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "controls.json", "DirectInput", "controller", "wheel" },
            References = new[]
            {
                Ref("Key bindings reset to default", "ACC Steam Community", "https://steamcommunity.com/app/805550/discussions/0/562535555862292385/", "ACC users document moving controls.json aside as a targeted way to regenerate controller bindings.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-ffb",
            Game = "Assetto Corsa Competizione",
            Issue = "Invalid or damaged force-feedback settings",
            Detection = "ACC log evidence identifies FFB or ffbUserSettings.json as failed, invalid, or unreadable.",
            RepairStrategy = "Preserve ffbUserSettings.json and remove only the active copy so ACC can regenerate force-feedback settings.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "ffbUserSettings.json", "FFB", "force feedback" },
            References = new[]
            {
                Ref("ACC v1.6 update notes", "Kunos Simulazioni", "https://store.steampowered.com/news/posts/?appids=805550&enddate=1607333042&feed=steam_community_announcements", "Kunos documents that per-car force-feedback values are stored in ffbUserSettings.json."),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-trueforce",
            Game = "Assetto Corsa Competizione",
            Issue = "Logitech TrueForce or manufacturer-library conflict",
            Detection = "ACC reports TrueForce, manufacturer extras, or wheel-library failure near a controller-related crash.",
            RepairStrategy = "Back up controls.json and set enableManufacturerExtras to false so ACC stops calling external wheel-manufacturer libraries during the controlled retest.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "TrueForce", "enableManufacturerExtras", "manufacturer extras" },
            References = new[]
            {
                Ref("ACC v1.5.7 and v1.6.1 update notes", "Kunos Simulazioni", "https://store.steampowered.com/news/posts/?appids=805550&enddate=1607333042&feed=steam_community_announcements", "Kunos explicitly documents enableManufacturerExtras=false as a troubleshooting control for Logitech, Thrustmaster, and Fanatec libraries."),
                Ref("UE4 AC2 fatal error and TrueForce workaround", "ACC Steam Community", "https://steamcommunity.com/app/805550/discussions/0/2967271684630200427/", "A Kunos developer response supplied the same controls.json change while a TrueForce crash was under investigation.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-control-presets",
            Game = "Assetto Corsa Competizione",
            Issue = "Saved control preset crashes the Controls menu",
            Detection = "ACC identifies a saved Customs\\Controls preset as invalid or failed immediately before a Controls-menu crash.",
            RepairStrategy = "Preserve the complete saved control-preset folder and move the active copy aside for a clean Controls-menu retest.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "Customs\\Controls", "control preset", "Controls menu" },
            References = new[]
            {
                Ref("Assetto Corsa Competizione support forum", "Kunos Simulazioni", "https://www.assettocorsa.net/forum/index.php?forums/acc-troubleshooting.79/", "ACC troubleshooting reports identify Customs\\Controls as the saved preset location; PitMedic acts only when the implicated preset is named and moves it intact rather than deleting it.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-user-profile",
            Game = "Assetto Corsa Competizione",
            Issue = "Damaged ACC user configuration",
            Detection = "Repeated startup failure remains after targeted repairs and ACC reports corrupt or unparseable configuration state.",
            RepairStrategy = "Preserve the ACC Documents configuration and LocalAppData Saved\\Config folders, then move the active copies aside so ACC can generate clean settings.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "configuration corruption", "parse error", "Saved\\Config" },
            References = new[]
            {
                Ref("Assetto Corsa Competizione support forum", "Kunos Simulazioni", "https://www.assettocorsa.net/forum/index.php?forums/acc-troubleshooting.79/", "Kunos support uses ACC configuration and crash evidence to isolate persistent user-state failures; PitMedic preserves all moved data for rollback."),
            }
        },
        new KnowledgeEntry
        {
            Id = "acc-steam-content",
            Game = "Assetto Corsa Competizione",
            Issue = "Missing or damaged ACC game files",
            Detection = "ACC explicitly reports a missing, corrupt, or unreadable game package or asset rather than a driver, Windows, or optional plugin file.",
            RepairStrategy = "Ask Steam to verify Assetto Corsa Competizione and reacquire missing or damaged game files.",
            Safety = "Reversible / approval required when expected over two minutes",
            Signatures = new[] { ".pak", "pak file", "asset registry", "failed to load package" },
            References = new[]
            {
                Ref("Verify Integrity of Game Files", "Steam Support", "https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB", "Valve documents file verification for missing or damaged game content."),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-graphics-config",
            Game = "Automobilista 2",
            Issue = "Broken graphics or display configuration",
            Detection = "AMS2 evidence identifies graphicsconfigdx11.xml near a display, startup, or settings failure.",
            RepairStrategy = "Preserve graphicsconfigdx11.xml and remove the active copy so AMS2 can regenerate clean display settings.",
            Safety = "Automatic / reversible / one-click",
            Signatures = new[] { "graphicsconfigdx11.xml", "graphics configuration", "display settings" },
            References = new[]
            {
                Ref("Graphics settings reset on launch", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/graphics-setting-are-not-saved-reset-on-game-launch.35342/", "Reiza staff and users document regenerating AMS2 Documents configuration while investigating graphics-state failures.", false),
                Ref("Can't access Options", "AMS2 Steam Community", "https://steamcommunity.com/app/1066890/discussions/0/586181727714649000/", "A resolved report confirms that removing only graphicsconfigdx11.xml allowed AMS2 to regenerate the correct display state.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-vr-config",
            Game = "Automobilista 2",
            Issue = "Broken OpenVR or Oculus configuration",
            Detection = "AMS2 evidence names OpenVR, Oculus, or the related graphics/settings XML files near a VR startup failure.",
            RepairStrategy = "Back up only the OpenVR/Oculus graphics and settings XML files, then remove the active copies so VR configuration can be regenerated.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "graphicsconfigopenvrdx11.xml", "openvrsettings.xml", "graphicsconfigoculusdx11.xml", "oculussettings.xml" },
            References = new[]
            {
                Ref("Automobilista 2 troubleshooting", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/troubleshooting-automobilista-2.9860/", "The Reiza troubleshooting forum documents simulator-specific configuration isolation and retesting for VR and startup problems.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-controller-config",
            Game = "Automobilista 2",
            Issue = "Controller input or calibration is lost",
            Detection = "AMS2 evidence identifies controller settings or default.controllersettings.v1.03.sav after an update, USB-port change, or driver change.",
            RepairStrategy = "Preserve and remove only default.controllersettings.v1.03.sav so wheel/controller bindings can be rebuilt without resetting unrelated game settings.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "default.controllersettings.v1.03.sav", "controller config", "calibration" },
            References = new[]
            {
                Ref("Automobilista 2 v1.5.5.0 development update", "Reiza Studios", "https://store.steampowered.com/news/posts/?appgroupname=Automobilista+2&appids=1066890&enddate=1707913912&feed=steam_community_announcements", "Reiza explicitly recommends deleting only default.controllersettings.v1.03.sav when controller input or calibration is lost after updates or hardware changes."),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-ffb-custom",
            Game = "Automobilista 2",
            Issue = "Damaged custom force-feedback configuration",
            Detection = "AMS2 evidence names ffb_custom_settings.txt near a force-feedback initialization or parsing problem.",
            RepairStrategy = "Preserve ffb_custom_settings.txt and remove only the active copy so AMS2 can return to a clean/default force-feedback state.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "ffb_custom_settings.txt", "custom FFB", "force feedback" },
            References = new[]
            {
                Ref("Automobilista 2 FFB discussion", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/automobilista-2-custom-force-feedback-overview-recommendations.11135/", "The Reiza community documents ffb_custom_settings.txt as AMS2's custom force-feedback configuration file.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-tuning-setups",
            Game = "Automobilista 2",
            Issue = "Car setups are incompatible after a physics update",
            Detection = "AMS2 evidence points to tuning setup or vehiclesetups data while an affected car or setup fails after an update.",
            RepairStrategy = "Preserve the tuning setup folders and move their active copies aside so the affected cars can load without stale setup data.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "tuningsetups", "vehiclesetups", "tuning setup" },
            References = new[]
            {
                Ref("AMS2 file backup locations", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/file-backup.32850/", "The Reiza community identifies vehiclesetups folders as the location of saved car setup data; PitMedic preserves the folders intact.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-championship-state",
            Game = "Automobilista 2",
            Issue = "Corrupted custom championship state",
            Detection = "A championship or singlechamps save file changed immediately before a Championship-mode failure and no stronger system fault is present.",
            RepairStrategy = "Preserve only the implicated championship save-state files and move them aside so AMS2 can rebuild the mode without deleting other profile data.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "championship", "singlechamps", "championship save" },
            References = new[]
            {
                Ref("Automobilista 2 troubleshooting", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/troubleshooting-automobilista-2.9860/", "Reiza's troubleshooting forum establishes the user save hierarchy used to isolate mode-specific state; PitMedic acts only when recent championship evidence exists.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-default-profile",
            Game = "Automobilista 2",
            Issue = "Damaged default profile settings",
            Detection = "AMS2 reports profile corruption involving default.sav rather than the controller-specific profile file.",
            RepairStrategy = "Preserve default.sav and remove only the active copy so AMS2 can rebuild default profile state.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "default.sav", "profile corruption", "invalid profile" },
            References = new[]
            {
                Ref("Steering linearity and profile recovery", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/steering-linearity.9686/", "AMS2 community troubleshooting documents resetting profile save files when in-game reset and recalibration do not correct damaged state.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-user-profile",
            Game = "Automobilista 2",
            Issue = "Damaged Automobilista 2 user profile",
            Detection = "Repeated startup failure remains after targeted repairs and AMS2 evidence identifies corrupt, invalid, or unparseable profile state.",
            RepairStrategy = "Preserve the complete Documents\\Automobilista 2 folder and move the active copy aside so AMS2 can generate clean user data.",
            Safety = "Significant / reversible / ask first",
            Signatures = new[] { "profile corrupt", "invalid profile", "Documents\\Automobilista 2" },
            References = new[]
            {
                Ref("Crashing on game startup", "Reiza Studios Forum", "https://forum.reizastudios.com/tags/crash-to-desktop/", "A resolved Reiza forum report confirms that regenerating the Documents\\Automobilista 2 folder restored startup. PitMedic preserves the original folder instead of deleting it.", false),
                Ref("Graphics settings reset on launch", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/graphics-setting-are-not-saved-reset-on-game-launch.35342/", "Reiza staff list regenerating the AMS2 Documents folder as a broader recovery after targeted steps fail.", false),
            }
        },
        new KnowledgeEntry
        {
            Id = "ams2-steam-content",
            Game = "Automobilista 2",
            Issue = "Missing or damaged Automobilista 2 game files",
            Detection = "AMS2 evidence explicitly reports a missing, corrupt, or unreadable game file or package.",
            RepairStrategy = "Ask Steam to verify Automobilista 2 and reacquire missing or damaged game files.",
            Safety = "Reversible / approval required when expected over two minutes",
            Signatures = new[] { "missing file", "corrupt package", "failed to load" },
            References = new[]
            {
                Ref("Verify Integrity of Game Files", "Steam Support", "https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB", "Valve documents file verification for missing or damaged game content."),
                Ref("Graphics settings reset on launch", "Reiza Studios Forum", "https://forum.reizastudios.com/threads/graphics-setting-are-not-saved-reset-on-game-launch.35342/", "Reiza staff include Steam file verification among the supported AMS2 troubleshooting steps.", false),
            }
        },
    };

    public static KnowledgeEntry? Find(string id) => Entries.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string? PublicIdForPlan(string planId)
    {
        var id = planId switch
        {
            "lmu-targeted-content-reacquire" => "lmu-content-corruption",
            "lmu-steam-verify" => "lmu-content-corruption",
            "lmu-reset-dx11-config" => "lmu-startup-config",
            "lmu-disable-plugins" => "lmu-plugin-conflict",
            "lmu-reinstall-eac" => "lmu-eac",
            "lmu-quarantine-reshade" => "lmu-reshade-runtime",
            "lmu-sync-windows-time" => "lmu-online-clock-eac",
            "lmu-close-overlay-tools" => "lmu-overlay-conflict",
            "lmu-close-ghub" => "lmu-ghub-conflict",
            "iracing-renderer-reset" => "iracing-renderer-config",
            "iracing-eac-reinstall" => "iracing-eac-error73",
            "iracing-update-reset" => "iracing-update-verification",
            "iracing-track-index-reset" => "iracing-track-loading-errors",
            "iracing-car-index-reset" => "iracing-car-loading-errors",
            "iracing-steam-track-repair" => "iracing-track-loading-errors",
            "iracing-release-content-lock" => "iracing-content-file-locked",
            "iracing-release-file-privileges" => "iracing-missing-file-privileges",
            "iracing-trueforce-disable" => "iracing-trueforce-stale-state",
            "iracing-logitech-led-disable" => "iracing-trueforce-stale-state",
            "iracing-logitech-shutdown-workaround" => "iracing-trueforce-stale-state",
            "iracing-reset-user-config" => "iracing-loading-error-3",
            "iracing-clear-compatibility" => "iracing-compatibility-flags",
            "iracing-updater-autoinstall" => "iracing-updater-autoinstall",
            "iracing-run-updater" => "iracing-digital-signature",
            "iracing-windows-integrity" => "iracing-windows-integrity",
            "ace-video-settings-reset" => "ace-video-settings",
            "ace-profile-reset" => "ace-user-profile",
            "ace-steam-verify" => "ace-steam-content",
            "raceroom-browser-reset" => "raceroom-browser-cache",
            "raceroom-graphics-reset" => "raceroom-graphics-config",
            "raceroom-shader-reset" => "raceroom-shader-cache",
            "raceroom-profile-reset" => "raceroom-user-config",
            "raceroom-steam-verify" => "raceroom-steam-content",
            "acc-game-user-settings-reset" => "acc-game-user-settings",
            "acc-engine-config-reset" => "acc-engine-config",
            "acc-controls-reset" => "acc-controls",
            "acc-ffb-reset" => "acc-ffb",
            "acc-trueforce-disable" => "acc-trueforce",
            "acc-control-presets-reset" => "acc-control-presets",
            "acc-profile-reset" => "acc-user-profile",
            "acc-steam-verify" => "acc-steam-content",
            "ams2-graphics-reset" => "ams2-graphics-config",
            "ams2-vr-reset" => "ams2-vr-config",
            "ams2-controller-reset" => "ams2-controller-config",
            "ams2-ffb-reset" => "ams2-ffb-custom",
            "ams2-tuning-reset" => "ams2-tuning-setups",
            "ams2-championship-reset" => "ams2-championship-state",
            "ams2-default-profile-reset" => "ams2-default-profile",
            "ams2-profile-reset" => "ams2-user-profile",
            "ams2-steam-verify" => "ams2-steam-content",
            _ => planId
        };
        return Find(id) is not null || CompanionRecoveryPolicy.IsSupportedRepairId(id) ? id : null;
    }

    public static string? DiagnosticLibraryUrlForPlan(string planId)
    {
        var id = PublicIdForPlan(planId);
        return id is null ? null : $"https://pitmedic.com/diagnostic-library/{id}/";
    }

    public static IReadOnlyList<RepairReference> ReferencesForPlan(string planId)
    {
        var id = PublicIdForPlan(planId);
        return id is null ? Array.Empty<RepairReference>() : Find(id)?.References ?? Array.Empty<RepairReference>();
    }
}
