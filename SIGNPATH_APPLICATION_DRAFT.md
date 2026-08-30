# SignPath Foundation application draft

Replace the bracketed URLs after the public repository and preview release exist. The rest is ready to paste into the SignPath Foundation application.

## Project

**Name:** PitMedic

**Repository:** https://github.com/rholmes426/PitMedic

**Project website:** https://pitmedic.com

**Preview download:** https://github.com/rholmes426/PitMedic/releases/tag/v0.5.0.0

**License:** GPL-3.0-or-later

**Code-signing policy:** https://github.com/rholmes426/PitMedic/blob/main/CODE_SIGNING_POLICY.md

**Privacy statement:** https://github.com/rholmes426/PitMedic/blob/main/Source/PRIVACY.md

## Description

PitMedic is a free, ad-free Windows simulator reliability monitor and repair assistant. It monitors supported sim-racing titles, records local evidence when a software failure occurs, explains findings in plain language, and offers safe, reversible repairs for known problems. Repairs are user-visible, bounded by duration and approval rules, and use recovery copies where applicable.

## Signed artifacts

PitMedic is a .NET 10 WPF Windows x64 application with a commandless read-only sensor service, a separate one-shot elevated repair helper, and an Inno Setup installer. The binary artifact configuration preserves the self-contained publish directory and Authenticode-signs only the PitMedic-owned `PitMedic.exe`, `PitMedic.SensorHelper.exe`, and `PitMedic.RepairHelper.exe`. A second artifact configuration signs only the completed `PitMedic-Setup-x64.exe` installer. Third-party assemblies remain under their upstream identities and are not signed as PitMedic.

## Build provenance

The checked-in `.github/workflows/sign-release.yml` workflow checks out an explicit protected release tag, verifies that the tag and all three project versions agree, and builds the application and scoped helpers on a GitHub-hosted Windows runner. It submits that exact binary artifact to SignPath and verifies all three returned signatures. Only then does it build the installer from the signed payload, submit the installer to SignPath, and verify the returned publisher, version, signature, timestamp, and SHA-256 value. Every production signing request requires manual approval.

## Network and privacy behavior

PitMedic never transmits diagnostics, findings, repairs, hardware information, simulator activity, advertising identifiers, or contribution identity to the project. Beginning with v0.6.0.0, users may explicitly opt in to a once-daily anonymous active-installation count containing only protocol, app version, release channel, installer/portable type, and rotating daily/monthly tokens. It is off by default, shows the complete payload before consent and in Settings, uses no permanent identifier, and deletes its local key/state when disabled. A user-approved repair may ask an installed third-party application such as Steam to validate local game files under that application's own privacy terms.

## Elevation explanation

Ordinary monitoring runs without administrator rights. The installer registers a commandless, read-only CPU sensor service during its one setup approval. A separate helper requests elevation only for a compiled allowlist of repairs involving protected simulator files, installed services or anti-cheat, Windows time synchronization, or Windows integrity tools.

The helper accepts only a one-shot request from the installed PitMedic process. It validates the parent process, request identifier, incident location, and evidence-derived repair plan; it rejects arbitrary commands and non-allowlisted repair IDs. Live status returns through a current-user-only named pipe, elevated backups/logs are isolated under `%ProgramData%\PitMedic`, and the helper exits when the repair finishes.

## Maintainer and signing roles

The initial repository owner is the committer, reviewer, and release approver while PitMedic has a one-person maintenance team. Outside contributions require review. GitHub and SignPath multi-factor authentication will be enabled, and production signing will be protected by the GitHub `production-signing` environment plus SignPath manual approval.
