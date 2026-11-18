# Manual Scout review - November 17, 2026

## Baseline, scope and result

Baseline main: `4145eb25cbee6c98aa32b3cf743ecddc46daba65`.
Public app: **1.0.0.12**, published November 14. This review does not change
the public app version or invoke signing. Dates use Pacific time; research
continued November 18 UTC.

Reviewed actual vendor articles, developer replies and community outcomes
against current planners, executors, process definitions and Repair Tools.
Scope includes seven sims (original Assetto Corsa added in this implementation)
and seven companion families. Selected discussions received deeper investigation;
this is not an exhaustive audit of every source, post or hardware combination.

Results, kept separate to avoid counting existing repairs as new:

- **Two repair proposals:** one new Asetek profile-cache recovery, one revalidated
  and narrowed iRacing/SimHub memory setting from the November 13 backlog.
- **Two access improvements:** user-requested companion recovery and more
  symptom-based access to existing simulator repairs.
- **Four validation candidates:** iRacing UTF-8 compatibility, AMS2 shared memory,
  LMU DirectInput fallback, legacy SimPro/HPR output ownership.
- **Three guidance items:** two existing website guides prepared for publication;
  one queued LMU startup-claim correction requiring coordinated app knowledge.
- Two existing hardware-validation items carried forward: Simagic polling and
  Simucube Link-port behavior. Neither is counted as new.

The separately requested **original Assetto Corsa integration is implemented**:
two scoped settings actions plus access to existing Steam verification machinery.
Its three paired guides are prepared and gated to the next supporting app release
(`1.0.0.13` minimum). No original-AC gameplay or physical-wheel success is claimed.
RaceRoom Racing Experience already exists in the app and website; it is not
added a second time.

## Discovery sources and retained evidence

The manual inventory now contains **55 entries**: 27 enabled intake sources and
28 additional research sources. Six additions cover Asetek's exact wheel article,
SimHub's developer forum, Trading Paints compatibility, RaceRoom publisher news,
original AC Steam discussions and original AC Reddit leads. Inventory size is
not a count of fully reviewed sources. Existing retrieval statuses/dates are
preserved. Reddit AC remains an indexed lead source, not verified remedies.

The exact `www.overtake.gg` host is added to the citation allowlist for an opened
original-AC outcome discussion. It remains a community citation; this does not
make it an official authority or add an automatic network repair.

