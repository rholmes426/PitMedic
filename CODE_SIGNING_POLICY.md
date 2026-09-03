# Code signing policy

Official PitMedic Windows releases use Microsoft Azure Artifact Signing with PitMedic's public-trust certificate profile.

## Privacy

PitMedic never transfers diagnostics, findings, repairs, hardware data, or simulator activity to the project. Users may explicitly opt in to a once-daily anonymous active-installation count containing only the documented six-field payload; it is off by default and uses no permanent identifier. See [the complete privacy statement](Source/PRIVACY.md).

## Team roles

- Committers and reviewers: [PitMedic repository maintainers](https://github.com/rholmes426/PitMedic/graphs/contributors).
- Approvers: [rholmes426](https://github.com/rholmes426) and any future repository owners who are also authorized for the protected GitHub signing environment and Azure signing account.
- Outside contributions require review by a maintainer before merge.
- Every production signing request requires manual approval.

The initial repository owner may hold all three roles while PitMedic has a one-person maintenance team. Roles must be updated here when maintainers are added or removed.

## Build and signing controls

- Signed artifacts are built from the public repository by the checked-in GitHub Actions workflow.
- Production signatures are requested only from protected release tags.
- PitMedic-owned binaries use consistent product and version metadata.
- The ordinary application runs unelevated. The installer registers the signed read-only sensor executable as a LocalSystem Windows service in the protected PitMedic installation directory; users approve this once during setup rather than at every launch.
- The sensor service accepts no commands or network connections. It writes only current CPU temperature, load, clock, power, timestamp, and error state to a users-read-only file under `%ProgramData%\PitMedic`.
- The compiled repair-helper allowlist requests administrator rights only when a selected repair must change a protected resource.
- The signed repair helper accepts no arbitrary command, executable, or destination path and exits after one validated repair.
- Third-party binaries are not re-signed as PitMedic.
- `PitMedic.exe`, `PitMedic.SensorHelper.exe`, and `PitMedic.RepairHelper.exe` are signed and verified before the installer is built.
- The final installer is signed and independently verified after it is built from the signed payload.
- Every signed executable must have a valid timestamped Authenticode signature.
- Release checksums are generated only after signature verification.
- Maintainers must enable multi-factor authentication for GitHub and Microsoft Azure.
- A signed binary that cannot be traced to its source commit and workflow run must not be distributed.
