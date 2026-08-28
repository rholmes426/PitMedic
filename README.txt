PITMEDIC v0.4.3.0 - FULL DEVELOPMENT PACKAGE
==============================================

PitMedic is a Windows sim-racing diagnostics and automated repair utility.

THIS PACKAGE IS COMPLETE
------------------------
This ZIP contains the full source tree, PitMedic assets, repair knowledge base,
documentation, and Windows build/run command file. It is not a patch package.

WHAT CHANGED IN v0.4.3.0
------------------------
- Open-sourced the complete PitMedic project under GPL-3.0-or-later.
- Added contributor, security, privacy, and code-signing policies.
- Added a Windows release builder, CI validation, and a protected SignPath
  Foundation workflow for free public Authenticode signing.
- Added strict verification for publisher identity, signature validity,
  timestamp presence, version metadata, and SHA-256 release information.
- Kept unsigned development builds separate from official signed releases.
- Preserved all v0.4.2.1 functionality and user-interface behavior.

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
The package completed a Release compile for the net10.0-windows target using the
.NET 10.0.400 SDK. WPF markup compilation, XAML/XML structure, and code-behind
event-handler bindings were validated. A self-contained Windows executable was
not launched in the Linux packaging environment; run the included build command
on Windows for final publish, hardware, navigation, sensor, tray, UAC, and repair
testing.
