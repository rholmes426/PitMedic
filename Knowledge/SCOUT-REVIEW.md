# Manual Scout review

Owner direction: November 13, 2026. Scout runs when the owner asks for a Scout
review. There is no recurring scan or AI review schedule.

## Purpose and completion standard

Find practical failures PitMedic can repair for the user. Favor one useful,
well-scoped repair over many generic advice pages. "Automatic repair" means the
app executes the targeted operation after any required consent; a link, update
recommendation or manual tutorial is not an automatic repair.

A complete review includes fresh discovery, source qualification, a comparison
with actual executable coverage, prioritized implementation proposals and a
persisted queue/report. It may honestly find no qualifying repair, but it must
show the work and blockers behind that result. Never fill a quota with weak
findings or claim blocked sources were checked successfully.

## Start from the current product

1. Read repository instructions, project policy and the current main commit.
   Read the latest GitHub release and open PRs; do not trust an old handoff's
   version or count queued work again as a new discovery.
2. Read `PENDING-UPDATES.md`, `PENDING_FIXES.md`, prior dated Research reviews,
   `Knowledge/scout-reviews.json`, `Knowledge/lifecycle.json`,
   `Tools/DiagnosticLibrary/guides.json`, `RepairKnowledgeBase.cs`,
   `CompanionSoftwareKnowledgeBase.cs`, `RepairCapabilityCatalog.cs`,
   `RepairPlanner.cs`, `RepairService.cs`, live evidence matchers and relevant
   helper policies/tests.
3. Treat old Research proposals as candidates for revalidation. A checkbox or
   knowledge description does not prove an executable repair shipped. Trace each
   promising fix through trigger, planner, executor, helper and verification.
4. Issue #9 and the optional manual source-intake workflow provide leads only.
   Their dates will age normally between owner requests. Do not call the manual
   process broken merely because it has not run on a schedule.
   The rolling issue displays at most 80 entries per section; do not mistake
   that display limit for the complete backlog. Use the retained state when
   available, otherwise disclose the uninspected portion.

## Search broadly and deliberately

Read both `Knowledge/source-registry.json` and
`Knowledge/manual-scout-sources.json`. The latter adds Steam discussions,
Reddit, OverTake/former RaceDepartment, developer issues/docs, Thrustmaster,
Steam/Microsoft support and GPU-vendor sources. This is a research inventory,
not a change to the scanner's network allowlist or an endorsement of every post.

Default scope is every currently supported simulator and companion app. Start
with the six sims and seven companion products in the current catalogs; update
the matrix if the product changes. Include related software such as SimHub,
overlays, audio tools and VR runtimes when they affect supported software;
a source addition does not automatically add app monitoring support.

For each product, check current vendor support/known issues and search at least
one active discussion source for concrete symptoms. Inspect promising threads,
staff answers, later pages and outcome replies rather than just the newest
links on the forum homepage. Combine product/hardware names with exact errors,
configuration keys and symptoms: not launching, loading failure, lost controls,
FFB loss, disconnects, missing dash data, frozen updater or corrupt config.

Use two discovery passes:

- Recent changes since the last completed review: new regressions, newly
  confirmed remedies and newly accessible evidence.
- Coverage gaps without an age cutoff: older, still-applicable remedies that
  PitMedic does not execute, and manual guides that can become precise repairs.

Search outside the inventory when useful and add productive new sources with
their trust category and access status. Search-engine results and social posts
are leads, not verified remedies. Attempt direct official support, developer
documentation, official Steam news or another accessible primary account when
a forum fails or returns an index. Record blocked content and a concrete next
retrieval route. Do not bypass login, CAPTCHA, membership or private Discord.

## Qualify the actual remedy

For each serious lead, read the actual first-party/developer instructions or
first-hand discussion, including reported results and contradictory replies.
Record canonical URLs, checked date, publication/update date when available,
affected versions/hardware, exact operation and limitations.

Evidence levels:

