# PitMedic code signing

PitMedic is prepared for free Authenticode signing through SignPath Foundation. The public repository and its CI workflow are the source of truth for signed releases; maintainers must never upload a locally substituted binary for signing.

## Release architecture

1. GitHub Actions checks out the exact release commit.
2. `Build/Build-UnsignedRelease.ps1` publishes the Windows x64 application and the narrowly scoped repair helper.
3. The unsigned binary artifact is uploaded directly from that workflow run.
4. SignPath verifies the build origin and signs `PitMedic.exe` and `PitMedic.RepairHelper.exe` after manual approval.
5. `Build/Verify-SignedRelease.ps1` rejects a missing, invalid, unexpected, or untimestamped binary signature.
6. `Build/Build-Installer.ps1` builds the Inno Setup installer only from those verified signed binaries.
7. SignPath signs the completed installer, and the verifier checks its publisher, timestamp, version, and hash.
8. `Build/Assemble-SignedRelease.ps1` produces the installer, portable ZIP, signature manifests, and SHA-256 checksums retained by GitHub.

The SignPath private key never enters the repository or a maintainer's computer. Repository secrets contain only the SignPath API token; organization and project identifiers are repository variables.

## Required repository settings

Create these in **Settings → Secrets and variables → Actions** after SignPath approves the project:

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | API token created in SignPath |
| Variable | `SIGNPATH_ORGANIZATION_ID` | Organization ID supplied by SignPath |
| Variable | `SIGNPATH_PROJECT_SLUG` | Project slug supplied by SignPath |
| Variable | `SIGNPATH_POLICY_SLUG` | Approved release-signing policy slug |

The workflow expects two SignPath artifact configurations:

- `pitmedic-windows-x64-binaries` preserves the self-contained publish directory and Authenticode-signs only `PitMedic.exe` and `PitMedic.RepairHelper.exe`.
- `pitmedic-windows-x64-installer` Authenticode-signs only `PitMedic-Setup-x64.exe`.

Third-party assemblies must not be re-signed as PitMedic.

## Local builds

`Build and Run PitMedic.cmd` remains a development-only, unsigned build. `Build/Build-UnsignedRelease.ps1` creates the deterministic CI input. Neither is a public release.

Only the signed output returned by SignPath and accepted by `Build/Verify-SignedRelease.ps1` may be labeled as an official public download.

## Privilege separation and installer

`PitMedic.exe` runs as the signed-in user. It monitors applications and performs user-profile repairs without administrator rights. Repairs that touch a protected simulator installation, Windows service, anti-cheat installation, time synchronization, or Windows integrity tools are routed through `PitMedic.RepairHelper.exe`.

The helper accepts one request-directory argument, validates the installed parent process and stored incident, reconstructs the repair from diagnostic evidence, rejects repair IDs outside its compiled allowlist, reports status over a current-user-only named pipe, and exits after that one repair. Elevated backups and logs use the installer-created `%ProgramData%\PitMedic` area; the helper does not persist elevated output into user-controlled incident folders.

The Inno Setup installer installs both binaries in `%ProgramFiles%\PitMedic`, creates Start Menu and optional desktop shortcuts, preserves repair backups during uninstall, and launches the ordinary application as the original signed-in user. The installer is not release-ready until its own SignPath signature passes verification.
