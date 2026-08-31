# PitMedic v0.6.0.5

PitMedic is a Windows .NET 10 WPF simulator reliability monitor and repair assistant.

## v0.6.0.5

- Checks for updates automatically whenever PitMedic launches while preserving user approval for every download and installation.
- Adds a visible footer repair action to findings that have an approved guided repair.
- Places monitored distance and time beneath recent findings and removes Clean Streak from the visible activity panel.
- Hides the redundant no-findings card when resolved captured evidence is already displayed, leaving more room for activity and benchmark details.
- Captures exact track, layout, car, and best-lap combinations from iRacing and compares them with the best trustworthy official or web source available.
- Keeps unsupported simulator combinations explicit instead of estimating or inventing comparison data.

## v0.6.0.4

- Made the in-app updater window responsive so Cancel and Install now remain visible at practical display sizes and scaling levels.
- Explicitly requests Windows administrator approval before PitMedic closes and records the installer handoff in the local diagnostic log.

## v0.6.0.3

- Reduced System Tools to Power Mode, Startup Apps, Storage, and Graphics Settings.
- Removed the unreliable Windows Update restart-pending classification and the unused Game Mode, Task Manager, and Background Load cards.
- Changed idle status text to Waiting for simulator; Monitoring active now appears only while a supported simulator is running.
- Made the installed CPU sensor service start after Windows settles, retry hardware initialization, tolerate transient sample-file contention, and use multiple recovery attempts.
- Updated anonymous usage records to the latest reported app version without increasing active-installation totals or sending duplicate install alerts.

## v0.6.0.0

- Added an explicit one-time choice for anonymous app-usage counting; sharing is off unless the user opts in.
- Limited the heartbeat to one request per UTC day containing only version, release channel, installer/portable type, and rotating daily/monthly anonymous tokens.
- Added an exact-data preview and a Settings toggle that deletes the local anonymous key and sending history when disabled.
- Added the separately deployed Cloudflare Worker/D1 service, strict payload allowlist, deduplication tests, and automatic deletion of raw rotating tokens after their counting period closes.
- Added a quiet startup update check, dismissible in-app update banner, direct download/release-note actions, and a Settings control; PitMedic never downloads or installs an update automatically.
- Documented website analytics and optional app usage separately so diagnostics, findings, repairs, hardware data, simulator activity, and permanent identifiers remain excluded.
- Preserved the prominent Support PitMedic links and all v0.5.0.0 monitoring, repair, sensor-service, acknowledgement, and clean-uninstall behavior.

## v0.5.0.1

- Added a persistent Support PitMedic button to the upper-right application title bar.
- Kept the existing About-page support explanation and PayPal link unchanged.
- Preserved the complete v0.5.0.0 monitoring, repair, installer-service, acknowledgement, and clean-uninstall behavior.

## v0.5.0.0

- Added a simple About page with version information, developer contact details, the public source link, and an optional PayPal support link.
- Kept every PitMedic feature free; contributions unlock nothing and payment details are handled only by PayPal in the user's browser.
- Replaced the per-launch elevated sensor helper with an installer-managed, commandless read-only Windows service.
- The installer starts the sensor service during its one setup approval; routine PitMedic launches remain unelevated and no longer require a sensor UAC prompt.
- Limited the service output to current CPU temperature, load, clock, power, timestamp, and local error state in a users-read-only ProgramData file.
- Kept protected repairs isolated in the separate one-shot repair helper and preserved all v0.4.4.2 monitoring, acknowledgement, history, and clean-uninstall behavior.

## v0.4.4.2

- Restored CPU temperature readings on systems that require protected sensor access without elevating the main PitMedic interface.
- Added a separate read-only sensor helper that validates its PitMedic parent, emits only CPU telemetry through a current-user-only pipe, accepts no commands, and exits with the parent app.
- Removed the redundant Home-page banner; All Findings and Settings remain in the persistent left navigation.
- Updated development, release, installer, signature-verification, and CI preparation for the third signed executable.
- Preserved the v0.4.4.1 acknowledgement, history, single-instance, and clean-uninstall changes.

## v0.4.4.1

