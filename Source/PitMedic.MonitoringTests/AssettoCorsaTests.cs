using System.Reflection;
using System.Text.Json;
using PitMedic.Models;
using PitMedic.Services;

internal static class AssettoCorsaTests
{
    public static void Run()
    {
        var count = 0;
        void Check(bool ok, string message)
        {
            if (!ok) throw new Exception("Assetto Corsa: " + message);
            count++;
        }
        var game = GameDefinition.Supported.Single(x => x.Kind == GameKind.AssettoCorsa);
        Check(GameDefinition.Supported.Count == 7, "Seven distinct simulators are configured.");
        Check((int)GameKind.Automobilista2 == 5 && (int)GameKind.RaceRoom == 3 && (int)GameKind.AssettoCorsa == 6,
            "Existing persisted enum values are unchanged.");
        Check(game.DisplayName == "Assetto Corsa" && game.ProcessNames.SequenceEqual(new[] { "acs", "acs_x86" }),
            "Only the original 64/32-bit race processes start sessions.");
        Check(!game.ProcessNames.Contains("AssettoCorsa") && !game.ProcessNames.Contains("Content Manager"),
            "Launcher/Content Manager lifetime is separate from racing.");
        Check(JsonSerializer.Deserialize<AppSettings>("{\"MonitorIRacing\":false}")!.MonitorAssettoCorsa,
            "Older settings enable the new monitor without losing their prior settings.");
        Check(JsonSerializer.Deserialize<AppSettings>("{\"MonitorAssettoCorsa\":false}")!.MonitorAssettoCorsa == false,
            "The new monitor opt-out is persisted.");
        Check(AssettoCorsaPolicy.MatchesEngineEvidence(@"Faulting application C:\Games\AC\acs_x86.exe, version 1.16"), "32-bit race faults match.");
        Check(AssettoCorsaPolicy.MatchesEngineEvidence("Faulting application: acs.exe"), "64-bit race faults match.");
        foreach (var wrong in new[] { "AssettoCorsa.exe", "AssettoCorsaEVO.exe", "AC2-Win64-Shipping.exe", "spacemacs.exe", "acs.exe.other" })
            Check(!AssettoCorsaPolicy.MatchesEngineEvidence(wrong), "Unrelated/launcher faults are not original race faults: " + wrong);
        Check(AssettoCorsaPolicy.IsEngineDumpFile("acs.exe.1234.dmp") && AssettoCorsaPolicy.IsEngineDumpFile("acs_x86.exe.5678.dmp"), "Both race dump formats match.");
        Check(!AssettoCorsaPolicy.IsEngineDumpFile("spacemacs.exe.1234.dmp") && !AssettoCorsaPolicy.IsEngineDumpFile("AssettoCorsa.exe.1234.dmp"), "Dump matching excludes launcher and substring collisions.");

        foreach (var line in new[] { "PRINTING VIDEO INI: video.ini", "controls.ini loaded successfully", "unhandled exception in a mod", "video.ini.bak is invalid" })
            Check(AssettoCorsaPolicy.MatchLog(line) is null, "Generic/normal log text does not select a reset: " + line);
        Check(AssettoCorsaPolicy.MatchLog("Cannot parse video.ini")?.Id == "ac-video-config", "Exact video failure matches.");
        Check(AssettoCorsaPolicy.MatchLog("Invalid controls.ini")?.Id == "ac-controls-config", "Exact controls failure matches.");
        Check(AssettoCorsaPolicy.PlanFromEvidence(new[] { "video.ini", "error in unrelated mod" }) is null,
            "Evidence lines are not joined into a manufactured configuration fault.");

        var root = Path.Combine(Path.GetTempPath(), "PitMedic-original-AC-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var log = Path.Combine(root, "log.txt");
            File.WriteAllText(log, "Cannot parse video.ini\n");
            var constructor = typeof(SimulatorLiveLogMonitor).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var live = (ILiveLogMonitor)constructor.Invoke(new object[] { game.Kind,
                new Func<IEnumerable<string>>(() => new[] { log }),
                new Func<string, (string Id, string Category, string Message)?>(AssettoCorsaPolicy.MatchLog) });
            live.StartSession(DateTimeOffset.Now);
            Check(live.Poll().Count == 0, "Cumulative errors from the previous race are baselined.");
            File.AppendAllText(log, "PRINTING VIDEO INI video.ini\nerror loading mod\n");
            Check(live.Poll().Count == 0, "INI diagnostics plus an unrelated mod error are quiet.");
            File.AppendAllText(log, "Cannot parse controls.ini\n");
            var fault = live.Poll().Single();
            Check(fault.Game == game.Kind && fault.SignatureId == "ac-controls-config", "Current original-AC errors retain their game identity.");
            Check(live.Poll().Count == 0, "The same log error is not replayed.");
            var empty = new CollectedEvidence(0, 0, Array.Empty<string>(), false, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
            var classification = new CrashClassifier().Classify(game, 0, Array.Empty<WindowsEventEvidence>(), Array.Empty<TelemetrySample>(), empty, new[] { fault });
            Check(classification.Summary.StartsWith("Assetto Corsa reported") && !classification.Summary.Contains("iRacing"), "Original AC findings name the correct simulator.");
            var plan = RepairPlanner.Create(game, classification, empty, new[] { fault });
            Check(plan?.Id == "ac-controls-config" && plan.RequiresApproval, "The current log selects the exact consented controls repair.");
            var record = new IncidentRecord { Game = game.DisplayName, Classification = classification };
            Check(RepairPlanner.TryCreateFromIncident(record)?.Id == plan!.Id, "History reconstructs the same plan from saved evidence.");
            var generic = new CrashClassification(CrashClassification.ApplicationFault, 90, "Generic failure", Array.Empty<string>());
            Check(RepairPlanner.Create(game, generic, empty) is null, "Generic original-AC crashes do not reset settings.");
            Check(new CrashClassifier().Classify(game, 0, Array.Empty<WindowsEventEvidence>(), Array.Empty<TelemetrySample>(), empty).Category == "Normal simulator exit", "A clean race exit stays normal.");
            Check(new CrashClassifier().Classify(game, 0, new[] { new WindowsEventEvidence("Application", DateTimeOffset.Now, "Application Error", 1000, "Error", "Faulting application spacemacs.exe") }, Array.Empty<TelemetrySample>(), empty).Category == "Normal simulator exit", "An unrelated substring-matching process fault cannot create an AC finding.");

            var cfg = Path.Combine(root, "cfg");
            var backups = Path.Combine(root, "Backups");
            Directory.CreateDirectory(cfg);
            File.WriteAllText(Path.Combine(cfg, "video.ini"), "[VIDEO]\r\nWIDTH=1920\r\n");
            File.WriteAllText(Path.Combine(cfg, "controls.ini"), "[HEADER]\r\nINPUT_METHOD=WHEEL\r\n");
            File.WriteAllText(Path.Combine(cfg, "ff_post_process.ini"), "LUT=custom.lut");
            foreach (var (id, name, other) in new[] { ("ac-video-config", "video.ini", "controls.ini"), ("ac-controls-config", "controls.ini", "video.ini") })
            {
                var original = File.ReadAllBytes(Path.Combine(cfg, name));
                var unrelated = File.ReadAllBytes(Path.Combine(cfg, other));
                ScopedConfigurationRepair.Execute(id, cfg, Path.Combine(backups, id), backups, CancellationToken.None);
                Check(!File.Exists(Path.Combine(cfg, name)) && File.ReadAllBytes(Path.Combine(cfg, other)).SequenceEqual(unrelated), "Reset affects only its allowlisted INI: " + id);
                File.WriteAllText(Path.Combine(cfg, name), "regenerated settings");
                ScopedConfigurationRepair.Execute(id + "-restore", cfg, Path.Combine(backups, id + "-restore"), backups, CancellationToken.None);
                Check(File.ReadAllBytes(Path.Combine(cfg, name)).SequenceEqual(original), "Restore recovers the original bytes: " + id);
                Check(File.ReadAllText(Path.Combine(backups, id + "-restore", "Files", name)) == "regenerated settings", "Restore preserves the replacement settings: " + id);
            }
            Check(File.ReadAllText(Path.Combine(cfg, "ff_post_process.ini")) == "LUT=custom.lut", "Custom FFB data is untouched.");
            var options = UserRequestedRepairPolicy.OptionsFor(game.Kind);
            Check(options.Select(x => x.Id).Order().SequenceEqual(new[] { "ac-controls-config", "ac-steam-verify", "ac-video-config" }), "All three actions are reachable without a crash.");
            var verify = UserRequestedRepairPolicy.CreatePlan(game.Kind, "ac-steam-verify")!;
            Check(verify.SteamAppId == "244210" && verify.RequiresApproval && verify.EstimatedMinutes > 2, "Verification uses the original app ID with approval and an estimate.");
            Check(UserRequestedRepairPolicy.CreatePlan(game.Kind, "ac-steam-verify-restore") is null, "Steam verification does not offer a fictional Restore.");
            Check(UserRequestedRepairPolicy.CreatePlan(GameKind.AssettoCorsaEvo, "ac-video-config") is null, "Original repairs cannot be requested for EVO.");
            var requested = new IncidentRecord { Game = game.DisplayName, IncidentFolder = root,
                Classification = new CrashClassification(ScopedConfigurationRepair.RequestCategory, 100, "Requested action",
                    new[] { ScopedConfigurationRepair.RequestPrefix + "ac-steam-verify" }) };
            Check(RepairPlanner.TryCreateFromIncident(requested)?.Id == "ac-steam-verify", "Manual verification history reconstructs from an allowlisted request.");
            File.WriteAllText(Path.Combine(root, "repair.json"), JsonSerializer.Serialize(new { state = new { IsComplete = true, Success = true, Message = "Steam verification started" } }));
            var details = IncidentDetailsService.Build(requested);
            Check(details.NeedsRetest && details.NextStep.Contains("Steam") && details.OutcomeHeadline == "Verification started; retest needed", "Starting Steam verification is labeled for retest, not gameplay success.");
            Check(RepairKnowledgeBase.PublicIdForPlan("ac-video-config-restore") == "ac-video-config" && RepairKnowledgeBase.PublicIdForPlan("ac-steam-verify") == "ac-steam-content", "Repair and Restore deep links resolve to their paired guides.");
        }
        finally { Directory.Delete(root, recursive: true); }
        Console.WriteLine($"Original Assetto Corsa regression checks passed ({count}).");
    }
}
