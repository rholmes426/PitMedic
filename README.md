# PitMedic

PitMedic is a free, ad-free, open-source Windows simulator reliability monitor and repair assistant. It watches supported racing simulators, captures useful evidence when something goes wrong, explains the finding in plain language, and offers safe, reversible repairs when a known automatic fix is available.

Current development version: **0.4.3.0**

## Supported simulators

- Le Mans Ultimate
- iRacing
- Assetto Corsa EVO
- RaceRoom Racing Experience
- Assetto Corsa Competizione
- Automobilista 2

## Build on Windows

Install the .NET 10 SDK, clone the repository, and run `Build and Run PitMedic.cmd`. The development builder creates an unsigned, self-contained Windows x64 build in `Output` and starts it.

Unsigned development builds are not official public releases. Official releases must pass the repository's SignPath workflow and signature verification.

The release workflow produces `PitMedic-Setup-x64.exe` plus a portable ZIP. PitMedic itself runs without administrator rights. Windows asks for administrator approval only when a repair must use the separate, allowlisted `PitMedic.RepairHelper.exe` to change protected files or system components.

## Project commitments

- Every feature remains free; voluntary contributions unlock nothing.
- No advertising, affiliate repair recommendations, license keys, or paid tiers.
- Diagnostics remain local unless a future feature explicitly previews an upload and the user approves it.
- Automatic repairs follow the backup, duration, approval, and recovery rules documented in `Source/PROJECT_POLICY.md` and `Source/REPAIR_AUTOMATION_MATRIX.md`.
- Simulator names are compatibility references; PitMedic is not affiliated with or endorsed by simulator publishers or Valve.

## Open source and security

PitMedic is licensed under [GPL-3.0-or-later](LICENSE). See [CONTRIBUTING.md](CONTRIBUTING.md), [SECURITY.md](SECURITY.md), the [privacy statement](Source/PRIVACY.md), and the [code signing policy](CODE_SIGNING_POLICY.md).

Free code signing is planned through SignPath.io, certificate by SignPath Foundation. Signed releases will be built from the public repository, manually approved, timestamped, verified, and accompanied by SHA-256 information. The app and repair helper are signed before the installer is built; the completed installer is then signed and verified separately.