- Added an `Acknowledge & Clear` action to Finding Review for findings the user chooses not to repair.
- Removed acknowledged findings from active dashboard and simulator status views while retaining them as `Acknowledged` in All Findings.
- Added single-instance detection and a maintenance shutdown request so the uninstaller automatically closes an idle tray instance before removing files.
- Kept the application mutex as a fallback when a repair is active or automatic shutdown cannot complete.
- Preserved all v0.4.4.0 monitoring, scoped elevation, repair, history, and installer behavior.

## v0.4.4.0

- Replaced always-on administrator execution with a one-shot elevated repair helper restricted to an explicit repair allowlist.
- Added caller, evidence, request-path, incident, and repair-policy validation before protected repairs can run.
- Added an Inno Setup installer and a two-stage workflow that signs the app/helper before building and signing the installer.
- Kept normal monitoring and unprotected repairs running without administrator rights.
- Preserved all v0.4.3.0 interface, telemetry, finding-review, repair, history, Settings, tray, build, and signing behavior.

## v0.4.3.0

- Open-sourced PitMedic under GPL-3.0-or-later.
- Added contribution, security, privacy, and public code-signing policies.
- Added reproducible Windows release-build automation with fixed product and file version metadata.
- Added GitHub Actions validation and a manual, protected SignPath release workflow.
- Added post-signing checks that reject an invalid, unexpected, or untimestamped Authenticode signature and generate a SHA-256 release manifest.
- Kept development builds clearly separated from official signed releases.
- Preserved all v0.4.2.1 interface, telemetry, finding-review, repair, history, Settings, and tray behavior.

## Supported simulators
- Le Mans Ultimate
- iRacing
- Assetto Corsa EVO
- RaceRoom Racing Experience
- Assetto Corsa Competizione
- Automobilista 2

## v0.4.2.1

- Added a Home destination above Le Mans Ultimate and made it the default startup page.
- Added a live system overview with CPU/GPU temperature, GPU power, memory usage, Windows/runtime details, simulator readiness, and the latest finding.
- Changed every finding action to open a user-friendly Finding Review window before any repair or evidence-folder action.
- Added plain-language explanations, evidence context, repair outcome, actual repair activity, timestamps, next steps, and recovery-copy access to Finding Review.
- Added a `RUN AUTOMATIC REPAIR` action for unresolved findings with an available repair; the existing safety, approval, duration, and backup rules are still enforced.
- Updated Findings History to use the same Review finding workflow.
- Improved the development build error with the exact .NET 10 SDK installation command and download page.

## v0.4.2.0

- Rebuilt the main interface around a persistent left simulator list and a dedicated page for the selected simulator.
- Removed the numeric prefixes and all simulator artwork from the navigation and Settings window.
- Replaced the three large status blocks with a compact Session Story showing the latest relevant event, captured evidence, repair readiness, and review action.
- Added per-simulator page state for running, monitoring, issue detected, repair available, and resolved findings.
- Filtered the findings card, evidence summary, and footer to the selected simulator while keeping complete history available through All Findings.
- Preserved the live CPU, GPU, GPU Power, load, memory, VRAM, trace, hover, repair, tray, and settings functionality.
- Added `PROJECT_POLICY.md` for the free/ad-free voluntary-support strategy.
- Added `PRIVACY.md` documenting local storage and the absence of diagnostic, analytics, donor, or advertising transmission in this release.
- Added `DEPENDENCY_INVENTORY.md` and `PUBLIC_RELEASE_CHECKLIST.md` so licensing, signing, privilege separation, clean-machine testing, and website disclosures remain tracked before public launch.
- Removed simulator logos, icons, and publisher artwork from the distributable source package.

## v0.4.1.0
- Migrated the application target from .NET 8 to .NET 10 LTS.
- Updated System.Diagnostics.EventLog from 8.0.0 to 10.0.11.
- Updated the Windows development build command to require and verify a .NET 10 SDK.
- Added an SDK policy that keeps builds on the latest installed .NET 10 feature band even when newer major SDKs are installed.
- Preserved the existing monitoring, diagnostic, repair, telemetry, and user-interface behavior.

## v0.4.0.12
- Rebalanced the GPU Power trace with additional vertical headroom so normal wattage no longer rides near the top of the chart.

- Added GPU Power to the telemetry trace with an independent wattage scale.
- Trace hover now reports Time, CPU, GPU, and GPU Power.
- GPU hotspot remains background-only for diagnostics.
