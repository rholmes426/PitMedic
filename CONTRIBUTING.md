# Contributing to PitMedic

Thank you for helping make simulator troubleshooting safer and easier.

## Before contributing

- Open an issue before a large behavior or architecture change.
- Keep PitMedic free, ad-free, local-first, and compatible with `Source/PROJECT_POLICY.md`.
- Repairs must be targeted, reversible, backed up where applicable, and honest about what changed.
- A repair expected to exceed two minutes must show an estimate and obtain user approval before it starts.
- Do not add simulator artwork, telemetry collection, advertising, affiliate links, payment handling, credential collection, or silent network uploads.
- Never commit secrets, signing tokens, certificates, user logs, diagnostic captures, or personal paths.

## Pull requests

1. Work from a focused branch.
2. Explain the user problem and the proposed behavior.
3. Include or update validation for every changed repair and safety path.
4. Update documentation and version-specific notes when user-facing behavior changes.
5. Confirm a Release build completes with the .NET 10 SDK.

Outside pull requests require maintainer review. Merging a contribution licenses it as part of PitMedic under GPL-3.0-or-later; contributors retain copyright in their work.
