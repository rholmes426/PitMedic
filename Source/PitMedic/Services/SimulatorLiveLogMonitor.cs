using System.Text;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Passive, read-only tailing for simulator-owned text logs. This deliberately uses normal file I/O
/// only: no process memory reads, code injection, hooks, DLL loading, packet inspection, or debugger APIs.
/// Files are opened with shared read/write/delete access so the simulator remains fully in control.
/// </summary>
public sealed class SimulatorLiveLogMonitor : ILiveLogMonitor
{
    private readonly GameKind _game;
    private readonly Func<IEnumerable<string>> _candidateFiles;
    private readonly Func<string, (string Id, string Category, string Message)?> _matcher;
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _seen = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _started;

    private SimulatorLiveLogMonitor(GameKind game, Func<IEnumerable<string>> candidateFiles,
        Func<string, (string Id, string Category, string Message)?> matcher)
    {
        _game = game;
        _candidateFiles = candidateFiles;
        _matcher = matcher;
    }

    public static ILiveLogMonitor? Create(GameKind game) => game switch
    {
        GameKind.IRacing => new IRacingLiveLogMonitor(),
        GameKind.AssettoCorsaEvo => new SimulatorLiveLogMonitor(game, AceFiles, MatchAce),
        GameKind.RaceRoom => new SimulatorLiveLogMonitor(game, RaceRoomFiles, MatchRaceRoom),
        GameKind.AssettoCorsaCompetizione => new SimulatorLiveLogMonitor(game, AccFiles, MatchAcc),
        // AMS2 does not expose a consistently documented, always-on support text log comparable
        // to the three monitors above. It remains monitored by process state, telemetry, Windows
        // events and exit-time evidence collection rather than guessing at an unstable log path.
        _ => null
    };

    public void StartSession(DateTimeOffset started)
    {
        _started = started;
        _offsets.Clear();
        _seen.Clear();
        foreach (var file in SafeFiles())
        {
            try { _offsets[file] = new FileInfo(file).Length; } catch { }
        }
    }

    public IReadOnlyList<LiveFaultEvidence> Poll()
    {
        var found = new List<LiveFaultEvidence>();
        foreach (var file in SafeFiles())
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < _started.AddSeconds(-8).UtcDateTime) continue;
                var offset = _offsets.TryGetValue(file, out var saved) ? saved : 0;
                if (info.Length < offset) offset = 0;
                if (info.Length == offset) continue;
                var maxStart = Math.Max(0, info.Length - 256 * 1024);
                if (offset < maxStart) offset = maxStart;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var text = reader.ReadToEnd();
                _offsets[file] = stream.Position;

                foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = _matcher(raw);
                    if (match is null) continue;
                    var now = DateTimeOffset.Now;
                    if (_seen.TryGetValue(match.Value.Id, out var last) && now - last < TimeSpan.FromSeconds(75)) continue;
                    _seen[match.Value.Id] = now;
                    found.Add(new LiveFaultEvidence(now, match.Value.Id, match.Value.Category,
                        Truncate(match.Value.Message), Path.GetFileName(file), _game));
                }
            }
            catch { }
        }
        return found;
    }

    private IEnumerable<string> SafeFiles()
    {
        IEnumerable<string> files;
        try { files = _candidateFiles(); } catch { yield break; }
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(file)) yield return file;
    }

    private static IEnumerable<string> AceFiles()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var root in new[] { Path.Combine(profile, "Saved Games", "ACE"), Path.Combine(documents, "ACE") })
        {
            if (!Directory.Exists(root)) continue;
            var primary = Path.Combine(root, "log.txt");
            if (File.Exists(primary)) yield return primary;
            foreach (var f in EnumerateTop(root, "*.log")) yield return f;
        }
    }

    private static IEnumerable<string> RaceRoomFiles()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var name in new[] { "RaceRoom Racing Experience", "RaceRoom Racing Experience Install 2", "RaceRoom Racing Experience Install 3" })
        {
            var log = Path.Combine(documents, "My Games", "SimBin", name, "UserData", "Log");
            foreach (var f in EnumerateTop(log, "game_*.log")) yield return f;
            foreach (var f in EnumerateTop(log, "*.log")) yield return f;
        }
    }

    private static IEnumerable<string> AccFiles()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var logs = Path.Combine(local, "AC2", "Saved", "Logs");
        var primary = Path.Combine(logs, "AC2.log");
        if (File.Exists(primary)) yield return primary;
        foreach (var f in EnumerateTop(logs, "*.log")) yield return f;
    }

    private static IEnumerable<string> EnumerateTop(string root, string pattern)
    {
        if (!Directory.Exists(root)) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root, pattern, SearchOption.TopDirectoryOnly); } catch { yield break; }
        foreach (var f in files) yield return f;
    }

    private static (string, string, string)? MatchAce(string raw)
    {
        var l = raw.Trim(); var x = l.ToLowerInvariant();
        if (x.Contains("video.videosettings") || ((x.Contains("video") || x.Contains("graphics")) && Bad(x)))
            return ("ace-video-settings", "Assetto Corsa EVO video settings failure", l);
        if (x.Contains("profile") && Bad(x)) return ("ace-user-profile", "Assetto Corsa EVO profile failure", l);
        if ((x.Contains("file") && (x.Contains("missing") || x.Contains("corrupt") || x.Contains("failed to load"))) || x.Contains("content error"))
            return ("ace-steam-content", "Assetto Corsa EVO content failure", l);
        if (Fatal(x)) return ("ace-video-settings", "Assetto Corsa EVO fatal startup/graphics failure", l);
        return null;
    }

    private static (string, string, string)? MatchRaceRoom(string raw)
    {
        var l = raw.Trim(); var x = l.ToLowerInvariant();
        if (x.Contains("503") || x.Contains("browserdata") || (x.Contains("cef") && Bad(x)))
            return ("raceroom-browser-cache", "RaceRoom browser/UI failure", l);
        if (x.Contains("shadercache") || (x.Contains("shader") && Bad(x)))
            return ("raceroom-shader-cache", "RaceRoom shader-cache failure", l);
        if (x.Contains("graphics_options") || ((x.Contains("resolution") || x.Contains("display mode")) && Bad(x)))
            return ("raceroom-graphics-config", "RaceRoom graphics configuration failure", l);
        if (x.Contains("userdata") && (x.Contains("corrupt") || x.Contains("invalid") || x.Contains("parse error")))
            return ("raceroom-user-config", "RaceRoom user configuration failure", l);
        if ((x.Contains("file") && (x.Contains("missing") || x.Contains("corrupt") || x.Contains("failed to load"))))
            return ("raceroom-steam-content", "RaceRoom content failure", l);
        return null;
    }

    private static (string, string, string)? MatchAcc(string raw)
    {
        var l = raw.Trim(); var x = l.ToLowerInvariant();
        if ((x.Contains("gameusersettings") || x.Contains("resolution") || x.Contains("fullscreen")) && Bad(x))
            return ("acc-game-user-settings", "ACC display/startup configuration failure", l);
        if (IsAccGraphicsFailure(x))
            return ("acc-engine-config", "ACC engine/graphics configuration failure", l);
        if ((x.Contains("controls.json") || x.Contains("controller") || x.Contains("directinput") || x.Contains("wheel")) && Bad(x))
            return ("acc-controls", "ACC controller configuration failure", l);
        if ((x.Contains("trueforce") || x.Contains("manufacturerextras") || x.Contains("manufacturer extras")) && Bad(x))
            return ("acc-trueforce", "ACC Logitech TrueForce integration failure", l);
        if (x.Contains("ffb") && Bad(x)) return ("acc-ffb", "ACC force-feedback configuration failure", l);
        if (IsAccContentFailure(x))
            return ("acc-steam-content", "ACC local content failure", l);
        if (Fatal(x)) return ("acc-game-user-settings", "ACC fatal simulator failure", l);
        return null;
    }

    private static bool IsAccGraphicsFailure(string x)
    {
        // Unreal uses the Error log level for benign object-dependency diagnostics during normal
        // loading and shutdown. In particular, TextRenderComponent lines contain "render" but do
        // not indicate a graphics failure. Require an explicit configuration or GPU-device signal.
        return (x.Contains("engine.ini") && Bad(x))
            || x.Contains("dxgi_error")
            || x.Contains("d3d device lost")
            || x.Contains("device being lost")
            || x.Contains("device removed")
            || x.Contains("gpu crashed")
            || x.Contains("rendering thread exception");
    }

    private static bool IsAccContentFailure(string x)
    {
        // NVIDIA NGX/DLSS can log a failed optional DLL load from Windows DriverStore even when
        // ACC runs and exits normally. A path containing "FileRepository" is not evidence that
        // an ACC game file is missing, so content findings require an explicit package/archive.
        if (x.Contains("logdlssngx") || x.Contains("nvngx") || x.Contains("driverstore")
            || x.Contains("\\windows\\system32"))
            return false;

        var damaged = x.Contains("missing")
            || x.Contains("corrupt")
            || x.Contains("failed to load")
            || x.Contains("failed to open")
            || x.Contains("failed to read")
            || x.Contains("couldn't find")
            || x.Contains("cannot find")
            || x.Contains("can't find");
        var gamePackage = x.Contains(".pak")
            || x.Contains("pak file")
            || x.Contains("package /game/")
            || x.Contains("file for package")
            || x.Contains("file for asset")
            || x.Contains("asset registry");
        return damaged && gamePackage;
    }

    private static bool Bad(string x) => x.Contains("error") || x.Contains("failed") || x.Contains("failure") || x.Contains("invalid") || x.Contains("corrupt") || x.Contains("fatal");
    private static bool Fatal(string x) => x.Contains("fatal error") || x.Contains("unhandled exception") || x.Contains("access violation") || x.Contains("critical error");
    private static string Truncate(string s) => s.Length <= 240 ? s : s[..240] + "…";
}
