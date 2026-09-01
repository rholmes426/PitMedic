namespace PitMedic.Services;

public static class ElevatedRepairPolicy
{
    private static readonly HashSet<string> ElevatedRepairIds = new(StringComparer.OrdinalIgnoreCase)
    {
        // Le Mans Ultimate files beneath the Steam installation.
        "lmu-targeted-content-reacquire",
        "lmu-shader-cache",
        "lmu-reset-dx11-config",
        "lmu-disable-plugins",
        "lmu-reinstall-eac",
        "lmu-quarantine-reshade",

        // Windows system operation.
        "lmu-sync-windows-time",

        // iRacing installation files, service helpers, and Windows repair tools.
        "iracing-helper-service",
        "iracing-ui-cache",
        "iracing-eac-reinstall",
        "iracing-update-reset",
        "iracing-updater-autoinstall",
        "iracing-track-index-reset",
        "iracing-car-index-reset",
        "iracing-steam-track-repair",
        "iracing-release-content-lock",
        "iracing-run-updater",
        "iracing-windows-integrity",

        // Logitech's official G HUB loading-loop recovery restarts its Windows service.
        "companion-logitech-ghub-service-recovery"
    };

    public static IReadOnlyCollection<string> RepairIds => ElevatedRepairIds;

    public static bool RequiresElevation(string repairId) =>
        !string.IsNullOrWhiteSpace(repairId) && ElevatedRepairIds.Contains(repairId);
}
