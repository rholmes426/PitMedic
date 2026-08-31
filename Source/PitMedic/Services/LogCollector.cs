using System.Diagnostics;
using System.Text.RegularExpressions;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class LogCollector
{
    private static readonly string[] StrongCrashTokens =
    {
        "fatal error",
        "onfatalerror",
        "crash submitted",
        "access violation",
        "unhandled exception",
        "dxgi_error_device_removed",
        "dxgi_error_device_hung",
        "device removed",
        "out of memory",
        "gpu hang",
        "error decompressing file",
        "error initializing scene file",
        "cube error loading scene file"
    };

    private static readonly string[] LmuCleanExitTokens =
    {
        "Executing NAV_EXIT",
        "Changing state from Setup to Exit",
        "Entered Game::Exit()",
        "Entered OSMan::Exit()",
        "Still has 0 bytes allocated"
    };

    private static readonly Regex InstalledContentRegex = new(
        @"(?is)(?:error\s+decompressing\s+file|error\s+loading\s+mesh|error\s+initializing\s+scene\s+file|cube\s+error\s+loading\s+scene\s+file).{0,1200}?[\\/]Installed[\\/](?<kind>Locations|Vehicles)[\\/](?<name>[^\\/\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CollectedEvidence Collect(GameDefinition game, DateTimeOffset sessionStarted, DateTimeOffset incidentTime, string incidentFolder)
    {
        var logsDest = Path.Combine(incidentFolder, "Logs");
        var dumpsDest = Path.Combine(incidentFolder, "Dumps");
        Directory.CreateDirectory(logsDest);
        Directory.CreateDirectory(dumpsDest);

        var sessionCutoff = sessionStarted.AddSeconds(-5);
        var logCutoff = incidentTime.AddMinutes(-15);
        if (sessionCutoff > logCutoff) logCutoff = sessionCutoff;
        var dumpCutoff = incidentTime.AddSeconds(-90);
        if (sessionCutoff > dumpCutoff) dumpCutoff = sessionCutoff;
        var logFiles = 0;
        var dumpFiles = 0;
        var hints = new List<string>();
        var cleanHints = new List<string>();
        var affectedContent = new List<string>();
        var repairSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (game.Kind == GameKind.IRacing)
        {
            var iracing = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing");
            logFiles += CopyRecent(iracing, logsDest, logCutoff, f =>
                f.Name.StartsWith("iRacingSim64Dx11", StringComparison.OrdinalIgnoreCase)
                || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
        }
        else if (game.Kind == GameKind.LeMansUltimate)
        {
            var lmu = SteamLibraryLocator.FindLeMansUltimateRoot();
            if (lmu is not null)
            {
                logFiles += CopyRecent(Path.Combine(lmu, "UserData", "Log"), logsDest, logCutoff,
                    f => f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                      || f.Name.StartsWith("trace_", StringComparison.OrdinalIgnoreCase)
                      || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
                dumpFiles += CopyRecent(Path.Combine(lmu, "UserData"), dumpsDest, dumpCutoff,
                    f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (game.Kind == GameKind.AssettoCorsaEvo)
        {
            foreach (var ace in GetAceUserDataRoots())
            {
                logFiles += CopyRecent(ace, logsDest, logCutoff, f =>
                    f.Name.Equals("log.txt", StringComparison.OrdinalIgnoreCase)
                    || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
                dumpFiles += CopyRecent(ace, dumpsDest, dumpCutoff, f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (game.Kind == GameKind.RaceRoom)
        {
            foreach (var raceroom in GetRaceRoomUserRoots())
            {
                logFiles += CopyRecent(Path.Combine(raceroom, "UserData", "Log"), logsDest, logCutoff, f =>
                    f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase)
                    || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));
            }
            foreach (var simbin in GetRaceRoomSimBinRoots())
                dumpFiles += CopyRecent(Path.Combine(simbin, "Crash Dumps"), dumpsDest, dumpCutoff, f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase));
        }
        else if (game.Kind == GameKind.AssettoCorsaCompetizione)
        {
            var saved = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AC2", "Saved");
            logFiles += CopyRecent(Path.Combine(saved, "Logs"), logsDest, logCutoff, f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase));
            dumpFiles += CopyRecent(Path.Combine(saved, "Crashes"), dumpsDest, dumpCutoff, f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase) || f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
        }
        else if (game.Kind == GameKind.Automobilista2)
        {
            var ams2 = GetAms2DocumentsRoot();
            // AMS2 does not provide a consistently documented always-on support log. Collect any
            // recent simulator-owned diagnostic text that exists, plus Windows/CrashDumps below.
            logFiles += CopyRecent(ams2, logsDest, logCutoff, f => f.Extension.Equals(".log", StringComparison.OrdinalIgnoreCase));
        }

        var crashDumps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
        dumpFiles += CopyRecent(crashDumps, dumpsDest, dumpCutoff,
            f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
              && game.ProcessNames.Any(p => f.Name.Contains(p, StringComparison.OrdinalIgnoreCase)));

        dumpFiles += CopyRecent(Path.GetTempPath(), dumpsDest, dumpCutoff,
            f => f.Extension.Equals(".dmp", StringComparison.OrdinalIgnoreCase)
              && game.ProcessNames.Any(p => f.Name.Contains(p.Split(' ')[0], StringComparison.OrdinalIgnoreCase)));

        try
        {
            foreach (var file in Directory.EnumerateFiles(logsDest, "*", SearchOption.TopDirectoryOnly))
            {
                string text;
                try { text = ReadTail(file, 1024 * 1024); }
                catch { continue; }

                foreach (var token in StrongCrashTokens)
                {
                    if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        hints.Add($"{Path.GetFileName(file)} contains strong failure marker '{token}'.");
                        break;
                    }
                }

                foreach (var relative in ExtractAffectedInstalledContent(text))
                    affectedContent.Add(relative);

                DetectRepairSignatures(game, text, repairSignatures);

                if (game.Kind == GameKind.LeMansUltimate
                    && Path.GetFileName(file).StartsWith("trace", StringComparison.OrdinalIgnoreCase))
                {
                    string tail;
                    try { tail = ReadTail(file, 96 * 1024); }
                    catch { continue; }

                    foreach (var token in LmuCleanExitTokens)
                    {
                        if (tail.Contains(token, StringComparison.OrdinalIgnoreCase))
                            cleanHints.Add($"{Path.GetFileName(file)} contains clean shutdown marker '{token}'.");
                    }
                }
            }
        }
        catch { }

        if (game.Kind == GameKind.LeMansUltimate && hints.Count > 0)
        {
            // Community reports are secondary evidence only. These signatures are intentionally
            // lower priority than official/content-specific signatures and their repairs require
            // explicit approval before PitMedic closes any third-party process.
            if (IsAnyProcessRunning("MSIAfterburner", "RTSS", "RivaTunerStatisticsServer"))
                repairSignatures.Add("lmu-overlay-conflict");
            if (IsAnyProcessRunning("lghub", "lghub_agent", "lghub_updater"))
                repairSignatures.Add("lmu-ghub-conflict");
        }

        if (game.Kind == GameKind.Automobilista2)
        {
            try
            {
                var savegame = Path.Combine(GetAms2DocumentsRoot(), "savegame");
                if (Directory.Exists(savegame))
                {
                    var recentChampionshipState = Directory.EnumerateFiles(savegame, "*.sav", SearchOption.AllDirectories)
                        .Select(f => new FileInfo(f))
                        .Any(f => f.LastWriteTimeUtc >= incidentTime.AddMinutes(-10).UtcDateTime
                            && (f.Name.Contains("championship", StringComparison.OrdinalIgnoreCase)
                                || (f.DirectoryName?.Contains("singlechamps", StringComparison.OrdinalIgnoreCase) ?? false)
                                || f.Name.Equals(".sav", StringComparison.OrdinalIgnoreCase)));
                    if (recentChampionshipState) repairSignatures.Add("ams2-championship-state");
                }
            }
            catch { }
        }

        var cleanExitDetected = game.Kind == GameKind.LeMansUltimate && cleanHints.Count >= 3;
        return new CollectedEvidence(
            logFiles,
            dumpFiles,
            hints.Distinct().Take(8).ToArray(),
            cleanExitDetected,
            cleanHints.Distinct().Take(6).ToArray(),
            affectedContent.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            repairSignatures.Take(12).ToArray());
    }

    private static void DetectRepairSignatures(GameDefinition game, string text, ISet<string> signatures)
    {
        var lower = text.ToLowerInvariant();
        if (game.Kind == GameKind.LeMansUltimate)
        {
            if (lower.Contains("error decompressing file") || lower.Contains("cube error loading scene file")) signatures.Add("lmu-content-corruption");
            if ((lower.Contains("shader") && (lower.Contains("error") || lower.Contains("failed"))) || lower.Contains("dynamic.cache")) signatures.Add("lmu-shader-cache");
            if (lower.Contains("config_dx11") || lower.Contains("config_dx11.ini") || lower.Contains("dx11_config")) signatures.Add("lmu-startup-config");
            if (lower.Contains("custompluginvariables") || (lower.Contains("plugin") && (lower.Contains("crash") || lower.Contains("exception") || lower.Contains("failed")))) signatures.Add("lmu-plugin-conflict");
            if (lower.Contains("failed to allocate a section of memory") || lower.Contains("out of memory")) signatures.Add("lmu-memory-allocation");
            if (lower.Contains("easy anti-cheat") || lower.Contains("easyanticheat") || lower.Contains("eac"))
            {
                if (lower.Contains("error") || lower.Contains("failed") || lower.Contains("not installed")) signatures.Add("lmu-eac");
            }
            if (lower.Contains("0xc000007b") || lower.Contains("reshade")) signatures.Add("lmu-reshade-runtime");
        }
        else if (game.Kind == GameKind.AssettoCorsaEvo)
        {
            if (lower.Contains("video.videosettings") || ((lower.Contains("video") || lower.Contains("graphics")) && (lower.Contains("failed") || lower.Contains("error"))))
                signatures.Add("ace-video-settings");
            if ((lower.Contains("file") && (lower.Contains("missing") || lower.Contains("corrupt") || lower.Contains("failed to load")))
                || lower.Contains("content error"))
                signatures.Add("ace-steam-content");
            if (lower.Contains("profile") && (lower.Contains("corrupt") || lower.Contains("failed")))
                signatures.Add("ace-user-profile");
        }
        else if (game.Kind == GameKind.RaceRoom)
        {
            if (lower.Contains("503") || lower.Contains("browserdata") || (lower.Contains("cef") && (lower.Contains("error") || lower.Contains("failed"))))
                signatures.Add("raceroom-browser-cache");
            if (lower.Contains("shadercache") || (lower.Contains("shader") && (lower.Contains("error") || lower.Contains("failed"))))
                signatures.Add("raceroom-shader-cache");
            if (lower.Contains("graphics_options") || lower.Contains("resolution") || lower.Contains("refresh rate") || lower.Contains("display mode"))
                signatures.Add("raceroom-graphics-config");
            if (lower.Contains("userdata") && (lower.Contains("corrupt") || lower.Contains("parse error") || lower.Contains("invalid setting")))
                signatures.Add("raceroom-user-config");
            if (lower.Contains("file") && (lower.Contains("missing") || lower.Contains("corrupt") || lower.Contains("failed to load")))
                signatures.Add("raceroom-steam-content");
        }
        else if (game.Kind == GameKind.AssettoCorsaCompetizione)
        {
            if (lower.Contains("gameusersettings") || lower.Contains("resolution") || lower.Contains("fullscreen")) signatures.Add("acc-game-user-settings");
            if (IsAccGraphicsFailure(lower)) signatures.Add("acc-engine-config");
            if (lower.Contains("controls.json") || lower.Contains("directinput") || (lower.Contains("controller") && (lower.Contains("failed") || lower.Contains("invalid")))) signatures.Add("acc-controls");
            if ((lower.Contains("trueforce") || lower.Contains("manufacturerextras") || lower.Contains("manufacturer extras")) && (lower.Contains("failed") || lower.Contains("error") || lower.Contains("crash"))) signatures.Add("acc-trueforce");
            if (lower.Contains("ffb") && (lower.Contains("failed") || lower.Contains("invalid") || lower.Contains("error"))) signatures.Add("acc-ffb");
            if (lower.Contains("customs\\controls") && (lower.Contains("crash") || lower.Contains("failed") || lower.Contains("invalid"))) signatures.Add("acc-control-presets");
            if (IsAccContentFailure(lower)) signatures.Add("acc-steam-content");
            if (lower.Contains("config") && (lower.Contains("corrupt") || lower.Contains("parse error"))) signatures.Add("acc-user-profile");
        }
        else if (game.Kind == GameKind.Automobilista2)
        {
            if (lower.Contains("graphicsconfigdx11")) signatures.Add("ams2-graphics-config");
            if (lower.Contains("openvr") || lower.Contains("oculus")) signatures.Add("ams2-vr-config");
            if (lower.Contains("controllersettings") || lower.Contains("controller config")) signatures.Add("ams2-controller-config");
            if (lower.Contains("ffb_custom_settings")) signatures.Add("ams2-ffb-custom");
            if (lower.Contains("tuningsetups") || lower.Contains("tuning setup")) signatures.Add("ams2-tuning-setups");
            if (lower.Contains("championship") || lower.Contains("singlechamps")) signatures.Add("ams2-championship-state");
            if (lower.Contains("default.sav") && lower.Contains("profile")) signatures.Add("ams2-default-profile");
            if ((lower.Contains("file") || lower.Contains("package")) && (lower.Contains("missing") || lower.Contains("corrupt") || lower.Contains("failed to load"))) signatures.Add("ams2-steam-content");
            if (lower.Contains("profile") && (lower.Contains("corrupt") || lower.Contains("invalid") || lower.Contains("parse"))) signatures.Add("ams2-user-profile");
        }
    }

    private static bool IsAccGraphicsFailure(string lower) =>
        (lower.Contains("engine.ini") && IsFailureText(lower))
        || lower.Contains("dxgi_error")
        || lower.Contains("d3d device lost")
        || lower.Contains("device being lost")
        || lower.Contains("device removed")
        || lower.Contains("gpu crashed")
        || lower.Contains("rendering thread exception");

    private static bool IsAccContentFailure(string lower)
    {
        if (lower.Contains("logdlssngx") || lower.Contains("nvngx") || lower.Contains("driverstore")
            || lower.Contains("\\windows\\system32")) return false;
        var damaged = lower.Contains("missing") || lower.Contains("corrupt")
            || lower.Contains("failed to load") || lower.Contains("failed to open")
            || lower.Contains("failed to read") || lower.Contains("couldn't find")
            || lower.Contains("cannot find") || lower.Contains("can't find");
        var package = lower.Contains(".pak") || lower.Contains("pak file")
            || lower.Contains("package /game/") || lower.Contains("file for package")
            || lower.Contains("file for asset") || lower.Contains("asset registry");
        return damaged && package;
    }

    private static bool IsFailureText(string lower) => lower.Contains("error")
        || lower.Contains("failed") || lower.Contains("failure") || lower.Contains("invalid")
        || lower.Contains("corrupt") || lower.Contains("fatal");

    private static IReadOnlyList<string> GetAceUserDataRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in new[]
        {
            Path.Combine(documents, "ACE"),
            Path.Combine(profile, "Saved Games", "ACE"),
            Path.Combine(profile, "Documents", "ACE")
        })
        {
            if (Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
        }
        return roots.ToArray();
    }

    private static IReadOnlyList<string> GetRaceRoomSimBinRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var path in new[]
        {
            Path.Combine(documents, "My Games", "SimBin"),
            Path.Combine(profile, "Documents", "My Games", "SimBin")
        })
        {
            if (Directory.Exists(path)) roots.Add(Path.GetFullPath(path));
        }
        return roots.ToArray();
    }

    private static IReadOnlyList<string> GetRaceRoomUserRoots()
    {
        var roots = new List<string>();
        foreach (var simbin in GetRaceRoomSimBinRoots())
        {
            foreach (var name in new[] { "RaceRoom Racing Experience", "RaceRoom Racing Experience Install 2", "RaceRoom Racing Experience Install 3" })
            {
                var path = Path.Combine(simbin, name);
                if (Directory.Exists(path)) roots.Add(path);
            }
        }
        return roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string GetAms2DocumentsRoot() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Automobilista 2");

    private static bool IsAnyProcessRunning(params string[] names)
    {
        foreach (var name in names)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); } catch { continue; }
            try { if (processes.Length > 0) return true; }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        return false;
    }

    public static IReadOnlyList<string> FindAffectedContentInIncidentFolder(string incidentFolder)
    {
        var logs = Path.Combine(incidentFolder, "Logs");
        if (!Directory.Exists(logs)) return Array.Empty<string>();
        var found = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(logs, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var text = ReadTail(file, 1024 * 1024);
                    found.AddRange(ExtractAffectedInstalledContent(text));
                }
                catch { }
            }
        }
        catch { }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> ExtractAffectedInstalledContent(string text)
    {
        foreach (Match match in InstalledContentRegex.Matches(text))
        {
            var kind = match.Groups["kind"].Value;
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name)) continue;
            if (name.Contains("..", StringComparison.Ordinal)) continue;
            yield return Path.Combine(kind, name);
        }
    }

    private static string ReadTail(string file, int maxBytes)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var length = Math.Min(stream.Length, maxBytes);
        if (stream.Length > length) stream.Seek(-length, SeekOrigin.End);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int CopyRecent(string source, string destination, DateTimeOffset cutoff, Func<FileInfo, bool> predicate)
    {
        if (!Directory.Exists(source)) return 0;
        var copied = 0;
        try
        {
            foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                FileInfo info;
                try { info = new FileInfo(path); } catch { continue; }
                if (info.LastWriteTimeUtc < cutoff.UtcDateTime || !predicate(info)) continue;
                try
                {
                    var safe = Sanitize(info.Name);
                    var target = Path.Combine(destination, safe);
                    if (File.Exists(target)) target = Path.Combine(destination, $"{Path.GetFileNameWithoutExtension(safe)}_{Guid.NewGuid():N}{info.Extension}");
                    info.CopyTo(target, false);
                    copied++;
                }
                catch { }
            }
        }
        catch { }
        return copied;
    }

    private static string Sanitize(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) fileName = fileName.Replace(c, '_');
        return fileName;
    }
}
