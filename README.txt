PITMEDIC v0.6.0.0 - FULL DEVELOPMENT PACKAGE
==============================================

PitMedic is a Windows sim-racing diagnostics and automated repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This ZIP contains the full source tree, PitMedic assets, repair knowledge base,
documentation, and Windows build/run command file. It is not a patch package.

WHAT CHANGED IN v0.6.0.0
------------------------
- Added an explicit one-time choice for once-daily anonymous app-usage counting.
  It is off unless the user opts in.
- Added an exact-data preview and a Settings switch. Turning sharing off deletes
  the local anonymous key and sending history.
- Limited the six-field request to version, release channel, installer/portable
  type, protocol, and rotating daily/monthly anonymous tokens. Diagnostics,
  findings, repairs, hardware data, and simulator activity are never sent.
- Added and tested the separate Cloudflare Worker/D1 aggregation service.
- Added a once-daily update check and a dismissible in-app Download banner. The
  app never downloads or installs updates without the user choosing the button.
- Preserved the prominent Support PitMedic buttons and v0.5.0.0 monitoring,
  repair, installer-service, acknowledgement, and clean-uninstall behavior.

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
self-contained development build, then test the anonymous-usage consent and
Settings flows plus normal monitoring and tray behavior. The portable build does
not register the sensor service; use the public v0.5.0.0 installer for the current
one-time setup approval and protected CPU-temperature behavior until the v0.6.0.0
installer is compiled. Confirm PitMedic.exe remains unelevated after installation.
