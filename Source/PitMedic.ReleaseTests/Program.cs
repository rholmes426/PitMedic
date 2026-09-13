using PitMedic.Models;
using PitMedic.Services;
using System.Text.Json;

foreach (var invalid in new float?[] { null, 0, -1, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
{
    AssertFalse(CpuSensorPolicy.PositiveReading(invalid).HasValue, "Invalid CPU readings must be unavailable.");
    AssertTrue(CpuSensorPolicy.PreferValid(invalid, 47) == 47, "Invalid local readings must allow the service fallback.");
    AssertFalse(CpuSensorPolicy.PreferValid(invalid, 0).HasValue, "Invalid service readings must not display as 32 F.");
}
AssertTrue(CpuSensorPolicy.PreferValid(52, 47) == 52, "Valid primary CPU readings must be preserved.");
AssertTrue(CpuSensorPolicy.PreferValid(0, 4200) == 4200, "Zero MHz must allow the service clock fallback.");
AssertTrue(CpuSensorPolicy.PositiveReading(110) == 110, "Real overheating readings must not be hidden.");

var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);
var day = "2026-09-01";
var retryDelay = TimeSpan.FromHours(1);

AssertFalse(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        day, "0.6.0.9|preview|installer", now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "A successful heartbeat with unchanged dimensions must remain limited to one per UTC day.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        day, "0.6.0.8|preview|installer", now.AddMinutes(-10).ToString("o"), "0.6.0.8|preview|installer", retryDelay),
    "An app-version change must send immediately even after a recent successful heartbeat.");

AssertFalse(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|installer",
        null, null, now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "A failed heartbeat with unchanged dimensions must retain the one-hour retry throttle.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|preview|portable",
        null, null, now.AddMinutes(-10).ToString("o"), "0.6.0.9|preview|installer", retryDelay),
    "An installation-type change must bypass an attempt made with older dimensions.");

AssertTrue(
    AnonymousUsageThrottlePolicy.ShouldSend(now, day, "0.6.0.9|stable|installer",
        day, "0.6.0.9|preview|installer", now.AddMinutes(-10).ToString("o"), null, retryDelay),
    "Legacy state without attempt dimensions must not suppress the first heartbeat after an update.");

var preservedIRacingSignature = IRacingRepairSignaturePolicy.FindKnowledgeSignature(
[
    "Windows Application Error event 1000 matched the issue window.",
    "Live iRacing error detected: Easy Anti-Cheat Error 73 [PitMedic diagnostic signature: eac-error-73]"
]);
AssertTrue(
    string.Equals(preservedIRacingSignature, "iracing-eac-error73", StringComparison.Ordinal),
    "Saved iRacing EAC evidence must reconstruct the narrow anti-cheat repair before a generic crash category.");

AssertTrue(
    IRacingRepairSignaturePolicy.MapDiagnosticSignature("missing-file-privileges")
        .Equals("iracing-missing-file-privileges", StringComparison.Ordinal),
    "Steam Missing File Privileges must map to the targeted iRacing file-release repair.");
AssertTrue(
    ElevatedRepairPolicy.RequiresElevation("iracing-release-file-privileges"),
    "The iRacing Helper Service file-release workflow must remain approval-gated and elevated.");

