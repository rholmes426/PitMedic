using System.Text;
using System.Text.RegularExpressions;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Watches iRacing session logs while the simulator is still running. iRacing often presents
/// an error in-game and then exits normally only after the user clicks Quit, so process exit
/// code alone is not sufficient evidence of a clean session.
/// </summary>
public sealed class IRacingLiveLogMonitor : ILiveLogMonitor
{
    internal const string SpotterErrorSignature = "loading-error-63";
    internal const string SpotterErrorCategory = "iRacing spotter sound load failure";
    private const string SpotterErrorMessage = "Loading Error 63 while loading a spotter sound.";
    private static readonly Regex LoadingError63 = new(@"\bloading\s+error\s*[:#-]?\s*(?:63\b|\(\s*63\s*\))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly record struct LogPosition(long Offset, DateTime Created, string PartialLine);
    private readonly Dictionary<string, LogPosition> _positions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _seenSignatures = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset _sessionStarted;
    private readonly Func<bool> _isUpdating;
    private readonly Func<IEnumerable<string>> _candidateFiles;
    private readonly Func<DateTimeOffset> _now;
    private readonly Func<bool>? _isSpotterDialogVisible;
    private bool _spotterDialogWasVisible;

    public IRacingLiveLogMonitor(Func<bool>? isUpdating = null,
        Func<IEnumerable<string>>? candidateFiles = null, Func<DateTimeOffset>? now = null,
        Func<bool>? isSpotterDialogVisible = null)
    {
        _isUpdating = isUpdating ?? IRacingUpdateGuard.IsBusy;
        _candidateFiles = candidateFiles ?? CandidateFiles;
        _now = now ?? (() => DateTimeOffset.Now);
        _isSpotterDialogVisible = isSpotterDialogVisible;
    }

    public void StartSession(DateTimeOffset started)
    {
        _sessionStarted = started;
        _positions.Clear();
        _seenSignatures.Clear();
        _spotterDialogWasVisible = false;

        foreach (var file in _candidateFiles())
        {
            try
            {
                var info = new FileInfo(file);
                // Baseline old log text when PitMedic begins monitoring. Keep these positions
                // when control passes between the launcher and a simulator session.
                _positions[file] = new LogPosition(info.Length, info.CreationTimeUtc, string.Empty);
            }
            catch { }
        }
    }

    internal void BeginTrackedSession(DateTimeOffset started)
    {
        foreach (var signature in _seenSignatures.Where(entry => entry.Value < started).Select(entry => entry.Key).ToArray())
            _seenSignatures.Remove(signature);
    }

    public IReadOnlyList<LiveFaultEvidence> Poll() => Poll(simulatorExited: false);

    internal IReadOnlyList<LiveFaultEvidence> Poll(bool simulatorExited)
    {
        var found = new List<LiveFaultEvidence>();
        var updating = _isUpdating();
        if (!simulatorExited && _isSpotterDialogVisible is not null)
        {
            var visible = _isSpotterDialogVisible();
            if (visible && !_spotterDialogWasVisible)
                AddFault(SpotterErrorSignature, SpotterErrorCategory, SpotterErrorMessage, "iRacing dialog", updating, found);
            _spotterDialogWasVisible = visible;
        }
        foreach (var file in _candidateFiles())
        {
            try
            {
                var info = new FileInfo(file);
                if (info.LastWriteTimeUtc < _sessionStarted.AddSeconds(-8).UtcDateTime) continue;

                var created = info.CreationTimeUtc;
                var minOffset = Math.Max(0, info.Length - 256 * 1024);
                if (!_positions.TryGetValue(file, out var position) || position.Created != created || position.Offset > info.Length)
                    position = new LogPosition(0, created, string.Empty);
                if (position.Offset < minOffset)
                    position = new LogPosition(minOffset, created, string.Empty);
                if (info.Length == position.Offset && (!simulatorExited || position.PartialLine.Length == 0)) continue;

                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                stream.Seek(position.Offset, SeekOrigin.Begin);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: position.Offset == 0, leaveOpen: true);
                var text = position.PartialLine + reader.ReadToEnd();
                var lastBreak = text.LastIndexOfAny(new[] { '\r', '\n' });
                var tail = simulatorExited ? string.Empty : text[(lastBreak + 1)..];
                // Do not carry an update-time partial line into a later diagnostic poll.
                _positions[file] = new LogPosition(stream.Position, created, !updating && tail.Length <= 1024 ? tail : string.Empty);

                var complete = simulatorExited ? text : text[..(lastBreak + 1)];
                foreach (var line in complete.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    if (TryMatch(line, out var id, out var category, out var message))
                        AddFault(id, category, message, Path.GetFileName(file), updating, found);
            }
            catch { }
        }
        // EOS reports a generic Error 73 header alongside its specific file rejection.
        if (found.Any(f => IsContentVersionFault(f.SignatureId)))
            found.RemoveAll(f => f.SignatureId is "eac-error-73" or "eac-failure");
        if (found.Any(f => f.SignatureId == SpotterErrorSignature))
            found.RemoveAll(f => f.SignatureId == "helper-service");
        return found;
    }

    private void AddFault(string id, string category, string message, string source, bool updating, List<LiveFaultEvidence> found)
    {
        if (IRacingUpdateGuard.SuppressDiagnostic(id, updating)) return;
        var now = _now();
        if (_seenSignatures.TryGetValue(id, out var last) && now - last < TimeSpan.FromSeconds(75)) return;
        _seenSignatures[id] = now;
        found.Add(new LiveFaultEvidence(now, id, category, message, source, GameKind.IRacing));
    }

    internal bool SawRecentSpotterError =>
        _seenSignatures.TryGetValue(SpotterErrorSignature, out var seen) && _now() - seen < TimeSpan.FromSeconds(75);

    internal static bool IsLoadingError63(string text) => LoadingError63.IsMatch(text);

    internal static bool IsContentVersionFault(string id) =>
        id is "track-file-version" or "car-file-version" or "unknown-file-version";

    private static bool TryMatch(string rawLine, out string id, out string category, out string message)
    {
        id = category = message = string.Empty;
        var line = rawLine.Trim();
        if (line.Length == 0 || IRacingDiagnosticPolicy.IsInstallationProbe(line)) return false;
        var lower = line.ToLowerInvariant();

        if (IsLoadingError63(line))
            return Match(SpotterErrorSignature, SpotterErrorCategory, SpotterErrorMessage, out id, out category, out message);
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

        if (lower.Contains("unknown file version"))
        {
            var normalized = lower.Replace('\\', '/');
            if (normalized.Contains("(tracks/"))
                return Match("track-file-version", "iRacing track content version rejected", line, out id, out category, out message);
            if (normalized.Contains("(cars/"))
                return Match("car-file-version", "iRacing car content version rejected", line, out id, out category, out message);
            return Match("unknown-file-version", "iRacing file version rejected", line, out id, out category, out message);
        }

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

        if (IRacingLoadingError77Policy.IsError(line))
            return Match("loading-error-77", "iRacing Loading Error 77", line, out id, out category, out message);
        if (System.Text.RegularExpressions.Regex.IsMatch(lower, @"\b(?:error|socket error)\s*[:#-]?\s*(10057|10035|10054)\b"))
            return Match("socket-connection-error", "iRacing socket connection failure", line, out id, out category, out message);

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
        message = raw.Length <= 240 ? raw : raw[..240] + ".";
        return true;
    }

    private static bool ContainsAny(string text, params string[] values) => values.Any(text.Contains);

    private static IEnumerable<string> CandidateFiles() => CandidateFiles(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "iRacing"));

    internal static IEnumerable<string> CandidateFiles(string root)
    {
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
                    || name.Equals("eos_anticheat_errors.txt", StringComparison.OrdinalIgnoreCase)
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
