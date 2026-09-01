# Simulator forum gap review

Review date: 2026-09-01

## Outcome

This review found 16 actionable issue signatures or guided solutions that are not currently represented in PitMedic's repair knowledge, plus 5 simulator-specific precision gaps where PitMedic knows the broad problem class but not the most useful trigger and response. One additional Assetto Corsa EVO signature should stay on a watchlist until its repair is reproducible on current builds.

The review covered every simulator currently declared by the app:

- Le Mans Ultimate
- iRacing
- Assetto Corsa EVO
- RaceRoom Racing Experience
- Assetto Corsa Competizione
- Automobilista 2

The comparison baseline was the current repair knowledge base, capability catalog, repair planner, automation matrix, documentation, and PENDING_FIXES.md. In particular, the already-pending Automobilista 2 shared-memory/distance problem is not counted as a new discovery.

## Verification standard

A finding was accepted when at least one of these was true:

1. The simulator publisher or official support team documents the symptom and solution.
2. A forum moderator or developer gives the solution and one or more users confirm it.
3. Multiple independent users report the same narrow symptom and the same successful solution.

Single-user guesses, generic advice, unsupported DLL downloads, registry cleaners, broad antivirus disabling, and destructive profile deletion without a backup were excluded. Community-only findings are marked so PitMedic can keep their first implementation detect-only or guided.

## Recommended implementation order

| Priority | Work |
|---|---|
| P0 | Add exact iRacing Loading Error 77 and 63 signatures; add the Le Mans Ultimate locale detector; add the RaceRoom RX 9000-series black-screen diagnostic. |
| P1 | Add runtime prerequisite checks, exact controller-conflict guidance, the iRacing audio/Simagic signatures, and Controlled Folder Access precision checks. |
| P2 | Add the remaining performance/network diagnostics and user-approved storage cleanup warnings. |

## Le Mans Ultimate

### LMU-NEW-01 — Windows number-format locale can prevent launch

