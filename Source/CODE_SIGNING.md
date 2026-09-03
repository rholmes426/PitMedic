# PitMedic code signing

PitMedic uses Microsoft Azure Artifact Signing for public-trust Authenticode signatures. The public repository and its CI workflow are the source of truth for signed releases; maintainers must never upload a locally substituted binary for signing.

## Release architecture

1. GitHub Actions checks out the exact release commit.
2. `Build/Build-UnsignedRelease.ps1` publishes the Windows x64 application, read-only sensor service, and narrowly scoped repair helper.
3. The unsigned binary artifact is uploaded directly from that workflow run.
4. GitHub authenticates to Azure through OpenID Connect, and Azure Artifact Signing signs `PitMedic.exe`, `PitMedic.SensorHelper.exe`, and `PitMedic.RepairHelper.exe` with the approved public-trust certificate profile.
5. `Build/Verify-SignedRelease.ps1` rejects a missing, invalid, unexpected, or untimestamped binary signature.
6. `Build/Build-Installer.ps1` builds the Inno Setup installer only from those verified signed binaries.
7. Azure Artifact Signing signs the completed installer, and the verifier checks its publisher, timestamp, version, and hash.
8. `Build/Assemble-SignedRelease.ps1` produces the installer, portable ZIP, signature manifests, and SHA-256 checksums retained by GitHub.

The private signing key never enters the repository, GitHub Actions, or a maintainer's computer. GitHub uses a short-lived OIDC identity to access only the configured Azure signing account and certificate profile; no long-lived signing secret is stored in the repository.

## Required repository settings

The protected `production-signing` GitHub environment and Azure resources must match the checked-in workflow:

| Resource | Required configuration |
| --- | --- |
| GitHub environment | `production-signing` |
| Azure authentication | GitHub OIDC federated credential for the repository workflow |
| Artifact Signing account | `pitmedicsigning426` in the East US endpoint |
| Certificate profile | `PITMEDICPUBLIC` public-trust profile |

The workflow runs Azure signing twice: first for the three PitMedic-owned executables in the self-contained payload, then for `PitMedic-Setup-x64.exe` after the installer is built from those signed binaries.

Third-party assemblies must not be re-signed as PitMedic.

## Local builds

`Build and Run PitMedic.cmd` remains a development-only, unsigned build. `Build/Build-UnsignedRelease.ps1` creates the deterministic CI input. Neither is a public release.

Only Azure-signed output accepted by `Build/Verify-SignedRelease.ps1` may be labeled as an official public download.

## Privilege separation and installer

`PitMedic.exe` runs as the signed-in user. It monitors applications and performs user-profile repairs without administrator rights. During the installer's single setup approval, `PitMedic.SensorHelper.exe` is registered as the `PitMedicSensor` LocalSystem Windows service from the protected Program Files directory. The service accepts no commands or network connections and writes only current CPU temperature/load/clock/power values, a timestamp, and local error state to `%ProgramData%\PitMedic\sensor.json`, where ordinary users have read-only access. The app ignores stale data and uses those values only to fill CPU readings unavailable to the unelevated process. Repairs that touch a protected simulator installation, Windows service, anti-cheat installation, time synchronization, or Windows integrity tools are routed through `PitMedic.RepairHelper.exe`.

The helper accepts one request-directory argument, validates the installed parent process and stored incident, reconstructs the repair from diagnostic evidence, rejects repair IDs outside its compiled allowlist, reports status over a current-user-only named pipe, and exits after that one repair. Elevated backups and logs use the installer-created `%ProgramData%\PitMedic` area; the helper does not persist elevated output into user-controlled incident folders.

The Inno Setup installer installs all three binaries in `%ProgramFiles%\PitMedic`, registers and starts the read-only sensor service, creates Start Menu and optional desktop shortcuts, preserves repair backups during uninstall, and launches the ordinary application as the original signed-in user. Upgrade and uninstall stop the sensor service before replacing or removing its executable. The installer is not release-ready until its own Azure Artifact Signing signature passes verification.
