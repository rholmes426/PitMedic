# PitMedic repair knowledge lifecycle

This directory defines how PitMedic keeps its simulator and companion-software repair knowledge current.

## Governing policy

- A repair does not expire because it is old, has not appeared recently, or its citation moved.
- A broken citation starts a replacement-source search; it does not disable the repair.
- Scheduled review dates are reminders, not expiration dates.
- A repair may be disabled, version-gated, or superseded only when credible evidence shows that using it could cause harm. Age, redundancy, or ineffectiveness alone is not enough.
- Disabling, version-gating, enabling, or publishing a repair always requires human review. The Knowledge Scout cannot make those changes.
- Historical records remain in the lifecycle catalog after a repair is disabled or superseded, including the evidence and reason.

## Lifecycle states

| State | Meaning |
|---|---|
| `active` | Available when PitMedic's detector and safety policy allow it. |
| `guidance` | Informational guidance without an automatic mutation. |
| `disabled-for-safety` | Preserved but unavailable because reviewed evidence indicates possible harm. |
| `version-gated` | Available only for reviewed product versions where it remains appropriate. |
| `superseded` | Preserved for history and replaced by another entry. |

Only the first two states are assigned by adding ordinary knowledge. The final three require a reviewed code change with evidence recorded in `lifecycle.json`.

## Manual Scout review

Scout is owner-initiated only. When asked for a Scout review, follow
[SCOUT-REVIEW.md](SCOUT-REVIEW.md): actively search for useful executable repairs,
validate actual remedies and outcomes, compare with runtime coverage, and persist
ranked implementation proposals. Use the expanded
[manual source inventory](manual-scout-sources.json), which supplements the
27 enabled vendor sources with 22 community, developer and platform entries.
A keyword report is intake material, not a completed review.

The separate ChatGPT review remains disabled. The GitHub cron schedule was removed
at the owner's request on November 13, 2026. Do not enable recurring Scout work
without a new scheduling request.

## Optional manual source intake

`Tools/KnowledgeScout/knowledge_scout.py` performs a read-only review of the configured sources. It:

1. validates that implemented repair IDs have lifecycle records;
2. accepts monitored sources only from first-party sites or vendor-operated communities, then fetches only allowlisted HTTPS text with strict size and redirect limits;
3. treats every fetched page as untrusted text and never executes content;
4. compares source text and up to 40 keyword-matched links per source with the prior run;
5. highlights possible safety/harm language, source failures, and scheduled review reminders; and
6. fetches pending candidate pages for a bounded rule-based evidence review during the same run; and
7. writes discovery and evidence-review results into the same rolling GitHub issue.

The source-intake workflow runs only when manually dispatched. It never commits, opens a pull request, changes a repair state, or publishes a release.

The source-intake registry contains 27 enabled focused sources. Search results may help a maintainer locate a candidate, but the Scout monitors the direct vendor source rather than a search result, repost, or general social feed.

| Product | Trusted coverage |
|---|---|
| Le Mans Ultimate | Official release/news index and the vendor-operated community |
| iRacing | Official support center and official release-note collection |
| Assetto Corsa EVO | Kunos-operated announcements and troubleshooting |
| Assetto Corsa Competizione | Kunos-operated announcements and troubleshooting |
| RaceRoom | KW Studios announcements and community support |
| Automobilista 2 | Reiza announcements and help/support |
| MOZA Pit House | Official support, Pit House known issues, and downloads |
| Simucube Tuner / True Drive | Official Tuner documentation, downloads and changelog |
| Fanatec software | Official downloads, support release notes and the Fanatec-operated app release forum |
| Logitech G HUB | Official recovery guidance and release notes |
| SIMAGIC SimPro Manager | Official downloads and announcements |
| Asetek RaceHub | Official knowledge base and RaceHub download page |
| VRS DirectForce | Official downloads |

Support and forum indexes report newly matched issue links without treating routine page churn as a finding. Version-bearing download, release, and single-article pages can also report an in-place text change. The rolling issue state is compressed so the broader link baseline remains within GitHub's issue-body limit, while state written by the earlier Scout remains readable.

Known Fanatec category, VRS product and SIMAGIC product-detail routes, plus explicit
unrelated trading-platform spam, are excluded from new link discovery. Support and
release-note links remain eligible. Existing findings require recorded review
decisions; filtering never changes a repair lifecycle state.

## Human review outcome

For each useful finding, a maintainer should record one outcome:

- no change;
- add or improve detection;
- add guidance;
- add a narrowly scoped, reversible automatic repair;
- add a product-version gate to prevent evidenced harm; or
- disable for safety, with the supporting evidence and reason retained.

New automatic repairs still require the normal PitMedic rules: evidence-based detection, backups before mutation, narrow allowlists, simulator-closed checks where relevant, verification after repair, and an explicit user approval step.

## Source-intake keyword triage

The optional intake script checks both newly discovered and retained pending URLs
in the same manually dispatched GitHub Actions run. No separate ChatGPT task, model call, API key or AI credits are
used. This is deterministic evidence triage, not a full semantic or human review.
It reuses source pages already fetched, groups duplicate candidate URLs, checks
allowlisted pages, flags inaccessible/redirected content and possible harm wording,
and highlights remedy wording for further review. A keyword match never confirms
a fix or its version applicability. Existing review decisions are shown as context.

Up to 32 additional candidate pages are fetched per run. Results are retained in
the rolling issue state and retried after seven days, sooner for a new finding or
a page already fetched by the source scan. Oldest unchecked items are processed
first; overflow stays explicitly deferred. Offline runs never fetch evidence.
All candidates remain pending until a maintainer records a decision; no repair
state, release queue, app code or public release is changed automatically.

The separate scheduled ChatGPT review was disabled on September 18, 2026 to avoid
duplicate recurring AI usage. On November 13 the owner chose a manual substantive
review using SCOUT-REVIEW.md instead of either recurring process.
