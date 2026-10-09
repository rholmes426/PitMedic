# Public diagnostic guides

`generate.py` combines the app-owned diagnostic catalog and lifecycle records with
the website-only explanations in `guides.json`. Edit the source content, then run:

```sh
python Tools/DiagnosticLibrary/generate.py
python Tools/DiagnosticLibrary/generate.py --check
python Tools/KnowledgeScout/knowledge_scout.py --validate-only
```

Every existing record has practical checks, an expected outcome, and repair limits.
Keep these consistent with the app's actual implementation. Website explanations
do not enable a repair, change detection, or authorize a lifecycle transition.
Retain the existing vendor citations and distinguish diagnostic clues from proven
causes. Expected outcomes describe what a reader should check, not measured success
rates or guarantees.

`modified` is the editorial revision date. It is shown separately from the
knowledge record's `lastVerified` date; editing prose must not imply that vendor
evidence was rechecked. Sitemap and article modification dates use the later date.

The collection's sitemap date is the later of `INDEX_MODIFIED` and all guide/evidence
dates, so publishing a new guide cannot leave the collection dated September 7.
Update `INDEX_MODIFIED` when changing the collection's own editorial content.
The homepage and static simulator pages omit optional `lastmod` values because
they have no reliable editorial date source. Do not restore frozen dates or use
the build time as a substitute. Google ignores `changefreq` and `priority`.

Optional `manual_intro`, `manual_steps`, and `manual_sources` provide a standalone
procedure with explicit scope and direct official links. Verify those instructions
against the linked vendor article before editing them. They do not change the
app-owned repair or lifecycle verification dates.

## Evidence for the September 29, 2026 expansion

All 60 guides were expanded. Eighteen include relevant automated regression
examples, with immutable test-source links and the passing Windows job:

- Commit: `6faed4256d39b9067f8f0ccfdd08a09a2feea1e1`
- [Build validation run 36379079203](https://github.com/rholmes426/PitMedic/actions/runs/36379079203)
- [Windows build job 108790897664](https://github.com/rholmes426/PitMedic/actions/runs/36379079203/job/108790897664)
- The job log was read during this review. On September 28 it reports passing
  process-exit tests, eight Steam-window scenarios, twenty iRacing repair-selection
  scenarios, and the release-policy suite.

The examples explicitly describe synthetic inputs, policy assertions, or fake
windows. They do not claim a live simulator repair succeeded, that a vendor root
cause was established, or that a real user achieved a particular result. No user
logs, incident identifiers, machine details, or personal paths were published.
Pages without matching verified tests do not invent an example. Add future examples
only after reading both the relevant test and evidence that it passed.

## Publication scope

This is a website-only editorial change using the existing protected PR and Pages
workflow. It does not change app behavior, signing, analytics, or the current app
version. The generated diagnostic database remains app-owned and unchanged.