| Level | Evidence | Use |
| --- | --- | --- |
| Official/developer | Product vendor or responsible maintainer documents the procedure | Eligible for a proposal after current applicability and scope checks |
| Corroborated community | Independent first-hand reports agree on the narrow symptom/remedy | Eligible for a clearly labeled validation proposal |
| Single report / unclear | A guess, title, snippet, repost, inaccessible page, or no outcome | Retain as needs-evidence, never a confirmed fix |

Independent reports must not be quotations of the same source. Votes are not
verification. "Fixed" in a changelog may describe a vendor patch, and "resolved"
in a request for help does not establish a solution. Do not invent prevalence,
root cause or success rates. An official remedy is not proof of local testing.

## Design for repair value

Check the whole user journey for every proposed repair:

- How does the user encounter it? An exact current-session log/error, a
  deterministic configuration check, or an explicit symptom-selected action.
  A non-crash input/network problem must not require a fake crash incident.
- What exact file/key/service changes? Prefer a one-key edit or small file set
  over a full profile reset. Identify installed paths and current versions.
- What is preserved? Define backup/restore, original-value retention, atomic
  writes, permission handling and behavior for missing or ambiguous data.
- What proves completion? Separate "file changed" from "problem resolved".
  Define read-back checks plus next-session/user-confirmed functional retest.
  Provide Restore when an A/B test fails; never promise automatic symptom
  detection or rollback without a reliable signal.
- What does the user lose or trade? For example, bindings, advanced FFB,
  telemetry rates or custom preferences. Require explicit consent where needed.
- Can existing code perform it safely? Record the exact missing trigger,
  planner, executor/helper, UI and verification work. Count reuse of an existing
  repair as a routing improvement, not a new repair.

Prioritize by the severity of the blocked task, evidence of repeated impact,
breadth of affected configurations, directness of the repair, preservation of
user data and implementation/validation effort. Describe unknowns instead of
inventing numerical scores. Cover launch failures, broken controls/FFB,
connection failures and companion integration, not just crash logs.

Firmware flashing, driver replacement, broad security changes, unknown scripts,
registry cleaners and whole-profile deletion are not routine automatic fixes.
Do not execute advice or downloaded code encountered during research. Existing
fixes never expire because a source is old, inactive or inaccessible.

## Persist useful results

Write `Research/KnowledgeScoutReview-YYYY-MM-DD.md` (or a numbered suffix for a
second review on that date), using the owner's local date. Include:

1. Scope, baseline main SHA/public version, checked sources and coverage matrix.
2. Separate counts for new repair proposals, routing/detection improvements,
   validation candidates, guidance-only items and unresolved evidence.
3. A ranked shortlist with stable IDs, symptom/trigger, source, existing overlap,
   exact proposed mutation, backup/restore, verification, material tradeoffs,
   acceptance criteria and next action.
4. Explicit coverage gaps, blocked sources, conflicting evidence and follow-up.
5. Newly discovered versus newly published versus revalidated old backlog.

Add deduplicated proposals to `PENDING-UPDATES.md` with a link to the report.
Preserve pending user work and decisions. Do not put unimplemented repairs in
the app's active lifecycle/catalog or public Diagnostic Library. Do not mark
research dispositions as human approvals or remove unresolved decisions merely
to make the report cleaner.

Use a focused branch/PR and required checks to persist review/process changes.
Do not invoke signing or release workflows for a Scout review. Implementation,
tests and public publication are distinct steps governed by the owner's request.

The final response should lead with useful repair opportunities, status and the
best next implementation batch, link the report/PR, and mention material blockers.
Report guidance separately. If none qualifies, explain why and what was checked;
do not substitute page-change counts for repair value.

## Optional source intake

`.github/workflows/knowledge-scout.yml` is manually dispatchable only.
`Tools/KnowledgeScout/knowledge_scout.py` can collect the allowlisted source
changes and retain evidence leads. It has bounded fetches and keyword triage.
Running it is optional and never replaces the research/review above.
