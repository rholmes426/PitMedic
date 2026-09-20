# PitMedic pending updates

Public app baseline: **1.0.0.3**, published September 19, 2026.
The signed app, helpers and installer passed verification in [release run 35459555625](https://github.com/rholmes426/PitMedic/actions/runs/35459555625).
[Publication PR #82](https://github.com/rholmes426/PitMedic/pull/82) updates the README,
website and updater together; its deployment performs the final public checksum verification.
The September 14 items below are release history; the two blocked Vitest 5 upgrades remain deferred.

## Approved 1.0.0.3 release — September 19, 2026

The owner requested processing pending updates and publishing the app. This authorizes
the protected signed release and its required collector, dashboard and website deployments.
Implemented and merged in [PR #81](https://github.com/rholmes426/PitMedic/pull/81).
Required Windows Build Validation passed. The collector migration and dashboard
[deployment](https://github.com/rholmes426/PitMedic/actions/runs/35459486295) succeeded
before signed app publication. The protected release workflow completed successfully.

- Integrated usage PR #78 and the complete review record from #80 without discarding either branch's work.
- Implemented guidance proposals KS-20260918-AMS2-SHIPPED, AMS2-SETUPS, GHUB and SIMPRO;
  refreshed vendor evidence September 19 and regenerated all 60 Diagnostic Library records.
  No detector signatures, automatic repair actions or lifecycle states change.
- Included eight compatible dependency proposals: EventLog 10.0.12 (#75), Node types
  26.5.1 in all three services (#14/#16/#74), Cloudflare test plugin 1.1.8 (#24/#28),
  and Wrangler 4.131.1 (#26/#27). Lockfiles resolve these tested versions.
- Vitest 5 (#19/#20) remains blocked: plugin 1.1.8 still requires Vitest ^4.1.0.
- The SIMAGIC firmware-interruption audit remains pending. The executor closes the
  declared SimPro process set but has no proven firmware-state signal. Added explicit
  user guidance not to recover during flashing or uncertain completion; this is not
  an automatic firmware guard. A guard requires reliable observability and active/unknown
  update tests before implementation. No fix is retired or marked human-approved.
- All inaccessible/uncertain Scout evidence remains pending as recorded in the September 18 report.
- Local release/Scout tests pass (8 + 20); catalog validates 60 repairs and 26 sources.
  Required Windows CI, installer prerequisite checks and production analytics deployment passed.
  Interactive Windows visual smoke testing is unavailable in this Linux workspace.

## Shipped in 1.0.0.3 — optional usage reliability

- Check reporting every 15 minutes while running, including while minimized to the tray; retry failed unchanged reports hourly.
- Add a separate opt-in first-launch report for new local profiles. Preserve state across upgrades; exclude existing profiles; deduplicate retries with a one-time receipt retained for at most the 90-day reporting window.
- Show first launches separately from active profiles and downloads; remove misleading monthly-first-seen install emails.
- Shorten the first-launch consent dialog and Settings explanation; retain No thanks, the payload preview, privacy details and all existing choices.
- Backward-compatible collector route and database migration must deploy before app distribution. The September 19 request authorizes the release; verify deployment first.
- Windows visual smoke check remains for the next requested build.

The owner approves development pushes and validation for requested changes. Keep
fixes and updates queued together; deliver a build only when requested, without a
separate ZIP or installer for each fix. Signing and public release require explicit
owner approval.

## Shipped in 1.0.0.2 — Steam window control

| Change | Implementation and validation | Tracking |
| --- | --- | --- |
| Hide Steam only during repair startup; let the user reopen it and preserve subsequent window choices. Update repair messages to match. | Merged into main. Eight Steam regression scenarios and the full required Windows Build Validation passed. A live Steam desktop smoke check remains for the next requested test build. | [PR #72](https://github.com/rholmes426/PitMedic/pull/72) |

## Approved dependency batch

All 19 proposals below are approved for the next update. Seventeen are prepared on
`codex/queued-dependency-updates`; their combined CI results are tracked on the batch
pull request. Two Vitest 5 proposals remain queued with a compatibility blocker.

The 17 prepared proposals merged in PR #73 on September 14, 2026, triggering the
existing website and analytics deployment workflows. Verify their production run
results before marking deployment complete.

| Area | Dependency | Current -> requested | Status | Proposal |
| --- | --- | --- | --- | --- |
| Workflows | actions/deploy-pages | 4 -> 5 | Merged in #73 | [#11](https://github.com/rholmes426/PitMedic/pull/11) |
| Workflows | actions/checkout | 4 -> 7 | Merged in #73 | [#12](https://github.com/rholmes426/PitMedic/pull/12) |
| Workflows | actions/upload-pages-artifact | 3 -> 5 | Merged in #73 | [#13](https://github.com/rholmes426/PitMedic/pull/13) |
| Worker | @types/node | 24.13.3 -> 26.4.1 | Merged in #73 | [#14](https://github.com/rholmes426/PitMedic/pull/14) |
| Workflows | actions/download-artifact | 4 -> 8 | Merged in #73 | [#15](https://github.com/rholmes426/PitMedic/pull/15) |
| Dashboard | @types/node | 24.13.3 -> 26.4.1 | Merged in #73 | [#16](https://github.com/rholmes426/PitMedic/pull/16) |
| Workflows | actions/setup-node | 4 -> 7 | Merged in #73 | [#17](https://github.com/rholmes426/PitMedic/pull/17) |
| Backend | typescript | 5.9.3 -> 7.0.2 | Merged in #73 | [#18](https://github.com/rholmes426/PitMedic/pull/18) |
| Worker | vitest | 4.1.11 -> 5.0.0 | Blocked: Cloudflare plugin compatibility | [#19](https://github.com/rholmes426/PitMedic/pull/19) |
| Dashboard | vitest | 4.1.11 -> 5.0.0 | Blocked: Cloudflare plugin compatibility | [#20](https://github.com/rholmes426/PitMedic/pull/20) |
| Backend | esbuild | 0.25.12 -> 0.28.2 | Merged in #73 | [#21](https://github.com/rholmes426/PitMedic/pull/21) |
| Worker | typescript | 5.9.3 -> 7.0.2 | Merged in #73 | [#22](https://github.com/rholmes426/PitMedic/pull/22) |
| Backend | @types/node | 24.13.3 -> 26.4.1 | Merged in #73 | [#23](https://github.com/rholmes426/PitMedic/pull/23) |
| Dashboard | @cloudflare/vitest-plugin | 1.1.2 -> 1.1.3 | Merged in #73 | [#24](https://github.com/rholmes426/PitMedic/pull/24) |
| Backend | @neon/functions | 0.8.0 -> 0.9.0 | Merged in #73 | [#25](https://github.com/rholmes426/PitMedic/pull/25) |
| Worker | wrangler | 4.127.1 -> 4.128.0 | Merged in #73 | [#26](https://github.com/rholmes426/PitMedic/pull/26) |
| Dashboard | wrangler | 4.127.1 -> 4.128.0 | Merged in #73 | [#27](https://github.com/rholmes426/PitMedic/pull/27) |
| Worker | @cloudflare/vitest-plugin | 1.1.2 -> 1.1.3 | Merged in #73 | [#28](https://github.com/rholmes426/PitMedic/pull/28) |
| Dashboard | typescript | 5.9.3 -> 7.0.2 | Merged in #73 | [#29](https://github.com/rholmes426/PitMedic/pull/29) |

### Compatibility blocker

The published npm metadata for `@cloudflare/vitest-plugin@1.1.3` requires Vitest,
`@vitest/runner`, and `@vitest/snapshot` **^4.1.0**. The latest stable plugin,
**1.1.8**, still declares that same requirement when checked on September 13, 2026.
Both projects therefore retain their working Vitest **4.1.11** lockfile versions.

Before including #19 and #20, obtain a Cloudflare plugin release that explicitly
supports Vitest 5, then regenerate both lockfiles and rerun type checks and all
Worker/Dashboard tests. Do not use forced installs, legacy peer dependency mode,
or peer overrides to hide this conflict.

### Validation

- Worker: TypeScript 7 checks, all 15 tests, and deployment dry-run passed locally.
- Dashboard: TypeScript 7 checks, all 18 tests, and deployment dry-run passed locally.
- Backend: TypeScript 7 checks and the esbuild bundle passed locally. The CI job
  now runs the type check as well as the bundle build.
- Release automation: all eight existing regression tests passed locally.
- Workflow inputs, permissions, triggers, runner operating systems, runtime Node
  version, and release approvals are preserved. The batch PR runs the required CI.
- Production deployment and signed-artifact promotion are authorized by the owner's
  September 14 request. Signing still uses the existing protected workflow.

## Knowledge Scout review — September 14, 2026

[Full dispositions and evidence](Research/KnowledgeScoutReview-2026-09-14.md) cover the
September 11 rolling report. Review/triage is complete; the following changes are
implemented in the 1.0.0.2 preparation branch; full CI validation and publication
are tracked on its release PR. The app guidance expands existing diagnostic
records without adding detector signatures or automatic repair actions.

| Group | Queued work | Acceptance criteria |
| --- | --- | --- |
| iRacing | Add update-first guidance for vendor-fixed minimize/replay crashes and the Season 4 AI-transition/Porsche Mission R crashes. | Cite exact vendor releases; establish symptom/version relevance; unknown versions get conditional guidance. Preserve genuine crash evidence and existing updater guards. |
| Le Mans Ultimate | Add vendor-known network/phantom-collision guidance. | Distinguish announced from shipped fixes; verify September 15 patch status before publication. Do not map generic crashes to this issue or make broad network changes. |
| Automobilista 2 | Add vendor-update guidance for weather-transition hitches and multiplayer crashes. | Confirm shipped release/version before asserting fixed status; resolve inconsistent version strings in the announcement. Do not reset profiles for a vendor bug. |
| Fanatec / RaceRoom | Explain App-versus-legacy compatibility; add CSL DD/GT DD Pro menu-return oscillation guidance backed by the official 1.5.4.1 hotfix. | Show relevant hardware/software context; link vendor instructions; keep uninstall, driver and firmware changes manual. Do not retire legacy repairs merely because development ended. |
| Scout maintenance | Deduplicate across sources, group forum pagination, validate canonical URLs, scope safety text to relevant products, retain review dispositions between scans, and check source health from the actual runner. | Regression examples: repeated iRacing URLs, malformed Reiza paths, unrelated driver-conduct/Logitech banners. Retain unresolved items across rolling report replacement; no auto-promotion to code or release. |

### Evidence still needed

- LMU G29/LmuFFB thread: reader returned forum index; obtain actual discussion before considering a remedy.
- LMU warping/crash thread: obtain actual discussion; official networking announcement alone does not prove the reported crash cause.
- AMS2 development thread page 2: inaccessible in this review; grouped with its parent without claiming its comments were verified.
- Fanatec forum: accessible through browser, but the Scout runner's 403 has not been retested.

Two unrelated safety signals were dismissed. G HUB service recovery is already
covered. No new automatic repair or repair-state change was approved by this review.

Keep this file current as queue items are validated, deployed, or released.

## Proposed Knowledge Scout follow-up — September 18, 2026

**Pending human review; not approved or release-authorized.**
[Evidence, dates and all dispositions](Research/KnowledgeScoutReview-2026-09-18.md).
Four guidance proposals from 30 retained sources; seven need evidence and nineteen
require no new entry. Four older unresolved records remain open. These are newly
reviewed sources, not four newly released fixes.

| ID | Proposed work | Acceptance / limits |
| --- | --- | --- |
| KS-20260918-AMS2-SHIPPED | Update existing AMS2 graphics/content guidance using shipped 1.6.9.95 notes instead of announcement-only evidence. | Match version/scenario; preserve unknown-cause wording and genuine faults. No reset, rollback or detector change. |
| KS-20260918-AMS2-SETUPS | Explain 1.6.8 setup-autoload behavior before assuming setup corruption. | Check version/preference; preserve saved data and existing evidence-based recovery. |
| KS-20260918-GHUB | Extend existing G HUB guidance with the reviewed July/August 2026 vendor escalation options. | Version-aware, manual vendor path; preserve settings and restart policy; no inferred crash signature. |
| KS-20260918-SIMPRO | Add conditional SimPro display/game-data update guidance. | Match issue, build and hardware; preserve generation compatibility; no firmware or profile mutation. |
| KS-20260918-SIMPRO-FIRMWARE | Safety-review-required: assess recovery while firmware flashing is active or uncertain. | Audit execution guards and test state handling; no proven PitMedic incident and no repair-state change authorized here. |
| KS-20260918-SCOUT-INTEGRATION | **Implemented in merged [PR #79](https://github.com/rholmes426/PitMedic/pull/79).** Evidence triage now runs inside each Scout scan; no separate recurring review task. | Deterministic triage only: bounded fetches, canonical deduplication, cache reuse, deferred/inaccessible retention and explicit pending status. No semantic approval, repair-state change or release action. |

Historical AMS2 CPU-thread workaround stays in investigation until current-build
and hardware-specific A/B validation; it is not a queued automatic optimization.
Fanatec runner 403 and inaccessible LMU/Kunos/KW discussions remain visible.
The separate review automation remains disabled. GitHub discovery and inline
evidence triage are both active; human review and release approval remain separate.
