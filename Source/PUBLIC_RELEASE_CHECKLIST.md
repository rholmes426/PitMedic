# PitMedic voluntary-support public-release checklist

## Completed in v0.4.3.0

- PitMedic is licensed as free and open-source software under GPL-3.0-or-later.
- The application remains completely free and ad-free.
- No license, entitlement, donor, advertising, affiliate, or payment code is present.
- Simulator publisher artwork is absent from the source package.
- Simulator names are presented only as compatibility references.
- Local-first privacy behavior is documented.
- Contribution, security, and public code-signing policies are included.
- Product, file, and assembly versions are consistently set to 0.4.3.0.
- A Windows CI build and separate protected SignPath release workflow are included.
- Signed-release validation checks the publisher, signature, timestamp, and SHA-256 value.

## User/account steps required for free signing

- Create the official public GitHub repository and push this complete source package.
- Enable multi-factor authentication on GitHub and SignPath.
- Publish at least one clearly labeled unsigned preview release so SignPath can evaluate the running project; do not call it an official signed release.
- Apply to SignPath Foundation with the public repository and project description.
- After approval, configure the four repository secret/variables listed in `CODE_SIGNING.md`.
- Configure the `pitmedic-windows-x64` artifact policy to sign only PitMedic-owned binaries.
- Protect release tags and the `production-signing` GitHub environment; require manual approval.
- Run the signing workflow against the exact protected release tag and retain the verified output.

## Required before the first public signed download

- Complete the license-text and source-link archive for every direct and transitive dependency, including the PawnIO prerequisite arrangement.
- Replace always-on administrator execution with a narrowly scoped, signed repair helper that elevates only when required.
- Test install, upgrade, uninstall, monitoring, every repair path, and recovery on clean supported Windows 10 and Windows 11 systems.
- Scan each release with Microsoft Defender and verify the installer and updater hashes.
- Complete a formal trademark clearance review for the PitMedic name.
- Publish a website privacy policy, compatibility disclaimer, contribution disclosure, support policy, and code-signing policy.
- Use a hosted contribution processor so the application and website never handle card data directly.
- Add the “Support PitMedic” link only after the real contribution page and disclosures are live.

## Ongoing release rules

- Contributions never unlock features or preferential support.
- Safety, restore, history, and access to the user's own diagnostics remain free.
- Diagnostics stay local unless a future user explicitly previews and approves an upload.
- Any future analytics or cloud feature requires prior documentation, consent design, and privacy review.
