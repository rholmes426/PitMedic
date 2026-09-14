# Knowledge Scout review — 2026-09-14

Scope: the September 11 scan in [rolling issue #9](https://github.com/rholmes426/PitMedic/issues/9), containing 13 source findings, two safety signals, and one availability failure. Every finding has a disposition below. This is completed triage, not a claim that inaccessible evidence has been verified or queued work implemented.

## Decisions

| Finding | Outcome | Evidence and reasoning |
| --- | --- | --- |
| LMU September community update | Queue guidance | Studio 397 acknowledges network/phantom-collision issues and announced a September 15 fix. Explain the vendor-side issue and check the actual released patch before claiming resolution. No local network reset or repair suppression justified. [Official update](https://lemansultimate.com/community-update-september-2026/) |
| LMU Logitech G29 LmuFFB discussion | Needs evidence | Both the captured post link and thread link returned the forum index through this reader; alternate path was unavailable. No verified remedy. Keep on investigation list; do not install or alter third-party FFB software based on the title. [Captured source](https://community.lemansultimate.com/index.php?threads/logitech-g29-lmuffb.17859/post-99353) |
| LMU warping/steering/crashes discussion | Needs evidence | Captured thread content could not be retrieved; do not infer crash cause from its title. Official network guidance above is independently supported, but does not establish this user's crash cause. [Captured source](https://community.lemansultimate.com/index.php?threads/other-cars-warping-steering-makes-you-crash-and-game-keeps-crashing.18894/post-99384) |
| LMU driver-reporting megathread | No repair queued; title-only exclusion | Title concerns driver conduct in practice servers, not PC drivers. Actual post could not be retrieved. Retain this access limitation; reconsider only if software-repair evidence emerges. [Captured source](https://community.lemansultimate.com/index.php?threads/driver-reporting-megathread-practice-server-only.8590/post-99387) |
| iRacing Season 4 Hotfix 1 | Queue update-first guidance | Vendor fixes simulator crashes on minimizing and replay VCR interaction in 2026.09.11.01. Show relevant known-issue guidance when symptoms/version are established; retain genuine fault reporting. [Release notes](https://support.iracing.com/support/solutions/articles/31000179637-2026-season-4-hotfix-1-release-notes-2026-09-11-01-) |
| iRacing Season 4 initial release | Queue supporting guidance | Vendor fixes AI server-transition and Porsche Mission R fuel-related crashes in 2026.09.09.01. Include contextual update-first guidance, without treating every crash as one of these bugs or inventing machine-detectable signatures. [Release notes](https://support.iracing.com/support/solutions/articles/31000179517-2026-season-4-initial-release-notes-2026-09-09-01-) |
| Second iRacing Hotfix 1 entry | Duplicate | Same URL as the reviewed hotfix entry. One queue item. |
| Second iRacing initial-release entry | Duplicate | Same URL as the reviewed initial-release entry. One queue item. |
| AMS2 September development update | Queue guidance with release-status prerequisite | Official post announces weather-transition CPU improvements and multiplayer crash fixes. Use announcement wording until shipped version is independently confirmed; the post contains inconsistent version strings, so do not encode a version threshold from it. [Corrected source](https://forum.reizastudios.com/threads/automobilista-2-september-2026-development-update.36665/) |
| AMS2 page 2 | Consolidated; evidence access unresolved | Same discussion, not an independent fix. Original nested URL and corrected page-2 route failed. Do not mark its comments verified. |
| AMS2 page 3 | Consolidated; no separate repair | Corrected page is accessible; reviewed discussion is predominantly update anticipation/gameplay commentary, without a verified local recovery procedure. [Page 3](https://forum.reizastudios.com/threads/automobilista-2-september-2026-development-update.36665/page-3) |
| Fanatec downloads changed | Queue compatibility guidance | Official downloads page says legacy Driver/Control Panel and FanaLab are no longer developed and instructs uninstalling the Fanatec App before using legacy software. Explain supported choice; never automatically uninstall, migrate profiles, or retire legacy recovery simply due to age. Current page advertises App 1.5.4.2; no inference about its changes without matching notes. [Downloads](https://www.fanatec.com/ca/en/s/download-apps-driver) |
| Logitech G HUB page changed | Already covered; no new repair | Official close-agent/UI, restart updater service, relaunch sequence matches current documented recovery. Preserve existing implementation; no settings deletion. [Official article](https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops) |

## Safety and availability

- LMU safety signal: dismiss as unrelated. Captured “unsafe rejoin” concerns driving conduct; it provides no evidence of a harmful PitMedic repair.
- Logitech safety signal: dismiss as unrelated. Captured Presentation-software retirement text and current generic notices concern other Logitech products, not G HUB recovery.
- Fanatec 403: source is available through the browser today, including an official employee release post. This does not prove GitHub runner access is restored. Keep the runner availability problem open; do not remove its source or existing repairs.
- Additional evidence recovered from that forum: Fanatec's 1.5.4.1 hotfix addresses CSL DD/GT DD Pro oscillation after leaving menus in RaceRoom and other named titles. Queue model-specific vendor-update guidance alongside Fanatec compatibility guidance; no automatic firmware changes. [Official employee post](https://forum.fanatec.com/topic/20482-fanatec-app-v1541-hotfix/)

## Queue and implementation boundaries

Four product guidance groups are recorded in PENDING-UPDATES.md: iRacing, LMU, AMS2, and Fanatec. None is implemented or release-ready by this review. No new automatic repair or change to repair availability is justified by the reviewed evidence.

One Scout maintenance group records: cross-source deduplication, canonical forum URLs/pagination grouping, product-specific safety text, persistent review dispositions, and runner source-health verification. Validate original HTML/base URLs before assigning the malformed-link cause to the Scout; LMU query normalization may be reader-specific.

Outstanding evidence: LMU G29 discussion, LMU warping/crash discussion, AMS2 page 2. Runner access to Fanatec also needs verification. These remain visible investigation items rather than falsely completed reviews.

Existing source comparison state in #9 is preserved. No release, signing, installer, app behavior, or website publication is part of this review.
