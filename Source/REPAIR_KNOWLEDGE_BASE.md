# PitMedic Repair Knowledge Base — v0.4.4.0

This document summarizes the repair research represented in the executable knowledge base. Official vendor guidance is preferred. Community reports are used only when the remediation is narrow/reversible or as corroborating evidence.

## Le Mans Ultimate
Current automated categories include targeted/general Steam content repair, shader/cache rebuild, DX11 configuration reset, plugin disable/reset, EAC reinstall, ReShade/local-hook quarantine, Windows time synchronization, and controlled retests with implicated RTSS/Afterburner or G HUB processes closed.

Primary references:
- https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes
- https://guide.lemansultimate.com/hc/en-gb/articles/13192678574095-Connection-Issues
- https://guide.lemansultimate.com/hc/en-gb/articles/14524562770447-How-do-I-uninstall-or-reinstall-Easy-Anti-Cheat

## iRacing
Current automated categories cover Helper Service recovery, Electron UI cache, UISafe, EAC, update verification/autoinstall, track/car content metadata, Steam content locks, renderer configuration, Logitech shutdown state, user configuration, AppCompat flags, updater/digital-signature failures, and approved DISM/SFC Windows-integrity repair.

Primary references:
- https://support.iracing.com/support/solutions/articles/31000162469
- https://support.iracing.com/support/solutions/articles/31000178873-iracing-ui-crashing-or-stuck-on-welcome-to-iracing-
- https://support.iracing.com/support/solutions/articles/31000133527-what-does-system-not-in-service-indicate-
- https://support.iracing.com/support/solutions/articles/31000173542-error-73-fatal-error-detected
- https://support.iracing.com/support/solutions/articles/31000155062-verification-failure-error-while-updating
- https://support.iracing.com/support/solutions/articles/31000178953-iracingsim64dx11-exe-application-error-0xc0000005-

## Assetto Corsa EVO

| Pattern | PitMedic repair | Evidence basis |
|---|---|---|
| Video/graphics startup state, poor FPS after settings/update | Preserve/reset `Video.videosettings` | Multiple AC EVO community reports relay this targeted workaround; it avoids wiping the rest of the profile |
| Repeated startup crash / corrupt ACE user data | Preserve and move `Saved Games\ACE` and/or `Documents\ACE` aside | Accepted Steam answer reports Kunos support instructed deletion of these ACE folders; PitMedic substitutes a reversible move |
| Missing/damaged local files | Steam verification | Standard Steam integrity recovery |

References:
- https://steamcommunity.com/app/3058630/discussions/0/576046907555585146/
- https://steamcommunity.com/app/3058630/discussions/0/695372460980302280/
- https://www.assettocorsa.net/forum/
- https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB

Not automated: antivirus/Defender exclusions, GPU-driver replacement, global NVIDIA/AMD cache deletion, wheel-driver/firmware changes, or broad Windows changes.

## RaceRoom Racing Experience

| Pattern | PitMedic repair | Evidence basis |
|---|---|---|
| Error 503 / embedded-browser UI state | Preserve/clear `BrowserData` | Recent RaceRoom Steam discussion reports BrowserData deletion or Steam verification resolving Error 503 |
| Black screen / bad display or resolution state | Preserve/reset `graphics_options.xml` | KW Studios community support repeatedly recommends renaming/deleting this file so RaceRoom regenerates defaults |
| Shader-cache corruption / some graphics startup problems | Preserve/reset `ShaderCache.bin` | RaceRoom community reports successful cache rebuilds; file is generated state |
| Persistent damaged user configuration | Preserve/move the whole RaceRoom profile aside | KW Studios community support recommends a fresh profile when targeted config resets do not resolve the problem |
| Missing/damaged local files | Steam verification | Standard Steam integrity recovery; also reported in recent RaceRoom Error 503 troubleshooting |

References:
- https://steamcommunity.com/app/211500/discussions/1/587308261888346216/
- https://forum.kw-studios.com/index.php?threads/graphic-resolution-cannot-change-settings-off-the-screen.19714/
- https://forum.kw-studios.com/index.php?threads/control-settings-are-missing-and-game-keeps-going-to-black-screen.19776/
- https://steamcommunity.com/app/211500/discussions/1/601901942630817124/
- https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB

Not automated: changing Steam's global administrator state, old CEF launch flags, driver changes, antivirus/firewall changes, or broad OS/network modifications.

## Trust policy
PitMedic may automatically perform a short, low-risk file/cache/config reset when it can preserve the previous state. Repairs expected over 2 minutes and significant repairs require approval. Broad system/security/driver changes remain diagnostic or guided-only until there is a narrowly scoped and reliably reversible implementation.

