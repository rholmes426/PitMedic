# PitMedic v0.4.4.0 Repair Automation Matrix

PitMedic prioritizes narrow, repeatable and reversible repairs. Repairs expected to take more than 2 minutes require explicit approval; significant repairs require approval regardless of duration.

Ordinary monitoring and user-profile repairs run without administrator rights. Repairs that touch protected simulator-install files, an installed service or anti-cheat component, Windows time synchronization, or Windows integrity tools run through the one-shot elevated helper. The helper reconstructs the repair from the stored incident, accepts only IDs in `ElevatedRepairPolicy`, reports status through a current-user-only pipe, and exits when that repair finishes.

## Le Mans Ultimate — 9 automated fix categories
Targeted/general Steam content repair; shader/cache rebuild; DX11 configuration reset; plugin disable/reset; Easy Anti-Cheat reinstall; ReShade/local-hook quarantine; Windows time synchronization; controlled RTSS/Afterburner retest; controlled G HUB retest.

## iRacing — 16 automated fix categories
Helper Service recovery; Electron UI cache; UISafe; EOS anti-cheat recovery; update verification/autoinstall; track/car content metadata; Steam content locks; renderer configuration; Logitech shutdown state; user configuration; AppCompat flags; updater/signature failures; Windows integrity repair and related supported content paths.

## Assetto Corsa EVO — 3 automated fix categories
Video settings reset; preserved user-profile reset; Steam file verification.

## RaceRoom Racing Experience — 5 automated fix categories
BrowserData refresh; graphics_options.xml reset; ShaderCache.bin rebuild; preserved user-profile reset; Steam file verification.

## Assetto Corsa Competizione — 8 automated fix categories
- GameUserSettings.ini reset for broken display/startup state.
- Engine.ini reset for engine/graphics configuration failures.
- controls.json reset for wheel/controller configuration corruption.
- ffbUserSettings.json reset for force-feedback state corruption.
- Targeted `enableManufacturerExtras=false` repair when captured evidence points to Logitech TrueForce/manufacturer-extras failure.
- Quarantine saved Controls presets when an old preset is implicated in Controls-menu failure.
- Preserved ACC configuration/profile reset.
- Steam file verification (App ID 805550).

## Automobilista 2 — 9 automated fix categories
- graphicsconfigdx11.xml reset for display/startup state.
- OpenVR/Oculus configuration reset.
- default.controllersettings.v1.03.sav reset for wheel/controller state.
- ffb_custom_settings.txt reset.
- Preserve/move stale tuning setup data.
- default.sav reset for damaged default profile state.
- Preserve/quarantine corrupted custom championship save state.
- Preserved full Documents\Automobilista 2 profile reset.
- Steam file verification (App ID 1066890).

## Monitoring coverage
- iRacing: dedicated live-log monitor plus process state, Windows events and exit-time collection.
- Assetto Corsa EVO: passive live tailing of supported simulator-owned text logs when present, plus process/events/exit-time collection.
- RaceRoom: passive live tailing of UserData\Log files, plus crash-dump collection and Windows events.
- Assetto Corsa Competizione: passive live tailing of `%LOCALAPPDATA%\AC2\Saved\Logs`, plus Unreal crash folders, Windows events and exit-time evidence.
- Automobilista 2: process state, telemetry, Windows events, Windows crash dumps and exit-time evidence. AMS2 does not expose a consistently documented always-on support text log, so PitMedic does not invent an unstable live-log path.

## Not automated by design
Broad security, driver, BIOS/overclock, page-file, firewall/Defender, router and operating-system changes remain diagnostic/guided rather than automatic unless a future implementation is narrowly scoped and safely reversible.
