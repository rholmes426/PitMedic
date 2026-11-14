# PitMedic 1.0.0.12 release review — November 14, 2026

Owner authorized processing pending app/library updates and publishing the next
signed release. Baseline public app: 1.0.0.11. Baseline main: ccf6635bdeff4bac5e9f55dd68642a1bd27a290b.

## Included

- Previously merged crash-finding foreground/review behavior for simulator dump
  and matching Windows fault cases without an executable repair.
- Three scoped configuration repairs from the manual Scout shortlist: iRacing
  socket setting, Error 63 app.ini recovery and LMU's exact controller-file set.
- Repair Tools and Restore on the relevant simulator pages, allowing a user to
  select a symptom without generating a false crash. Manual history records are
  labeled user-requested troubleshooting.
- Exact Error 77 classification and guarded single-content recovery; unknown,
  ambiguous or unsafe targets use supported verification without guessed removal.
- Three paired Diagnostic Library guides plus Error 77 guide updates. Availability
  metadata prevents new repair pages appearing with an older public download.
- Manual Scout process/source expansion already merged in PRs #189/#190.

The last release already included the November 7 companion/RaceRoom/iRacing
guidance and app visual-parity work. Those are reconciled as shipped, not counted
again as new fixes.

## Validation and limits

Current official iRacing socket/Error 63/Error 77 articles and LMU Common Fixes were
read again on November 14. The remedies are vendor-documented; tests use temporary
configuration and log fixtures. No live wheel, pedal or simulator-success claim
is made.

New regression coverage exercises encoding/comments/sections, missing and duplicate
keys, backup integrity, redirects to an actual supplied Documents path, no-op
behavior, Restore with preservation of regenerated/current data, locked files,
cancellation, exact content/path bounds, live-log baselines, real matcher-to-plan
routing, manual request reconstruction and retest labeling. Publication tests
check that only the supporting app version enables the new guide pages.

Local app Release compilation and the pre-existing process/Steam/repair suites
passed during implementation. Full required Windows CI, helper/sensor builds,
installer checks, protected signing and publication verification must pass before
this release is reported published.

## Remaining investigations

Simagic/360 Hz, SimHub telemetry settings, LMU DirectInput fallback, Simucube
Link-port automation, Tuner process identity and reliable firmware-state
observability remain pending their recorded applicability/hardware/evidence
requirements. Other older research items need a future substantive review before
being treated as executable fixes. They are not active new repair claims.

Open backend/analytics/dependency proposals are outside this app repair release.
No firmware flashing, driver replacement, global security changes, telemetry
collection or analytics deployment is added.
