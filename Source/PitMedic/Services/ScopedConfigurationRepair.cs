using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed record ConfigurationRepairOption(string Id, GameKind Game, string Title, string Description, string[] Files, bool IsCache = false);

public static class ScopedConfigurationRepair
{
    public const string RequestCategory = "User-requested troubleshooting";
    public const string RequestPrefix = "PitMedic requested repair: ";
    private const string ManifestName = "configuration-backup.json";
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxCacheFiles = 512;
    private const int MaxCacheBytes = 128 * 1024 * 1024;
    public const string RFactor2CacheId = "rfactor2-shader-cache";

    public static IReadOnlyList<ConfigurationRepairOption> Options { get; } = new[]
    {
        new ConfigurationRepairOption(RFactor2CacheId, GameKind.RFactor2,
            "Rebuild shader and track caches",
            "Back up generated shaders and track cache files for rFactor 2 to rebuild. Close the simulator and its tools first. The first track load can take longer. Restore is available.",
            new[] { "Shaders", "CBash" }, IsCache: true),
        new ConfigurationRepairOption("ac-video-config", GameKind.AssettoCorsa,
            "Original AC display settings are broken",
            "Preserve video.ini and let the original Assetto Corsa rebuild its display settings. Close the game, launcher and Content Manager first. Resolution, VR and display preferences may need reconfiguration. Restore is available.",
            new[] { "video.ini" }),
        new ConfigurationRepairOption("ac-controls-config", GameKind.AssettoCorsa,
            "Original AC wheel or pedals do not respond",
            "Preserve controls.ini and let the original Assetto Corsa rebuild its active bindings. Close the game, launcher and Content Manager first. Remap and recalibrate afterward; saved presets and setups are preserved. Restore is available.",
            new[] { "controls.ini" }),
        new ConfigurationRepairOption("iracing-socket-connection", GameKind.IRacing,
            "Connection error 10057, 10035 or 10054",
            "Try iRacing's supported connection setting. Changes only connect_sockets in core.ini. Restore the original setting if the same connection test still fails.",
            new[] { "core.ini" }),
        new ConfigurationRepairOption("iracing-spotter-loading-error-63", GameKind.IRacing,
            "Loading Error 63 (spotter sound)",
            "Preserve app.ini and let iRacing regenerate it. Application preferences may reset; setups, paints, replays and spotter packages are preserved.",
            new[] { "app.ini" }),
        new ConfigurationRepairOption("lmu-controller-config", GameKind.LeMansUltimate,
            "Wheel, pedals or buttons stopped working",
            "Preserve LMU's four controller files and let the game rebuild them. Try reconnecting and rebinding first. You may need to remap controls and recalibrate.",
            new[] { "direct input.json", "current controls.json", "keyboard.json", "gamepad.json" })
    };

    public static string BaseId(string id) => id.EndsWith("-restore", StringComparison.Ordinal)
        ? id[..^"-restore".Length] : id;

    public static ConfigurationRepairOption? Find(string id) =>
        Options.FirstOrDefault(x => x.Id.Equals(BaseId(id), StringComparison.Ordinal));

    public static bool IsSupported(string id) => Find(id) is not null;
    public static bool IsRestore(string id) => IsSupported(id) && id.EndsWith("-restore", StringComparison.Ordinal);

    public static RepairPlan? CreatePlan(GameKind game, string id)
    {
        var option = Find(id);
        if (option is null || option.Game != game) return null;
        var restore = IsRestore(id);
        return new RepairPlan
        {
            Id = id, Game = GameDefinition.Supported.Single(x => x.Kind == game).DisplayName,
            Title = restore ? (option.IsCache ? "Restore previous shader and track caches" : "Restore previous configuration") : option.Title,
            Summary = restore
                ? "Restore the latest successful backup for this repair. Current files are preserved separately first. Close the simulator and any update before continuing."
                : option.Description,
            Safety = RepairSafety.Significant, RequiresApproval = true, EstimatedMinutes = option.IsCache ? 3 : 1,
            Steps = new[] { "Confirm simulator and updates are closed", "Preserve original files",
                option.IsCache
                    ? (restore ? "Restore backed-up cache files" : "Clear the generated cache files")
                    : (restore ? "Restore the previous configuration" : "Apply the scoped configuration repair"),
                "Verify file changes, then retest the same symptom" },
            References = RepairKnowledgeBase.ReferencesForPlan(option.Id)
        };
    }