// Normal update activity must not become a cache-reset or stopped-service repair.
var updateState = new IRacingUpdateState();
AssertFalse(updateState.Observe(false, now), "Ordinary startup must not suppress iRacing diagnostics.");
AssertTrue(updateState.Observe(true, now), "An active updater must suspend maintenance diagnostics.");
AssertTrue(updateState.Observe(false, now.AddSeconds(20)), "Updater process handoffs need a settling interval.");
AssertFalse(updateState.Observe(false, now.AddSeconds(31)), "Monitoring must resume after update settling.");
AssertTrue(IRacingUpdateGuard.IsUpdateProcess("iRacingUpdater", ""), "The standalone updater must be recognized.");
AssertTrue(IRacingUpdateGuard.IsUpdateProcess("iRacingUpdater64", ""), "The 64-bit updater must be recognized.");
AssertTrue(IRacingUpdateGuard.IsUpdateProcess("iRacingUI", "Updating"), "The iRacing UI updating window must be recognized.");
AssertFalse(IRacingUpdateGuard.IsUpdateProcess("iRacingUI", "iRacing"), "An ordinary open UI must not hide failures.");
AssertFalse(IRacingUpdateGuard.IsUpdateProcess("OtherApp", "Updating"), "Another application's update must not affect iRacing.");
AssertTrue(IRacingUpdateGuard.SuppressDiagnostic("helper-service", true), "Service replacement during an update is not a stopped-service finding.");
AssertFalse(IRacingUpdateGuard.SuppressDiagnostic("helper-service", false), "A stopped service outside an update must remain detectable.");
AssertTrue(IRacingUpdateGuard.BlocksRepair("iracing-reset-update-cache", true), "Old cache-reset findings must not interrupt an active update.");
AssertTrue(IRacingUpdateGuard.BlocksRepair("iracing-release-file-privileges", true), "Elevated service repairs must also wait for updates.");
AssertFalse(IRacingUpdateGuard.BlocksRepair("iracing-reset-update-cache", false), "A real post-update failure must remain repairable.");
AssertFalse(IRacingUpdateGuard.BlocksRepair("lmu-shader-cache", true), "An iRacing update must not block unrelated repairs.");

