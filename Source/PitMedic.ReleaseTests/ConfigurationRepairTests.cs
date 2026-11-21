using System.Text;
using System.Text.Json.Nodes;
using PitMedic.Models;
using PitMedic.Services;

internal static class ConfigurationRepairTests
{
    private static int _checks;
    private static void Check(bool value, string message) { _checks++; if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action, string message)
    {
        _checks++;
        try { action(); } catch (IOException) { return; } catch (InvalidDataException) { return; } catch (UnauthorizedAccessException) { return; }
        throw new InvalidOperationException(message);
    }

    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "PitMedic-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ini = "[Other]\r\nconnect_sockets=0\r\n[Communications]\r\n  connect_sockets = 0  ; keep note\r\n[After]\r\nkey=2\r\n";
            var enabled = Encoding.UTF8.GetString(ScopedConfigurationRepair.EnableSocketConnection(Encoding.UTF8.GetBytes(ini)));
            Check(enabled == ini.Replace("  connect_sockets = 0", "  connect_sockets = 1"), "Only the Communications key may change.");
            foreach (var encoding in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
            {
                var raw = encoding.GetPreamble().Concat(encoding.GetBytes(ini + "; café\r\n")).ToArray();
                var next = ScopedConfigurationRepair.EnableSocketConnection(raw);
                Check(next.SequenceEqual(encoding.GetPreamble().Concat(encoding.GetBytes((ini + "; café\r\n").Replace("  connect_sockets = 0", "  connect_sockets = 1")))),
                    "BOM, encoding, comments and line endings must survive.");
            }
            foreach (var invalid in new[]
            {
                "[Other]\nconnect_sockets=0\n",
                "[Communications]\nconnect_sockets=2\n",
                "[Communications]\nconnect_sockets=0\nconnect_sockets=1\n",
                "[Communications]\nconnect_sockets=0\n[Communications]\nconnect_sockets=1\n",
                "[Communications]\n; connect_sockets=0\n"
            }) Reject(() => ScopedConfigurationRepair.EnableSocketConnection(Encoding.UTF8.GetBytes(invalid)), "Ambiguous or unsupported settings must stop.");

            var docs = Path.Combine(root, "Redirected Documents", "iRacing");
            Directory.CreateDirectory(docs);
            var core = Path.Combine(docs, "core.ini");
            File.WriteAllBytes(core, Encoding.UTF8.GetBytes(ini));
            var backups = Path.Combine(root, "Backups");
            var first = Path.Combine(backups, "first");
            ScopedConfigurationRepair.Execute("iracing-socket-connection", docs, first, backups, CancellationToken.None);
            Check(File.ReadAllText(core) == enabled, "The selected socket edit must be applied.");
            Check(File.ReadAllBytes(Path.Combine(first, "Files", "core.ini")).SequenceEqual(Encoding.UTF8.GetBytes(ini)), "The original must be preserved byte-for-byte.");
            var changed = enabled + "; later user settings\n";
            File.WriteAllText(core, changed);
            var restore = Path.Combine(backups, "restore");
            ScopedConfigurationRepair.Execute("iracing-socket-connection-restore", docs, restore, backups, CancellationToken.None);
            Check(File.ReadAllText(core) == ini, "Restore must recover the original.");
            Check(File.ReadAllText(Path.Combine(restore, "Files", "core.ini")) == changed, "Restore must preserve the current settings before replacing them.");

            File.WriteAllText(core, enabled);
            var noop = Path.Combine(backups, "noop");
            ScopedConfigurationRepair.Execute("iracing-socket-connection", docs, noop, backups, CancellationToken.None);
            Check(!File.Exists(Path.Combine(noop, "configuration-backup.json")), "A no-op must not hide the previous useful recovery point.");

            File.WriteAllText(Path.Combine(first, "Files", "core.ini"), "tampered");
            Reject(() => ScopedConfigurationRepair.Execute("iracing-socket-connection-restore", docs, Path.Combine(backups, "badrestore"), backups, CancellationToken.None),
                "A corrupted backup must not be restored.");
            Check(File.ReadAllText(core) == enabled, "A failed integrity check must leave the current file intact.");

            var player = Path.Combine(root, "LMU", "UserData", "player");
            Directory.CreateDirectory(player);
            foreach (var name in new[] { "direct input.json", "current controls.json", "keyboard.json" })
                File.WriteAllText(Path.Combine(player, name), "{\"binding\":\"keep\"}");
            File.WriteAllText(Path.Combine(player, "setup.json"), "unrelated");
            var lmuBackup = Path.Combine(backups, "lmu");
            ScopedConfigurationRepair.Execute("lmu-controller-config", player, lmuBackup, backups, CancellationToken.None);
            Check(!File.Exists(Path.Combine(player, "current controls.json")) && File.ReadAllText(Path.Combine(player, "setup.json")) == "unrelated",
                "Only the official allowlisted files may be reset.");
            File.WriteAllText(Path.Combine(player, "current controls.json"), "{\"new\":true}");
            ScopedConfigurationRepair.Execute("lmu-controller-config-restore", player, Path.Combine(backups, "lmu-restore"), backups, CancellationToken.None);
            Check(File.ReadAllText(Path.Combine(player, "current controls.json")) == "{\"binding\":\"keep\"}", "LMU Restore must recover old bindings.");
            Check(File.ReadAllText(Path.Combine(backups, "lmu-restore", "Files", "current controls.json")) == "{\"new\":true}", "Regenerated controls must be preserved on Restore.");
            Check(!File.Exists(Path.Combine(player, "gamepad.json")), "Originally absent optional files must not be invented.");

            var app = Path.Combine(docs, "app.ini");
            File.WriteAllText(app, "[SPCC]\nvoice=custom\n");
            File.WriteAllText(Path.Combine(docs, "setups.txt"), "keep");
            ScopedConfigurationRepair.Execute("iracing-spotter-loading-error-63", docs, Path.Combine(backups, "spotter"), backups, CancellationToken.None);
            Check(!File.Exists(app) && File.ReadAllText(Path.Combine(docs, "setups.txt")) == "keep", "Error 63 must not reset the whole user profile.");
            ScopedConfigurationRepair.Execute("iracing-spotter-loading-error-63-restore", docs, Path.Combine(backups, "spotter-restore"), backups, CancellationToken.None);
            Check(File.ReadAllText(app).Contains("voice=custom"), "Error 63 settings must be restorable.");

            var beforeCancel = File.ReadAllBytes(core);
            using (var cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                try { ScopedConfigurationRepair.Execute("iracing-socket-connection", docs, Path.Combine(backups, "cancel"), backups, cancel.Token); }
                catch (OperationCanceledException) { }
            }
            Check(File.ReadAllBytes(core).SequenceEqual(beforeCancel), "Cancellation must not mutate the file.");
            File.WriteAllText(core, ini);
            var beforeLocked = File.ReadAllBytes(core);
            using (var held = new FileStream(core, FileMode.Open, FileAccess.Read, FileShare.Read))
                Reject(() => ScopedConfigurationRepair.Execute("iracing-socket-connection", docs, Path.Combine(backups, "locked"), backups, CancellationToken.None),
                    "An in-use file must not be overwritten.");
            Check(File.ReadAllBytes(core).SequenceEqual(beforeLocked), "An in-use-file failure must preserve bytes.");

            var plan = ScopedConfigurationRepair.CreatePlan(GameKind.IRacing, "iracing-socket-connection")!;
            Check(plan.RequiresApproval && plan.Safety == RepairSafety.Significant, "New setting tests always require consent.");
            Check(ScopedConfigurationRepair.CreatePlan(GameKind.LeMansUltimate, plan.Id) is null, "Cross-game requests must be rejected.");
            var record = new IncidentRecord { Game = "iRacing", Classification = new(ScopedConfigurationRepair.RequestCategory, 100, "User selected symptom",
                new[] { ScopedConfigurationRepair.RequestPrefix + plan.Id }) };
            Check(RepairPlanner.TryCreateFromIncident(record)?.Id == plan.Id, "Manual requests must survive history/helper reconstruction.");
            Check(ScopedConfigurationRepair.FromRequest(record with { Game = "Le Mans Ultimate" }) is null, "Forged cross-game manual evidence must fail.");

            var evidence = new[] { "Loading Error 77: tracks\\monza\\track.dat" };
            var content = IRacingLoadingError77Policy.Create(evidence, false);
            Check(content.AffectedContentRelativePaths.SequenceEqual(new[] { "tracks/monza" }), "Error 77 must select only the named content.");
            foreach (var unsafeLine in new[] { "Loading Error 77", "Loading Error 77 tracks/../Windows", "Loading Error 77 tracks/a cars/b" })
            {
                var fallback = IRacingLoadingError77Policy.Create(new[] { unsafeLine }, false);
                Check(fallback.AffectedContentRelativePaths.Count == 0 && fallback.Id == "iracing-error77-verify", "Ambiguous Error 77 must not remove guessed content.");
            }
            Check(!IRacingLoadingError77Policy.IsError("Loading Error 770"), "Error numbers must match completely.");
            var install = Path.Combine(root, "install");
            Directory.CreateDirectory(Path.Combine(install, "tracks", "monza"));
            Check(IRacingLoadingError77Policy.ValidateTarget(install, "tracks/monza").EndsWith("monza"), "A valid content target must resolve.");
            Reject(() => IRacingLoadingError77Policy.ValidateTarget(install, "tracks/../Windows"), "Content traversal must be rejected.");

            var log = Path.Combine(root, "session.log");
            File.WriteAllText(log, "Loading Error 63\n");
            var now = DateTimeOffset.Now;
            var monitor = new IRacingLiveLogMonitor(() => false, () => new[] { log }, () => now);
            monitor.StartSession(now);
            Check(monitor.Poll().Count == 0, "Old errors must not be replayed.");
            File.AppendAllText(log, "Loading Error 63\nError 10057\nLoading Error 77: tracks/monza/track.dat\n");
            var faults = monitor.Poll();
            Check(faults.Select(x => x.SignatureId).Order().SequenceEqual(new[] { "loading-error-63", "loading-error-77", "socket-connection-error" }.Order()),
                "The real live monitor must identify all three exact errors.");
            var busy = true;
            var updateLog = Path.Combine(root, "updating.log");
            File.WriteAllText(updateLog, "");
            var updatingMonitor = new IRacingLiveLogMonitor(() => busy, () => new[] { updateLog }, () => now);
            updatingMonitor.StartSession(now);
            File.AppendAllText(updateLog, "Loading Error 63\nError 10057\nLoading Error 77\n");
            Check(updatingMonitor.Poll().Count == 0, "Update-time errors must not create repair alerts.");
            busy = false;
            Check(updatingMonitor.Poll().Count == 0, "Update-time errors must be consumed rather than replayed.");
            File.AppendAllText(updateLog, "Loading Error 630\nError 100570\nLoading Error 770\n");
            Check(updatingMonitor.Poll().Count == 0, "Unrelated error-number prefixes must not route to these repairs.");
            var game = GameDefinition.Supported.Single(x => x.Kind == GameKind.IRacing);
            foreach (var fault in faults.Where(x => x.SignatureId != "loading-error-77"))
            {
                var selected = RepairPlanner.Create(game, new(fault.Category, 90, fault.Message, new[] { fault.ToEvidenceText() }),
                    new(0, 0, Array.Empty<string>(), false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), new[] { fault });
                Check(selected?.Id == (fault.SignatureId == "loading-error-63" ? "iracing-spotter-loading-error-63" : "iracing-socket-connection"),
                    "Live faults must reach the exact executable repair.");
            }
            var startupLog = Path.Combine(root, "startup.log");
            File.WriteAllText(startupLog, "Loading Error 63\n");
            var dialogVisible = false;
            var clock = now;
            var startup = new IRacingLiveLogMonitor(() => busy, () => new[] { startupLog }, () => clock, () => dialogVisible);
            startup.StartSession(now);
            File.AppendAllText(startupLog, "System Not in Service\nLoading Err");
            Check(startup.Poll().Single().SignatureId == "helper-service", "A split log entry stays pending until complete.");
            clock = clock.AddSeconds(2);
            startup.BeginTrackedSession(clock);
            File.AppendAllText(startupLog, "or (63)\n");
            var launchFault = startup.Poll().Single();
            Check(launchFault.SignatureId == IRacingLiveLogMonitor.SpotterErrorSignature,
                "Exact startup errors written during attachment must survive the log-monitor handoff.");
            dialogVisible = true;
            Check(startup.Poll().Count == 0, "A matching native dialog and log must produce only one Error 63 finding.");
            Check(startup.Poll(simulatorExited: true).Count == 0, "A previously detected error must not be replayed when the simulator closes.");

            var dialogOnly = new IRacingLiveLogMonitor(() => busy, () => Array.Empty<string>(), () => clock, () => dialogVisible);
            dialogOnly.StartSession(clock);
            var dialogFault = dialogOnly.Poll().Single();
            Check(dialogFault.SourceFile == "iRacing dialog" && dialogFault.SignatureId == IRacingLiveLogMonitor.SpotterErrorSignature,
                "An exact native iRacing dialog can produce a finding without a log or a process exit.");
            clock = clock.AddMinutes(2);
            Check(dialogOnly.Poll().Count == 0, "Leaving the same dialog open must not repeatedly alert.");
            dialogVisible = false;
            dialogOnly.Poll();
            busy = true;
            dialogVisible = true;
            Check(dialogOnly.Poll().Count == 0, "A dialog observed during an update must not offer a repair.");
            busy = false;
            Check(dialogOnly.Poll().Count == 0, "An update-time dialog must not replay when the update ends.");
            Check(!IRacingLiveLogMonitor.IsLoadingError63("Loading Error 630") && !IRacingLiveLogMonitor.IsLoadingError63("Error 63"),
                "The dialog must identify the entire Loading Error 63 code.");

            var simultaneous = new IRacingLiveLogMonitor(() => false, () => new[] { startupLog }, () => clock);
            simultaneous.StartSession(now);
            File.AppendAllText(startupLog, "System Not in Service\nLoading Error: 63\n");
            Check(simultaneous.Poll().Single().SignatureId == IRacingLiveLogMonitor.SpotterErrorSignature,
                "A generic service error in the same log poll must not displace Error 63.");

            var processOnly = new LiveFaultEvidence(now, "helper-service", "iRacing Helper Service failure",
                IRacingDiagnosticPolicy.HelperProcessAbsent, "process-state", GameKind.IRacing);
            var explicitService = processOnly with { Message = "System Not in Service", SourceFile = "main-ui.log" };
            IncidentRecord WithEvidence(params LiveFaultEvidence[] events) => new()
            {
                Game = "iRacing",
                Classification = new CrashClassification(processOnly.Category, 90, processOnly.Message,
                    events.Select(fault => fault.ToEvidenceText()).ToArray())
            };
            Check(RepairPlanner.TryCreateFromIncident(WithEvidence(processOnly)) is { Id: "iracing-helper-service", RequiresApproval: true },
                "The process-only fallback must ask before restarting the service.");
            Check(RepairPlanner.TryCreateFromIncident(WithEvidence(explicitService)) is { Id: "iracing-helper-service", RequiresApproval: false },
                "A clear service failure retains its confirmed service route.");
            Check(RepairPlanner.TryCreateFromIncident(WithEvidence(processOnly, dialogFault))
                is { Id: "iracing-spotter-loading-error-63", RequiresApproval: true },
                "Exact Error 63 evidence overrides a service finding and selects only the backed-up app.ini recovery.");

            var detailFolder = Path.Combine(root, "detail-record");
            Directory.CreateDirectory(detailFolder);
            var detailRecord = record with { IncidentFolder = detailFolder, RecommendedRepair = plan };
            File.WriteAllText(Path.Combine(detailFolder, "repair.json"), System.Text.Json.JsonSerializer.Serialize(new { plan, state = new { IsComplete = true, Success = true, Message = "Setting changed; retest." } }));
            var details = IncidentDetailsService.Build(detailRecord);
            Check(details.NeedsRetest && details.OutcomeHeadline.Contains("retest"), "A file operation must not claim that the symptom was resolved.");
            Check(RepairKnowledgeBase.DiagnosticLibraryUrlForPlan("lmu-controller-config-restore")?.EndsWith("/lmu-controller-config/") == true,
                "Restore must link to the same canonical guide.");
        }
        finally
        {
            if (Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
        Console.WriteLine($"Configuration repair regression checks passed ({_checks}).");
    }
}
