using System.Diagnostics;
using System.Text.RegularExpressions;
using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>Original Assetto Corsa only. The launcher and Content Manager are not race sessions.</summary>
public static class AssettoCorsaPolicy
{
    public const string SteamAppId = "244210";
    public static string DocumentsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Assetto Corsa");
    public static string ConfigurationRoot => Path.Combine(DocumentsRoot, "cfg");
    private static readonly string[] ConfigurationWriters = { "acs", "acs_x86", "AssettoCorsa", "Content Manager", "ContentManager" };

    public static bool MatchesEngineEvidence(string message) =>
        Regex.IsMatch(message, @"(?<![\w.-])acs(?:_x86)?\.exe(?![\w.])", RegexOptions.IgnoreCase);

    public static bool IsEngineDumpFile(string name) =>
        Regex.IsMatch(name, @"^acs(?:_x86)?(?:\.exe)?(?:[._-][0-9]+)?\.dmp$", RegexOptions.IgnoreCase);

    public static (string Id, string Category, string Message)? MatchLog(string line)
    {
        // Normal logs print entire INI files. A filename alone, or a failure on another line,
        // must not turn that diagnostic output into a settings reset.
        if (!Regex.IsMatch(line, @"\b(error|failed|invalid|corrupt|unable|cannot|can't)\b", RegexOptions.IgnoreCase)) return null;
        if (Regex.IsMatch(line, @"(?<![\w.])video\.ini(?![\w.])", RegexOptions.IgnoreCase))
            return ("ac-video-config", "Assetto Corsa display configuration failure", line);
        if (Regex.IsMatch(line, @"(?<![\w.])controls\.ini(?![\w.])", RegexOptions.IgnoreCase))
            return ("ac-controls-config", "Assetto Corsa controls configuration failure", line);
        return null;
    }

    public static RepairPlan? PlanFromEvidence(IEnumerable<string> evidence)
    {
        foreach (var line in evidence)
        {
            var match = MatchLog(line);
            if (match is not null) return ScopedConfigurationRepair.CreatePlan(GameKind.AssettoCorsa, match.Value.Id);
        }
        return null;
    }

    public static void EnsureConfigurationWritersClosed()
    {
        foreach (var name in ConfigurationWriters)
        {
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch { throw new InvalidOperationException("Assetto Corsa's process state could not be checked. Close the game, launcher and Content Manager, then retry."); }
            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                            throw new InvalidOperationException("Close Assetto Corsa, its launcher and Content Manager before repairing its files.");
                    }
                    catch (System.ComponentModel.Win32Exception)
                    { throw new InvalidOperationException("Assetto Corsa's process state could not be checked. Close the game, launcher and Content Manager, then retry."); }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
    }
}
