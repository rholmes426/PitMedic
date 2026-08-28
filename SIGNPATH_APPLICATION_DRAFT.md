# SignPath Foundation application draft

Replace the bracketed URLs after the public repository and preview release exist. The rest is ready to paste into the SignPath Foundation application.

## Project

**Name:** PitMedic

**Repository:** https://github.com/rholmes426/PitMedic

**Project website:** https://pitmedic.com

**Preview download:** `[PUBLIC GITHUB PREVIEW RELEASE URL]`

**License:** GPL-3.0-or-later

**Code-signing policy:** https://github.com/rholmes426/PitMedic/blob/main/CODE_SIGNING_POLICY.md

**Privacy statement:** https://github.com/rholmes426/PitMedic/blob/main/Source/PRIVACY.md

## Description

PitMedic is a free, ad-free Windows simulator reliability monitor and repair assistant. It monitors supported sim-racing titles, records local evidence when a software failure occurs, explains findings in plain language, and offers safe, reversible repairs for known problems. Repairs are user-visible, bounded by duration and approval rules, and use recovery copies where applicable.

## Signed artifact

PitMedic is a .NET 10 WPF Windows x64 application. The initial artifact configuration should preserve the self-contained publish directory and Authenticode-sign only the PitMedic-owned `PitMedic.exe`. Third-party assemblies must remain under their upstream identities and must not be signed as PitMedic.

## Build provenance

The checked-in `.github/workflows/sign-release.yml` workflow checks out an explicit protected release tag, verifies that the tag and project version agree, builds the application on a GitHub-hosted Windows runner, uploads that exact workflow artifact, submits it to SignPath, and verifies the returned publisher, version, signature, timestamp, and SHA-256 value. Every production signing request requires manual approval.

## Network and privacy behavior

PitMedic does not transmit diagnostics, usage statistics, analytics, advertising identifiers, or contribution identity to the PitMedic project. Diagnostics and repair records remain local unless the user deliberately exports or shares them. A user-approved repair may ask an installed third-party application such as Steam to validate local game files under that application's own privacy terms.

## Elevation explanation

The current preview requests administrator rights because it reads hardware sensors and can perform user-approved repairs involving protected files or system settings. Repair actions are documented in the public source and repair matrix. Before the first general public signed release, PitMedic plans to move elevated operations into a narrowly scoped signed repair helper so ordinary monitoring can run without administrator rights.

## Maintainer and signing roles

The initial repository owner is the committer, reviewer, and release approver while PitMedic has a one-person maintenance team. Outside contributions require review. GitHub and SignPath multi-factor authentication will be enabled, and production signing will be protected by the GitHub `production-signing` environment plus SignPath manual approval.