    public static RepairPlan? FromRequest(IncidentRecord incident)
    {
        if (incident.Classification.Category != RequestCategory || incident.Classification.Evidence.Count != 1) return null;
        var line = incident.Classification.Evidence[0];
        if (!line.StartsWith(RequestPrefix, StringComparison.Ordinal)) return null;
        var game = GameDefinition.Supported.FirstOrDefault(x => x.DisplayName == incident.Game);
        return game is null ? null : CreatePlan(game.Kind, line[RequestPrefix.Length..]);
    }

    public static string Execute(string id, string root, string backupRoot, string backupsParent, CancellationToken token)
    {
        var option = Find(id) ?? throw new InvalidOperationException("Unsupported configuration repair.");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The simulator configuration folder was not found.");
        CheckPath(root, option.Files[0]);
        CheckPath(backupRoot, ManifestName);
        token.ThrowIfCancellationRequested();

        if (IsRestore(id)) return Restore(option, root, backupRoot, backupsParent, token);

        var originals = ReadFiles(option, root);
        if (originals.Count == 0) throw new FileNotFoundException(option.IsCache
            ? "No recognized rFactor 2 cache files were found. Nothing was changed."
            : "None of the expected configuration files exists. Nothing was changed.");
        var replacements = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, bytes) in originals)
            replacements[name] = option.Id == "iracing-socket-connection" ? EnableSocketConnection(bytes) : null;
        if (replacements.All(x => x.Value is not null && originals[x.Key].SequenceEqual(x.Value)))
            return "connect_sockets is already 1 in [Communications]. Nothing was changed. Retest the connection or review another cause.";

        var manifest = Preserve(option, root, backupRoot, originals, false);
        var changed = new List<string>();
        try
        {
            foreach (var (name, original) in originals)
            {
                token.ThrowIfCancellationRequested();
                var path = CheckPath(root, name);
                RequireBytes(path, original);
                changed.Add(name);
                if (replacements[name] is byte[] next) WriteAtomic(path, next);
                else File.Delete(path);
            }
            foreach (var (name, next) in replacements)
            {
                var path = CheckPath(root, name);
                if (next is null && File.Exists(path)) throw new IOException("A configuration file reappeared during repair. Close the simulator and retry.");
                if (next is not null) RequireBytes(path, next);
            }
            WriteManifest(backupRoot, manifest with { Completed = true });
        }
        catch
        {
            foreach (var name in changed.AsEnumerable().Reverse())
            {
                var path = CheckPath(root, name);
                var next = replacements[name];
                if (!File.Exists(path) || next is not null && File.ReadAllBytes(path).SequenceEqual(next))
                    WriteAtomic(path, originals[name]);
            }
            throw;
        }
        if (option.IsCache)
            return "The generated rFactor 2 cache files were backed up and cleared. Relaunch rFactor 2 and retry the same track. Use Restore last backup if needed.";
        return option.Id == "iracing-socket-connection"
            ? "connect_sockets was set to 1 in [Communications]. The original core.ini is preserved. Retry the same connection; use Restore last backup if it does not help."
            : "The selected configuration files were preserved and removed from the active folder. Start the simulator to regenerate them, then retest. Use Restore last backup to return to the previous configuration.";
    }

    public static byte[] EnableSocketConnection(byte[] bytes)
    {
        var (encoding, preambleLength) = EncodingFor(bytes);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var section = Regex.Matches(text, @"(?m)^[ \t]*\[Communications\][ \t]*(?:[;#][^\r\n]*)?\r?$", RegexOptions.IgnoreCase);
        if (section.Count != 1) throw new InvalidDataException("core.ini must contain exactly one [Communications] section.");
        var start = section[0].Index + section[0].Length;
        var following = Regex.Match(text[start..], @"(?m)^[ \t]*\[[^\]\r\n]+\]");
        var end = following.Success ? start + following.Index : text.Length;
        var body = text[start..end];
        var keys = Regex.Matches(body, @"(?m)^(?<before>[ \t]*connect_sockets[ \t]*=[ \t]*)(?<value>[^;#\r\n]*)(?<tail>[;#][^\r\n]*)?\r?$", RegexOptions.IgnoreCase);
        if (keys.Count != 1 || keys[0].Groups["value"].Value.Trim() is not ("0" or "1"))
            throw new InvalidDataException("The existing connect_sockets setting is missing, duplicated or unsupported. Nothing was changed.");
        var key = keys[0].Groups["value"];
        var valueStart = start + key.Index;
        var value = key.Value;
        var trailing = value[value.TrimEnd().Length..];
        text = text[..valueStart] + "1" + trailing + text[(valueStart + key.Length)..];
        return bytes[..preambleLength].Concat(encoding.GetBytes(text)).ToArray();
    }

    private static (Encoding Encoding, int PreambleLength) EncodingFor(byte[] bytes)
    {
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE })) return (new UnicodeEncoding(false, false, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF })) return (new UnicodeEncoding(true, false, true), 2);
        if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) return (new UTF8Encoding(false, true), 3);
        return (new UTF8Encoding(false, true), 0);
    }

    private static Dictionary<string, byte[]> ReadFiles(ConfigurationRepairOption option, string root)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var files = option.IsCache ? EnumerateCacheFiles(option, root) : option.Files;
        var bytesRemaining = option.IsCache ? MaxCacheBytes : int.MaxValue;
        foreach (var name in files)
        {
            var path = CheckPath(root, name);
            if (!File.Exists(path)) continue;
            if (option.IsCache && result.Count == MaxCacheFiles)
                throw new InvalidDataException("The cache has too many files for this repair. Nothing was changed.");
            var bytes = ReadBounded(path, option.IsCache ? bytesRemaining : MaxFileBytes);
            bytesRemaining -= bytes.Length;
            result.Add(name, bytes);
        }
        return result;
    }

    private static IEnumerable<string> EnumerateCacheFiles(ConfigurationRepairOption option, string root)
    {
        foreach (var directory in option.Files)
        {
            var path = CheckPath(root, directory);
            if (!Directory.Exists(path)) continue;
            foreach (var file in Directory.EnumerateFiles(path))
            {
                var name = Path.Combine(directory, Path.GetFileName(file));
                if (IsAllowedFile(option, name)) yield return name;
            }
        }
    }

    private static bool IsAllowedFile(ConfigurationRepairOption option, string name)
    {
        if (!option.IsCache) return option.Files.Contains(name, StringComparer.OrdinalIgnoreCase);
        var directory = Path.GetDirectoryName(name);
        var leaf = Path.GetFileName(name);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(leaf)
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !option.Files.Contains(directory, StringComparer.OrdinalIgnoreCase)) return false;
        var extension = directory.Equals("Shaders", StringComparison.OrdinalIgnoreCase) ? ".rrshd" : ".cbash";
        return Path.GetExtension(leaf).Equals(extension, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] ReadBounded(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > maximumBytes)
            throw new InvalidDataException("The files exceed the supported backup size. Nothing was changed.");
        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private sealed record Snapshot(string Name, string Sha256);
    private sealed record Manifest(string RepairId, string Root, Snapshot[] Files, bool IsRestore, bool Completed = false);

    private static Manifest Preserve(ConfigurationRepairOption option, string root, string backupRoot,
        Dictionary<string, byte[]> originals, bool restore)
    {
        Directory.CreateDirectory(backupRoot);
        var manifest = new Manifest(option.Id, Path.GetFullPath(root),
            originals.Select(x => new Snapshot(x.Key, Hash(x.Value))).ToArray(), restore);
        foreach (var (name, bytes) in originals)
        {
            var folder = Path.Combine(backupRoot, "Files");
            var target = CheckPath(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(bytes);
            stream.Flush(true);
        }
        WriteManifest(backupRoot, manifest);
        return manifest;
    }

    private static string Restore(ConfigurationRepairOption option, string root, string backupRoot, string backupsParent, CancellationToken token)
    {
        var candidates = Directory.Exists(backupsParent)
            ? Directory.EnumerateDirectories(backupsParent).OrderByDescending(Directory.GetCreationTimeUtc).Take(2000)
            : Enumerable.Empty<string>();
        Manifest? selected = null;
        string? selectedFolder = null;
        foreach (var folder in candidates)
        {
            var path = CheckPath(folder, ManifestName);
            if (!File.Exists(path) || new FileInfo(path).Length > (option.IsCache ? 256 * 1024 : 32768)) continue;
            Manifest? manifest;
            try { manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path)); } catch { continue; }
            if (manifest is null || !manifest.Completed || manifest.IsRestore || manifest.RepairId != option.Id
                || !Path.GetFullPath(manifest.Root).Equals(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) continue;
            selected = manifest; selectedFolder = folder; break;
        }
        if (selected is null || selectedFolder is null) throw new FileNotFoundException("No completed backup for this repair and configuration folder was found.");
        if (selected.Files.Length == 0 || selected.Files.Length > (option.IsCache ? MaxCacheFiles : option.Files.Length)
            || selected.Files.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Files.Length)
            throw new InvalidDataException("The repair backup manifest is invalid.");
        var restoreFiles = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var bytesRemaining = option.IsCache ? MaxCacheBytes : int.MaxValue;
        foreach (var snapshot in selected.Files)
        {
            if (!IsAllowedFile(option, snapshot.Name)) throw new InvalidDataException("The backup contains an unexpected file.");
            var file = CheckPath(Path.Combine(selectedFolder, "Files"), snapshot.Name);
            var bytes = ReadBounded(file, option.IsCache ? bytesRemaining : MaxFileBytes);
            if (Hash(bytes) != snapshot.Sha256) throw new InvalidDataException("The backup failed its integrity check.");
            restoreFiles.Add(snapshot.Name, bytes);
            bytesRemaining -= bytes.Length;
        }
        var current = ReadFiles(option, root);
        var safety = Preserve(option, root, backupRoot, current, true);
        var changed = new List<string>();
        try
        {
            foreach (var (name, bytes) in restoreFiles)
            {
                token.ThrowIfCancellationRequested();
                var path = CheckPath(root, name);
                if (current.TryGetValue(name, out var original)) RequireBytes(path, original);
                else if (File.Exists(path)) throw new IOException("Configuration changed during restore.");
                changed.Add(name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                WriteAtomic(path, bytes);
                RequireBytes(path, bytes);
            }
            WriteManifest(backupRoot, safety with { Completed = true });
        }
        catch
        {
            foreach (var name in changed.AsEnumerable().Reverse())
            {
                var path = CheckPath(root, name);
                if (!File.Exists(path) || File.ReadAllBytes(path).SequenceEqual(restoreFiles[name]))
                {
                    if (current.TryGetValue(name, out var original)) WriteAtomic(path, original);
                    else if (File.Exists(path)) File.Delete(path);
                }
            }
            throw;
        }
        return option.IsCache
            ? "The backed-up rFactor 2 cache files were restored. Files that were present before Restore are preserved in this repair's backup. Retest the same track."
            : "The original configuration was restored. Files that were present before Restore are preserved in this repair's backup. Retest the same simulator session.";
    }

    private static void WriteManifest(string root, Manifest value) =>
        File.WriteAllText(CheckPath(root, ManifestName), JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void RequireBytes(string path, byte[] expected)
    {
        if (!File.Exists(path) || !ReadBounded(path, expected.Length).SequenceEqual(expected))
            throw new IOException("Configuration changed while the repair was being prepared. Nothing else will be changed.");
    }

    private static string CheckPath(string root, string name)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root) || Path.IsPathRooted(name))
            throw new InvalidDataException("A repair path is invalid.");
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var target = Path.GetFullPath(Path.Combine(fullRoot, name));
        var prefix = Path.EndsInDirectorySeparator(fullRoot) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || target == fullRoot)
            throw new InvalidDataException("A repair path is outside the expected folder.");
        for (var current = target; !string.IsNullOrWhiteSpace(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("Repair cannot follow a symbolic link or junction.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return target;
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporary = path + ".pitmedic-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
