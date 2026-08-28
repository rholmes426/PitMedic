# Code signing policy

Official PitMedic Windows releases use free code signing provided by SignPath.io, certificate by SignPath Foundation.

## Privacy

PitMedic does not transfer diagnostics, usage statistics, advertising identifiers, or analytics to the PitMedic project. Information stays on the user's computer unless the user deliberately opens, copies, exports, or shares it. See [the complete privacy statement](Source/PRIVACY.md).

## Team roles

- Committers and reviewers: [PitMedic repository maintainers](https://github.com/rholmes426/PitMedic/graphs/contributors).
- Approvers: [rholmes426](https://github.com/rholmes426) and any future repository owners who are also authorized as release approvers in SignPath.
- Outside contributions require review by a maintainer before merge.
- Every production signing request requires manual approval.

The initial repository owner may hold all three roles while PitMedic has a one-person maintenance team. Roles must be updated here when maintainers are added or removed.

## Build and signing controls

- Signed artifacts are built from the public repository by the checked-in GitHub Actions workflow.
- Production signatures are requested only from protected release tags.
- PitMedic-owned binaries use consistent product and version metadata.
- The ordinary application runs unelevated; only the compiled repair-helper allowlist may request administrator rights.
- The signed repair helper accepts no arbitrary command, executable, or destination path and exits after one validated repair.
- Third-party binaries are not re-signed as PitMedic.
- `PitMedic.exe` and `PitMedic.RepairHelper.exe` are signed and verified before the installer is built.
- The final installer is signed and independently verified after it is built from the signed payload.
- Every signed executable must have a valid timestamped Authenticode signature.
- Release checksums are generated only after signature verification.
- Maintainers must enable multi-factor authentication for GitHub and SignPath.
- A signed binary that cannot be traced to its source commit and workflow run must not be distributed.
