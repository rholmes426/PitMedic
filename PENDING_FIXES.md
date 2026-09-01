# Pending fixes

## v0.6.0.9

- [x] Move update checking to the top of Settings and present the manual check as a prominent primary action.

- [x] Allow an anonymous-usage heartbeat to send immediately when the app version, release channel, or installation type changes, even when another heartbeat was attempted or accepted within the previous hour.
  - Preserve the one-hour retry throttle for repeated failed attempts with the same usage dimensions.
  - Record the dimensions associated with the most recent attempt so older state files migrate safely and do not suppress the first heartbeat from an updated app.
  - Continue reusing the existing rotating daily and monthly tokens so the dashboard row is updated without increasing active-installation totals or sending a duplicate installation alert.
  - Add coverage for upgrading within one hour of a successful heartbeat and for retrying a failed heartbeat with unchanged dimensions.

- [x] Preserve iRacing live detector signatures when Windows crash evidence supplies the displayed classification.
  - Ensure the elevated helper reconstructs the same narrow repair selected by the normal app, including `iracing-eac-reinstall`.
  - Prevent repeated “saved diagnosis no longer matches” failures where an EAC repair is reconstructed as `iracing-windows-integrity`.
  - Keep the elevated helper's independent validation and allowlist checks intact.

- [x] Make Automobilista 2 distance telemetry reliable and diagnosable.
  - Detect when AMS2 is running without its `Project CARS 2` shared-memory feed and show setup guidance instead of silently displaying `0.0` distance.
  - Integrate AMS2's simulator-reported speed as a distance fallback when the shared-memory odometer is unavailable, invalid, or not advancing.
  - Avoid double-counting when switching between odometer deltas and speed integration.
  - Persist the final session increment before the AMS2 adapter is stopped.

- [x] Add detected-only companion-software reliability monitoring and approved automatic recovery.
  - Detect installed or running MOZA Pit House, Simucube True Drive, Fanatec software, Logitech G HUB, SIMAGIC SimPro Manager, Asetek RaceHub, and VRS DirectForce.
  - Show one conditional Companion Software card on Home; hide both the card and all undetected apps.
  - Capture only confirmed faults: matching Windows Application Error/Hang/WER evidence, a matching crash dump, or a non-zero process exit.
  - Preserve the app identity, executable path, Windows events, dumps, and surrounding CPU/GPU telemetry in the normal finding/history workflow.
  - After every supported simulator is closed, offer an approval-gated one-click restart that closes only the affected vendor app's process set and relaunches the validated captured executable.
  - Never change wheel profiles, drivers, firmware, security controls, or global Windows configuration through generic companion-app recovery.
  - Include vendor-specific known-issue guidance for Pit House runtimes/updater health, True Drive blocked files/runtimes, Fanatec telemetry stoppage, G HUB loading loops, SimPro hidden processes, RaceHub logs/cache, and VRS current-software support.

- [x] Add the verified simulator forum findings from `Research/SimulatorForumGapReview-2026-09-01.md` to the v0.6.0.9 diagnostic and repair backlog.
  - Le Mans Ultimate: locale separator launch check, scoped sound-config reset, official four-file input reset, and LMU-only Steam Input A/B guidance.
  - iRacing: exact Loading Error 77 and 63 routing, audio-exclusive stutter guidance, VC++ x64 prerequisite check, Simagic 360 Hz/haptic traffic guidance, replay-spooling storage diagnostics, and backed-up `connect_sockets` A/B testing.
  - Assetto Corsa EVO: VC++ x64 health and secondary XInput/binding conflicts; add exact Controlled Folder Access correlation while leaving pipeline-library cleanup on the watchlist.
  - RaceRoom: VC++ 2010 SP1 signature, RX 9000-series black-screen sequence, and Steam elevation/compatibility diagnostics.
  - Assetto Corsa Competizione: MoTeC storage warning/approved archive and exact Controlled Folder Access correlation.
  - Automobilista 2: wheel-enumeration/binding timing guidance and exact Controlled Folder Access/read-only correlation.
  - Keep security disabling, broad administrator changes, third-party DLL downloads, unverified driver changes, and destructive profile deletion outside automatic repair.
