# PitMedic v0.6.0.12

PitMedic is a Windows .NET 10 WPF simulator reliability monitor and repair assistant.

## v0.6.0.12

- Adds a browsable Diagnostic Library generated directly from the app's repair knowledge, with 60 simulator and companion-software records, source citations, safety details, related issues, search filters, and crawlable issue pages.
- Adds Diagnostic Library deep links to incident details and repair prompts so users can move directly from a finding to its complete public explanation.
- Formalizes 17 existing Assetto Corsa Competizione and Automobilista 2 repair paths in the app knowledge base so they remain synchronized with the public library.
- Adds a targeted iRacing Steam Missing File Privileges diagnosis and approval-gated automatic repair that stops the iRacing Helper Service, opens Steam validation, and restarts the service without deleting user content.
- Adds generated-content consistency checks to CI so the app catalog, public JSON database, issue pages, and sitemap cannot drift silently.

## v0.6.0.11

- Publishes the first signed and timestamped PitMedic release, carrying forward the v0.6.0.10 companion-software recovery and Knowledge Scout changes.

## v0.6.0.10

- Replaces the single generic companion restart with vendor-specific recovery plans for MOZA Pit House, Simucube True Drive, Fanatec software, Logitech G HUB, SIMAGIC SimPro Manager, Asetek RaceHub, and VRS DirectForce.
- Follows Logitech's documented G HUB loading-loop recovery order by closing only its UI and agent, restarting the allowlisted updater service, relaunching the validated executable, and checking that it remains running.
- Preserves existing v0.6.0.9 companion findings by reconstructing the current vendor-specific plan while continuing to exclude firmware, drivers, profile deletion, third-party downloads, and security-control changes.
- Removes the redundant companion-software disclaimer and adds a complete data-driven monitored-software catalog to About and the public website.
- Adds the read-only Knowledge Scout for twice-weekly and manual review of allowlisted simulator and companion sources, lifecycle coverage, source availability, changed guidance, and possible safety signals.
- Keeps fixes active regardless of age, inactivity, or a moved citation; disabling or version-gating requires human-reviewed evidence that using the fix could cause harm.

## v0.6.0.9

- Shows only detected wheelbase and companion software, monitors confirmed crashes and hangs, and offers an approval-gated restart only after every supported simulator has closed.
- Covers MOZA Pit House, Simucube True Drive, Fanatec/FanaLab, Logitech G HUB, Simagic SimPro Manager, Asetek RaceHub, and VRS DirectForce with vendor-specific known-issue references while leaving profiles, drivers, firmware, and security controls unchanged.
- Makes AMS2 distance telemetry diagnosable, integrates simulator-reported speed when its odometer stalls, prevents double counting when the odometer resumes, and saves the last session increment.
- Preserves iRacing live diagnostic signatures so the elevated helper reconstructs a narrow repair such as Easy Anti-Cheat instead of falling back to generic Windows integrity work.
- Moves Check for updates to the top of Settings and presents it as the primary update action.
- Sends an anonymous heartbeat immediately after the app version, release channel, or installation type changes while retaining same-dimension retry throttling and rotating-token deduplication.
- Adds the September 2026 verified simulator-forum issue review to the versioned diagnostic and repair backlog, with unsafe community advice explicitly excluded from automation.

## v0.6.0.8

- Refreshes the anonymous usage dashboard immediately when the app version, release channel, or installation type changes within the same UTC day.
- Reuses the existing rotating anonymous tokens, so the refreshed dimensions replace the previous row without increasing active-installation totals or sending another install alert.

## v0.6.0.7

- Captures exact simulator-reported session-best lap, track, layout, and car data for ACC, RaceRoom, Automobilista 2, Le Mans Ultimate, and Assetto Corsa EVO in addition to iRacing.
- Uses LMU's local player profile and official session result so multiplayer laps from other drivers are never attributed to the user.
- Ignores benign ACC TextRender and NVIDIA NGX/DriverStore shutdown diagnostics while preserving real package and GPU-device failures.

## v0.6.0.6

- Keeps recent findings, captured evidence, and Open All Findings together in one uninterrupted section.
- Separates Driving Stats from the 48-hour findings window, with explicit lifetime totals and current/last-session best-lap labels.
- Compares the latest iRacing session best against an exact track, layout, and car reference lap.
- Broadens safe YouTube matching for common track and car aliases while continuing to reject mismatched layouts and cars.
- Adds a clearly labeled link to watch the source lap whenever a web video supplies the reference time.
- Cleans up the comparison card so its label, lap time, gap, source, and action remain readable in the narrow right rail.

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
