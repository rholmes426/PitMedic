using System.IO;
using System.Text.RegularExpressions;

namespace PitMedic.Services;

public static class SteamLibraryLocator
{
    public static IReadOnlyList<string> GetLibraries()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        foreach (var candidate in new[] { Path.Combine(programFilesX86, "Steam"), Path.Combine(programFiles, "Steam") })
        {
            if (Directory.Exists(candidate)) roots.Add(candidate);
        }

        foreach (var steamRoot in roots.ToArray())
        {
            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                var text = File.ReadAllText(vdf);
                foreach (Match match in Regex.Matches(text, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    var value = match.Groups["path"].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(value)) roots.Add(value);
                }
            }
            catch { }
        }
        return roots.ToArray();
    }

    public static string? FindGameRoot(string commonFolder)
    {
        foreach (var library in GetLibraries())
        {
            var path = Path.Combine(library, "steamapps", "common", commonFolder);
            if (Directory.Exists(path)) return path;
        }
        return null;
    }

    public static string? FindLeMansUltimateRoot() => FindGameRoot("Le Mans Ultimate");
    public static string? FindAssettoCorsaEvoRoot() => FindGameRoot("Assetto Corsa EVO");
    public static string? FindRaceRoomRoot() => FindGameRoot("raceroom racing experience");
}