The [rolling intake issue #9](https://github.com/rholmes426/PitMedic/issues/9)
still contains the November 13 snapshot. Its displayed 80-item sections are
presentation limits, not full backlog counts. The compressed retained state was
decoded for actual counts: 105 pending findings, 105 inline-review entries
(64 needs-evidence, 34 candidate-remedy, seven safety-review-required), and 27
source states (25 ok, two errors). These are retained intake classifications,
not validated repairs or confirmed harm. This review researches selected relevant remedies and preserves
the remaining evidence; it does not claim all 105 have been freshly resolved.
No recurring task or GitHub schedule was enabled.

## Coverage and decisions

| Product | Sources and actual discussion checks | Decision against current code |
| --- | --- | --- |
| Le Mans Ultimate | Official Common Fixes, Known Issues, V1.0.2.2 and V1.4 notes; pedal-flicker reports and Steam controller/FFB discussion | Existing four-file recovery overlaps; a narrower backend test needs current layout/version validation. Historical CEF claim also needs correction. |
| iRacing | Official error/support and November notes; SimHub developer wiki/issues #2388, #2260, #1199 and #1202; current stutter reports | Existing sockets/Error 63/Error 77 work remains shipped. Missing memory-feed setting is a narrower proposal; encoding advice conflicts across apps. |
| Original Assetto Corsa | Steam discussion with original poster's controls-reset success and another outcome; OverTake author's video-reset success; Valve verification support | Implemented separate race-engine monitoring and two opt-in INI resets. Launcher, Content Manager, EVO and ACC remain distinct. Community outcomes are not vendor guarantees. |
| Assetto Corsa EVO | Official announcement/support routes and opened Steam launch discussion, including contrary replies | Existing recovery already handles both profile locations. No new universal runtime/profile reset justified. Individual Kunos route returned an index. |
| Assetto Corsa Competizione | Publisher announcements, Steam VR/audio discussion; Kunos route/index | Single hardware-specific audio account does not justify Windows-wide audio/driver changes. Existing control/video recovery can become easier to request. |
| Automobilista 2 | Reiza Asetek-wheel discussion with moderator and original-poster follow-up; Simucube game setup and SimHub developer forum | Original poster identified SimHub Control Mapper, not the first proposed remedy. Project CARS 2 shared-memory mode is a validation candidate; no guessed binary edit. |
| RaceRoom Racing Experience | Official Steam November 11/15/17 notes; opened Steam error-503 discussion and moderator qualification | New vendor fixes merit existing-guide checks. Server 503 is not proof of a local cache fault; current cache/graphics repairs already overlap. |
| MOZA Pit House | Official game-telemetry/port configuration and FAQ; powered-hub/dashboard community report | Existing recovery overlaps. An unverified real-time process-priority suggestion is excluded; no automatic priority/USB/network change. |
| Simucube True Drive / Tuner | Official Tuner notes/game configuration; SC2 community FFB accounts | Preserve output-ownership and True Drive/Tuner distinctions. Tuner monitoring still needs verified executable identity; no new process name invented. |
| Fanatec software | Current official stale-feed/restart article and App 1.7.1.4 notes; forum discovery | Existing restart can help without a crash but lacks symptom access. No new firmware or driver repair inferred. |
| Logitech G HUB | Official release/support pages; actual G29 thread with staff and original-poster outcome | Update fixed one input-preview symptom while other FFB reports persisted. Existing guidance/restart overlap; add explicit access rather than promise a universal FFB fix. |
| SIMAGIC SimPro Manager | Official downloads (3.2.4, October 28); HPR discussions and SimHub maintainer's November 15 reply | Legacy 3.0.3 ownership issue is plausible, but current-build and firmware-state gaps prevent automatic termination/downgrade. |
| Asetek RaceHub | Official steering-wheel, wheelbase and pedal troubleshooting; Reiza outcome; indexed bootloader report | Exact wheel/display profile recovery is a new scoped proposal. Bootloader, USB and firmware cases are excluded from it. |
| VRS DirectForce / VRS ONE | Full official downloads page and community EVO/FFB discussion | Current VRS ONE 1.0.14 is already represented. Archived DirectForce 1.0.0.1 search text is not evidence of a current detection gap. No repeated verified new remedy. |

## Ranked implementation shortlist

### KS-MAN-AS-RACEHUB-CACHE - new targeted repair proposal

**Problem:** a powered/illuminated Asetek wheel is absent from RaceHub because
local wheel/display profile data is damaged. The vendor distinguishes this from
firmware and physical connection failures.
[Official steering-wheel troubleshooting](https://www.asetek.com/simsports/knowledge-base/steering-wheel-troubleshooting/)
was opened; its documented update is September 15, 2026.

Current executor restarts RaceHub/helper processes. It does not implement this
profile repair. Proposed action: preserve and quarantine only the documented
`%APPDATA%\Asetek SimSports\RaceHub\Base profiles\Steering wheel` contents and
`Display` when applicable. Resolve and validate the exact local layout first.
Require all sims/RaceHub closed, user-confirmed wheelbase power off, no firmware
or update activity, and explicit consent. Unknown update state must block the
action. Do not delete the entire RaceHub tree, drivers, firmware or other profiles.

Acceptance: bounded path/reparse checks; durable manifest and hash-verified
backups; atomic failure handling; Restore preserves regenerated data; tests for
missing/unsupported layout and partial failure. Functional verification requires
manual power-on/reconnection, RaceHub detection and a wheel-button practice test.
Process launch alone cannot mark device recognition repaired.

**Library: proposed** new `asetek-wheel-detection-config`, linked from existing
`companion-asetek-clean-recovery`; explain matching symptom, power-off requirement,
profile impact, Restore and manual retest. Next: implement the constrained cache
transaction and add observable firmware/update guards before exposing it.

### KS-MAN-IR-SIMHUB - revalidated existing proposal, narrower mutation

**Problem:** iRacing runs but SimHub receives no shared-memory data.
The [developer's configuration wiki](https://github.com/SHWotever/SimHub/wiki/SimHub-Basics----Games-config-and-troubleshooting)
documents the relevant settings. The executable has no specific memory-feed edit.

Propose a user-requested action only for an existing, unambiguous
`[Misc] irsdkEnableMem=0` in `app.ini`, changing it to `1`. Prefer SimHub's own
configuration check first. Require iRacing/updater closed, backup/Restore and
explicit consent because the setting may have been intentionally disabled.
Use the existing scoped INI machinery; absent/duplicate keys or unknown encoding
must stop the operation. An existing `1` is a no-op.

Do not bundle `irsdkLog360Hz`, FFB polling, `irsdkUTF8SessionStr`, global Windows
locale, or logging-rate changes. Acceptance includes byte/section preservation,
wrong-file/key and updater tests, Restore, readback and a live practice-session
SimHub-data check. File readback is not a functional success claim.

**Library: proposed** `iracing-simhub-data-config`; this refines the same November
13 queue item rather than creating a duplicate. Next: implement/test that one-key
action with an honest retest state and paired app-owned knowledge.

### KS-MAN-COMPANION-REQUEST - access to existing recoveries

Fanatec's stale feed or G HUB's loading problem may not crash. Existing clean
restart executors depend on an incident and validated executable evidence; the
new Repair Tools currently expose simulator configuration actions only.

Add explicit symptom requests after locating/validating the installed executable.
Keep normal user-request history labeling, all-simulator-closed requirements,
firmware/update exclusions and existing helper boundaries. Never manufacture a
crash or assume a running process proves live telemetry/FFB. Fanatec's process
set needs a specific updater check before reuse. No settings mutation is added;
record that restarting loses current unsaved work. Test unavailable/ambiguous
installs and active games/updaters. Retest the actual feed or wheel input.

**Library: proposed updates** `companion-fanatec-clean-recovery` and
`companion-logitech-ghub-service-recovery`, explaining the new request entry point
and retained limits. Next: validate executable/update evidence and add the UI path.

### KS-MAN-SIM-REQUEST - expose suitable existing simulator actions

ACC/AMS2 controller and EVO/RaceRoom configuration recoveries already exist but
are not all reachable from symptom-based Repair Tools. Review each executor's
scope and backup/Restore before exposure; do not turn generic crashes into
unconditional settings resets. Preserve saved bindings/presets and close-state
checks, warn about remapping, and verify the same control/display symptom.

**Library: proposed updates** `acc-controls`, `ams2-controller-config`,
`ace-video-settings`, `raceroom-graphics-config` as each request path validates.
Next: implement only actions whose existing transaction and Restore meet the
current scoped-repair contract. This is access improvement, not four new repairs.

## Validation candidates, not available automatic repairs

| ID | Evidence and unresolved boundary | Required next action and paired library work |
| --- | --- | --- |
| KS-MAN-IR-UTF8-COMPAT | [SimHub #2388](https://github.com/SHWotever/SimHub/issues/2388), November 11-14, includes maintainer advice for session-YAML decoding/encoding; a future-fix statement and closed issue do not establish a shipped fix. [Trading Paints official compatibility advice](https://help.tradingpaints.com/downloader/what-should-i-do-if-opponent-paints-are-not-appearing-after-iracings-2026-season-4-build/) says stable 2.1.5 needs `irsdkUTF8SessionStr=0`, opposing a universal enable. | Confirm installed consumers and released fixes, fail on unknown versions, prefer SimHub's built-in configuration, then validate explicit-consent A/B and Restore. Proposed compatibility section in `iracing-simhub-data-config`; create a separate encoding guide only if a distinct validated operation warrants one. No Windows locale change. |
| KS-MAN-AMS2-SHARED-MEMORY | [Simucube AMS2 setup](https://docs.simucube.com/TunerSoftware/games/Automobilista2.html) and a SimHub developer reply require Project CARS 2 shared memory. Current audit has no stable, verified profile-writing format. | Validate current AMS2 file layout and an exact supported mutation before automation. No guessed binary offset, UDP port or firewall rule. Proposed `ams2-companion-telemetry-config`, with backup/Restore, consumer retest and mode tradeoffs if implemented. |
| KS-MAN-LMU-DIRECTINPUT | [Current known issues](https://guide.lemansultimate.com/hc/en-gb/articles/13240843908623-Known-Issues-and-Advice) strengthens the earlier community lead, but [older notes](https://guide.lemansultimate.com/hc/en-gb/articles/13755123150991-V1-0-2-2-Patch-2-Hotfix-2) and current article disagree on `current controls.json` versus `controls.json`. [V1.4 notes](https://guide.lemansultimate.com/hc/en-gb/articles/16987981296015-V1-4-release-notes) fix some devices with fallback false. | Reproduce on matching current build/hardware; identify the actual file/property and version gate before one-property edit, with exact backup/Restore and all-axis retest. Existing `lmu-controller-config` gains separate manual checks now. A future narrow action can get `lmu-directinput-pedal-flicker` once validated. |
| KS-MAN-SIMPRO-HPR | [SimHub #2260](https://github.com/SHWotever/SimHub/issues/2260) has a November 15 maintainer reply identifying legacy SimPro 3.0.3 ownership conflict. Applicability to current 3.2.4 and observable firmware state remain unknown. | Validate exclusive output ownership on current versions before any opt-in close/retest action; restarting SimPro can reintroduce the conflict. No downgrade, firmware flash or uncertain process termination. Proposed updates to `companion-simagic-clean-recovery` and the planned `iracing-simhub-data-config`. |

Carry forward **KS-MAN-IR-SIMAGIC** (`loadSimagicAPI=0`) with hardware/section
and live A/B requirements; native pedal vibration is a separate setting.
Proposed `iracing-simagic-stutter` remains unpublished. Existing Simucube Link-port
guidance is already public in `iracing-simucube-link-port-ffb-guidance`; automation
still needs the matching mixed-wheelbase/ActivePedal hardware test. No fresh
implementation or success claim is made for either.

## Guidance and app-owned claim audit

1. **RaceRoom existing guide prepared:** `raceroom-loading-ffb-update-guidance`
   adds manual version checks for official publisher fixes: 0.9.9.24 cold-start
   error 11001/stale steering force; 0.9.9.27 Drive/Resume USB freeze; 0.9.9.28
   resolution/window-mode crash with FXAA/sharpening and connection-limited ISP
   loading. [Publisher notes](https://steamcommunity.com/app/211500/allnews)
   were read through November 17. Retain earlier checks. Updating the game is
   manual; no blanket VPN, DNS or IPv6 mutation and no new PitMedic repair.
2. **LMU existing guide prepared:** `lmu-controller-config` explains the current
   backend-test evidence, conflicting filenames and update/calibration-first
   checks separately from the shipped four-file operation.
3. **KS-MAN-LMU-STARTUP-CLAIMS queued:** `RepairKnowledgeBase.cs` describes CEF
   tests while `RepairLmuDx11ConfigAsync` resets two named INI files. Current LMU
   release notes replaced CEF with Coherent. Audit historic instructions against
   current game layout and executor, then update the same `lmu-startup-config`
   guide and app-owned claim together. Do not retire an existing repair merely
   because an old source moved. This audit item is not a new automatic action.

Editorial dates advance for the two edited website guides. Existing app evidence
`lastVerified` dates do not advance merely because an article or link was read.
The new original-AC records describe implemented file operations, backed by
fixture tests and clearly labeled community evidence, not local gameplay success.

## Original Assetto Corsa implementation and paired library

- Append the game enum to preserve stored numeric identities. Enable a separate
  monitoring setting and original-AC navigation/readiness status.
- Monitor `acs.exe` and `acs_x86.exe`; launcher/Content Manager remaining open
  cannot turn into one endless race. Windows fault/dump matching is bounded to
  the exact engine names, excluding substring collisions such as `spacemacs.exe`.
- Baseline cumulative logs and accept fresh exact `video.ini`/`controls.ini`
  failure evidence only. Generic mod crashes or normal printed configuration do
  not silently select a reset. User-selected symptoms remain reachable without
  creating fake crash evidence.
- Reuse the scoped manifest/hash backup transaction to reset only `cfg/video.ini`
  or `cfg/controls.ini` under Documents/Assetto Corsa. Require approval and closed
  race process, original launcher and Content Manager. Restore preserves newly
  generated settings; saved presets, setups, FFB LUTs and mods are outside scope.
- Start supported Steam verification for app **244210**, with the estimate and
  modified-stock-file warning. Label completion as verification started and
  require Steam completion/stock-session retest. No fictional Steam Restore.
- Three app-owned knowledge/lifecycle records and prepared guides:
  `ac-video-config`, `ac-controls-config`, `ac-steam-content`. The generator hides
  all three and their empty collection group while the download remains .12.
  Publishing a supporting .13 or later adds those pages, group, sitemap and
  homepage lineup link together. Existing EVO/ACC links stay distinct.

Evidence: [controls outcome](https://steamcommunity.com/app/244210/discussions/0/2263564102386178208/),
[video outcome](https://www.overtake.gg/threads/assetto-corsa-gfx-pp-settings.121820/),
[Valve verification](https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB).
Generic CSP/Content Manager installations, mod dependency replacement and broad
profile deletion are not included. No driving-stat SDK adapter is claimed.

## Validation and limits

Local .NET 10 Release build and monitoring tests pass, including **46 new
original-AC checks** covering process identity, cumulative-log behavior, clean
exits, exact repair routing/history, byte-preserving Restore, unrelated files,
manual request boundaries and Steam retest/deep links. Python generation tests
check both .12 and .13 pages/group/sitemap and homepage publication together.
The actual WPF home markup and original-AC Repair Tools render correctly;
Steam verification disables Restore. No production monitors or repairs run in
this visual fixture. Release-policy tests and 48 Python tests pass.
Catalog validation reports 73 implemented records; only 70 are publicly available
with .12. Required protected PR Build Validation remains the merge gate.

No local sim, Steam operation, firmware update or device was exercised. Hardware
retest requirements above remain open. No public app release is initiated.
Publication of the two existing guidance pages must receive an actual live check;
the three original-AC pages remain prepared until their supporting app ships.

Access limits: individual Kunos routes returned forum indexes, not readable
threads; earlier KW forum failures are not presented as a fresh forum audit.
Steam publisher/discussion fallbacks were read. An Asetek bootloader Reddit lead
and an inaccessible RaceHub PDF are not accepted as verified remedies. SimHub
#2388 was read through GitHub API after the web route failed. Private Discord
channels were not searched or joined; no CAPTCHA/login was bypassed. The full
retained intake backlog and generic Windows/GPU/runtime remedies remain for
later targeted reviews; nothing is automatically retired or marked repaired.
