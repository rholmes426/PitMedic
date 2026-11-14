# Manual Scout review — November 13, 2026

## Scope and result

Owner requested a manual, repair-focused process and broader discovery sources.
Baseline main: `18c43a7ba70fbf7d342894d4b6014a3779eeaa77`.
Latest public release checked: `v1.0.0.11`.
The November 13 intake report found 10 changed/new items but is not a substantive
repair review. It shows 80 retained entries per displayed section; that is a
presentation cap, not a verified total backlog size.

This is an initial targeted engineering-gap review and source-expansion pass,
not an exhaustive review of all 49 source entries or every old finding.
Deep review concentrated on iRacing/LMU remedies. Other products received the
source checks recorded below. No local runtime reproduction or hardware success
is claimed. Runtime repairs and public library entries are not changed by this PR.

Results:

- Three official-source repair proposals: socket setting, LMU input files,
  iRacing Error 63 app.ini recovery.
- One detection/routing improvement using existing content-recovery work: Error 77.
- Three validation candidates: Simagic polling A/B test, SimHub/iRacing telemetry
  configuration, LMU DirectInput Fallback.
- One existing hardware-validation item carried forward without counting it new:
  iRacing/Simucube Link port.
- No new generic update-guide page proposed as an automatic repair.

The first three and the routing improvement are proposed engineering work, not
tested or approved repair implementations. Exact acceptance criteria follow.
There is no reliable prevalence estimate; priority reflects directness,
scope, user impact and evidence quality rather than invented success scores.

## Source expansion

`Knowledge/manual-scout-sources.json` adds 22 entries to the 27 enabled vendor
sources: all six simulator Steam discussion hubs; seven relevant Reddit
communities; OverTake (former RaceDepartment); SimHub developer wiki and issues;
Thrustmaster support; Steam and Microsoft support; NVIDIA driver support/forum;
AMD support.

Entries distinguish an opened body/index from indexed leads or a seed requiring
retrieval. The 49-entry inventory is a place to research, not a claim that 49
sources were fully reviewed or that every post is reliable. The original
scanner allowlist and source-authority validation are unchanged.

Broader discovery produced a concrete community lead: multiple first-hand LMU
users report that a DirectInput fallback setting corrected flickering pedal or
clutch input. This is narrower than deleting all controls, but current-build and
hardware validation is still required.

## Code audit

Compared current `RepairKnowledgeBase.cs`, `RepairCapabilityCatalog.cs`,
`RepairPlanner.cs`, `RepairService.cs`, `IRacingLiveLogMonitor.cs`,
`SimulatorLiveLogMonitor.cs`, `ElevatedRepairPolicy.cs`, existing Research
records and pending files.

The first four candidates revisit older research whose proposals did not become
these executable paths. This is revalidated backlog, not four newly published
vendor fixes. No current executor/planner path for `connect_sockets`, the LMU
four-file set, Error 63's app.ini recovery, `loadSimagicAPI`, or
`simucubeTreatLinkPortAsWheel` was found in the inspected code. Error 77 is
absent from the live iRacing matcher; nearby errors 22/71 and 49/61/62/72 are
handled. The socket-error numbers also lack their specific repair routing.

A separate reachability problem matters: loss of controls, connection failures
and missing companion data can happen without a crash. Implementations must
define a real evidence trigger or a user-selected symptom check; do not make a
user manufacture a crash to access a useful fix.

## Ranked engineering shortlist

### KS-MAN-IR-SOCKETS — scoped iRacing socket connection test

Status: proposed-reversible-repair. Priority: first implementation batch.

