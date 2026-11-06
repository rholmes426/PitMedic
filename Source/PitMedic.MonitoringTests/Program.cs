using System.Reflection;
using PitMedic.Models;
using PitMedic.Services;

// No simulator or Steam process is launched. All log fixtures stay in a unique temp directory.
var root = Path.Combine(Path.GetTempPath(), "PitMedic-monitor-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(Path.Combine(root, "logs"));
var log = Path.Combine(root, "logs", "eos_anticheat_errors.txt");
var oldError = "Unknown file version (tracks/oldtrack/oldtrack.dat)\n";
File.WriteAllText(log, oldError);
var candidates = typeof(IRacingLiveLogMonitor).GetMethod("CandidateFiles", BindingFlags.Static | BindingFlags.NonPublic,
    null, new[] { typeof(string) }, null)!;
var updating = false;
var monitor = new IRacingLiveLogMonitor(() => updating,
    () => (IEnumerable<string>)candidates.Invoke(null, new object[] { root })!);
var passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception("FAIL: " + description);
    Console.WriteLine("PASS: " + description);
    passed++;
}
monitor.StartSession(DateTimeOffset.Now);
Check(monitor.Poll().Count == 0, "Existing cumulative EOS errors are not replayed");
File.AppendAllText(log, "========== ERROR 73 ==========\n2026-11-05 20:35:01:\nUnknown file version (tracks\\sebring\\sebring.dat)\n");
var faults = monitor.Poll();
Check(faults.Count == 1 && faults[0].SignatureId == "track-file-version", "EOS txt file is monitored and specific track rejection supersedes Error 73");
Check(faults[0].Message.Contains("tracks\\sebring\\sebring.dat"), "Rejected file path is retained in evidence");
Check(monitor.Poll().Count == 0, "Unchanged log does not produce duplicate incidents");
File.AppendAllText(log, "Unknown file version (tracks\\sebring\\sebring.dat)\n");
Check(monitor.Poll().Count == 0, "Repeated EOS callback is deduplicated");
var empty = new CollectedEvidence(0, 0, Array.Empty<string>(), false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
var game = GameDefinition.Supported.First(g => g.Kind == GameKind.IRacing);
var generic = new LiveFaultEvidence(DateTimeOffset.Now, "eac-error-73", "Easy Anti-Cheat Error 73", "ERROR 73", "eos_anticheat_errors.txt", GameKind.IRacing);
var combined = new[] { generic, faults[0] };
var classification = new CrashClassifier().Classify(game, 0, Array.Empty<WindowsEventEvidence>(), Array.Empty<TelemetrySample>(), empty, combined);
Check(classification.Category == "iRacing track content version rejected", "Earlier generic Error 73 cannot override content diagnosis after normal exit");
Check(classification.Evidence.Any(e => e.Contains("sebring.dat")), "Normal-exit incident retains rejected path");
var plan = RepairPlanner.Create(game, classification, empty, combined);
var install = IRacingLocator.FindRoot();
var steam = install is not null && IRacingLocator.IsSteamInstall(install);
Check(steam ? plan?.Id == "iracing-steam-verify" && plan.RequiresApproval && plan.SteamAppId == "266410" : plan is null,
    "Content rejection uses approved Steam validation or remains diagnostic-only for direct/unknown installs");
var historicalPlan = RepairPlanner.TryCreateFromIncident(new IncidentRecord
{
    Game = "iRacing", Classification = classification,
    RecommendedRepair = new RepairPlan { Id = "iracing-eac-reinstall" }
});
Check(steam ? historicalPlan?.Id == "iracing-steam-verify" : historicalPlan is null,
    "History revalidation replaces an obsolete anti-cheat reinstall recommendation");
var clean = new CrashClassifier().Classify(game, 0, Array.Empty<WindowsEventEvidence>(), Array.Empty<TelemetrySample>(), empty);
Check(clean.Category == "Normal simulator exit", "Clean sessions remain normal");
File.WriteAllText(log, "Unknown file version (cars/testcar/testcar.dat)\n");
Check(monitor.Poll().Single().SignatureId == "car-file-version", "Shorter rotated log and forward-slash car paths are handled");
File.AppendAllText(log, "Unknown file version (engine.dll)\n");
Check(monitor.Poll().Single().SignatureId == "unknown-file-version", "Other rejected files are not misdiagnosed as EAC installation failures");
monitor.StartSession(DateTimeOffset.Now);
Check(monitor.Poll().Count == 0, "Next session baselines old content errors");
File.WriteAllText(Path.Combine(root, "logs", "sim_launch.txt"), "ERROR 73\n");
Check(monitor.Poll().Single().SignatureId == "eac-error-73", "New sim_launch file retains generic Error 73 detection");
File.AppendAllText(log, "Warning/LogEOSAntiCheat [AntiCheatClient] Calling the ClientIntegrityViolated callback with violation information.\n");
Check(monitor.Poll().Count == 0, "Callback boilerplate alone does not manufacture a diagnosis");
updating = true;
File.AppendAllText(log, "Unknown file version (tracks/sebring/sebring.dat)\n");
Check(monitor.Poll().Count == 0, "Content errors are suppressed while iRacing is updating");
updating = false;
Check(monitor.Poll().Count == 0, "Update-time errors are consumed rather than replayed afterward");
File.AppendAllText(log, "Unknown file version (tracks/sebring/sebring.dat)\n");
Check(monitor.Poll().Single().SignatureId == "track-file-version", "A fresh rejection after updating remains detectable");
// Cleanup is restricted to this randomly generated test directory.
Directory.Delete(root, recursive: true);
Console.WriteLine($"{passed} regression checks passed.");
