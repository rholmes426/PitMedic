# PitMedic repository instructions

Read `CONTRIBUTING.md` and `Source/PROJECT_POLICY.md` before changing this repository.
Use focused branches, pull requests and required Build Validation. Preserve unrelated work.

## User-initiated Scout review

When the owner asks for a "Scout review", "run Scout", "check Scout", or an equivalent
request to find fixes, follow [Knowledge/SCOUT-REVIEW.md](Knowledge/SCOUT-REVIEW.md).
Scout is manual only. Do not create or resume recurring Scout tasks or restore a
GitHub schedule unless the owner explicitly requests scheduling.

A review is active research into useful automatic repairs across supported sims
and companion software. Read the current code and queue, use the expanded
[manual source inventory](Knowledge/manual-scout-sources.json), investigate
actual discussions and outcome replies, and compare remedies with executable
coverage. A source scan, keyword report or vendor changelog summary is not a
completed Scout review.

Prioritize narrow repair actions and ways to make existing repairs reachable.
Include user-requested symptom checks when a failure produces no crash.
Persist evidence, implementation gaps, priority, validation and rollback
requirements in a dated Research report and deduplicated pending queue.
Distinguish proposed, tested, implemented and published work. Do not claim
success rates, current applicability or a repair that has not been verified.

A request for research authorizes the review and proposed queue documentation.
Implement runtime changes within the owner's actual requested scope; public
signed releases still require explicit release authorization and the protected
release workflow. Preserve existing repair lifecycle and security requirements.
