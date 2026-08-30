PITMEDIC v0.6.0.3 - FULL DEVELOPMENT PACKAGE
==============================================

PitMedic is a Windows sim-racing diagnostics and automated repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This repository contains the full source tree, PitMedic assets, repair knowledge
base, documentation, website, telemetry services, and Windows build/run command
file. It is not a patch-only package.

WHAT CHANGED IN v0.6.0.3
------------------------
- Reduced System Tools to Power Mode, Startup Apps, Storage, and Graphics
  Settings, removing Game Mode, Task Manager, Windows Update, and Background Load.
- Removed the Windows Update check that could misclassify ordinary pending file
  replacements as a required Windows restart.
- Idle pages now say Waiting for simulator. Monitoring active appears only while
  a supported simulator is actually running.
- Hardened the installed CPU sensor service with delayed startup, hardware-access
  retries, transient sample-write recovery, and multiple service restart attempts.
- The anonymous usage backend now updates an existing installation to its current
  app version without increasing the count or sending another install alert.

THE v0.6 LINE ALSO INCLUDES
---------------------------
- Explicit one-time consent for anonymous app-usage counting; sharing is off
  unless the user opts in.
- Exact-data preview and Settings switch. Turning sharing off deletes the local
  anonymous key and sending history.
- A six-field privacy-preserving request limited to version, release channel,
  installer/portable type, protocol, and rotating daily/monthly anonymous tokens.
  Diagnostics, findings, repairs, hardware data, and simulator activity are never
  sent.
- Cloudflare Worker/D1 aggregate usage service and private usage dashboard.
- Once-daily update checks and a dismissible in-app Download banner; PitMedic
  never downloads or installs updates without the user choosing the action.
- Simulator-specific monitored time and clean streaks, plus verified live
  distance telemetry for iRacing, ACC, RaceRoom, and Automobilista 2.
- Metric/Imperial display settings and persistent supported-simulator distance
  cards.

BUILD + RUN
-----------
Extract the entire package to a new folder and double-click:
    Build and Run PitMedic.cmd

The script requires a stable .NET 10 SDK. It publishes a self-contained Windows
x64 build to:
    Output\PitMedic.exe

PUBLIC PREVIEW
--------------
The public v0.6.0.3 test preview is intentionally unsigned while the PitMedic
SignPath application is pending. Windows may display Unknown publisher or a
SmartScreen warning. Release assets include a clearly labeled unsigned installer,
portable ZIP, SHA-256 checksums, and release notes.

The installed build registers the narrowly scoped read-only CPU sensor service
during setup. Normal PitMedic launches remain unelevated. Protected repairs use
the separate one-shot PitMedic.RepairHelper.exe only when a protected change is
selected.

OPEN-SOURCE RELEASE PREPARATION
-------------------------------
The package includes the GPL license, public repository documentation, GitHub
Actions build validation, unsigned-preview publishing, and SignPath
submission/verification workflows. See CODE_SIGNING_POLICY.md and
Source\CODE_SIGNING.md for the signed-release path.
