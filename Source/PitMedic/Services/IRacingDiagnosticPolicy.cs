using PitMedic.Models;

namespace PitMedic.Services;

public static class IRacingDiagnosticPolicy
{
    public const string StatusOnlyCategory = "iRacing installation status check";

    // An installation probe is not evidence that a simulator launch was attempted or failed.
    // Match the known status payload narrowly; appended launch errors remain actionable.
    public static bool IsInstallationProbe(string text)
    {
        const string marker = "CheckIfAntiCheatInstalledForIRacing:";
        var start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        var payload = text[(start + marker.Length)..];
        var signature = payload.IndexOf("[PitMedic diagnostic signature:", StringComparison.OrdinalIgnoreCase);
        if (signature >= 0) payload = payload[..signature];
        return payload.Trim().TrimEnd('.').Equals(
            "AntiCheat is not installed for iRacing", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsStatusOnlyFinding(IncidentRecord record) =>
        record.Game.Equals("iRacing", StringComparison.OrdinalIgnoreCase)
        && (record.ExitCode is null or 0)
        && (record.Classification.Category.Equals("Easy Anti-Cheat failure", StringComparison.OrdinalIgnoreCase)
            || record.Classification.Category == StatusOnlyCategory)
        && record.Classification.Evidence.Count > 0
        && record.Classification.Evidence.All(IsInstallationProbe);

    public static IncidentRecord Reassess(IncidentRecord record) => IsStatusOnlyFinding(record)
        && (record.RecommendedRepair is not null || record.Classification.Category != StatusOnlyCategory)
        ? record with
        {
            RecommendedRepair = null,
            Classification = new CrashClassification(StatusOnlyCategory, 100,
                "iRacing logged an installation status check. This message alone does not establish a failed launch, so no repair is recommended. The original evidence has been retained.",
                record.Classification.Evidence)
        }
        : record;
}
