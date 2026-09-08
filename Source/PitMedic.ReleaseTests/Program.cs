using PitMedic.Models;
using PitMedic.Services;
using System.Text.Json;

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

Console.WriteLine("PitMedic release policy tests passed.");

static void AssertTrue(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message) => AssertTrue(!value, message);