## Assetto Corsa Competizione — v0.4.0.0 additions
Automated repair paths now include targeted resets for Unreal display/engine configuration, controller and FFB state, saved control presets, a preserved profile reset, Steam verification, and a targeted Logitech TrueForce/manufacturer-extras compatibility repair when captured evidence specifically points to that integration.

Research references include community troubleshooting where ACC regenerates `GameUserSettings.ini` / `Engine.ini`, `controls.json` resets restore wheel/FFB behavior, and reported Logitech TrueForce startup failures were resolved by disabling manufacturer extras in `controls.json`.

References:
- https://steamcommunity.com/app/805550/discussions/0/3196989419233428550/
- https://steamcommunity.com/app/805550/discussions/0/3606765810641600567/
- https://steamcommunity.com/app/805550/discussions/0/4351114159713676676/
- https://steamcommunity.com/app/805550/discussions/0/2967271684630200427/
- https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB

## Automobilista 2 — v0.4.0.0 additions
Automated repair paths cover graphics configuration, VR settings, controller profiles, custom FFB, stale tuning setups, default profile state, corrupted championship state, full preserved user-profile reset and Steam verification. Championship repair preserves and quarantines the affected `.sav` data rather than deleting it.

References:
- https://forum.reizastudios.com/threads/screen-resolution-issue.32354/
- https://forum.reizastudios.com/threads/original-command-file.35335/
- https://forum.reizastudios.com/threads/ffb-wheel-not-working-now.24196/
- https://forum.reizastudios.com/threads/loading-championship-results-in-game-crash.31545/
- https://forum.reizastudios.com/threads/how-do-you-delete-a-custom-championship-with-mod-tracks.32749/
- https://forum.reizastudios.com/threads/championship-mode-bug.32498/
- https://forum.reizastudios.com/threads/graphics-setting-are-not-saved-reset-on-game-launch.35342/
- https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB

## Companion software

Only installed or running companion apps appear on Home. About lists the full monitored catalog. After a confirmed application fault, crash dump, or non-zero exit, and once every supported simulator is closed, PitMedic offers the affected app's vendor-specific approved recovery.

| App | Known issue represented | Current PitMedic response | Further vendor-backed guidance |
|---|---|---|---|
| MOZA Pit House | Application crash; missing Visual C++ x86 runtime or damaged updater/maintenance components | Approved Pit House process cleanup, validated relaunch, and running-state verification | Guided runtime/installation check; no automatic driver or firmware change |
| Simucube True Drive | Application crash; Windows-blocked downloaded executable; missing runtime prerequisite | Approved True Drive process cleanup, validated relaunch, and running-state verification | Guided unblock/runtime check |
| Fanatec software | Application crash or app telemetry feed stopping after a background failure | Approved Fanatec app, Control Panel, and FanaLab process cleanup followed by validated relaunch | Guided update/reboot when relaunch does not restore the feed |
| Logitech G HUB | Agent crash, spinning-logo/loading loop, or stuck updater service | Approved official-order recovery: close UI/agent, restart `LGHUBUpdaterService`, relaunch, and verify | Guided reinstall only if service recovery fails; settings are never removed automatically |
| SIMAGIC SimPro Manager | Application crash, SimPro 2/3 conflict, or a hidden/stale daemon preventing relaunch | Approved cleanup of known SimPro 2/3/daemon processes followed by relaunch of the captured installed generation | Guided current-build check; firmware changes remain manual |
| Asetek RaceHub | Application crash or stale RaceHub/elevated helper state | Approved RaceHub/elevated-helper cleanup followed by validated relaunch | Guided update/log collection; cache reset requires an exact signature and verified backup |
| VRS DirectForce | Configuration-app crash | Approved VRS configuration-app cleanup, validated relaunch, and running-state verification | Guided vendor support/current-software check for unresolved device faults |

Primary references:

- https://support.mozaracing.com/en/support/solutions/articles/70000625635-moza-pit-house-user-manual
- https://support.mozaracing.com/en/support/solutions/articles/70000627928-moza-pit-house-faqs
- https://granitedevices.com/wiki/Simucube_2_True_Drive_releases
- https://community.granitedevices.com/t/true-drive-update-not-starting/10772
- https://help.fanatec.com/hc/en-us/articles/47862678424593-The-game-telemetry-function-of-the-Fanatec-app-is-no-longer-working
- https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops
- https://support.logi.com/hc/en-150/articles/360023192454-G-HUB-Install-Uninstall-Update-Troubleshooting
- https://simagic.com/pages/download-center
- https://www.asetek.com/simsports/knowledge-base/troubleshooting/
- https://www.asetek.com/simsports/knowledge-base/how-to-find-serial-number-and-racehub-log-files/
- https://vrs.racing/hardware
