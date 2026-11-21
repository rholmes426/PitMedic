using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

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

        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steamPath
                    && Path.IsPathFullyQualified(steamPath) && Directory.Exists(steamPath))
                    roots.Add(steamPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
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
    public static bool HasRFactor2Install() => GetLibraries().Any(library =>
        File.Exists(Path.Combine(library, "steamapps", "appmanifest_365960.acf")));

    public static string FindRFactor2RootForRepair()
    {
        var found = GetLibraries()
            .Where(library => File.Exists(Path.Combine(library, "steamapps", "appmanifest_365960.acf")))
            .Select(library => Path.GetFullPath(Path.Combine(library, "steamapps", "common", "rFactor 2")))
            .Where(root => File.Exists(Path.Combine(root, "Bin64", "rFactor2.exe")))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2).ToArray();
        return found.Length switch
        {
            1 => found[0],
            0 => throw new DirectoryNotFoundException("PitMedic could not find the installed Steam rFactor 2 client."),
            _ => throw new InvalidOperationException("PitMedic found more than one rFactor 2 installation and could not safely select one.")
        };
    }

    public static string? FindRaceRoomRoot() => FindGameRoot("raceroom racing experience");
}
