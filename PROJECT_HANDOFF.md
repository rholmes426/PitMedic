# PitMedic technical handoff

Use the repository's current code and technical records for project context.
Chat transcripts, conversation exports and private conversation summaries do not
belong in this public repository.

## Start here

- [Repository instructions](AGENTS.md) and [contributing guide](CONTRIBUTING.md).
- [Product policy](Source/PROJECT_POLICY.md) and [code signing policy](CODE_SIGNING_POLICY.md).
- [Release workflow](Build/RELEASING.md).
- [Published release metadata](website/update.json) and [README](README.md).
- [Pending updates](PENDING-UPDATES.md) and [pending fixes](PENDING_FIXES.md).
- [Knowledge catalog](Knowledge/README.md) and [manual Scout review](Knowledge/SCOUT-REVIEW.md).

Published release metadata and the actual queue take precedence over older notes.
Keep this handoff technical and concise; use issues, pull requests and release
notes for the public implementation record.

## Architecture

- `Source/PitMedic`: .NET 10 WPF app, monitoring, findings, repair planning,
  settings, companion software recovery and optional anonymous usage reporting.
- `Source/PitMedic.RepairHelper`: allowlisted elevated one-shot repairs.
- `Source/PitMedic.SensorHelper`: read-only protected CPU sensor service.
- `TelemetryWorker`: Cloudflare Worker and D1 migrations for optional aggregate usage.
- `TelemetryDashboard`: private dashboard for aggregate app and website data.
- `NeonAnalytics`: private analytics function.
- `Tools/KnowledgeScout`: read-only source discovery and evidence intake.
- `Tools/DiagnosticLibrary`: generates the public library from app-owned knowledge.
- `website`: public site, updater metadata and diagnostic pages.

## Maintenance and publication

Keep PitMedic free, ad-free, local-first and GPL-3.0-or-later. Repairs must remain
targeted, reversible, backed up where applicable and honest about the evidence.
Obtain approval before starting a repair expected to exceed two minutes.

Use focused branches, pull requests and required Build Validation. Run checks
appropriate to the changed paths, including full release validation for app,
Build or workflow changes. Preserve unrelated work.

Requested website updates use the normal validation and publication workflow,
followed by a live check. Public signed app releases require explicit release
authorization and the protected release workflow. Do not replace published tags
or re-sign an existing release. Verify release metadata, installer URLs and
checksums together.

Scout is manual only. Follow the review playbook and source inventory when a
review is requested. Distinguish researched, implemented, tested and published
repairs. Pair every implemented repair with its Diagnostic Library deliverable.
Do not retire a repair solely because a citation is old or inaccessible.

## Private conversation material

Store conversation exports and private project-history summaries privately
outside the public checkout. Do not commit them, attach them to public issues or
pull requests, or include them in release archives. Maintain public handoffs from
the technical sources above without reproducing private discussions.