- Evidence: Official LMU Known Issues says the game may not launch when the decimal symbol is a comma and the digit-group symbol is a period.
- Gap: PitMedic checks time, graphics, plugins, EAC, ReShade, and common launch files, but not Windows number formatting.
- Recommended behavior: Read the current user's decimal and group separators. If they match the known failing combination and LMU has a launch failure, explain the exact setting. Treat changing locale as guided/user-approved because it affects other applications.
- Confidence / priority: Official, high / P0.
- Source: [LMU Known Issues and Advice](https://guide.lemansultimate.com/hc/en-gb/articles/13240843908623-Known-Issues-and-Advice)

### LMU-NEW-02 — Corrupt sound configuration can cause an immediate startup crash

- Evidence: An LMU community moderator's recovery sequence includes removing the sound configuration after the DX11 and CEF steps; multiple users reported that the sound-file reset restored startup.
- Gap: The current LMU repair set resets graphics, shaders, plugins, and other known launch blockers, but not sound.conf or snd.cfg.
- Recommended behavior: Only offer after a matching startup failure survives the existing DX11/CEF repairs. Back up and quarantine the sound configuration, then let LMU regenerate it. Do not delete it permanently.
- Confidence / priority: Moderator-led and corroborated / P1.
- Source: [LMU startup-crash community thread](https://community.lemansultimate.com/index.php?threads/crash-the-game-instantly-crashes-after-start.8664/)

### LMU-NEW-03 — Broken input JSON files have an official targeted reset

- Evidence: The official Common Fixes page names direct input.json, current controls.json, keyboard.json, and gamepad.json as controller repair targets.
- Gap: PitMedic has broader profile/controller guidance but does not encode this exact four-file set as a reversible repair.
- Recommended behavior: Detect parse failures or controller-init symptoms, back up only the four named files, and regenerate them. Warn that bindings will need to be restored.
- Confidence / priority: Official, high / P1.
- Source: [LMU Common Fixes](https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes)

### LMU-NEW-04 — Steam Input can cause controller-specific on-track stutter

- Evidence: LMU's Common Fixes says disabling Steam Input can help some controller/wheel users whose cars stutter.
- Gap: Current stutter handling focuses on graphics, overlays, plugins, and input software but lacks this narrow controller correlation.
- Recommended behavior: When stutter coincides with a controller device, offer a guided Steam Input A/B test for LMU only. Do not change Steam-wide settings.
- Confidence / priority: Official but explicitly conditional / P2.
- Source: [LMU Common Fixes](https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes)

## iRacing

### IR-NEW-01 — Loading Error 77 should route directly to content redownload

- Evidence: Official iRacing support maps Loading Error 77 to deleting the affected track or car and downloading it again.
- Gap: PitMedic already has car/track content repair capabilities, but the exact error number is not a first-class signature.
- Recommended behavior: Parse Error 77 and route it to the existing scoped content repair instead of generic verification.
- Confidence / priority: Official, high / P0.
- Source: [iRacing Loading Error 77](https://support.iracing.com/support/solutions/articles/31000147702-loading-error-77)

### IR-NEW-02 — Loading Error 63 can be a corrupt custom spotter sound

- Evidence: Official iRacing support identifies a corrupt custom spotter sound as the first target and recommends backing up/renaming app.ini later in the sequence.
- Gap: No current repair signature distinguishes Error 63 from generic content or launch failures.
- Recommended behavior: Detect Error 63, identify non-default spotter packs, offer a reversible custom-spotter quarantine, and only then offer a backed-up app.ini reset.
- Confidence / priority: Official, high / P0.
- Source: [iRacing Loading Error 63](https://support.iracing.com/support/solutions/articles/31000156684-loading-error-63)

### IR-NEW-03 — Audio exclusive mode can cause no sound plus severe stutter

- Evidence: Official iRacing support links no in-sim sound with red C/S bars and severe stutter to another media application holding the device in Exclusive Mode.
- Gap: Current stutter classification does not connect this symptom cluster to Windows audio ownership.
- Recommended behavior: Detect the combined no-audio/red-C-or-S/stutter signature and guide the user to disable exclusive mode for the affected playback device or select a different device. Do not change audio policy silently.
- Confidence / priority: Official, high / P1.
- Source: [iRacing no sound and massive stuttering](https://support.iracing.com/support/solutions/articles/31000163354-no-sound-in-sim-and-or-massive-stuttering)

### IR-NEW-04 — Visual C++ x64 runtime health is an explicit launch prerequisite

- Evidence: Current official iRacing launch guidance says to install the Microsoft Visual C++ x64 redistributable first for the Unexpected Error path.
- Gap: PitMedic does not have a simulator-specific VC++ runtime presence/repair check.
- Recommended behavior: Detect the installed runtime/version and missing-runtime event signatures. Link only to Microsoft's official installer and keep repair user-approved.
- Confidence / priority: Official, high / P1.
- Source: [iRacing Unexpected Error when launching](https://support.iracing.com/support/solutions/articles/31000171857-unexpected-error-when-launching-iracing)

### IR-NEW-05 — Simagic 360 Hz and haptic-pedal API traffic can cause stutter

- Evidence: Official iRacing stutter guidance identifies Simagic Alpha 360 Hz and Haptic Pedal Reactor traffic as possible USB-flood sources and documents loadSimagicAPI=0 / pedal-vibration A/B tests.
- Gap: Current overlay/USB guidance is generic and misses these exact devices and setting.
- Recommended behavior: Detect Simagic devices and the matching stutter pattern, preserve the current configuration, and offer a reversible guided A/B test.
- Confidence / priority: Official, high / P1.
- Source: [iRacing freezing and stuttering guide](https://support.iracing.com/support/solutions/articles/31000141916-dealing-with-freezing-and-or-stuttering-issues)

### IR-NEW-06 — Replay spooling/storage layout can produce page-fault stutter

- Evidence: Official iRacing guidance notes that replay spooling can trigger page faults when Documents/iRacing is on a slow disk or arranged poorly relative to the install.
- Gap: PitMedic does not correlate replay spooling, disk location/type, and page-fault evidence.
- Recommended behavior: Detect replay-spooling state, storage type, free space, and page-fault pressure; recommend a controlled A/B test before suggesting any folder move.
- Confidence / priority: Official, high / P2.
- Source: [iRacing freezing and stuttering guide](https://support.iracing.com/support/solutions/articles/31000141916-dealing-with-freezing-and-or-stuttering-issues)

### IR-NEW-07 — connect_sockets is an official narrow test for Cannot Connect

- Evidence: Official iRacing support documents changing connect_sockets from 0 to 1 in core.ini as a connection test and says to revert it if it does not help.
- Gap: Current networking guidance covers firewall/allowlisting but not this reversible iRacing-specific test.
- Recommended behavior: After ordinary service and firewall checks, offer an evidence-backed, backed-up edit with automatic rollback if the next connection test fails.
- Confidence / priority: Official, high / P2.
- Source: [iRacing Cannot connect to Server](https://support.iracing.com/support/solutions/articles/31000133469-cannot-connect-to-server)

## Assetto Corsa EVO

### ACE-NEW-01 — Visual C++ x64 repair can restore startup

- Evidence: Multiple ACE launch threads report success after repairing the current Microsoft Visual C++ x64 redistributable and rebooting.
- Gap: Current ACE repairs focus on profile, graphics, and Steam files, not runtime health.
- Recommended behavior: Check runtime presence and relevant Windows event signatures, then offer Microsoft's official installer/repair. Never recommend third-party DLL sites.
- Confidence / priority: Community-corroborated / P1.
- Sources: [ACE launch thread](https://steamcommunity.com/app/3058630/discussions/0/756141976595760712/), [ACE VC++ thread](https://steamcommunity.com/app/3058630/discussions/0/756142145462622478/)

### ACE-NEW-02 — A second XInput controller or conflicting bindings can block wheel mapping

- Evidence: Users with different wheel hardware confirmed that disconnecting the gamepad and clearing conflicting binds allowed steering, throttle, and brake assignment.
- Gap: PitMedic can reset controller data but does not first test for a lower-impact device conflict.
- Recommended behavior: Inventory active HID/XInput devices, explain the conflict, and guide a temporary disconnect/relaunch test before offering any profile reset.
- Confidence / priority: Community-corroborated / P1.
- Source: [ACE wheel-binding fix thread](https://steamcommunity.com/app/3058630/discussions/0/756141976595806554/)

### ACE-PRECISION-01 — Controlled Folder Access has an exact ACE symptom

- Evidence: Multiple users report that Windows Security blocked ACE from launching or writing its profile; allowing the exact game executable restored operation.
- Existing overlap: PitMedic deliberately treats security-software changes as guided, not automatic.
- Improvement: Detect denied file-write events and missing/unwritable ACE profile paths, then show the exact blocked executable and Windows Security route. Never disable Defender or broad protection.
- Confidence / priority: Community-corroborated / P1.
- Source: [ACE launch discussion](https://steamcommunity.com/app/3058630/discussions/0/756141976595777598/)

### ACE-WATCH-01 — Generated pipeline-library failure

- Evidence: ACE 0.8 reports identify failures around the generated ks.psolibrary.pipelinelibrary file, while a later hotfix fixed a pipeline-library corruption bug.
- Reason to wait: Deleting the file did not consistently repair affected systems, and current builds may already include the root fix.
- Next evidence needed: Reproduction on a current build showing that a backed-up quarantine is both safe and more effective than updating/verifying.
- Source: [ACE update discussion](https://steamcommunity.com/app/3058630/discussions/0/576047170584644220/)

## RaceRoom Racing Experience

### R3E-NEW-01 — Visual C++ 2010 SP1 can be the missing launch prerequisite

- Evidence: Multiple RaceRoom users with an immediate exit/red-launcher symptom restored startup by installing Microsoft's Visual C++ 2010 SP1 runtime.
- Gap: PitMedic has no RaceRoom-specific check for MSVCR100/MSVCP100 runtime failures.
- Recommended behavior: Detect missing MSVCR100.dll or MSVCP100.dll/event evidence and offer the official Microsoft runtime only. Do not download individual DLL files.
- Confidence / priority: Community-corroborated / P1.
- Sources: [RaceRoom launch thread](https://steamcommunity.com/app/211500/discussions/1/660467372238509744/), [second RaceRoom launch thread](https://steamcommunity.com/app/211500/discussions/1/694249110938675925/)

### R3E-PRECISION-01 — RX 9000-series session black screen has a narrow settings fallback

- Evidence: RaceRoom forum reports consistently describe a black screen with audio/cursor on newer AMD RX 9060/9070 hardware. Disabling motion blur and/or MSAA was repeatedly confirmed; borderless or the official DXVK launch mode also helped some configurations.
- Existing overlap: PitMedic already has generic graphics/profile reset and shader repair.
- Improvement: Detect the GPU family plus session-entry black-screen signature and offer the least invasive sequence: motion blur off, MSAA off, borderless test, then RaceRoom's official DXVK launch option. Do not inject third-party DXVK files.
- Confidence / priority: Moderator/community-corroborated / P0.
- Source: [RaceRoom AMD black-screen thread](https://steamcommunity.com/app/211500/discussions/1/601901942630817124/)

### R3E-PRECISION-02 — Steam elevation/compatibility can break Portal Services

- Evidence: Some Portal Services threads report recovery after removing Steam's Run as administrator or compatibility flags, although results are mixed.
- Existing overlap: PitMedic already avoids changing Steam-wide elevation state automatically.
- Improvement: Detect mismatched elevation/compatibility flags and present them as a diagnostic with a rollback instruction. Do not auto-edit global Steam properties.
- Confidence / priority: Mixed community evidence / P2.
- Source: [RaceRoom Portal Services discussion](https://steamcommunity.com/app/211500/discussions/1/797839485450631750/)

## Assetto Corsa Competizione

### ACC-NEW-01 — MoTeC telemetry can silently consume tens of gigabytes

- Evidence: ACC records telemetry when Telemetry Laps is enabled. Multiple users reported 18–28 GB MoTeC folders after long-term use, including settings distributed with telemetry enabled.
- Gap: PitMedic does not inspect simulator-generated telemetry storage.
- Recommended behavior: Add a non-blocking folder-size warning, explain why files exist, and offer user-approved archive or age-based cleanup. Never delete telemetry automatically.
- Confidence / priority: Community-corroborated behavior / P2.
- Sources: [ACC MoTeC storage discussion](https://www.reddit.com/r/ACCompetizione/comments/x2fmoa), [ACC telemetry location discussion](https://www.reddit.com/r/ACCompetizione/comments/10ltmrf)

### ACC-PRECISION-01 — Controlled Folder Access causes settings/setup save failures

- Evidence: Multiple ACC reports tie settings that will not save, setup-write failures, or startup fatal errors to denied writes under Documents; allowing the exact ACC executable resolves the issue.
- Existing overlap: PitMedic knows the broad security/read-write class but does not encode the ACC symptom/path pairing.
- Improvement: Check write access and Defender event evidence for the ACC Documents tree, then provide exact allowlist guidance. Do not disable security features.
- Confidence / priority: Community-corroborated / P1.
- Source: [ACC settings-not-saving discussion](https://steamcommunity.com/app/805550/discussions/0/1741094390463854431/)

## Automobilista 2

### AMS2-NEW-01 — Wheel enumeration and binding timing are easy to misdiagnose

- Evidence: The pinned community guidance says wheel/controller hardware should be powered and calibrated before AMS2 starts because hot-swap is unreliable. Axis assignment also requires a quick turn-and-recenter or press-and-release; holding an input can produce no assignment and moving too slowly can produce Multiple Inputs Detected.
- Gap: Current AMS2 controller repair jumps toward profile/config repair without encoding these lower-impact preconditions.
- Recommended behavior: Detect newly connected input devices after process start, prompt for a clean relaunch, and show the short recenter/release instruction before offering a controller-profile reset.
- Confidence / priority: Pinned and repeatedly confirmed community guidance / P1.
- Source: [AMS2 wheel/controller assignment guide](https://steamcommunity.com/app/1066890/discussions/0/592901971597424335/)

### AMS2-PRECISION-01 — Controlled Folder Access/read-only state prevents settings saves

- Evidence: Multiple AMS2 users report settings reverting until the game executable was allowed through Controlled Folder Access or the profile path was made writable.
- Existing overlap: PitMedic has broad permissions/security guidance and profile repair.
- Improvement: Test the AMS2 Documents path for actual write/rename access and correlate Defender events before recommending a security exception.
- Confidence / priority: Community-corroborated / P1.
- Source: [AMS2 settings-not-saving discussion](https://steamcommunity.com/app/1066890/discussions/0/4594180031254034488/)

## Already known and deliberately not counted

- AMS2 distance not recording because Use Shared Memory is not set to Project CARS 2: already in PENDING_FIXES.md for 0.6.0.9.
- AMS2 championship corruption, graphics/profile resets, controller/FFB resets, and Steam verification: already covered.
- ACC controls, FFB, TrueForce, preset/profile repair, graphics/engine resets, and Steam verification: already covered.
- RaceRoom cache/shader/profile/graphics/browser repairs and Steam verification: already covered.
- ACE profile/video reset, both Documents and Saved Games profile locations, and Steam verification: already covered.
- LMU DX11 config, shader cache, plugins, EAC, ReShade, RTSS/G Hub, clock/time, and Steam verification: already covered.
- iRacing service/UI cache/UISafe/EAC/updater/content/profile and compatibility-mode repairs: already covered.

## Rejected or unsafe advice

These recurring forum suggestions should not become PitMedic repairs:

- Disable antivirus or Windows Defender entirely.
- Run every simulator or Steam permanently as administrator.
- Download individual DLLs from third-party DLL sites.
- Apply registry cleaners or broad driver-cleaner tools without a matching signature.
- Inject unofficial DXVK/ReShade files into a protected or anti-cheat game.
- Delete an entire user profile before backing it up and trying a narrower repair.
- Downgrade drivers based on a single report without a publisher-confirmed compatibility issue.

## Suggested engineering slices

1. Signature-only release: add the exact error numbers, locale/runtime/device detection, and guided instructions without new destructive actions.
2. Reversible repair release: add scoped backup/quarantine for LMU sound/input files, iRacing custom spotters/app.ini, and core.ini A/B testing.
3. Precision diagnostics release: correlate Windows Defender events, path write tests, GPU family, audio-device ownership, USB device inventory, and storage growth.
4. Telemetry validation: record which signature fired, which action was offered, whether the user accepted it, and whether the next launch/session succeeded. This turns community evidence into PitMedic-specific repair efficacy data.

## Source-quality caveat

Forum history is open-ended and changes with game patches, so this is a point-in-time review rather than a claim that every post on every forum has been exhausted. Official support material was preferred, corroborated community fixes were retained, and weak or unsafe advice was excluded. Before automating a community-only repair, reproduce it on the current simulator build and keep the first implementation reversible.
