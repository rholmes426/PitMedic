# PitMedic pending updates

Public app baseline: **1.0.0.1**. Next app release: **1.0.0.2**. The owner authorized publishing all queued changes on
September 14, 2026. Signed app publication is pending the protected release workflow.

The owner approves development pushes and validation for requested changes. Keep
fixes and updates queued together; deliver a build only when requested, without a
separate ZIP or installer for each fix. Signing and public release require explicit
owner approval.

## App fix ready for the next version

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
