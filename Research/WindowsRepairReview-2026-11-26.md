# Windows 10/11 compatibility and repair review - November 26, 2026

Baseline: PitMedic 1.0.0.13, main dd654a05a9a22b05f88dc71dd85bb1e248148414.
Scope: Windows faults that affect supported simulators and companion software,
including failures before a simulator process starts. This is a research and
queue update, with five new repair proposals, one reuse improvement and four
diagnostic/validation items. No Windows repair was executed or benchmarked.

## Existing coverage and gap

WindowsEventService reads Application and System events around exits, including
application faults, hangs, WER, GPU driver, WHEA and power/hardware evidence.
CrashClassifier prioritizes WHEA/display events and matches other app errors
from message text. The collector accepts broad provider/event-ID matches, so
nearby unrelated events are context only. WindowsEventEvidence currently stores
the formatted message rather than typed EventData. Narrow existing evidence,
such as LMU content corruption and iRacing loader errors, must still select the
matching repair. Generic Windows event IDs can belong to different providers.

RepairPlanner and RepairService already offer iRacing DISM/SFC system integrity,
iRacing compatibility flags, LMU time sync and custom graphics hooks, and
vendor-specific service/process recovery. LMU allocation errors are currently
diagnostic-only. The helper reconstructs a saved plan independently and uses an
elevation allowlist. There is no shared Windows runtime repair, audio-session
recovery, DNS-cache recovery or cross-product OS diagnostic policy.

Prioritize a shared Windows Troubleshooting entry point on Home and relevant
sim/companion finding screens. A user can select "won't launch", "no sound",
"device not recognized", "out of memory", "connection fails" or "Windows
crash" even if the app never launched. Inspect only the selected installed
software, relevant recent events and local OS state. Keep the present polling
cadence and use bounded, provider-qualified event queries on failure or request;
do not add a continuous global event scan. Preserve source, record ID,
timestamp, application path, module, exception/status and relevant structured
fields where available. Match a current app identity, session and exact code.
Global events can be correlated without attributing the root cause.

A shared plan should be available for any supported app when the same verified
symptom applies. Report diagnosis, intended mutation, consent, operation result
and actual user/application retest distinctly. Preserve existing narrow plans
and incident history compatibility. Use the current helper for fixed, allowlisted
elevated actions and recheck prerequisites there. Display estimated duration,
machine-wide effects, restart requirement and rollback limits before approval.
No executable or arbitrary command from an event/log may run.

## New repair proposals

### KS-WIN-VC14 - Visual C++ runtime v14 recovery

**Priority: high.** A named missing MSVCP140.dll, VCRUNTIME140.dll or
VCRUNTIME140_1.dll, or an exact vendor prerequisite message, is relevant
at launch. A generic 0xc0000135 (missing component), 0xc000007b (invalid image
format), side-by-side error or application fault cannot identify the package.

