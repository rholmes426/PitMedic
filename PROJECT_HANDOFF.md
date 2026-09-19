# PitMedic project handoff

This file is the working brief for another coding assistant, including GitHub Copilot.
The repository is the source of truth for code, tests, issues, pull requests, release
workflows, and queued work. Read this file together with `CONTRIBUTING.md`,
`Build/RELEASING.md`, `CODE_SIGNING_POLICY.md`, `Source/PROJECT_POLICY.md`,
`Knowledge/README.md`, `PENDING-UPDATES.md`, and `PENDING_FIXES.md`.

## Current state

- Repository: `rholmes426/PitMedic`
- Main branch: protected; use pull requests and required Build Validation checks.
- Current public release: `v1.0.0.3`.
- Release metadata PR: #82 (`release-metadata/v1.0.0.3`).
- The 1.0.0.3 implementation batch merged through PR #81.
- Analytics migration, Worker, dashboard, and website deployment completed before signing.
- Vitest 5 upgrade PRs #19 and #20 remain blocked by the Cloudflare Vitest plugin peer requirement.
- SIMAGIC firmware interruption protection remains a safety-review item; do not claim it is implemented.
- Unresolved Knowledge Scout findings remain retained. Broken or inaccessible citations do not retire fixes.

## Product rules

PitMedic is free, ad-free, local-first, and GPL-3.0-or-later. Repairs must be narrow,
reversible, backed up where applicable, approval-gated when needed, and honest about
what was verified. Do not add telemetry in the sim-racing sense, diagnostics uploads,
advertising, payments, credentials, silent downloads, or public releases without the
protected release workflow.

Do not mark machine dispositions as human approvals. Do not change lifecycle state,
repair state, signing configuration, or release tags to hide pending decisions.

## Architecture

- `Source/PitMedic`: .NET 10 WPF app, monitoring, findings, repair planning, settings,
  companion software recovery, and optional anonymous usage reporting.
- `Source/PitMedic.RepairHelper`: allowlisted elevated one-shot repairs.
- `Source/PitMedic.SensorHelper`: read-only protected CPU sensor service.
- `TelemetryWorker`: Cloudflare Worker and D1 migrations for optional aggregate usage.
- `TelemetryDashboard`: private dashboard for aggregate app and website data.
- `NeonAnalytics`: private analytics function.
- `Tools/KnowledgeScout`: deterministic, read-only source discovery and same-run evidence triage.
- `Tools/DiagnosticLibrary`: generates the public library from app knowledge.
- `website`: public site, updater metadata, and diagnostic pages.

## Release rules

Routine changes go through a focused branch and PR. Run the relevant tests and the full
release validation when app, Build, workflow, or unknown files change. Public signing is
performed only by `.github/workflows/publish-release.yml` from protected `main`, with an
explicit approved version. Never replace an existing tag or re-sign a published release.
The workflow creates a generated metadata PR; inspect and merge that PR through normal
branch protection, then verify README, latest release, homepage, updater, installer URL,
and SHA-256 checksum.

## Knowledge Scout rules

Use actual first-party/vendor-community pages, not titles, snippets, forum indexes, or
announcements of unshipped fixes. Separate newly discovered from newly published material.
Classify findings as no-change/duplicate/unrelated, needs-evidence, proposed-guidance,
proposed-detection, proposed-reversible-repair, or safety-review-required. Preserve
uncertain findings and never retire an existing fix because it is old or its citation broke.

## Chat history

`CHAT_TRANSCRIPTS/` contains the available project-history index and the instructions for
adding verbatim exports. The assistant runtime does not expose the complete raw ChatGPT
transcript as a repository file, so the included history is explicitly labeled as a
summary/index. Do not present it as a complete verbatim export. If a full export is
provided later, place it there without rewriting it and record its source/date.
