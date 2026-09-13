# PitMedic pending updates

Public app baseline: **1.0.0.1**. The next version number and release date are not
assigned. Public release has not been approved.

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

The prepared batch must stay unmerged until the next approved deployment: changes
to the analytics packages and Pages workflow trigger production deployment when
merged into main. No dependency from this batch has been deployed yet.

| Area | Dependency | Current -> requested | Status | Proposal |
| --- | --- | --- | --- | --- |
| Workflows | actions/deploy-pages | 4 -> 5 | Prepared | [#11](https://github.com/rholmes426/PitMedic/pull/11) |
| Workflows | actions/checkout | 4 -> 7 | Prepared | [#12](https://github.com/rholmes426/PitMedic/pull/12) |
| Workflows | actions/upload-pages-artifact | 3 -> 5 | Prepared | [#13](https://github.com/rholmes426/PitMedic/pull/13) |
| Worker | @types/node | 24.13.3 -> 26.4.1 | Prepared | [#14](https://github.com/rholmes426/PitMedic/pull/14) |
| Workflows | actions/download-artifact | 4 -> 8 | Prepared | [#15](https://github.com/rholmes426/PitMedic/pull/15) |
| Dashboard | @types/node | 24.13.3 -> 26.4.1 | Prepared | [#16](https://github.com/rholmes426/PitMedic/pull/16) |
| Workflows | actions/setup-node | 4 -> 7 | Prepared | [#17](https://github.com/rholmes426/PitMedic/pull/17) |
| Backend | typescript | 5.9.3 -> 7.0.2 | Prepared | [#18](https://github.com/rholmes426/PitMedic/pull/18) |
| Worker | vitest | 4.1.11 -> 5.0.0 | Blocked: Cloudflare plugin compatibility | [#19](https://github.com/rholmes426/PitMedic/pull/19) |
| Dashboard | vitest | 4.1.11 -> 5.0.0 | Blocked: Cloudflare plugin compatibility | [#20](https://github.com/rholmes426/PitMedic/pull/20) |
| Backend | esbuild | 0.25.12 -> 0.28.2 | Prepared | [#21](https://github.com/rholmes426/PitMedic/pull/21) |
| Worker | typescript | 5.9.3 -> 7.0.2 | Prepared | [#22](https://github.com/rholmes426/PitMedic/pull/22) |
| Backend | @types/node | 24.13.3 -> 26.4.1 | Prepared | [#23](https://github.com/rholmes426/PitMedic/pull/23) |
| Dashboard | @cloudflare/vitest-plugin | 1.1.2 -> 1.1.3 | Prepared | [#24](https://github.com/rholmes426/PitMedic/pull/24) |
| Backend | @neon/functions | 0.8.0 -> 0.9.0 | Prepared | [#25](https://github.com/rholmes426/PitMedic/pull/25) |
| Worker | wrangler | 4.127.1 -> 4.128.0 | Prepared | [#26](https://github.com/rholmes426/PitMedic/pull/26) |
| Dashboard | wrangler | 4.127.1 -> 4.128.0 | Prepared | [#27](https://github.com/rholmes426/PitMedic/pull/27) |
| Worker | @cloudflare/vitest-plugin | 1.1.2 -> 1.1.3 | Prepared | [#28](https://github.com/rholmes426/PitMedic/pull/28) |
| Dashboard | typescript | 5.9.3 -> 7.0.2 | Prepared | [#29](https://github.com/rholmes426/PitMedic/pull/29) |

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
- Production-only Pages deployment and signed-artifact promotion are held for
  their next explicitly approved deployment; they are not invoked as a test.

Keep this file current as queue items are validated, deployed, or released.
