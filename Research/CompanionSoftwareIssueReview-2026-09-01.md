# Companion-software issue review

Review date: 2026-09-01

## Release outcome

PitMedic v0.6.0.9 will treat wheelbase and peripheral utilities as companion software instead of simulators. The Home page shows a single Companion Software card only when at least one supported app is installed or running. Undetected apps are omitted.

For every supported app, the safe automatic recovery is the same narrow operation: after a confirmed application fault, matching crash dump, or non-zero process exit, PitMedic waits until every supported simulator is closed, asks for approval, closes only that app's known processes, relaunches the validated executable captured from the failed process, and verifies that it remains running. It does not change profiles, drivers, firmware, security settings, or global Windows settings.

## Supported applications and verified issue handling

| Companion app | Verified issue | v0.6.0.9 handling | Evidence |
|---|---|---|---|
| MOZA Pit House | App crashes; startup can also fail when the documented Visual C++ 2015–2019 x86 prerequisite or maintenance components are unavailable | Confirmed crash: approved automatic restart. Startup prerequisite/updater problems: guided official install/repair guidance | [MOZA manual](https://support.mozaracing.com/en/support/solutions/articles/70000625635-moza-pit-house-user-manual), [MOZA FAQs](https://support.mozaracing.com/en/support/solutions/articles/70000627928-moza-pit-house-faqs) |
| Simucube True Drive | App crash; a downloaded True Drive build can be blocked by Windows; current releases document runtime/startup fixes | Confirmed crash: approved automatic restart. Blocked-file/runtime cases: guided check only | [True Drive releases](https://granitedevices.com/wiki/Simucube_2_True_Drive_releases), [confirmed blocked-file case](https://community.granitedevices.com/t/true-drive-update-not-starting/10772) |
| Fanatec software | App telemetry can stop after the app is backgrounded or crashes | Confirmed crash: approved automatic restart. Feed-only failure: guided app restart/update | [Fanatec support](https://help.fanatec.com/hc/en-us/articles/47862678424593-The-game-telemetry-function-of-the-Fanatec-app-is-no-longer-working) |
| Logitech G HUB | Agent/UI crash, spinning-logo loop, or stale agent/UI/updater processes | Approved automatic close-and-restart of the known G HUB process set after a confirmed fault | [Loading-loop recovery](https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops), [install/update troubleshooting](https://support.logi.com/hc/en-150/articles/360023192454-G-HUB-Install-Uninstall-Update-Troubleshooting) |
| SIMAGIC SimPro Manager | App exits and a hidden/stale SimPro process can prevent reopening | Approved automatic close-and-restart of known SimPro/daemon processes after a confirmed fault | [SIMAGIC download center](https://simagic.com/pages/download-center), [corroborated stale-process reports](https://www.reddit.com/r/Simagic/comments/1qgzd6o/simpro_manager_suddenly_turning_off_and_not/) |
| Asetek RaceHub | App or elevated helper can stop; Asetek recommends restart/update and log collection first | Approved automatic close-and-restart after a confirmed fault. Cache/profile repair remains guided and backup-gated until an exact signature is present | [Asetek troubleshooting](https://www.asetek.com/simsports/knowledge-base/troubleshooting/), [RaceHub logs](https://www.asetek.com/simsports/knowledge-base/how-to-find-serial-number-and-racehub-log-files/) |
| VRS DirectForce | Configuration-app crash | Approved automatic restart after a confirmed fault; unresolved device faults route to current vendor software/support guidance | [VRS hardware and software](https://vrs.racing/hardware) |

## Safety decisions

- A normal user-initiated app close is ignored.
- An exit without matching Windows/dump evidence is ignored unless the process returned a non-zero code.
- Generic recovery never edits vendor-owned configuration data.
- G HUB and SimPro recovery can close their documented agent/daemon companions, but not unrelated simulator or device processes.
- Asetek wheel/display cache resets are not triggered by a generic crash. They require a future exact signature, user approval, and a verified backup.
- Firmware flashing, driver replacement, device resets, exploit-protection changes, Defender exclusions, and third-party DLL downloads are excluded.

## Next-release follow-up

The next release replaces the shared generic restart identifier with one recovery policy per vendor. This makes the affected process set, repair title, steps, elevation requirements, and UI coverage explicit for each monitored app.

- Logitech G HUB now follows Logitech's documented loading-loop order: close G HUB UI/agent processes, leave user settings intact, restart `LGHUBUpdaterService` through PitMedic's allowlisted elevated helper, relaunch G HUB, and verify it remains running.
- Fanatec recovery explicitly closes the Fanatec app, Control Panel, and FanaLab set before relaunch, matching Fanatec's official restart guidance for a stalled telemetry feed.
- SimPro recovery explicitly covers conflicts between SimPro 2, SimPro 3, and the SimPro daemon before relaunching the captured installed generation.
- Pit House, True Drive, RaceHub, and VRS retain scoped clean recovery because their deeper official paths involve runtime installation, Windows file unblocking, application updates, drivers, firmware, or insufficiently documented vendor state. Those actions remain guided until PitMedic has a deterministic signature and a safe rollback.
