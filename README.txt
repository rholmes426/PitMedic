PITMEDIC v0.6.0.18 - FULL DEVELOPMENT PACKAGE
===============================================

PitMedic is a free, open-source Windows sim-racing diagnostics and automated
repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This repository contains the full source tree, PitMedic assets, repair knowledge
base, documentation, website, telemetry services, and Windows build/run command
file. It is not a patch-only package.

WHAT CHANGED IN v0.6.0.18
-------------------------
- Suppresses misleading iRacing update-related findings while the updater runs.
- Defers iRacing repairs until the update and a short settling period finish.
- Loads the tray icon directly and refreshes desktop and Start menu shortcut icons.
- Updates the matching iRacing Diagnostic Library guidance.

PREVIOUS UPDATE v0.6.0.17
-------------------------
- New racing-line P branding across the app, installer, website and browser icons.
- Matching horizontal wordmark and multi-resolution Windows icons.

PREVIOUS UPDATE v0.6.0.16
-------------------------
- PitMedic automatically opens the detected simulator page and selects it again
  when the session ends, leaving the last-run simulator active afterward.
- Every simulator page now shows the last completed session's local start time,
  duration, and observed result in a summary at the bottom of the page.
- Only the latest session summary for each simulator is stored locally. It is not
  included in anonymous usage reporting.
- The main window opens centered at 1500 by 1000 pixels so Home fits without
  scrollbars at normal Windows scaling.

BUILD + RUN
-----------
Extract the complete package and double-click:
    Build and Run PitMedic.cmd

The script requires a stable .NET 10 SDK and creates an unsigned development
build. Official public installers are built, signed, timestamped, and verified
by the protected GitHub release workflow.

PUBLIC RELEASE
--------------
Official releases include a signed Windows installer, portable ZIP, SHA-256
checksums, and signature manifests. PitMedic remains free and open source;
voluntary support unlocks nothing.

The ordinary app runs unelevated. The installer registers the narrowly scoped
read-only CPU sensor service, and protected repairs use a separate one-shot
repair helper only when a protected change is selected.