Microsoft documents architecture-aware installation and repair, and MOZA's
Pit House manual specifically requires the x86 Visual C++ v14 runtime.
A Fanatec user reported resolving an older pedal-updater DLL failure with
x86 v14; another similarly titled discussion ended with a USB cable fix.
This makes the actual named DLL and affected binary architecture decisive.
[Microsoft deployment](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files),
[Downloads and Windows 10/11 compatibility](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist),
[MOZA prerequisite](https://support.mozaracing.com/en/support/solutions/articles/70000625635-moza-pit-house-user-manual),
[Fanatec outcome](https://forum.fanatec.com/topic/7031-pedal-v3-firmware-stuck-in-v130/),
[Contrasting cable outcome](https://forum.fanatec.com/topic/8860-resolved-fanatec-firmware-errors-windows-1011/).

Read affected EXE architecture and installed v14 version for that architecture.
Prefer a supported Microsoft-signed installer matching the required architecture
and installed package version, or the current supported v14 package if absent
and applicable. Use documented install/repair and no-restart options after
approval. Stage in trusted storage, independently check provenance and
Authenticode trust, and refuse older or incompatible packages. Record package
identity/version before and after, installer output and reboot-required outcome.
Preserve profiles and older side-by-side runtime generations. Shared packages
are serviced by Windows/MSI, so there is no invented one-click DLL rollback.
Require installed-app verification and a same-app launch test. Test x86 on x64,
x64, absent/healthy/newer packages, cancellation, signature failure, non-English
loader evidence, installer busy/reboot conditions and unrelated exceptions.
No firmware updater is launched as part of this repair.

Library: proposed windows-visual-cpp-runtime, with MOZA and affected app
cross-links, plus existing lmu-reshade-runtime distinction.

### KS-WIN-DIRECTX-LEGACY - legacy game runtime recovery

An explicit missing legacy D3DX/XInput/XAudio component or vendor legacy
DirectX prerequisite message can justify checking the corresponding optional
runtime. Microsoft documents those separate legacy libraries, and Steamworks
supports shared redistributables. An ACC Steam discussion has mixed outcomes:
multiple users report the runtime procedure restored launch and another did
not succeed. Some suggestions involve removing system files, which are not
part of this proposal. The old discussion is not current-build validation.
[Microsoft package](https://www.microsoft.com/en-us/download/details.aspx?id=8109),
[DirectX setup documentation](https://learn.microsoft.com/en-us/windows/desktop/DxTechArts/directx-setup-for-game-developers),
[Steamworks](https://partner.steamgames.com/doc/features/common_redist),
[ACC discussion](https://steamcommunity.com/app/805550/discussions/0/1741094390477055711).

Validate current Win10/11 vendor-required package and exact dependency.
Prefer the game's signed, Steam-managed legacy setup where documented and
verified. Otherwise use a Microsoft-supported package only after checking
applicability (the legacy package's download page lists older OS versions).
Require approval and closed supported apps, check installed version and
signatures for all executed setup components, capture setup result, separate
reboot from completion, then verify the component and relaunch. Core OS
DirectX 11/12, DXGI device-removed errors and custom DLL conflicts are different
conditions. Never delete or replace System32/SysWOW64 DLLs. No direct rollback
is claimed for shared legacy components. Validate on current Windows 10 and 11
with exact missing library fixtures and a supported game, including absent,
tampered and inapplicable setup packages.

Library: proposed windows-legacy-directx-runtime.

### KS-WIN-AUDIO-MUTE - restore an explicitly muted app audio session

**Priority: high, small scope.** Windows volume mixer can mute an app while
system audio is available. Microsoft documents both this symptom and supported
Core Audio session interfaces. Enumerate active render sessions only on request,
match the exact supported process, user session, endpoint and session instance.
Read mute state and existing volume. If a uniquely owned shared-mode session is
muted, show its name and explain that unmuting will restore its previous
volume. Apply the documented session mute operation after approval and only
when the user is parked or otherwise ready for audio to resume. Restore the
original mute value on request for that same session, revalidate identity,
and verify read-back. Leave master/endpoint volume, other apps, routing and
exclusive-mode streams alone. Cross-process/shared ownership, disconnected or
missing sessions, ambiguity and an already-unmuted app are guidance cases.
Test multiple endpoints, PID reuse, shared sessions and session expiry.
[Microsoft troubleshooting](https://support.microsoft.com/en-us/windows/hardware/audio/fix-app-audio-not-working-while-system-sounds-work-in-windows),
[Session enumeration](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessionmanager2-getsessionenumerator),
[Process identity](https://learn.microsoft.com/en-us/windows/win32/api/audiopolicy/nf-audiopolicy-iaudiosessioncontrol2-getprocessid),
[Mute operation](https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-isimpleaudiovolume-setmute).

Library: proposed windows-app-audio-mute. Functional playback retest remains
user-confirmed.

### KS-WIN-AUDIO-SERVICE - Windows audio service recovery

A no-sound or stuttering report plus stopped/unavailable Windows audio service
can support a controlled recovery. Microsoft documents restarting Windows
Audio and Windows Audio Endpoint Builder, including app-only audio trouble.
iRacing support also describes sound loss/stuttering from audio interactions
and exclusive mode, so distinguish routing, mute and driver problems first.
[Microsoft audio service advice](https://support.microsoft.com/en-us/windows/hardware/audio/fix-audio-stops-working-after-a-windows-update-in-windows),
[iRacing sound troubleshooting](https://support.iracing.com/support/solutions/articles/31000163354-no-sound-in-sim-and-or-massive-stuttering).

Use a fixed two-service allowlist (Audiosrv and AudioEndpointBuilder). Read
both states/dependencies and administrative policy, use the supported service
control sequence with bounded wait and return services to running, and confirm
state afterward. Require approval with a notice that other apps, voice calls
and audio will briefly be interrupted. Close sims and ensure no firmware/update
operation. If manually disabled, access denied or dependencies cannot safely
be stopped/restarted, stop with guidance. Preserve startup types, routing and
driver settings. Do not restart RPC or force a service kill. Capture service
and endpoint health before/after and the user's playback test. No persistent
change to Restore is expected.

Library: proposed windows-audio-service-recovery.

### KS-WIN-DNS-CACHE - stale Windows resolver cache

A named host resolution failure plus a reproducible stale/negative cache result
can justify a DNS-cache refresh. A successful query to the configured resolver
while the normal Windows lookup fails or disagrees is evidence to investigate;
CDN variation, VPN/split DNS and service outages can explain differences.
Microsoft documents the targeted resolver-cache refresh. The RaceRoom and LMU
discussion leads do not establish a general cure for disconnections (the LMU
poster already tried flushing and still had intermittent failures).
[Microsoft DNS client diagnostics](https://learn.microsoft.com/windows-server/networking/dns/troubleshoot/troubleshoot-dns-client),
[Microsoft ipconfig](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/ipconfig),
[RaceRoom discussion](https://steamcommunity.com/app/211500/discussions/1/597412823492333719/),
[LMU discussion](https://steamcommunity.com/app/2399420/discussions/0/801218218523976155/).

Require explicit symptom selection and a verified product service hostname.
Resolve locally using configured DNS, distinguish failed DNS from reachability
and avoid arbitrary hosts supplied by logs. If evidence supports cached stale
or negative resolution, approve a single Windows client cache refresh with
fixed parameters. Record the query result before/after without storing browsing
history; retest the exact service. Cache repopulates normally. No DNS server,
firewall, adapter, VPN or Winsock configuration changes. Test no network, split
DNS, IPv4/IPv6, timeouts, repeated requests, no cache entry and unrelated HTTP
or packet-loss errors.

Library: proposed windows-dns-cache-recovery.

## Shared repair refinement

### KS-WIN-INTEGRITY - evidence-led DISM/SFC across supported apps

Existing iRacing repair proceeds directly to DISM RestoreHealth and SFC
scannow and requires approval. Its planner can offer it for general application
faults or abnormal termination. Microsoft explains that faulting Windows
modules like ntdll.dll or kernelbase.dll often appear in crashes and are
insufficient to prove OS corruption.
[Application failure analysis](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-crashing-behavior),
[Windows integrity](https://support.microsoft.com/en-us/windows/experience/backup-recovery/using-system-file-checker-in-windows).

Make it available across all supported apps via a Windows troubleshooting
request or strong corroborated integrity evidence. Start with verification
(DISM health check/scan and SFC verify-only where applicable) and explicit
time/administrative consent. Only offer repair after known corruption or
user-reviewed diagnostic findings, via the existing fixed command executor.
Preserve the existing protected plan reconstruction and distinguish no
corruption, repaired, not repaired, missing source and reboot required. A
successful integrity scan is not a solved crash. Test OS language-independent
status and output handling, timeouts, cancellation, pending servicing, and
more-specific sim signatures. System component servicing has no PitMedic
one-click Restore; Windows-supported servicing/recovery is the rollback route.
Library: proposed windows-system-file-integrity and update existing
iracing-windows-integrity. This reuses an existing repair.

## Diagnostic and validation work

| ID | Evidence and planned behavior | Library target |
| --- | --- | --- |
| KS-WIN-COMMIT | Add read-only system commit total/limit sampling via GetPerformanceInfo and correlate provider-qualified Resource-Exhaustion-Detector event 2004 with the session. Show top consumers, actual page-file configuration and relevant free space. Microsoft says page-file sizing depends on peak commit and dump needs. LMU documents memory allocation/stutter guidance, but its proposed fixed 64 GB is workload-specific. Preserve diagnostic-only state until current drive/config, dump requirements, backup/Restore and reboot behavior validate for a targeted page-file change. | Proposed windows-commit-exhaustion and update lmu-memory-allocation. |
| KS-WIN-DEVICE | Distinguish app not seeing a wheel from a current Windows device problem such as Code 10 or 43. Read current instance ID, hardware ID, problem code and provider-qualified events. PnPUtil supports restarting one instance from Windows 10 2004 onward. Validate vendor-specific leaf-device identity and safety with wheel/pedals in a controlled idle condition; firmware, hubs, system input and other devices are outside scope. Device restart stays validation-only until verified on hardware. | Proposed windows-wheel-device-problem, linked from companion recovery guides. |
| KS-WIN-GPU-OS-BUILDS | Capture actual driver version and matching display/TDR events, WHEA and Windows build, with DXGI/device error context when available. Windows GPU timeout recovery already occurs; temperature, an access violation and unrelated nearby events are distinct. Microsoft explicitly limits TDR registry changes to driver debugging. Match exact current OS release-health issues with build, hardware and symptom before offering supported update guidance. Real example: USB Audio Class 1.0 Code 10/no output after September 2026 patches, remaining symptoms resolved November 10 (Windows 10 22H2 KB5131096 and Windows 11 24H2 KB5132061). Other builds require their own KB mapping and applicable servicing availability. | Proposed windows-graphics-driver-timeout and windows-known-update-issues, linked from audio/device guides. |
| KS-WIN-SAVE-STORAGE | Read actual Documents/install/temp locations, OneDrive redirection, disk free space and matched access/write failure or documented Windows block event. Check exact Defender Controlled Folder Access event 1123 through permitted local reads and display the cause for the matching path, with a local link to Windows Protection History. Existing iRacing guide already explains OneDrive; do not auto-change cloud sync, permissions, protection, disk configuration or files. Distinguish disk-full from disk I/O/hardware evidence. | Proposed windows-save-and-storage-errors and update iracing-could-not-find-sim. |

Sources for diagnostic work:
[Commit limit and page files](https://learn.microsoft.com/en-us/troubleshoot/windows-client/performance/how-to-determine-the-appropriate-page-file-size-for-64-bit-versions-of-windows),
[Resource exhaustion](https://learn.microsoft.com/en-us/troubleshoot/windows-server/performance/troubleshoot-application-service-memory-leaks),
[Performance information](https://learn.microsoft.com/en-us/windows/win32/api/psapi/nf-psapi-getperformanceinfo),
[LMU performance](https://guide.lemansultimate.com/hc/en-gb/articles/15769559223439-Performance-Optimization-Guide),
[Device codes](https://support.microsoft.com/en-us/windows/hardware/drivers/error-codes-in-device-manager-in-windows),
[PnP utility](https://learn.microsoft.com/en-us/windows-hardware/drivers/devtest/pnputil-command-syntax),
[TDR](https://learn.microsoft.com/en-us/windows-hardware/drivers/display/tdr-registry-keys),
[Windows 10 22H2 resolved issues](https://learn.microsoft.com/en-us/windows/release-health/resolved-issues-windows-10-22h2),
[Windows 11 24H2 resolved issues](https://learn.microsoft.com/en-us/windows/release-health/resolved-issues-windows-11-24h2),
[Controlled Folder Access event monitoring](https://learn.microsoft.com/en-us/defender-endpoint/controlled-folder-access-monitor),
[iRacing OneDrive guidance](https://support.iracing.com/support/solutions/articles/31000178048-disabling-onedrive-backups-of-sim-data-directory),
[Windows storage](https://support.microsoft.com/en-us/windows/experience/storage-filemanagement/free-up-drive-space-in-windows).

## Scope and implementation sequence

Start with exact Windows evidence matching, VC14, app audio mute, and then
the already existing integrity executor/access improvement. Add legacy DirectX,
audio service and DNS in separate validated plans. Next, expose commit/device
and system evidence with useful testable diagnoses across eight sims and
seven monitored companion families. Test fixtures on both Windows 10 and 11,
including current supported servicing branches, non-administrator user,
missing event/sensor access, older saved findings, simulator-updater activity,
deduplication and unrelated events. No generic performance tuning, setting
changes or automatic OS/driver updating. Keep diagnostics local, with
new Windows library group and related guides only when the corresponding
behavior ships; all listed guides are planned. Research source coverage is
targeted to common Windows conditions, not a prevalence estimate or a
reproduction on every sim/hardware combination.

## Additional sysadmin source review (November 26)

The Windows scope also includes standalone OS problems. User-selected
[PatchManagement.org](https://patchmanagement.org/) and
[r/sysadmin source recommendations](https://www.reddit.com/r/sysadmin/comments/572cmu/is_there_a_site_that_lists_windows_server_updates/)
were examined. PatchManagement's public [MARC archive](https://marc.info/?l=patchmanagement)
ends in April 2019, and its [Google Group](https://groups.google.com/g/patchmanagement)
returned permission denied. Both are retained with that access status.
The source thread also recommends AskWoody, old Shavlik patch notes and
technology coverage. AskWoody returned HTTP 403. Ivanti's
[November Patch Tuesday article](https://www.ivanti.com/blog/november-2026-patch-tuesday)
was readable, and BleepingComputer's Windows news provided current OS
regression reporting. Use these for leads, with Microsoft as the servicing
authority.

The [November 10 r/sysadmin megathread](https://www.reddit.com/r/sysadmin/comments/1xsp3bk/patch_tuesday_megathread_november_10_2026/)
and [Hotpatch printer outcome](https://www.reddit.com/r/sysadmin/comments/1xyoihd/windows_nov_hot_patch_breaking_print_spooler/)
were readable. The latter reports stalled/crashing Print Spooler with
KB5132043 builds ending .9911, with recovery after the standard cumulative update
KB5132061 ending .9974. That is a community outcome on managed machines,
not a generic spooler restart or permission to remove security updates.
Microsoft's standard update has other documented printing fixes, and
the [Hotpatch release note](https://support.microsoft.com/en-us/servicing/os/hotpatch/windows-11/2026/november-kb5132043-windows-11-24h2-25h2-26h2-security-update)
adds a November 25 known issue involving msi.dll and certain 32-bit Windows
Installer consumers. Distinguish that exact Hotpatch/build and symptom from
generic application faults. Record the installed build and affected app,
then refer to supported vendor servicing. No registry mitigation or patch
uninstallation is implemented.

BleepingComputer's [November installation issue report](https://www.bleepingcomputer.com/news/microsoft/microsoft-confirms-kb5132061-installation-issues-0x80070306-0x800f0989-0x800f0993-errors/)
matches the [Microsoft standard-update notes](https://support.microsoft.com/en-gb/servicing/os/windows-11/2026/11/kb5132061-windows-11-24h2-25h2-26h2-security-update)
for October preview KB5127936 followed by the November update retry/error
0x80070306, 0x800f0989 or 0x800f0993. Microsoft delivered automatic servicing
mitigation. A repeated error needs current servicing status, not a blanket
Windows Update reset. Its [Bluetooth audio article](https://www.bleepingcomputer.com/news/microsoft/microsoft-testing-fix-for-windows-11-bluetooth-audio-quality-drops/)
describes Insider Beta/Experimental work; it is a preview lead and not a
stable Windows 11 fix.

Extend KS-WIN-GPU-OS-BUILDS to include standalone applicable update/installer
and service regressions. Planned windows-known-update-issues guide should
explain exact KB/build/channel conditions and pending versus released remedies.
Further source monitoring remains manual.
