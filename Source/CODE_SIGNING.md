# PitMedic code signing

PitMedic is prepared for free Authenticode signing through SignPath Foundation. The public repository and its CI workflow are the source of truth for signed releases; maintainers must never upload a locally substituted binary for signing.

## Release architecture

1. GitHub Actions checks out the exact release commit.
2. `Build/Build-UnsignedRelease.ps1` publishes the Windows x64 application.
3. The unsigned artifact is uploaded directly from that workflow run.
4. SignPath verifies the build origin and signs the PitMedic executable after manual approval.
5. `Build/Verify-SignedRelease.ps1` rejects a missing, invalid, or untimestamped signature.
6. GitHub retains the verified signed artifact and its SHA-256 manifest.

The SignPath private key never enters the repository or a maintainer's computer. Repository secrets contain only the SignPath API token; organization and project identifiers are repository variables.

## Required repository settings

Create these in **Settings → Secrets and variables → Actions** after SignPath approves the project:

| Kind | Name | Value |
| --- | --- | --- |
| Secret | `SIGNPATH_API_TOKEN` | API token created in SignPath |
| Variable | `SIGNPATH_ORGANIZATION_ID` | Organization ID supplied by SignPath |
| Variable | `SIGNPATH_PROJECT_SLUG` | Project slug supplied by SignPath |
| Variable | `SIGNPATH_POLICY_SLUG` | Approved release-signing policy slug |

The workflow expects a SignPath artifact configuration named `pitmedic-windows-x64`. It must preserve the published artifact and Authenticode-sign only PitMedic-owned executable files, initially `PitMedic.exe`. Third-party assemblies must not be signed as PitMedic.

## Local builds

`Build and Run PitMedic.cmd` remains a development-only, unsigned build. `Build/Build-UnsignedRelease.ps1` creates the deterministic CI input. Neither is a public release.

Only the signed output returned by SignPath and accepted by `Build/Verify-SignedRelease.ps1` may be labeled as an official public download.

## Installer

PitMedic does not yet ship a production installer. When one is added, its build must run in the same trusted workflow, the installer must be added to the SignPath artifact configuration, and both the application and installer signatures must pass verification before release.