var updateLogRoot = Path.Combine(Path.GetTempPath(), "PitMedic-update-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(updateLogRoot);
try
{
    var log = Path.Combine(updateLogRoot, "updater.log");
    File.WriteAllText(log, "old verification failure\n");
    var updating = true;
    var monitor = new IRacingLiveLogMonitor(() => updating, () => new[] { log }, () => now);
    monitor.StartSession(now);
    AssertTrue(monitor.Poll().Count == 0, "Old log contents must remain baselined.");
    File.AppendAllText(log, "verification failure: version 1 check failed\nwaiting for iracingservice\n");
    AssertTrue(monitor.Poll().Count == 0, "Live update checks must not create repair findings.");
    File.AppendAllText(log, "DXGI_ERROR_DEVICE_REMOVED\n");
    AssertTrue(monitor.Poll().Single().SignatureId == "dxgi-device-failure", "Independent graphics faults must remain detectable during updates.");
    updating = false;
    AssertTrue(monitor.Poll().Count == 0, "Suppressed update lines must never be replayed after the update.");
    File.AppendAllText(log, "verification failure: update could not be verified\n");
    AssertTrue(monitor.Poll().Single().SignatureId == "verification-failure", "A fresh failure after updating must bypass any suppressed-line cooldown.");
}
finally { Directory.Delete(updateLogRoot, recursive: true); }

// Regression: installation checks seen during real iRacing updates are not failed launches.
const string installationProbe = "CheckIfAntiCheatInstalledForIRacing: AntiCheat is not installed for iRacing";
AssertTrue(IRacingDiagnosticPolicy.IsInstallationProbe(installationProbe), "The reported status-only message must be recognized.");
AssertTrue(IRacingDiagnosticPolicy.IsInstallationProbe("2026-09-11 INFO " + installationProbe.ToUpperInvariant()), "Timestamp prefixes and casing must not change probe handling.");
AssertFalse(IRacingDiagnosticPolicy.IsInstallationProbe(installationProbe + "; simulator launch failed"), "Additional launch failure evidence must not be discarded as a status check.");
AssertFalse(IRacingDiagnosticPolicy.IsInstallationProbe("Launch failed: AntiCheat is not installed for iRacing"), "An actual missing-installation launch failure must remain detectable.");
foreach (var signature in new[] { "eac-failure", "eac-error-73", "eac-error-10011" })
{
    AssertTrue(IRacingUpdateGuard.SuppressDiagnostic(signature, true), "EAC startup checks during updates must be suppressed.");
    AssertFalse(IRacingUpdateGuard.SuppressDiagnostic(signature, false), "Post-update EAC failures must remain detectable.");
}
var eacLogRoot = Path.Combine(Path.GetTempPath(), "PitMedic-eac-update-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(eacLogRoot);
try
{
    var log = Path.Combine(eacLogRoot, "main-ui.log");
    File.WriteAllText(log, "");
    var updating = false;
    var clock = now;
    var monitor = new IRacingLiveLogMonitor(() => updating, () => new[] { log }, () => clock);
    monitor.StartSession(clock);
    File.AppendAllText(log, installationProbe + "\n");
    AssertTrue(monitor.Poll().Count == 0, "A probe must not create a finding even if the updater process was not observed.");
    updating = true;
    File.AppendAllText(log, installationProbe + "\nEasy Anti-Cheat failed\nError 73\nLaunch error (10011)\n");
    AssertTrue(monitor.Poll().Count == 0, "Update-time EAC lines must all be consumed without a repair finding.");
    updating = false;
    AssertTrue(monitor.Poll().Count == 0, "EAC update lines must not replay when the updater exits.");
    File.AppendAllText(log, "Launch failed: AntiCheat is not installed for iRacing\nError 73\nLaunch error (10011)\n");
    var realFailures = monitor.Poll().Select(f => f.SignatureId).ToArray();
    AssertTrue(realFailures.SequenceEqual(new[] { "eac-failure", "eac-error-73", "eac-error-10011" }), "New post-update failures must remain detectable without suppressed-line cooldowns.");
    clock = now.AddMinutes(2);
    File.WriteAllText(log, installationProbe + "\n");
    AssertTrue(monitor.Poll().Count == 0, "A rotated UI log must not resurrect the probe as a failure.");
    File.AppendAllText(log, "DXGI_ERROR_DEVICE_REMOVED\n");
    AssertTrue(monitor.Poll().Single().SignatureId == "dxgi-device-failure", "An unrelated genuine graphics failure must remain detectable.");
}
finally { Directory.Delete(eacLogRoot, recursive: true); }
var savedProbe = new IncidentRecord
{
    Game = "iRacing",
    Classification = new CrashClassification("Easy Anti-Cheat failure", 90, "Old false positive",
        new[] { new LiveFaultEvidence(now, "eac-failure", "Easy Anti-Cheat failure", installationProbe, "main-ui.log", GameKind.IRacing).ToEvidenceText() }),
    RecommendedRepair = new RepairPlan { Id = "iracing-eac-reinstall" }
};
var reassessedProbe = IRacingDiagnosticPolicy.Reassess(savedProbe);
AssertTrue(reassessedProbe.RecommendedRepair is null, "The obsolete serialized repair must be removed.");
AssertTrue(reassessedProbe.Classification.Category == IRacingDiagnosticPolicy.StatusOnlyCategory, "A saved status-only finding must stop claiming launch failure.");
AssertTrue(reassessedProbe.Classification.Evidence.SequenceEqual(savedProbe.Classification.Evidence), "Reassessment must retain the original evidence.");
AssertTrue(ReferenceEquals(reassessedProbe, IRacingDiagnosticPolicy.Reassess(reassessedProbe)), "Reassessment should be idempotent.");
AssertFalse(IRacingDiagnosticPolicy.IsStatusOnlyFinding(savedProbe with { ExitCode = -1 }), "An abnormal process exit must not be silently reclassified.");
AssertFalse(IRacingDiagnosticPolicy.IsStatusOnlyFinding(savedProbe with { Game = "Le Mans Ultimate" }), "Other simulators must not be affected.");
var corroborated = savedProbe with { Classification = savedProbe.Classification with { Evidence = new[] { savedProbe.Classification.Evidence[0], "Windows Application Error event 1000 matched the issue window." } } };
AssertTrue(ReferenceEquals(corroborated, IRacingDiagnosticPolicy.Reassess(corroborated)), "A finding with independent fault evidence must be preserved.");

using var legacyDrivingStats = JsonDocument.Parse("""
    {"Games":{"IRacing":{"MonitoredSeconds":120,"LastSessionBestLap":{"LapSeconds":90},"BestLaps":{}}}}
    """);
AssertTrue(
    LegacyDrivingStatsPolicy.ContainsDrivingStatsData(legacyDrivingStats.RootElement),
    "An upgraded installation must detect and purge legacy per-simulator activity data.");
using var currentDrivingStats = JsonDocument.Parse("""
    {"MonitoringSince":"2026-09-01T12:00:00Z","SessionsMonitored":4,"AutomaticRepairsResolved":1}
    """);
AssertFalse(
    LegacyDrivingStatsPolicy.ContainsDrivingStatsData(currentDrivingStats.RootElement),
    "Current global usage counters must not trigger the legacy driving-stats migration.");

AssertTrue(
    SimulatorNavigationPolicy.SelectForStatusChange(GameKind.IRacing, wasRunning: false, isRunning: true)
        == GameKind.IRacing,
    "A newly detected simulator must select its simulator page.");
AssertFalse(
    SimulatorNavigationPolicy.SelectForStatusChange(GameKind.IRacing, wasRunning: true, isRunning: false)
        .HasValue,
    "A simulator exit must leave PitMedic on the last running simulator page.");
AssertFalse(
    SimulatorNavigationPolicy.SelectForStatusChange(GameKind.IRacing, wasRunning: true, isRunning: true)
        .HasValue,
    "Repeated running notifications must not keep overriding the user's navigation.");
AssertTrue(
    SimulatorNavigationPolicy.SelectForSessionCompleted(GameKind.IRacing) == GameKind.IRacing,
    "A completed iRacing session must leave the iRacing page selected.");

AssertTrue(
    SimulatorSessionPolicy.OutcomeFor(null) == SimulatorSessionOutcome.NoErrorObserved,
    "A normal simulator exit must be shown as having no observed error.");
var unconfirmedSession = new IncidentRecord
{
    Classification = new CrashClassification("Unconfirmed simulator exit", 45, "Review needed", Array.Empty<string>())
};
AssertTrue(
    SimulatorSessionPolicy.OutcomeFor(unconfirmedSession) == SimulatorSessionOutcome.ReviewNeeded,
    "An ambiguous captured exit must request review instead of claiming an error.");
var failedSession = new IncidentRecord
{
    Classification = new CrashClassification("Application fault", 90, "Error captured", Array.Empty<string>())
};
AssertTrue(
    SimulatorSessionPolicy.OutcomeFor(failedSession) == SimulatorSessionOutcome.ErrorObserved,
    "A classified finding must be shown as an observed error.");

var sessionStoreFolder = Path.Combine(Path.GetTempPath(), $"PitMedic-session-tests-{Guid.NewGuid():N}");
try
{
    var sessionStorePath = Path.Combine(sessionStoreFolder, "last-sessions.json");
    var storedSession = new SimulatorSessionSummary
    {
        Game = GameKind.IRacing,
        Started = now.AddMinutes(-42),
        Ended = now,
        Outcome = SimulatorSessionOutcome.NoErrorObserved
    };
    new SimulatorSessionStore(sessionStorePath).Save(storedSession);
    var reloadedSession = new SimulatorSessionStore(sessionStorePath).Get(GameKind.IRacing);
    AssertTrue(
        reloadedSession == storedSession && reloadedSession.Duration == TimeSpan.FromMinutes(42),
        "The last completed session must survive an app restart with its date, duration, and result intact.");
}
finally
{
    try { if (Directory.Exists(sessionStoreFolder)) Directory.Delete(sessionStoreFolder, true); } catch { }
}

AssertTrue(
    RepairKnowledgeBase.Entries.Count == 53,
    "Every simulator repair implemented for this release must have a formal knowledge record.");
AssertTrue(
    RepairKnowledgeBase.DiagnosticLibraryUrlForPlan("ams2-controller-reset")
        == "https://pitmedic.com/diagnostic-library/ams2-controller-config/",
    "App repair aliases must deep-link to their canonical Diagnostic Library page.");
AssertTrue(
    RepairKnowledgeBase.DiagnosticLibraryUrlForPlan("companion-moza-clean-recovery")
        == "https://pitmedic.com/diagnostic-library/companion-moza-clean-recovery/",
    "Companion recovery plans must deep-link to their Diagnostic Library page.");

AssertTrue(
    CompanionRecoveryPolicy.Supported.Count == CompanionSoftwareDefinition.Supported.Count,
    "Every monitored companion app must have one vendor-specific recovery policy.");
AssertTrue(
    CompanionRecoveryPolicy.Supported.Select(item => item.RepairId).Distinct(StringComparer.OrdinalIgnoreCase).Count()
        == CompanionRecoveryPolicy.Supported.Count,
    "Companion recovery identifiers must be unique.");
var gHubRecovery = CompanionRecoveryPolicy.For(CompanionSoftwareKind.LogitechGHub);
AssertTrue(
    gHubRecovery.WindowsServiceName.Equals("LGHUBUpdaterService", StringComparison.Ordinal)
    && gHubRecovery.RequiresElevation
    && ElevatedRepairPolicy.RequiresElevation(gHubRecovery.RepairId),
    "G HUB loading-loop recovery must restart only its allowlisted updater service with elevation.");

var classifier = new CrashClassifier();
var cleanLmuEvidence = ExitLogEvidencePolicy.Normalize(GameKind.LeMansUltimate, new CollectedEvidence(
    LogFiles: 1,
    DumpFiles: 0,
    CrashHints: new[] { "trace.txt contains strong failure marker 'error decompressing file'." },
    CleanExitDetected: true,
    CleanExitHints: new[]
    {
        "trace.txt contains clean shutdown marker 'Executing NAV_EXIT'.",
        "trace.txt contains clean shutdown marker 'Entered Game::Exit()'.",
        "trace.txt contains clean shutdown marker 'Entered OSMan::Exit()'."
    },
    AffectedInstalledContent: new[] { @"Locations\Silverstone_2025" },
    RepairSignatureIds: new[] { "lmu-content-corruption" }));
var cleanLmuClassification = classifier.Classify(
    GameDefinition.Supported.Single(game => game.Kind == GameKind.LeMansUltimate),
    0,
    Array.Empty<WindowsEventEvidence>(),
    Array.Empty<TelemetrySample>(),
    cleanLmuEvidence);
AssertTrue(
    cleanLmuClassification.Category.Equals("Normal simulator exit", StringComparison.Ordinal)
    && cleanLmuEvidence.AffectedInstalledContent.Count == 0
    && cleanLmuEvidence.RepairSignatureIds.Count == 0,
    "A clean LMU shutdown must suppress an isolated content warning and targeted repair prompt.");

AssertTrue(
    LmuLogEvidenceParser.ExtractAffectedInstalledContent(
        @"error decompressing file C:\Games\LMU\Installed\Locations\Silverstone_2025\Silverstone.mas").Count == 1,
    "A same-line LMU content read failure must remain detectable.");
AssertTrue(
    LmuLogEvidenceParser.ExtractAffectedInstalledContent(
        "error loading mesh from an optional package\r\nLoaded C:\\Games\\LMU\\Installed\\Locations\\Silverstone_2025 successfully").Count == 0,
    "An LMU error must not be joined to an unrelated installed-content path on another log line.");

var failedLmuEvidence = ExitLogEvidencePolicy.Normalize(GameKind.LeMansUltimate, new CollectedEvidence(
    LogFiles: 1,
    DumpFiles: 0,
    CrashHints: new[] { "trace.txt contains strong failure marker 'error decompressing file'." },
    CleanExitDetected: false,
    CleanExitHints: Array.Empty<string>(),
    AffectedInstalledContent: new[] { @"Locations\Silverstone_2025" },
    RepairSignatureIds: new[] { "lmu-content-corruption" }));
AssertTrue(
    classifier.Classify(
        GameDefinition.Supported.Single(game => game.Kind == GameKind.LeMansUltimate),
        1,
        Array.Empty<WindowsEventEvidence>(),
        Array.Empty<TelemetrySample>(),
        failedLmuEvidence).Category.Equals("LMU content read / decompression failure", StringComparison.Ordinal),
    "A content failure without LMU clean-shutdown evidence must remain actionable.");

foreach (var game in GameDefinition.Supported.Where(game => game.Kind != GameKind.LeMansUltimate))
{
    var archivedWarning = ExitLogEvidencePolicy.Normalize(game.Kind, new CollectedEvidence(
        LogFiles: 1,
        DumpFiles: 0,
        CrashHints: new[] { "A copied rolling log contains an old fatal error." },
        CleanExitDetected: false,
        CleanExitHints: Array.Empty<string>(),
        AffectedInstalledContent: Array.Empty<string>(),
        RepairSignatureIds: Array.Empty<string>()));
    var classification = classifier.Classify(
        game,
        0,
        Array.Empty<WindowsEventEvidence>(),
        Array.Empty<TelemetrySample>(),
        archivedWarning);
    AssertTrue(
        classification.Category.Equals("Normal simulator exit", StringComparison.Ordinal),
        $"A copied {game.DisplayName} log tail must not turn a successful exit into a finding.");
}

var currentAccFault = new LiveFaultEvidence(
    now,
    "acc-engine-config",
    "ACC graphics device failure",
    "DXGI_ERROR_DEVICE_REMOVED",
    "AC2.log",
    GameKind.AssettoCorsaCompetizione);
var accDefinition = GameDefinition.Supported.Single(game => game.Kind == GameKind.AssettoCorsaCompetizione);
AssertTrue(
    classifier.Classify(
        accDefinition,
        0,
        Array.Empty<WindowsEventEvidence>(),
        Array.Empty<TelemetrySample>(),
        ExitLogEvidencePolicy.Normalize(accDefinition.Kind, new CollectedEvidence(
            1, 0, new[] { "Old fatal error in copied log." }, false,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>())),
        new[] { currentAccFault }).Category.Equals("ACC graphics device failure", StringComparison.Ordinal),
    "Current-session live fault evidence must remain actionable even when the process later exits successfully.");

AssertFalse(
    SimulatorLiveLogMonitor.MatchLine(GameKind.AssettoCorsaEvo, "Loading Video.VideoSettings profile") is not null,
    "An ordinary Assetto Corsa EVO settings line must not be treated as a fault.");
AssertFalse(
    SimulatorLiveLogMonitor.MatchLine(GameKind.RaceRoom, "ShaderCache initialized successfully") is not null,
    "An ordinary RaceRoom shader-cache line must not be treated as a fault.");
AssertFalse(
    SimulatorLiveLogMonitor.MatchLine(GameKind.RaceRoom, "BrowserData directory opened") is not null,
    "An ordinary RaceRoom browser-data line must not be treated as a fault.");
AssertTrue(
    string.Equals(
        SimulatorLiveLogMonitor.MatchLine(GameKind.RaceRoom, "HTTP 503 Service Unavailable")?.Id,
        "raceroom-browser-cache",
        StringComparison.Ordinal),
    "A real RaceRoom HTTP 503 failure must remain detectable.");

SteamValidationWindowTests.Run();

Console.WriteLine("PitMedic release policy tests passed.");

static void AssertTrue(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message) => AssertTrue(!value, message);
