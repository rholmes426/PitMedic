# Knowledge Scout and external-source review — October 5, 2026

Status: research and proposed queue only. These are source-backed guidance candidates, not locally reproduced repairs or human approvals.

## Result

Six guidance groups are worth adding or improving. No new automatic mutation is ready for implementation from this evidence alone. Two engineering investigations and one citation refresh are recorded separately below.

The review covers all six supported simulators and seven companion-software families. Some findings were published recently; older vendor fixes are explicitly identified as newly discovered gaps, not new releases.

## Baseline and scout health

- Repository baseline: `d26cbfbaae44a94a189f92798dfb36e6b674c3d9`; public app baseline recorded in the repository is 1.0.0.7.
- [October 2 scout run](https://github.com/rholmes426/PitMedic/actions/runs/37066133609) completed successfully. [Rolling issue #9](https://github.com/rholmes426/PitMedic/issues/9) was checked at 2026-10-02T21:19:32Z. This is the latest scheduled run before this Monday review; discovery is not stale.
- Report: 26 sources, 3 changed findings, 47 retained findings, 3 availability failures, 0 reported harm signals and 0 catalog errors. The 3 changed findings also occur in the retained list: do not add them to produce 50 candidates.
- The failures are both Simucube URLs redirecting to `docs.simucube.com`, which is absent from the allowlist, and the Fanatec app forum returning HTTP 403. A successful overall workflow does not establish complete discovery.
- Compared current RepairKnowledgeBase, CompanionSoftwareKnowledgeBase, CompanionRecoveryPolicy, CompanionSoftwareDefinition, 60 generated Diagnostic Library entries, guide overrides, lifecycle, review decisions and pending queues. Read CONTRIBUTING.md, Source/PROJECT_POLICY.md and Knowledge/README.md. The recursive tree contains no AGENTS.md.
- September 18 proposals were already incorporated into 1.0.0.3 according to the current queue. They are not counted again. Open PRs inspected were dependency proposals; there was no open Scout review PR.
- Fresh searches covered vendor support, release notes and vendor forums. Underlying pages were read for promoted candidates. Search snippets, forum indexes and the Scout's keyword matches were not accepted as proof of a remedy.

## Proposed guidance queue

### KS-20261005-SIMPRO — repeated app failures and EVO startup conflict

[SIMAGIC release notes](https://simagic.com/pages/download-center): 3.2.2, September 22, 2026, addresses repeated application-failure reports in Windows Reliability Monitor and shutdown error prompts. The same source's older 3.0.3 notes (May 7, 2026) document Assetto Corsa EVO crashing or failing to start with SimPro 3 running.

Current guidance stops at 3.2.0/3.2.1 display and game-data fixes. Add the specific failure contexts to the companion page and EVO guidance, with manual vendor update instructions. Do not label every recorded fault harmless or suppress Windows evidence.

Acceptance: identify installed generation/version and matching symptoms; check hardware compatibility; preserve profiles; respect the existing no-recovery-during-flashing warning. No automatic update, downgrade or firmware action. This is one guidance group with a recent fix and an older coverage gap.

### KS-20261005-RACEHUB — input freeze and missing game data

[Asetek 4.5.1 release notes](https://www.asetek.com/simsports/wp-content/uploads/2026/09/Release-Note-for-RaceHub-version-4.5.1.pdf), September 24, 2026, explicitly fix inputs ceasing after a rotary control is turned, missing LMU TC/ABS data, and EVO DRS/data-display problems.

Existing coverage only offers a controlled app restart. Add version/symptom checks to RaceHub, LMU and EVO guidance. Acceptance: distinguish input freeze from missing display data; compare versions; use a manual supported update and retest the same control or display. Do not infer profile corruption or turn generic shifter improvements into a specific repair.

### KS-20261005-LMU — released crash fixes before config resets

[LMU 1.4.2](https://guide.lemansultimate.com/hc/en-gb/articles/17713037697807-V1-4-2-Update-1-4-Patch-2) documents shared-memory threading fixes, crashes involving temporary cars in hosted/practice sessions, an occasional car-position crash and a session-join crash after being off track in the prior session. [1.4.2.1](https://guide.lemansultimate.com/hc/en-gb/articles/17739847034383-v1-4-2-1-Update-4-Patch-2-Hotfix-1) fixes lengthy synchronization into team events. These are shipped notes, unlike the existing announcement-only network guidance.

Acceptance: record installed build and session context; explain the supported-update path before a destructive hypothesis. No generic crash signature proves one of these causes. Retest the affected session; preserve incident evidence. Exact publication dates were not exposed on the read pages, so none are invented. 1.4.2.3 concerns race-start penalties and supplies no new local crash remedy.

### KS-20261005-AMS2 — wheel/paddle input precheck

A [Reiza staff moderator response on September 20, 2026](https://forum.reizastudios.com/threads/my-wheel-and-paddles-simply-wont-work.36701/) recommends checking the per-game Steam Input setting when wheel/paddle inputs stop working. This check is absent from the current controller guide and should precede a controller-file reset.

Acceptance: wheel-specific, manual A/B check; record and restore the prior setting if ineffective; test steering and paddles. Do not change the global Steam setting or apply this automatically to gamepads. The thread does not confirm that this fixed the original poster's machine; label it a staff-recommended check, not a reproduced repair.

The existing 1.6.9.95 weather/multiplayer/setup guidance is already covered. The [current release post](https://forum.reizastudios.com/threads/automobilista-2-v1-6-9-95-released-updated-to-v1-6-9-96.36667/) also documents .96 settings-persistence corrections after P2P host migration; retain as contextual vendor-update evidence rather than inventing a local profile repair.

### KS-20261005-TUNER — separate Simucube Tuner guidance

[Official Tuner changelog](https://docs.simucube.com/TunerSoftware/changelog.html): 3.1.4 (August 20, 2026) fixes high GPU usage at Windows autostart and a crash when saving after a wireless wheel disconnects; 3.1.5 (September 9) fixes lost swapped-pedal roles across restarts and some input-curve mapping failures. These are older releases newly recovered through the moved documentation.

Acceptance: a separately named Tuner library guide, precise symptom/version scope and manual update/retest; never apply a True Drive restart recipe to Tuner. The existing website correctly warns about this distinction. App monitoring expansion needs the investigation below first.

### KS-20261005-IRACING — October patch guidance

[iRacing Season 4 Patch 1, build 2026.10.01.01](https://support.iracing.com/support/solutions/articles/31000179767-2026-season-4-patch-1-release-notes-2026-10-01-01-) fixes Reference Car Input display problems with foveated quad views/triple screens and spotter audio stopping. The source was modified October 5.

Acceptance: guidance for these exact display/audio symptoms and a supported update/retest. Do not reset graphics or audio configuration solely because these symptoms occur. These are troubleshooting-library additions; no automatic detector or crash remedy is established. iRacing's separate mobile companion-app retirement is outside PitMedic's monitored companion scope.

## Coverage across all supported products

| Product | Outcome |
| --- | --- |
| iRacing | October patch guidance proposed; prior September crash guidance already present. |
| Le Mans Ultimate | Released crash/synchronization guidance proposed; Asetek overlap cross-linked. Fanatec FFB thread remains unverified. |
| Assetto Corsa EVO | SIMAGIC startup and Asetek display guidance proposed; inaccessible 0.9.1 crash reports do not justify profile resets. |
| Assetto Corsa Competizione | Checked vendor troubleshooting and publisher release search. No independently verified new repair in this pass. Older control/TRUEFORCE material overlaps current coverage; individual forum evidence remains incomplete. |
| Automobilista 2 | Staff-recommended Steam Input check proposed; menu-only Simagic workaround remains lower-confidence investigation. |
| RaceRoom | Fanatec oscillation fix already covered. New black-screen/launch threads resolved to the forum index in the reader, so remedies remain unverified. |
| MOZA Pit House | [Current FAQ](https://support.mozaracing.com/en/support/solutions/articles/70000627928-moza-pit-house-faqs) reviewed. Runtime/updater/reporting recovery overlaps existing coverage. Beta/historical builds not auto-updating is useful context, but no new automatic repair established. |
| Simucube True Drive / Tuner | Tuner guidance and recognition investigation proposed. Preserve existing True Drive coverage. |
| Fanatec App / FanaLab | [Official 1.5.4.2 notes](https://help.fanatec.com/hc/en-us/articles/50512683066513-Fanatec-App-v1-5-4-2-Release-Notes) confirm the already-covered CSL DD/GT DD Pro menu-return oscillation fix; refresh citation/version context, not a new repair. |
| Logitech G HUB | [Loading-loop sequence](https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops) still matches existing recovery. Latest [release index](https://marketplace.logi.com/releasenotes/ghub/en) exposed 2026.6 headings without detailed fixes; no new remedy inferred. |
| SIMAGIC SimPro Manager | September 22 repeated-failure fix and older EVO startup fix proposed above. |
| Asetek RaceHub | September 24 release remedies proposed above. |
| VRS DirectForce / VRS ONE | [Official downloads](https://vrs.racing/downloads) reviewed; VRS ONE already recognized in the app. No new verified remedy found. Help-center retrieval failed; do not represent support coverage as complete. |

## Engineering and source maintenance queue

1. **KS-20261005-SCOUT-COVERAGE — proposed maintenance.** Verify and narrowly allow the official Simucube documentation host, then point the two old generic redirects at the actual [Tuner download page](https://docs.simucube.com/TunerSoftware/index.html) and changelog. Add Fanatec's accessible [official release-note collection](https://help.fanatec.com/hc/en-us/sections/47850807160721-Release-Notes) as fallback; retain the forum failure until the runner succeeds. Consider direct LMU release notes rather than relying on news/forum discovery. Retest in the actual Scout runner before claiming restored coverage.
2. **KS-20261005-TUNER-DETECTION — needs evidence.** Source inventory matches only True Drive executables/install names. Verify Tuner process names, signed executable identity, installation/uninstall metadata and device/update lifecycle on Windows before proposing monitor support. Do not merely add a guessed executable to the termination allowlist.
3. **KS-20261005-FANATEC-CITATION — proposed maintenance.** Existing 1.5.4.1 guidance has an accessible official 1.5.4.2 reference; the vendor dates the first hotfix September 3 and the full app release September 11. Preserve hardware restrictions and manual firmware handling.
4. **Retained safety investigation:** KS-20260918-SIMPRO-FIRMWARE remains open. Human instructions exist; reliable firmware-state detection does not. This review supplies no proof of a PitMedic flashing interruption and changes no repair state.

## Scout backlog reconciliation

The 47 retained entries include 30 URLs already in the September 18 report and 17 later entries. Previous outcomes are carried forward as history, not re-certified. Current changed pages and promising new entries received the focused review above. The appendix preserves every retained URL; it does not claim that every old reply was reread.

The solved LMU/Fanatec thread returned a forum index through both the original URL and a clicked thread link; a friendly URL was inaccessible. The Scout's quoted reinstall suggestion is not proof of what solved the FFB problem. Keep it pending. The same limitation affects the fresh RaceRoom threads and LMU Ferrari stutter report. Kunos 0.9.1 crash content was inaccessible.

The [Simagic menu discussion](https://forum.reizastudios.com/threads/cant-control-the-menu-with-my-simagic-wheel.34031/) is a 2024 community workaround resurfaced in 2026, with no staff confirmation or current-build reproduction. Do not automate keyboard remapping. The [Genesis announcement](https://lemansultimate.com/genesis-uses-le-mans-ultimate-for-esports-series-pro-sim-racing-drive-up-for-grabs/) is an esports event announcement, not a repair. Other conduct/name-change/dashboard/spam-looking titles are not usable fix evidence; retain unresolved content rather than deriving remedies from titles.

## Validation and limits

Documentation only. Checked proposals against the current app/library/queue and verified source dates or explicitly recorded missing dates. No Windows or hardware reproduction was performed. Version-aware guidance can be reviewed for implementation; none of these findings independently supports a new automatic file, service, driver or firmware mutation.

No changes to app runtime, diagnostic pages, machine review dispositions, lifecycle, Scout configuration, scheduled tasks or releases. Draft queue records preserve the normal implementation/review process.

## Retained URL appendix

| # | Scout entry | Disposition in this review |
| --- | --- | --- |
| 1 | RaceRoom Racing Experience: [During replays: No manual DOF Menu and no head on driver](https://forum.kw-studios.com/index.php?threads/during-replays-no-manual-dof-menu-and-no-head-on-driver.21548/) | Prior record carried; see September 18 review |
| 2 | Le Mans Ultimate: [HY/LMP/GTE/GT3 Dash for Old man or people with tiny screen (Need HY driver to complete)](https://community.lemansultimate.com/index.php?threads/hy-lmp-gte-gt3-dash-for-old-man-or-people-with-tiny-screen-need-hy-driver-to-complete.16851/) | Needs evidence; no remedy promoted |
| 3 | Asetek RaceHub: [asetek-racehub](https://www.asetek.com/simsports/racehub/) | Proposed guidance: KS-20261005-RACEHUB |
| 4 | SIMAGIC SimPro Manager: [simagic-downloads](https://simagic.com/pages/download-center) | Proposed guidance: KS-20261005-SIMPRO |
| 5 | Le Mans Ultimate: [Steady Deservonage™ / Official Website 【UPDATED 2026】-Exploring Its Trading Platform and Features!](https://community.lemansultimate.com/index.php?threads/steady-deservonage%E2%84%A2-official-website-%E3%80%90updated-2026%E3%80%91-exploring-its-trading-platform-and-features.19081/) | Needs evidence; no remedy promoted |
| 6 | Fanatec software: [fanatec-downloads](https://www.fanatec.com/ca/en/s/download-apps-driver) | Prior record carried; see September 18 review |
| 7 | Assetto Corsa EVO: [Crash](https://www.assettocorsa.net/forum/index.php?threads/crash.83931/) | Prior record carried; see September 18 review |
| 8 | Automobilista 2: [My wheel and paddles simply won't work.](https://forum.reizastudios.com/threads/my-wheel-and-paddles-simply-wont-work.36701/) | Proposed guidance: KS-20261005-AMS2 |
| 9 | Automobilista 2: [Automobilista 2 March 2025 Development Update](https://forum.reizastudios.com/threads/automobilista-2-march-2025-development-update.34939/) | Prior record carried; see September 18 review |
| 10 | Automobilista 2: [Automobilista 2 V1.1.2.0 RELEASED - Now updated to v1.1.2.5](https://forum.reizastudios.com/threads/automobilista-2-v1-1-2-0-released-now-updated-to-v1-1-2-5.16140/) | Prior record carried; see September 18 review |
| 11 | Le Mans Ultimate: [Messaging drivers post race](https://community.lemansultimate.com/index.php?threads/messaging-drivers-post-race.18637/) | Prior record carried; see September 18 review |
| 12 | Automobilista 2: [Automobilista 2 V1.6.8 & Lamborghini Dream Pack PT2 RELEASED - Now Updated to V1.6.8.1](https://forum.reizastudios.com/threads/automobilista-2-v1-6-8-lamborghini-dream-pack-pt2-released-now-updated-to-v1-6-8-1.35636/) | Prior record carried; see September 18 review |
| 13 | Logitech G HUB: [logitech-ghub-release-notes](https://support.logi.com/hc/en-gb/articles/360048967733-G-HUB-Update-Release-Notes) | Prior record carried; see September 18 review |
| 14 | Automobilista 2: [Automobilista 2 V1.6.4.2 & Super Trophy Trucks RELEASED - Now Updated to V1.6.4.3](https://forum.reizastudios.com/threads/automobilista-2-v1-6-4-2-super-trophy-trucks-released-now-updated-to-v1-6-4-3.34944/) | Prior record carried; see September 18 review |
| 15 | RaceRoom Racing Experience: [DTM Mercedes AMG C-Coupé 2013 game crash](https://forum.kw-studios.com/index.php?threads/dtm-mercedes-amg-c-coup%C3%A9-2013-game-crash.21544/) | Prior record carried; see September 18 review |
| 16 | Automobilista 2: [Automobilista 2 March 2024 Development Update](https://forum.reizastudios.com/threads/automobilista-2-march-2024-development-update.33116/) | Prior record carried; see September 18 review |
| 17 | Automobilista 2: [Automobilista 2 V1.5.5.0, Le Mans & Endurance Pack Pt1 RELEASED - Now Updated to V1.5.5.6](https://forum.reizastudios.com/threads/automobilista-2-v1-5-5-0-le-mans-endurance-pack-pt1-released-now-updated-to-v1-5-5-6.32534/) | Prior record carried; see September 18 review |
| 18 | Le Mans Ultimate: [Conspit Haptic Motor (GT-Lite / gt-eva chip) not working](https://community.lemansultimate.com/index.php?threads/conspit-haptic-motor-gt-lite-gt-eva-chip-not-working.19022/) | Needs evidence; no remedy promoted |
| 19 | Le Mans Ultimate: [Title Menu is flickering & Menu not working](https://community.lemansultimate.com/index.php?threads/title-menu-is-flickering-menu-not-working.18936/) | Prior record carried; see September 18 review |
| 20 | Assetto Corsa EVO: [0.9.1 AC Evo Crash](https://www.assettocorsa.net/forum/index.php?threads/0-9-1-ac-evo-crash.83947/) | Needs evidence; no remedy promoted |
| 21 | Le Mans Ultimate: [BlackRack Overlay – Free telemetry overlays for Le Mans Ultimate](https://community.lemansultimate.com/index.php?threads/blackrack-overlay-%E2%80%93-free-telemetry-overlays-for-le-mans-ultimate.18937/) | Prior record carried; see September 18 review |
| 22 | Automobilista 2: [Automobilista 2 July 2025 Development Update](https://forum.reizastudios.com/threads/automobilista-2-july-2025-development-update.35356/) | Prior record carried; see September 18 review |
| 23 | Automobilista 2: [Cant control the menu with my Simagic wheel](https://forum.reizastudios.com/threads/cant-control-the-menu-with-my-simagic-wheel.34031/) | Needs evidence: community mapping workaround only |
| 24 | Automobilista 2: [Automobilista 2 V1.2.3.0 RELEASED - Now updated to V1.2.3.1](https://forum.reizastudios.com/threads/automobilista-2-v1-2-3-0-released-now-updated-to-v1-2-3-1.20127/) | Prior record carried; see September 18 review |
| 25 | Automobilista 2: [Automobilista 2 V0.8.3.1 RELEASED - Now updated to v0.8.3.2](https://forum.reizastudios.com/threads/automobilista-2-v0-8-3-1-released-now-updated-to-v0-8-3-2.10269/) | Prior record carried; see September 18 review |
| 26 | Le Mans Ultimate: [Netcode caused collision. DNF'ed other driver. How to report myself?](https://community.lemansultimate.com/index.php?threads/netcode-caused-collision-dnfed-other-driver-how-to-report-myself.19151/) | Needs evidence; no remedy promoted |
| 27 | Automobilista 2: [Automobilista 2 V1.6.5 RELEASED - Now Updated to V1.6.5.2](https://forum.reizastudios.com/threads/automobilista-2-v1-6-5-released-now-updated-to-v1-6-5-2.35255/) | Prior record carried; see September 18 review |
| 28 | Logitech G HUB: [logitech-ghub-support](https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops) | No change: existing sequence matches |
| 29 | Assetto Corsa EVO: [Crashes v0.9.1](https://www.assettocorsa.net/forum/index.php?threads/crashes-v0-9-1.83945/) | Needs evidence; no remedy promoted |
| 30 | Automobilista 2: [Automobilista 2 V1.6.9.95 RELEASED! Updated to v1.6.9.96](https://forum.reizastudios.com/threads/automobilista-2-v1-6-9-95-released-updated-to-v1-6-9-96.36667/) | Prior record carried; see September 18 review |
| 31 | Automobilista 2: [Automobilista 2 V1.6 Developent Update - Changelog & Release Notes](https://forum.reizastudios.com/threads/automobilista-2-v1-6-developent-update-changelog-release-notes.34256/) | Prior record carried; see September 18 review |
| 32 | Le Mans Ultimate: [Genesis Magma Racing launches official esports series with Le Mans Ultimate](https://lemansultimate.com/genesis-uses-le-mans-ultimate-for-esports-series-pro-sim-racing-drive-up-for-grabs/) | No change: event announcement, body checked |
| 33 | RaceRoom Racing Experience: [Telemetry Tool and black screen](https://forum.kw-studios.com/index.php?threads/telemetry-tool-and-black-screen.21555/) | Needs evidence; no remedy promoted |
| 34 | Le Mans Ultimate: [Change my name plse i cant joint championship and starts in 5 min](https://community.lemansultimate.com/index.php?threads/change-my-name-plse-i-cant-joint-championship-and-starts-in-5-min.19128/) | Needs evidence; no remedy promoted |
| 35 | Automobilista 2: [Automobilista 2 V1.5.6.1 RELEASED - Now Updated to V1.5.6.3](https://forum.reizastudios.com/threads/automobilista-2-v1-5-6-1-released-now-updated-to-v1-5-6-3.33205/) | Prior record carried; see September 18 review |
| 36 | Automobilista 2: [Automobilista 2 V1.6 Development Update - The New Cars](https://forum.reizastudios.com/threads/automobilista-2-v1-6-development-update-the-new-cars.34156/) | Prior record carried; see September 18 review |
| 37 | Automobilista 2: [Automobilista 2 V1.4.1.0 Released - Now Updated to V1.4.1.3](https://forum.reizastudios.com/threads/automobilista-2-v1-4-1-0-released-now-updated-to-v1-4-1-3.26258/) | Prior record carried; see September 18 review |
| 38 | Le Mans Ultimate: [High Frametime / "Slow-Motion" Stutter Only with Ferrari 296 GT3](https://community.lemansultimate.com/index.php?threads/high-frametime-slow-motion-stutter-only-with-ferrari-296-gt3.14072/) | Needs evidence; no remedy promoted |
| 39 | Le Mans Ultimate: [improveyourlaptime.com Major Update and App](https://community.lemansultimate.com/index.php?threads/improveyourlaptime-com-major-update-and-app.18133/) | Needs evidence; no remedy promoted |
| 40 | Automobilista 2: [Automobilista 2 V1.6.8.5 RELEASED; Now Updated to V1.6.8.6](https://forum.reizastudios.com/threads/automobilista-2-v1-6-8-5-released-now-updated-to-v1-6-8-6.35731/) | Prior record carried; see September 18 review |
| 41 | Assetto Corsa EVO: [Crashes V9.0.1](https://www.assettocorsa.net/forum/index.php?threads/crashes-v9-0-1.83942/) | Needs evidence; no remedy promoted |
| 42 | Automobilista 2: [Automobilista 2 September 2025 Development Update](https://forum.reizastudios.com/threads/automobilista-2-september-2025-development-update.35619/) | Prior record carried; see September 18 review |
| 43 | MOZA Pit House: [moza-downloads](https://mozaracing.com/pages/download-center) | Prior record carried; see September 18 review |
| 44 | Le Mans Ultimate: [Bad drivers get rewarded....](https://community.lemansultimate.com/index.php?threads/bad-drivers-get-rewarded.19148/) | Needs evidence; no remedy promoted |
| 45 | RaceRoom Racing Experience: [Launch fails and problem with races on multiplayer servers](https://forum.kw-studios.com/index.php?threads/launch-fails-and-problem-with-races-on-multiplayer-servers.21552/) | Needs evidence; no remedy promoted |
| 46 | Automobilista 2: [Automobilista 2 V1.6.7 RELEASED - Now Updated to V1.6.7.2](https://forum.reizastudios.com/threads/automobilista-2-v1-6-7-released-now-updated-to-v1-6-7-2.35495/) | Prior record carried; see September 18 review |
| 47 | Le Mans Ultimate: [(Solved) Fanatec CSL DD FFB freezes](https://community.lemansultimate.com/index.php?threads/solved-fanatec-csl-dd-ffb-freezes.19018/) | Needs evidence: thread/index mismatch |
