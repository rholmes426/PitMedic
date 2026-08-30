using System.IO;

namespace PitMedic.Services;

public static class AppPaths
{
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string CommonAppData => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static string Root { get; } = Path.Combine(LocalAppData, "PitMedic");
    public static string ElevatedRoot { get; } = Path.Combine(CommonAppData, "PitMedic");
    public static string ElevatedRepairs { get; } = Path.Combine(ElevatedRoot, "RepairBackups");
    public static string ElevatedAppLog { get; } = Path.Combine(ElevatedRoot, "repair-helper.log");
    private static string LegacyRoot { get; } = Path.Combine(LocalAppData, "SimWatch");

    public static string Incidents { get; } = Path.Combine(Root, "Incidents");
    public static string Repairs { get; } = Path.Combine(Root, "Repairs");
    public static string RepairRequests { get; } = Path.Combine(Root, "RepairRequests");
    public static string SettingsFile { get; } = Path.Combine(Root, "settings.json");
    public static string StatsFile { get; } = Path.Combine(Root, "stats.json");
    public static string AnonymousUsageKeyFile { get; } = Path.Combine(Root, "anonymous-usage.key");
    public static string AnonymousUsageStateFile { get; } = Path.Combine(Root, "anonymous-usage-state.json");
    public static string UpdateCheckStateFile { get; } = Path.Combine(Root, "update-check-state.json");
    public static string SensorReport { get; } = Path.Combine(Root, "sensor-report.txt");
    public static string AppLog { get; } = Path.Combine(Root, "pitmedic.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Incidents);
        Directory.CreateDirectory(Repairs);
        Directory.CreateDirectory(RepairRequests);
        ImportLegacyDataIfNeeded();
    }

    private static void ImportLegacyDataIfNeeded()
    {
        try
        {
            if (!Directory.Exists(LegacyRoot)) return;
            CopyIfMissing(Path.Combine(LegacyRoot, "settings.json"), SettingsFile);
            CopyIfMissing(Path.Combine(LegacyRoot, "stats.json"), StatsFile);
            CopyDirectoryIfMissing(Path.Combine(LegacyRoot, "Incidents"), Incidents);
            CopyDirectoryIfMissing(Path.Combine(LegacyRoot, "Repairs"), Repairs);
        }
        catch { /* Legacy import is best-effort and must never block startup. */ }
    }

    private static void CopyIfMissing(string source, string destination)
    {
        if (File.Exists(source) && !File.Exists(destination)) File.Copy(source, destination);
    }

    private static void CopyDirectoryIfMissing(string source, string destination)
    {
        if (!Directory.Exists(source)) return;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!File.Exists(target)) File.Copy(file, target);
        }
    }
}
