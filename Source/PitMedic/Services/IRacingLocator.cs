using Microsoft.Win32;

namespace PitMedic.Services;

public static class IRacingLocator
{
    public static string? FindRoot()
    {
        foreach (var candidate in DirectCandidates())
            if (LooksLikeIRacing(candidate)) return candidate;

        foreach (var library in SteamLibraryLocator.GetLibraries())
        {
            var candidate = Path.Combine(library, "steamapps", "common", "iRacing");
            if (LooksLikeIRacing(candidate)) return candidate;
        }
        return null;
    }

    public static bool IsSteamInstall(string root)
    {
        try
        {
            var full = Path.GetFullPath(root);
            return SteamLibraryLocator.GetLibraries().Any(l =>
                full.StartsWith(Path.GetFullPath(Path.Combine(l, "steamapps", "common")) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch { return root.Contains("steamapps", StringComparison.OrdinalIgnoreCase); }
    }

    private static IReadOnlyList<string> DirectCandidates()
    {
        var result = new List<string>();
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        result.Add(Path.Combine(pf86, "iRacing"));
        result.Add(Path.Combine(pf, "iRacing"));

        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var subPath in new[]
                {
                    @"SOFTWARE\iRacing.com\iRacing.com Simulator",
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\iRacing.com Race Simulation"
                })
                {
                    using var key = baseKey.OpenSubKey(subPath);
                    var value = key?.GetValue("InstallLocation")?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) result.Add(value.TrimEnd('\\', '/'));
                }
            }
            catch { }
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool LooksLikeIRacing(string path)
    {
        try
        {
            return Directory.Exists(path)
                && (File.Exists(Path.Combine(path, "iRacingService64.exe"))
                    || File.Exists(Path.Combine(path, "Start_iRacingService.bat"))
                    || File.Exists(Path.Combine(path, "UI", "iRacingUI.exe")));
        }
        catch { return false; }
    }
}
