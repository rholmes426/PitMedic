using System.Text;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Watches iRacing session logs while the simulator is still running. iRacing often presents
/// an error in-game and then exits normally only after the user clicks Quit, so process exit
/// code alone is not sufficient evidence of a clean session.
/// </summary>
public sealed class IRacingLiveLogMonitor : ILiveLogMonitor
{
    private readonly Dictionary<string, long> _offsets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _seenSignatures = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _sessionStarted;

    public void StartSession(DateTimeOffset started)
    {
        _sessionStarted = started;
        _offsets.Clear();
        _seenSignatures.Clear();

        foreach (var file in CandidateFiles())
        {
            try
            {
                var info = new FileInfo(file);
                // Establish a clean baseline at the moment PitMedic attaches. This deliberately
                // avoids importing error text from a previous iRacing session. Any lines written
                // after attachment are monitored live; exit-time collection still captures startup
                // evidence that occurred before attachment.
                _offsets[file] = info.Length;
            }
            catch { }
        }
    }

    public IReadOnlyList<LiveFaultEvidence> Poll()
    {
        var found = new List<LiveFaultEvidence>();
        foreach (var file in CandidateFiles())
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < _sessionStarted.AddSeconds(-8).UtcDateTime) continue;

                if (!_offsets.TryGetValue(file, out var offset))
                    offset = 0; // New session log created after monitoring began.

                if (info.Length < offset) offset = 0; // Log rotated/truncated.
                if (info.Length == offset) continue;

                var maxStart = Math.Max(0, info.Length - 256 * 1024);
                if (offset < maxStart) offset = maxStart;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var text = reader.ReadToEnd();
                _offsets[file] = stream.Position;

                foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!TryMatch(line, out var id, out var category, out var message)) continue;
                    // Avoid duplicate UI/log lines while still allowing the same fault to be retested after a repair.
                    var now = DateTimeOffset.Now;
                    if (_seenSignatures.TryGetValue(id, out var last) && now - last < TimeSpan.FromSeconds(75)) continue;
                    _seenSignatures[id] = now;
                    found.Add(new LiveFaultEvidence(now, id, category, message, Path.GetFileName(file), GameKind.IRacing));
                }
            }
            catch { }
        }
        return found;
    }

    private static bool TryMatch(string rawLine, out string id, out string category, out string message)
    {
        id = category = message = string.Empty;
        var line = rawLine.Trim();
        if (line.Length == 0) return false;
        var lower = line.ToLowerInvariant();

        if (lower.Contains("could not find sim"))
            return Match("could-not-find-sim", "Could not find simulator", line, out id, out category, out message);

        if (lower.Contains("not in service") || lower.Contains("system not in service")
            || (lower.Contains("iracing.com helper service") && ContainsAny(lower, "stopped", "failed", "not running"))
            || lower.Contains("service not available"))
            return Match("helper-service", "iRacing Helper Service failure", line, out id, out category, out message);

        if (lower.Contains("waiting for iracingservice"))
            return Match("waiting-service", "iRacing updater waiting for service", line, out id, out category, out message);

        if (lower.Contains("welcome to iracing") && ContainsAny(lower, "stuck", "crash", "failed", "error"))
            return Match("ui-welcome", "iRacing UI startup failure", line, out id, out category, out message);

        if (ContainsAny(lower, "white screen", "black screen") && lower.Contains("iracing"))
            return Match("ui-render-failure", "iRacing UI startup failure", line, out id, out category, out message);

        if (lower.Contains("error 73"))
            return Match("eac-error-73", "Easy Anti-Cheat Error 73", line, out id, out category, out message);

        if (lower.Contains("error code: 10011") || lower.Contains("launch error (10011)"))
            return Match("eac-error-10011", "Easy Anti-Cheat launch error 10011", line, out id, out category, out message);

        if ((lower.Contains("easy anti-cheat") || lower.Contains("easyanticheat") || lower.Contains("anti-cheat") || lower.Contains("anticheat"))
            && ContainsAny(lower, "error", "failed", "failure", "fatal", "unable", "not installed", "not running"))
            return Match("eac-failure", "Easy Anti-Cheat failure", line, out id, out category, out message);

        if (lower.Contains("verification failure"))
            return Match("verification-failure", "iRacing update verification failure", line, out id, out category, out message);

        if (lower.Contains("content file locked"))
            return Match("content-file-locked", "iRacing Steam content file locked", line, out id, out category, out message);

        if (lower.Contains("missing file privileges"))
            return Match("missing-file-privileges", "iRacing Steam file privileges failure", line, out id, out category, out message);

        if (lower.Contains("loading error 3"))
            return Match("loading-error-3", "iRacing user configuration/content failure", line, out id, out category, out message);

        if (lower.Contains("loading error 61") || lower.Contains("loading error 62") || lower.Contains("loading error 72"))
            return Match("track-loading-error", "iRacing track content failure", line, out id, out category, out message);

        if (lower.Contains("loading error 22") || lower.Contains("loading error 71"))
            return Match("car-loading-error", "iRacing car content failure", line, out id, out category, out message);

        if (lower.Contains("loading error 49"))
            return Match("loading-error-49", "iRacing Steam track content failure", line, out id, out category, out message);

        if (ContainsAny(lower, "sim is already launched", "iracing is already running", "sim is already running",
            "please close the simulator if you wish to launch a new session"))
            return Match("already-running", "iRacing simulator already-running state", line, out id, out category, out message);

        if (lower.Contains("createprocessasuser"))
            return Match("createprocessasuser", "iRacing launch permission/compatibility failure", line, out id, out category, out message);

        if (lower.Contains("digital signature failed"))
            return Match("digital-signature", "iRacing updater digital signature failure", line, out id, out category, out message);

        if (lower.Contains("unsupported operating system") || lower.Contains("compatibility mode"))
            return Match("compatibility-mode", "iRacing compatibility-mode failure", line, out id, out category, out message);

        if (ContainsAny(lower, "dxgi_error_device_removed", "dxgi_error_device_hung", "device removed", "device hung"))
            return Match("dxgi-device-failure", "Graphics device failure", line, out id, out category, out message);

        if (ContainsAny(lower, "out of memory", "failed to allocate", "memory allocation failed"))
            return Match("memory-failure", "Memory allocation failure", line, out id, out category, out message);

        if (ContainsAny(lower, "fatal error", "unhandled exception", "access violation"))
            return Match("fatal-sim-error", "Simulator fatal error", line, out id, out category, out message);

        if ((lower.Contains("rendererdx11") || lower.Contains("graphics config")) && ContainsAny(lower, "error", "failed", "invalid", "corrupt", "crash"))
            return Match("renderer-config", "Renderer configuration failure", line, out id, out category, out message);

        if (ContainsAny(lower, "failed to connect", "connection failed", "network error", "socket error")
            && !lower.Contains("retry"))
            return Match("connection-failure", "Session connection failure", line, out id, out category, out message);

        return false;
    }

    private static bool Match(string matchId, string matchCategory, string raw, out string id, out string category, out string message)
    {
        id = matchId;
        category = matchCategory;
        message = raw.Length <= 240 ? raw : raw[..240] + "…";
        return true;
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static IEnumerable<string> CandidateFiles()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing");
        if (!Directory.Exists(root)) yield break;

        var roots = new[] { root, Path.Combine(root, "logs") };
        var returned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidateRoot in roots)
        {
            if (!Directory.Exists(candidateRoot)) continue;
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(candidateRoot, "*", SearchOption.TopDirectoryOnly); }
            catch { continue; }

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var ext = Path.GetExtension(file);
                var interesting = name.StartsWith("iRacingSim64Dx11", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("iRacingSim64DX11", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("sim_launch.txt", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("main-ui.log", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("updater", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("service", StringComparison.OrdinalIgnoreCase)
                    || (ext.Equals(".log", StringComparison.OrdinalIgnoreCase)
                        && (name.Contains("sim", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("error", StringComparison.OrdinalIgnoreCase)
                            || name.Contains("ui", StringComparison.OrdinalIgnoreCase)));
                if (interesting && returned.Add(file)) yield return file;
            }
        }
    }
}