- Source, checked November 13: [iRacing Error 10057/10035/10054](https://support.iracing.com/support/solutions/articles/31000156152-error-10057-error-10035-and-error-10054)
  (vendor modified June 5, 2024) and
  [Cannot connect to Server](https://support.iracing.com/support/solutions/articles/31000133469-can-not-connect-to-server)
  (January 12, 2024).
- Vendor procedure: set `[Communications] connect_sockets` from 0 to 1 in
  Documents/iRacing/core.ini; revert if it does not help.
- Trigger: exact socket error from current-session evidence, or an explicit
  "Cannot connect / socket error" check. A generic network hiccup does not prove
  this cause. Preserve the vendor's offline-test diagnostic distinction.
- Gap: the live matcher has general connection-failure reporting, but no
  corresponding scoped setting repair.
- Proposed operation: with iRacing and updates closed, resolve the user's actual
  Documents path, preserve the original bytes/value and change only the existing
  unambiguous key in the correct section. Do not edit firewall rules, services,
  network adapters or Windows security.
- Acceptance: preserve encoding/comments/other keys; handle redirected Documents;
  reject ambiguous/duplicate keys, symlinks/reparse escape and unsafe paths;
  already-1 is a no-op; failed write keeps original file; verify read-back;
  test wrong-section and stale-log negatives.
- Functional verification: retry the same join/offline test and ask the user
  whether it worked. Provide Restore for an unsuccessful A/B test. A successful
  file write alone is not a resolved connection.

### KS-MAN-LMU-INPUT — official four-file controller recovery

Status: proposed-reversible-repair. Priority: first implementation batch.

- Source, checked November 13: [LMU Common Fixes](https://guide.lemansultimate.com/hc/en-gb/articles/13260585473551-Common-Fixes).
  Publication/update date not supplied in the retrieved article.
- Vendor procedure names `direct input.json`, `current controls.json`,
  `keyboard.json` and `gamepad.json` in the installed game's
  `UserData/player` directory.
- Trigger: matching controller-initialization/parse evidence, or a user-selected
  "Controls stopped working" check. A missing optional file or an unrelated crash
  is insufficient evidence. Try less disruptive binding/device checks first.
- Gap: no exact LMU control-file plan/executor in the inspected code.
- Proposed operation: while LMU is closed, show that bindings/calibration may need
  rebuilding; preserve and quarantine only existing members of this exact set.
  Do not reset the rest of UserData, profiles, setups or device firmware.
- Acceptance: confirm actual installed layout/current names; one recoverable
  backup manifest per operation; fail safely if any backup fails; allow no-op
  when none exists; check writable paths and elevated-helper allowlist where
  needed; test restoration, partial failure and unrelated-file preservation.
- Functional verification: LMU regenerates readable files and the user tests
  steering, throttle, brake and buttons. Preserve both original backup and any
  newly regenerated data when restoring.

### KS-MAN-IR-63 — precise spotter-load recovery

Status: proposed-reversible-repair. Priority: first implementation batch.

- Source, checked November 13: [iRacing Loading Error 63](https://support.iracing.com/support/solutions/articles/31000156684-loading-error-63),
  vendor modified January 12, 2024.
- Vendor explains that this error occurs loading a spotter sound and documents
  preserving/renaming app.ini so iRacing can regenerate it.
- Trigger: exact current Error 63 or an explicit matching user check.
- Gap: no Error 63 matcher or dedicated app.ini reset plan was found. The existing
  Error 3 plan moves the broader user profile and is not an appropriate substitute.
- Proposed operation: preserve app.ini and move only that file aside with
  explicit consent; explain that settings may reset. Do not delete a custom
  spotter package based only on Error 63. The older research's custom-spotter
  quarantine needs separate path/evidence work and is not vendor-verified here.
- Acceptance: game/updater guards, redirected Documents, unique backup, no
  overwrite of an existing .old file, missing-file no-op, rollback/read-back,
  Error 3/63 separation, and unrelated setups/replays/paints preserved.
- Functional verification: retry the same car/session and confirm spotter loads.
  Successful regeneration is not proof that every custom pack works.

### KS-MAN-IR-77 — route known content errors to useful recovery

Status: proposed-detection / repair routing. Priority: first implementation batch.
This is an improvement to existing repair coverage, not a new repair family.

- Source, checked November 13: [iRacing Loading Error 77](https://support.iracing.com/support/solutions/articles/31000147702-loading-error-77),
  vendor modified November 2, 2023. It calls for reacquiring the affected track
  or car content.
- Gap: Error 77 is missing from the live matcher. Content-recovery plans exist,
  but exact affected-content extraction must be established before routing.
- Proposed operation: add exact error classification and independently determine
  a canonical installed car/track path from captured evidence. Reuse existing
  update/Steam/direct-install recovery where appropriately scoped.
- Acceptance: test known car, known track, traversal/absolute/unrecognized paths,
  stale logs and unknown content. Error 77 alone must never remove every car or
  track or guess which package failed. Unknown target remains diagnostic with
  an appropriate supported validation path. Respect active-update guards.
- Verification: content is reacquired and the same loading test succeeds.

## Validation candidates from expanded discovery

### KS-MAN-IR-SIMAGIC — device-specific stutter A/B test

Status: needs-validation; not part of the first implementation-ready batch.

[iRacing's stutter guide](https://support.iracing.com/support/solutions/articles/31000141916-dealing-with-freezing-and-or-stuttering-issues),
modified September 9, 2026 and checked November 13, documents disabling the
Simagic API using `loadSimagicAPI=0` for specific Alpha/360 Hz issues.
It describes a suspected device/polling problem, not a cause proven for every hitch.

No corresponding scoped repair exists in the inspected planner/service.
Confirm current key/section, supported hardware and actual installed values.
A proposal must offer an explicit temporary test, preserve/restore the exact
value and explain loss of 360 Hz integration. Compare the same practice scenario
before/after. Never automatically turn this off for all Simagic owners.
The separate pedal-vibration setting must not be inferred or silently changed.

### KS-MAN-IR-SIMHUB — restore expected iRacing data configuration

Status: needs-validation; new developer-source lead.

[SimHub developer troubleshooting](https://github.com/SHWotever/SimHub/wiki/SimHub-Basics----Games-config-and-troubleshooting),
last shown edited June 24, 2026 and checked November 13, specifies
`[Misc] irsdkEnableMem=1` and `irsdkLog360Hz=0` in app.ini.

Potential operation: a user-selected missing-SimHub-data check with installed
SimHub context and inspection of both keys. No crash is required. Before proposing
a mutation, verify current SimHub/iRacing compatibility and explain that the
logging-rate setting can affect the user's existing data workflow. Respect
intentional configuration and check SimHub's own configuration option first.
Require a backed-up, explicit opt-in test with functional confirmation in a live
practice session and Restore. This source addition does not implement SimHub
monitoring and does not resume PitMedic driving-stat collection.

### KS-MAN-LMU-DIRECTINPUT — narrower fix for pedal/clutch flicker

Status: needs-evidence/current-build hardware validation; new community lead.

[First-hand LMU pedal discussion](https://www.reddit.com/r/LeMansUltimateWEC/comments/1ngomed/throttle_input_problems_0_to_100_flickering/),
September 14, 2025, body and outcome replies checked November 13. Users report
changing `DirectInput Fallback` to true in `current controls.json` helped
P500 throttle or clutch flicker; later replies also describe recurrence.

This could preserve bindings compared with the four-file reset. It is not proof
of a universal or current-build fix. Obtain current vendor/developer confirmation
or reproduce on matching hardware; verify exact JSON key/type/location and
tradeoffs. Only then design a backed-up one-property edit with a clear symptom
selector, close/restart guard, read-back and an input retest. Do not infer that all
USB disconnections share this cause.

## Existing item carried forward

The [iRacing/Simucube Link-port guidance](../website/diagnostic-library/iracing-simucube-link-port-ffb-guidance/index.html)
already shipped in 1.0.0.11. A setting edit remains unimplemented pending the
previously recorded hardware validation. Keep that investigation visible;
do not count its guide or rediscovery as a new automatic repair.

## Coverage and blockers

| Product/source | Work in this pass | Outcome / next action |
| --- | --- | --- |
| Le Mans Ultimate | Official common fixes, prior research/code, Reddit outcomes, Steam and OverTake entry points | One official repair proposal; one narrower community lead needs current validation |
| iRacing | Actual vendor error/stutter articles, code/planner/matchers, developer wiki, community corroboration | Two repair proposals, one routing improvement, two validation candidates |
| AMS2 | Reiza wheel/paddles discussion, Steam hub, recent first-hand Reddit reports | Existing controller reset/Steam Input guide overlap; no additional automatic action established |
| Assetto Corsa EVO | Kunos route and Steam hub | Requested forum route returned main index; individual thread evidence remains unverified |
| ACC | Kunos route, Steam and Reddit entry points | Forum route returned main index; hubs alone establish no new remedy |
| RaceRoom | Current intake failure evidence and Steam discussion/news entry points | KW sources reported HTTP 503; Steam is a fallback, not proof of a new local repair |
| MOZA | Actual Pit House FAQ | Runtime/process guidance overlaps; firmware and old-OS driver procedures are not automatic candidates |
| Simucube | Actual Tuner changelog | Existing update/output-ownership coverage overlaps; do not reuse True Drive recovery for Tuner |
| Fanatec | Actual 1.7.1.4 notes | Existing released guidance overlaps; no new local repair established |
| Logitech | Existing recovery article/scan evidence and current recovery implementation | Existing service recovery overlap; deeper discussion review still due |
| SIMAGIC | Actual download/release-note body | 3.2.4 is visible; its Grid Legends fix does not by itself add a supported-sim repair |
| Asetek | Knowledge-base entry point | Index is discovery only; follow actual RaceHub troubleshooting articles next |
| VRS | Actual downloads body | Installer listing alone does not establish a remedy |
| OverTake / Steam / Reddit | Verified community entry points and selected actual discussions | Expand targeted threads next; do not call all hubs fully reviewed |
| Thrustmaster / Microsoft / GPU vendors | Discovered official support, driver and runtime sources | Additional evidence routes; no firmware/driver automation or generic optimizations proposed |

Important follow-up: finish deeper per-product discussion work outside the LMU/
iRacing shortlist on the next full review. Resolve the Kunos index-routing problem
using actual returned links, direct Steam developer threads or browser access.
Private Discord content has not been searched. Not finding a remedy in an index
is not evidence that no useful fixes exist.

## Process implementation and validation

The PR removes GitHub scheduling, retains optional manual intake and adds
repository instructions, a repair-focused manual playbook and the expanded source
inventory. The separate ChatGPT review task was verified disabled.

Validation: structured inventory uniqueness/HTTPS/product coverage checks and
static review of the manual-only workflow; required GitHub Build Validation must
pass before merge. Local shell execution was unavailable because the host sandbox
could not provision its lock directory. No simulator/hardware test was performed
for the proposed repairs. Tests on process changes do not validate these remedies.
