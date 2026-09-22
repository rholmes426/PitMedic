using System.Text.Json;
using PitMedic.Models;
using PitMedic.Services;

internal static class IRacingRepairPlanTests
{
    internal static void Run()
    {
        var game = GameDefinition.Supported.Single(g => g.Kind == GameKind.IRacing);
        var collected = new CollectedEvidence(0, 0, [], false, [], [], []);
        // Synthetic reproduction: no user logs, personal paths or incident identifiers.
        const string reset = """{"name":"iracing-electron","msg":"socket Error: read ECONNRESET"}""";
        var connection = new LiveFaultEvidence(DateTimeOffset.UnixEpoch, "connection-failure",
            "Session connection failure", reset, "main-ui.log", GameKind.IRacing);
        Check("Application fault", [connection], "iracing-windows-integrity");
        Check("Application fault", [], "iracing-windows-integrity", [reset]);
        Check("Session connection failure", [connection], null);
        Check("Unknown", [], null, [reset]);
        Check("Unknown", [], null, ["iracing-electron"]);
        Check("UI startup failure", [], "iracing-ui-cache");
        Check("Unknown", [], "iracing-ui-cache", ["UI shows a white screen"]);
        Check("Unknown", [], "iracing-ui-cache", ["UI shows a black screen"]);
        Check("Unknown", [], "iracing-ui-cache", ["UI cache is damaged"]);
        foreach (var (signature, expected) in new[]
        {
            ("ui-render-failure", "iracing-ui-cache"),
            ("eac-error-73", "iracing-eac-reinstall"),
            ("missing-file-privileges", "iracing-release-file-privileges"),
            ("ui-welcome", "iracing-ui-safe"),
            ("helper-service", "iracing-helper-service")
        })
        {
            var fault = connection with { SignatureId = signature, Message = "specific diagnostic" };
            Check("Application fault", [fault], expected);
            // An unmapped secondary error must not mask a later supported signature.
            Check("Application fault", [connection, fault], expected);
        }
        var status = new IncidentRecord
        {
            Game = "iRacing", ExitCode = 0,
            Classification = new("Easy Anti-Cheat failure", 80, "status only",
                ["CheckIfAntiCheatInstalledForIRacing: AntiCheat is not installed for iRacing"]),
            RecommendedRepair = new RepairPlan { Id = "iracing-eac-reinstall" }
        };
        Expect(RepairPlanner.TryCreateFromIncident(status), null, "Status-only findings stay diagnostic-only");
        Console.WriteLine("iRacing repair selection regression tests passed (20 scenarios).");

        void Check(string category, LiveFaultEvidence[] live, string? expected, string[]? extra = null)
        {
            var evidence = (extra ?? []).Concat(live.Select(f => f.ToEvidenceText())).ToArray();
            var classification = new CrashClassification(category, 88, "Synthetic finding", evidence);
            var initial = RepairPlanner.Create(game, classification, collected, live);
            Expect(initial, expected, "Capture");
            var saved = new IncidentRecord { Game = "iRacing", Classification = classification, RecommendedRepair = initial };
            // Exercise the real serialized incident roundtrip and helper's untrusted-plan removal.
            var loaded = JsonSerializer.Deserialize<IncidentRecord>(JsonSerializer.Serialize(saved))!;
            Expect(RepairPlanner.TryCreateFromIncident(loaded), expected, "History");
            Expect(RepairPlanner.TryCreateFromIncident(loaded with { RecommendedRepair = null }), expected, "Helper reconstruction");
            Expect(RepairPlanner.TryCreateFromIncident(loaded with
                { RecommendedRepair = new RepairPlan { Id = "iracing-ui-cache" } }), expected, "Stale cache plan");
            Expect(RepairPlanner.TryCreateFromIncident(loaded with
                { RecommendedRepair = new RepairPlan { Id = "untrusted-arbitrary-plan" } }), expected, "Untrusted saved plan");
            if (expected == "iracing-windows-integrity"
                && (initial?.RequiresApproval != true || !ElevatedRepairPolicy.RequiresElevation(initial.Id)))
                throw new InvalidOperationException("Windows repair must retain approval and elevation requirements.");
        }
    }

    private static void Expect(RepairPlan? actual, string? expected, string context)
    {
        if (actual?.Id != expected)
            throw new InvalidOperationException($"{context}: expected {expected ?? "no repair"}, got {actual?.Id ?? "no repair"}.");
    }
}
