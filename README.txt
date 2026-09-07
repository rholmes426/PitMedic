PITMEDIC v0.6.0.15 - FULL DEVELOPMENT PACKAGE
===============================================

PitMedic is a free, open-source Windows sim-racing diagnostics and automated
repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This repository contains the full source tree, PitMedic assets, repair knowledge
base, documentation, website, telemetry services, and Windows build/run command
file. It is not a patch-only package.

WHAT CHANGED IN v0.6.0.15
-------------------------
- Removed simulator driving-stat collection and reporting, including best laps,
  monitored time, distance, clean streaks, reference-lap lookups, and the entire
  Driving Stats interface.
- Added upgrade cleanup for legacy per-simulator activity fields while preserving
  global session and repair counters.
- Expanded the read-only Knowledge Scout from 13 to 26 trusted official and
  vendor-operated sources. Scout findings always require human review.

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
