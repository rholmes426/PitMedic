using PitMedic.Models;

namespace PitMedic.Services;

public static class WindowsRepairPolicy
{
    public const string Product = "Windows";
    public const string IntegrityCheckId = "windows-integrity-check";
    public const string IntegrityRepairId = "windows-system-file-integrity";
    public const string DnsCacheId = "windows-dns-cache-recovery";
    public const string AudioServiceId = "windows-audio-service-recovery";
    public const string ClockSyncId = "windows-time-sync";

    public static IReadOnlyList<RepairPlan> Options { get; } = new[]
    {
        Plan(IntegrityCheckId, "Check Windows system files", "Scan the Windows component store and verify protected system files. This can take a while. Review the saved diagnostic output before choosing any repair.", 20,
            "Check the component store with DISM", "Verify protected files with SFC", "Save the command results for review"),
        Plan(IntegrityRepairId, "Repair Windows system files", "If Windows integrity checks reported corruption, repair the component store with DISM and system files with SFC. This affects Windows, may need downloads and a restart, and has no PitMedic file Restore.", 20,
            "Repair the Windows component store", "Run System File Checker", "Record results and restart requirements"),
        Plan(DnsCacheId, "Refresh Windows DNS cache", "Clear the local Windows DNS resolver cache if websites, launchers or game services fail to resolve a name. The cache rebuilds automatically. DNS server, network adapter and firewall settings are preserved.", 1,
            "Clear the local DNS resolver cache", "Record the Windows response", "Retry the original service or website"),
        Plan(AudioServiceId, "Start stopped Windows audio services", "Check Windows Audio and Windows Audio Endpoint Builder and start either service if it is stopped and enabled. Running services, volume, device routing and startup settings are preserved.", 1,
            "Check both audio services", "Start only stopped, enabled services", "Verify running state and retest sound"),
        Plan(ClockSyncId, "Synchronize Windows clock", "Request a time resynchronization using the configured Windows time source. Use this if the computer clock is incorrect. Time zone and time-server configuration are preserved.", 1,
            "Request synchronization", "Query Windows time-service status", "Review the clock and retest")
    };

    public static RepairPlan? CreatePlan(string id) => Options.FirstOrDefault(option => option.Id == id);
    public static bool IsSupported(string id) => CreatePlan(id) is not null;

    public static RepairPlan? FromRequest(IncidentRecord incident)
    {
        if (incident.Game != Product || incident.Classification.Category != ScopedConfigurationRepair.RequestCategory
            || incident.Classification.Evidence.Count != 1) return null;
        var line = incident.Classification.Evidence[0];
        return line.StartsWith(ScopedConfigurationRepair.RequestPrefix, StringComparison.Ordinal)
            ? CreatePlan(line[ScopedConfigurationRepair.RequestPrefix.Length..])
            : null;
    }

    private static RepairPlan Plan(string id, string title, string summary, int minutes, params string[] steps) => new()
    {
        Id = id, Title = title, Summary = summary, Game = Product,
        RequiresApproval = true, Safety = RepairSafety.Significant, EstimatedMinutes = minutes, Steps = steps,
        References = RepairKnowledgeBase.ReferencesForPlan(id)
    };
}
