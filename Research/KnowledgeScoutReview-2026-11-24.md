# Manual Scout review - November 24, 2026

## Baseline and outcome

Main: [7443a73](https://github.com/rholmes426/PitMedic/commit/7443a73adc1971ef63f8dd18e9276afec02a5a0d).
Public download: [signed 1.0.0.13](https://github.com/rholmes426/PitMedic/releases/tag/v1.0.0.13). The [live homepage](https://pitmedic.com/) lists eight simulators and seven companion app families.
Open implementation PRs were checked for overlap. The open PRs returned by GitHub were dependency bumps.

One new repair proposal, one new access improvement covering two existing
repairs, one new validation candidate, and two guidance-only items. The
November 17 repair proposals remain in the queue. This review records new
research and planned library work. It does not change executable behavior or
publish new guides. No Windows simulator, Steam client or wheel hardware was
tested.

The rFactor 2 shader/track cache repair and Steam verification, original
Assetto Corsa video/controls recovery and Steam verification, and earlier
iRacing Error 63 detection already shipped. Confirmed live routes for the
[rFactor 2 cache guide](https://pitmedic.com/diagnostic-library/rfactor2-shader-cache/),
[original Assetto Corsa verification guide](https://pitmedic.com/diagnostic-library/ac-steam-content/),
and [iRacing Error 63 guide](https://pitmedic.com/diagnostic-library/iracing-spotter-loading-error-63/).
Current Error 63 coverage offers the spotter configuration repair for the
matching failure. It does not assume that a Helper Service restart will
resolve it.

## Prioritized work

### KS-MAN-LMU-RACELAB-TELEMETRY - new repair proposal

**Symptom.** LMU runs, Racelab is present, and its overlays show no live data
on track. Racelab's developer documents its own DLL and exact JSON key,
including the leading space in " Enabled". Its in-app Configure action is
also available. A SteadyState user reported restoring telemetry by enabling
that app's plugin in the same settings file. That outcome supports checking
the disabled-plugin diagnosis, but concerns a different plugin. LMU Pitwall
uses the game's native data source on current versions, and LMU Director
documents another plugin as a fallback. Neither is a reason to enable a
Racelab plugin for every telemetry failure.
[Developer instructions](https://garage.racelab.app/docs/first-steps/lmu/),
[SteadyState outcome](https://www.overtake.gg/threads/steadystate-lmu.296916/page-3),
[LMU Pitwall](https://github.com/Swizzjack/lmu-pitwall),
[LMU Director](https://racesimtools.com/lmu-director/documentation/shared-memory).

**App gap and design.** The LMU plugin-conflict plan runs
RepairLmuPluginsAsync, which backs up CustomPluginVariables.JSON and disables
enabled plugin entries. There is no inverse symptom-selected, single-plugin
action. Offer a requested check for missing Racelab data. Qualify it only
when the supported Steam LMU install contains Plugins/RacelabLMUPlugin.dll
from the user's existing installation and
UserData/player/CustomPluginVariables.JSON has one exact top-level
RacelabLMUPlugin.dll entry with numeric " Enabled" set to 0. With LMU
closed and Steam idle, require consent to enable that one existing key.
Describe the performance or startup tradeoff of re-enabling a plugin
that may have been disabled during crash isolation. Require the user to
confirm that Racelab is installed and expects the plugin. Missing, already
enabled, conflicting, malformed or ambiguous entries stop with guidance.
Leave other plugins and the game's global plugin switch untouched. Do not
fetch or replace a DLL or write any telemetry files.

**Validation and Restore.** Use the scoped configuration transaction,
extended for an exact JSON mutation, with original bytes backed up,
atomic replace, and read-back of the single changed value. Confirm the
backup is restorable with the game closed and preserve later user changes
before restoring. Test mismatched DLL names and paths, duplicate keys,
string versus numeric values, already enabled, concurrent file changes,
and config bytes that cannot be parsed. Confirm on a current LMU/Racelab
installation in one stock practice session, with telemetry disabled and
then restored, plus a rollback and retest. Record the file change
separately from whether Racelab received data.

**Diagnostic Library.** Proposed new lmu-racelab-telemetry-config guide.
Cross-link from lmu-plugin-conflict and explain the difference between
crash isolation and getting live data. Gate the new guide to the first
supporting app release.

### KS-MAN-IR-UI-REQUEST - new access improvement

**Symptom.** The iRacing UI is blank, frozen, or stuck on its welcome screen
and stays open. iRacing support documents local Electron cache recovery for
general UI startup trouble and a specific UISafe change for the welcome
screen. A community outcome for a similar loading-screen symptom reports a
cache reset helped. A November thread about a simulator crash contains an
unconfirmed suggestion to clear the cache. A simulator crash or generic
Electron connection reset is outside this proposed entry point.
[Vendor launch checks](https://support.iracing.com/support/solutions/articles/31000162469),
[Vendor Welcome-screen check](https://support.iracing.com/support/solutions/articles/31000178873-iracing-ui-crashing-or-stuck-on-welcome-to-iracing-),
[Loading-screen outcome](https://www.reddit.com/r/iRacing/comments/1hatq7p),
[Simulator crash discussion](https://www.reddit.com/r/iRacing/comments/1xwkfgu/why_is_it_crashing/).

**App gap and design.** RepairPlanner and RepairService already have both
finding-driven plans. UserRequestedRepairPolicy does not expose them from
iRacing Repair Tools. Add separate opt-in symptom choices. For the exact
Welcome screen, require a unique existing UISafe=1 under [Graphics DX11]
in the real Documents/iRacing/app.ini, change it to 0, and reuse the scoped
backup/Restore transaction. The current generic INI setter alone does not
prove that the expected setting is uniquely present. For blank/frozen UI,
reuse the backed-up roaming iracing-electron cache operation after updates
finish and all applicable iRacing processes are safely closed. Make the
existing executor's Helper Service restart visible in the consent and
warn that cache recovery can sign the user out. If the cache is absent,
report that condition without offering a cache reset as a completed fix.

**Validation and Restore.** Keep simulator and updater guards, validate
account scope and known install root, and repeat guards before mutation.
Test the elevated helper under the caller's identity to ensure the
intended user's roaming cache is targeted. Exercise missing, ambiguous,
updater-active and stale-evidence cases. Provide precise restore for the
INI and cache, preserving regenerated state. Relaunch the UI and retest
the previously blocked screen. The requested action should be recorded as
troubleshooting even when no failure was logged.

**Diagnostic Library.** Proposed updates to iracing-ui-safe and
iracing-ui-cache. Name the distinct symptoms, Repair Tools entry points,
side effects and Restore steps. This reuses two existing repairs.

### KS-MAN-RF2-CONTROLLER-JSON - validation candidate

Two independent users in one Steam discussion reported that regenerating
a user JSON file let the game start. One mainly needed player.JSON and the
other named Controller.JSON. A separate wheel-input discussion has mixed
results with firmware and Steam Input, followed by a broad reinstall that
does not isolate a file. The reports are old and do not establish a single
current remedy. Treat Controller.JSON as a validation target only when a
current rFactor 2 error identifies that exact active file and a local
read-only parse detects malformed JSON.
[JSON startup discussion](https://steamcommunity.com/app/365960/discussions/0/3987311729592647152/),
[Wheel input discussion](https://steamcommunity.com/app/365960/discussions/0/3436829654711761387/),
[Studio 397 controller setup](https://www.studio-397.com/rfactor-2-getting-started/).

The current app preserves fresh traces and offers cache rebuilding and Steam
verification. It has no controller-config parser or scoped reset for this
case. Validate the current retail build, filename, encoding and parser
rules, and record the actual error with a deliberately malformed copy.
When the exact scope validates, offer an approved move of only
UserData/player/Controller.JSON to a manifest-backed backup, with rFactor 2,
Mod Mode and Dedicated Server closed and Steam idle. Never reset saved
controller presets, other JSON files, setups or Workshop items. Confirm
one-file regeneration, game launch and in-game remapping/calibration,
then test Restore without discarding regenerated changes. The related
config_dx11.ini launch suggestion had a negative outcome from the person
who received it, so generic launch crashes remain insufficient evidence.
[Graphics-config thread](https://steamcommunity.com/app/365960/discussions/0/3765605292810324584/).

**Diagnostic Library.** Proposed rfactor2-controller-config if the fixture
and current build agree. Link the existing rfactor2-trace-log guide.
Keep the new guide unpublished while the file diagnosis is unverified.

## Guidance-only work for the next library edit

- **KS-MAN-RR-09931.** Update raceroom-loading-ffb-update-guidance with
  the November 24 client 0.9.9.31 / Steam build 26891219 fixes for quitting
  in VR, quitting while startup shaders are loading, and a multiplayer
  return-to-menu glitch. Compare the installed build and retest the same
  sequence after Steam updates. Keep the server-only crash correction
  distinct. These fixes are part of the publisher's build.
  [Publisher notes](https://steamcommunity.com/app/211500/allnews/).
- **KS-MAN-FANATEC-1723.** Update companion-fanatec-process-recovery with
  a preview-channel note for November 18 app 1.7.2.3, scoped to the GT DD
  Extreme quick-release connection loss while navigating the Tuning Menu.
  Match hardware and installed version before referring users to the vendor
  notes. The preview includes firmware changes. Keep PitMedic's existing
  process restart distinct from the device fix.
  [Vendor release notes](https://help.fanatec.com/hc/en-us/articles/53158250413841-Fanatec-App-v-1-7-2-3-Release-Notes).

## Coverage notes

| Product | Evidence reviewed and disposition |
| --- | --- |
| Le Mans Ultimate | [Publisher known issues](https://guide.lemansultimate.com/hc/en-gb/articles/13240843908623-Known-Issues-and-Advice), [Steam crash discussion](https://steamcommunity.com/app/2399420/discussions/0/693122745160047152/) and developer telemetry docs above. The prior DirectInput item still needs current layout and hardware validation. |
| iRacing | [Loading Error 63 support](https://support.iracing.com/support/solutions/articles/31000156684-loading-error-63) and the launch sources above. Keep the existing SimHub memory-setting proposal as a separate backlog item. |
| Original Assetto Corsa | [Steam verification](https://help.steampowered.com/en/faqs/view/0C48-FCBD-DA71-93EB) and [Content Manager outcome](https://steamcommunity.com/app/244210/discussions/0/845131095727093019/). One poster got the race to load by launching Content Manager directly. Insufficient reason to replace executables, write a launcher script, or broadly reset mods. |
| Assetto Corsa EVO | [505 0.9.1 notes](https://support.505games.com/hc/en/assetto-corsa-evo/articles/assetto-corsa-evo-0-9-1-update-steam-44) and [Steam startup thread](https://steamcommunity.com/app/3058630/discussions/0/586181093023119398/). A full Documents/ACE deletion did not help the poster. The existing profile recovery stays scoped to matching failures. |
| Assetto Corsa Competizione | [505 FFB advice](https://505games-help.freshdesk.com/support/solutions/articles/150000147306-my-wheel-s-force-feedback-in-assetto-corsa-competizione-feels-wrong-no-matter-what-i-do-anything-i-can-do-to-fix-it-) and [Steam controls discussion](https://steamcommunity.com/app/805550/discussions/0/562535555862292385/). Current controls/FFB recovery and the existing symptom-access proposal cover the actionable overlap. |
| Automobilista 2 | [Publisher news](https://steamcommunity.com/app/1066890/allnews/) and [Reiza user follow-up](https://forum.reizastudios.com/threads/having-to-reassign-controls-on-every-single-launch.35211/). Bindings remained present and profile deletion/Steam Input changes did not help that report. Leave the existing shared-memory file-write investigation open. |
| RaceRoom Racing Experience | [Publisher notes](https://steamcommunity.com/app/211500/allnews/) and [Steam wheel-detection discussion](https://steamcommunity.com/app/211500/discussions/1/3183487463218400249/). No confirmed scoped file remedy for the wheel report. Queue the patch guidance above. |
| rFactor 2 | [Studio 397 logging](https://docs.studio-397.com/pages/viewpage.action?pageId=86638877), released repair paths and Steam outcomes above. Verify the Controller.JSON hypothesis before adding a reset. |
| MOZA Pit House | [Vendor disconnection checks](https://support.mozaracing.com/en/support/solutions/articles/70000651007-wheel-base-or-pedals-disconnected-issue) and [current downloads](https://support.mozaracing.com/en/support/solutions/articles/70000627795-moza-pit-house-downloads). A November R5 rollback discussion was indexed but did not open. No automated firmware or generic USB setting change. |
| Simucube True Drive / Tuner | [Tuner changes](https://docs.simucube.com/TunerSoftware/changelog.html) and [Granite Devices staff discussion](https://community.granitedevices.com/t/update-tuner-software-issue/12787). Staff explained that force feedback is outside the Tuner process. Keep the existing process-identity and Link-port validation items pending. |
| Fanatec | [Official November 18 notes](https://help.fanatec.com/hc/en-us/articles/53158250413841-Fanatec-App-v-1-7-2-3-Release-Notes) and indexed community preview feedback. The forum discussion did not open reliably. The precise firmware fix warrants guidance, and the previous user-requested process recovery proposal remains queued. |
| Logitech G HUB | [Official service sequence](https://support.logi.com/hc/en-001/articles/35644288645655-G-HUB-Service-restart-guide) and [independent walkthrough](https://www.itechguides.com/get-logitech-g-hub-or-gaming-software-to-open-again/). The app already has scoped service recovery. Keep the November 17 symptom-access item. |
| SIMAGIC SimPro Manager | [Vendor downloads and compatibility notes](https://simagic.com/pages/download-center) and previous SimHub developer outcomes. Current HPR ownership and safe version handling still require hardware reproduction. |
| Asetek RaceHub | [Vendor wheel troubleshooting](https://www.asetek.com/simsports/knowledge-base/steering-wheel-troubleshooting/) still specifies the wheel/display Base Profiles path when the wheel lights up but is missing in RaceHub. This revalidates the earlier proposal without adding a duplicate. |
| VRS DirectForce / VRS ONE | [Vendor downloads](https://vrs.racing/downloads) and [user follow-up](https://www.reddit.com/r/simracing/comments/1u50ujs/need_help_with_vrs_direct_force_pedals_software/). The user got the current app working after switching from the older tool. The installed-app definition already recognizes VRSOne.exe, so this is not a detection gap or an automatic migration candidate. |

The [Thrustmaster T598 support page](https://support.thrustmaster.com/en/product/t598p-en/)
and [a report of LMU crashing after an update](https://www.reddit.com/r/Thrustmaster/comments/1xbi59q/after_updating_thrustmaster_panel_and_wheel_base/)
were reviewed as related leads. Vendor installation and firmware steps are
manual, and My Thrustmaster Panel is outside the monitored companion list.
No new automatic action or guide is warranted by that evidence alone.

The [rolling intake issue](https://github.com/rholmes426/PitMedic/issues/9)
still shows November 13. This pass used active research and the manual source
inventory, with new entries for rFactor 2 Steam discussions and Racelab's
official LMU setup. It did not rerun or independently rescreen the retained
backlog. Blocked community threads remain leads. Vendor pages, accessible
Steam/OverTake discussions and the Granite Devices thread provided direct
evidence where available. No private channels were required for the results.
