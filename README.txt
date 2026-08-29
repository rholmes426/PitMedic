PITMEDIC v0.5.0.0 - FULL DEVELOPMENT PACKAGE
==============================================

PitMedic is a Windows sim-racing diagnostics and automated repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This ZIP contains the full source tree, PitMedic assets, repair knowledge base,
documentation, and Windows build/run command file. It is not a patch package.

WHAT CHANGED IN v0.5.0.0
------------------------
- Added an About page with version details, developer contact information,
  source link, and an optional Support PitMedic link through PayPal.
- Replaced per-launch sensor elevation with an installer-managed, commandless
  read-only Windows service. Normal PitMedic launches remain unelevated.
- The service writes only current CPU telemetry to a users-read-only file on
  the local PC. It accepts no commands and has no network behavior.
- Preserved the v0.4.4.2 Home cleanup, CPU telemetry restoration, finding
  acknowledgement, history, clean-uninstall, and scoped repair changes.

BUILD + RUN
-----------
Extract the entire ZIP to a new folder and double-click:
    Build and Run PitMedic.cmd

The script requires a stable .NET 10 SDK. It publishes a self-contained Windows
x64 build to:
    Output\PitMedic.exe

OPEN-SOURCE REPOSITORY PREPARATION
----------------------------------
The package includes the GPL license, public repository documentation, GitHub
Actions build validation, and the SignPath submission/verification workflow.
See CODE_SIGNING_POLICY.md and Source\CODE_SIGNING.md before public release.

VALIDATION NOTE
---------------
This private package was not uploaded to GitHub or compiled by the public Windows
CI workflow. Run the included build command on Windows to compile and launch the
self-contained development build, then test finding acknowledgement and normal
tray behavior. The portable build does not register the sensor service; use the
v0.5.0.0 installer for the intended one-time setup approval and protected CPU
temperature behavior. Confirm PitMedic.exe remains unelevated after installation.
