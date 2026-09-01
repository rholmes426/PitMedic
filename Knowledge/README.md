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

## Automated scout

`Tools/KnowledgeScout/knowledge_scout.py` performs a read-only review of the configured sources. It:

1. validates that implemented repair IDs have lifecycle records;
2. fetches only allowlisted HTTPS text sources with strict size and redirect limits;
3. treats every fetched page as untrusted text and never executes content;
4. compares source text and linked issue discussions with the prior run;
5. highlights possible safety/harm language, source failures, and scheduled review reminders; and
6. writes one report for a rolling GitHub issue.

The scheduled workflow runs on Tuesday and Friday and can also be started manually. It never commits, opens a pull request, changes a repair state, or publishes a release.

## Human review outcome

For each useful finding, a maintainer should record one outcome:

- no change;
- add or improve detection;
- add guidance;
- add a narrowly scoped, reversible automatic repair;
- add a product-version gate to prevent evidenced harm; or
- disable for safety, with the supporting evidence and reason retained.

New automatic repairs still require the normal PitMedic rules: evidence-based detection, backups before mutation, narrow allowlists, simulator-closed checks where relevant, verification after repair, and an explicit user approval step.
