# PitMedic

PitMedic is a free, ad-free, open-source Windows simulator reliability monitor and repair assistant. It watches supported racing simulators, captures useful evidence when something goes wrong, explains the finding in plain language, and offers safe, reversible repairs when a known automatic fix is available.

Current signed release: **0.6.0.11**

- [Download the v0.6.0.11 signed Windows installer](https://github.com/rholmes426/PitMedic/releases/download/v0.6.0.11/PitMedic-Setup-x64.exe)
- [View the v0.6.0.11 release and checksums](https://github.com/rholmes426/PitMedic/releases/tag/v0.6.0.11)

The installer, PitMedic app, repair helper, and sensor service are signed and timestamped.

## Supported simulators

- Le Mans Ultimate
- iRacing
- Assetto Corsa EVO
- RaceRoom Racing Experience
- Assetto Corsa Competizione
- Automobilista 2

## v0.6 highlights

- The next release adds a public Diagnostic Library generated from the same records used by the app, formalizes 17 existing ACC and AMS2 repair paths, and adds a targeted iRacing Steam Missing File Privileges repair.
- v0.6.0.11 is PitMedic's first signed and timestamped public release and carries forward the complete v0.6.0.10 companion-software and Knowledge Scout work.
- v0.6.0.10 adds distinct vendor-specific recovery for all seven detected companion apps, including Logitech's updater-service recovery sequence, and publishes the complete monitored-software catalog in the app and on the website. It also adds a read-only Knowledge Scout that reviews allowlisted simulator and companion sources while preserving every fix unless reviewed evidence shows its use could cause harm.
- v0.6.0.9 adds detected-only wheelbase and companion-app monitoring with approval-gated restart recovery, makes AMS2 distance tracking diagnosable and resilient to a stalled odometer, preserves narrow iRacing repairs through elevated validation, promotes Check for updates in Settings, and corrects same-day usage-version refreshes.
- v0.6.0.8 refreshes the anonymous usage dashboard immediately after an app version, release channel, or installation-type change without double-counting the installation.
- v0.6.0.7 captures exact session-best laps and track/layout/car identity for all six supported simulators, and stops benign ACC shutdown diagnostics from offering an unnecessary game-file repair.
- v0.6.0.6 keeps recent findings and history actions together, separates driving stats from the 48-hour findings window, and adds exact-source lap comparisons with watchable source links.
- v0.6.0.5 checks for updates at every app launch, exposes guided repair actions clearly, moves monitored distance and time beside recent findings, and adds exact-combination iRacing best laps with trustworthy benchmark-source comparison.
- v0.6.0.4 keeps the updater's Cancel and Install now buttons visible on smaller displays and explicitly requests Windows administrator approval before closing PitMedic to install the verified package.
- v0.6.0.3 streamlines System Tools to Power Mode, Startup Apps, Storage, and Graphics Settings; makes active-monitoring labels reflect real simulator activity; improves CPU sensor-service startup and recovery; and keeps the anonymous usage dashboard's version label current after an in-month upgrade without double-counting the installation.
- Simulator-specific monitored time and distance.
- Verified live distance telemetry for iRacing, Assetto Corsa Competizione, RaceRoom, and Automobilista 2.
- Persistent simulator activity cards with Metric/Imperial display controls.
- Lighter background sampling and UI work while PitMedic is hidden.
- 48-hour recent-finding views and improved tray behavior.
- Project contact details use `robert@pitmedic.com` rather than personal developer details.
- A smoother upgrade path from v0.5, including automatic shutdown for maintenance and cleanup of legacy startup tasks.
- Upgrades and reinstalls silently reuse the existing PitMedic installation directory instead of showing an unnecessary folder-exists confirmation.
- Optional once-daily anonymous active-installation counting remains off until the user explicitly opts in.
- v0.6.0.1 fixes the anonymous usage heartbeat so the transmitted six-field payload exactly matches the user-visible preview and strict service allowlist, and failed sends can retry later instead of being suppressed for the rest of the UTC day.
- v0.6.0.1 preserves iRacing live diagnostic signatures so elevated automatic repairs can independently reconstruct the same narrow repair plan selected by the normal app. Legacy v0.6.0.0 findings prefer specific saved evidence before falling back to a broader category, and any genuine mismatch records both repair IDs before stopping safely.
- A quiet startup update check reads only the public PitMedic update manifest and never downloads or installs an update automatically.

## Build on Windows

Install the .NET 10 SDK, clone the repository, and run `Build and Run PitMedic.cmd`. The development builder creates an unsigned, self-contained Windows x64 build in `Output` and starts it.

Local development builds are not release artifacts. Public signed releases must pass the repository's SignPath workflow and signature verification.

The release workflow produces `PitMedic-Setup-x64.exe` plus a portable ZIP. PitMedic itself runs without administrator rights. The installer registers a narrowly scoped, read-only CPU sensor service during its one setup approval so normal app launches do not need administrator approval. Protected repairs use the separate, one-shot `PitMedic.RepairHelper.exe` only when a protected change is selected.

## Support PitMedic

PitMedic stays free and every feature is available without contributing. If it has been useful and you would like to help with hosting, signing, and development costs, you can make a voluntary contribution through [PayPal](https://paypal.me/PitMedicApp). Contributions do not unlock features or preferential support.

## Project commitments

- Every feature remains free; voluntary contributions unlock nothing.
- No advertising, affiliate repair recommendations, license keys, or paid tiers.
- Diagnostics always remain local. Optional anonymous usage counting is off by default, previews its complete six-field payload, and never includes diagnostics or a permanent identifier.
- Automatic repairs follow the backup, duration, approval, and recovery rules documented in `Source/PROJECT_POLICY.md` and `Source/REPAIR_AUTOMATION_MATRIX.md`.
- Simulator names are compatibility references; PitMedic is not affiliated with or endorsed by simulator publishers or Valve.

## Open source and security

PitMedic is licensed under [GPL-3.0-or-later](LICENSE). See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), the [privacy statement](Source/PRIVACY.md), and the [code signing policy](CODE_SIGNING_POLICY.md).

Free code signing is provided through SignPath.io, certificate by SignPath Foundation. Signed releases are built from the public repository, manually approved, timestamped, verified, and accompanied by SHA-256 information. The app and both scoped helpers are signed before the installer is built; the completed installer is then signed and verified separately.
