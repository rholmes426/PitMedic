# PitMedic voluntary-support public-release checklist

## Completed in v0.4.4.0

- PitMedic is licensed as free and open-source software under GPL-3.0-or-later.
- The application remains completely free and ad-free.
- No license, entitlement, donor, advertising, affiliate, or payment code is present.
- Simulator publisher artwork is absent from the source package.
- Simulator names are presented only as compatibility references.
- Local-first privacy behavior is documented.
- Contribution, security, and public code-signing policies are included.
- Product, file, and assembly versions are kept consistent across the app and scoped helpers.
- A Windows CI build and separate protected Azure Artifact Signing release workflow are included.
- The ordinary monitoring application runs without administrator rights.
- The installer creates a commandless read-only CPU sensor service during its one setup approval so protected sensor access does not prompt on every app launch.
- A one-shot repair helper elevates only evidence-derived repairs in its compiled allowlist.
- The Inno Setup installer installs the application and helper in Program Files and keeps elevated repair storage under ProgramData.
- Signed-release validation checks the app, both scoped helpers, and installer publisher, signature, timestamp, version, and SHA-256 value.

## Signing infrastructure now in use

- Create the official public GitHub repository and push this complete source package.
- Enable multi-factor authentication on GitHub and Microsoft Azure.
- Configure GitHub-to-Azure OIDC so the protected workflow uses short-lived credentials without a repository signing secret.
- Limit the Azure signing action to `PitMedic.exe`, `PitMedic.SensorHelper.exe`, `PitMedic.RepairHelper.exe`, and the completed `PitMedic-Setup-x64.exe` installer.
- Protect release tags and the `production-signing` GitHub environment; require manual approval.
- Run the signing workflow against the exact protected release tag and retain the verified output.

## Required before the first public signed download

- Complete the license-text and source-link archive for every direct and transitive dependency, including the PawnIO prerequisite arrangement.
- Test install, upgrade, uninstall, monitoring, every repair path, and recovery on clean supported Windows 10 and Windows 11 systems.
- Scan each release with Microsoft Defender and verify the installer and updater hashes.
- Complete a formal trademark clearance review for the PitMedic name.
- Publish a website privacy policy, compatibility disclaimer, contribution disclosure, support policy, and code-signing policy.
- Use a hosted contribution processor so the application and website never handle card data directly.
- The hosted PayPal contribution page and plain-language disclosure are live before enabling the “Support PitMedic” link.

## Ongoing release rules

- Every downloadable test build receives a new version number; a version is never reused after a tester can download it.
- Contributions never unlock features or preferential support.
- Safety, restore, history, and access to the user's own diagnostics remain free.
- Diagnostics stay local unless a future user explicitly previews and approves an upload.
- Website analytics remain cookie-free and are documented in the public privacy policy.
- Anonymous app-usage counting remains voluntary, off until the user makes a choice, limited to the documented six-field heartbeat, and independently disableable from update checks.
- Raw rotating usage tokens are rolled into anonymous counts and deleted after their day or month closes; only aggregate totals persist.
- Any future expansion of analytics or cloud features requires prior documentation, consent design, and privacy review.
