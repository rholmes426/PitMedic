using PitMedic.Models;

namespace PitMedic.Services;

public sealed record CompanionRecoveryDefinition(
    CompanionSoftwareKind Kind,
    string RepairId,
    string Title,
    string AutomaticCoverage,
    string Summary,
    IReadOnlyList<string> Steps,
    bool RequiresElevation = false,
    string WindowsServiceName = "");

public static class CompanionRecoveryPolicy
{
    public static IReadOnlyList<CompanionRecoveryDefinition> Supported { get; } = new[]
    {
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.MozaPitHouse,
            "companion-moza-clean-recovery",
            "Recover MOZA Pit House",
            "Confirmed crash or stale-process clean recovery",
            "PitMedic closes only remaining Pit House processes, relaunches the validated installed app, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close remaining MOZA Pit House processes",
                "Relaunch the validated Pit House executable",
                "Verify that Pit House remains running"
            }),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.SimucubeTrueDrive,
            "companion-simucube-clean-recovery",
            "Recover Simucube True Drive",
            "Confirmed crash or stale-process clean recovery",
            "PitMedic closes only remaining True Drive processes, relaunches the validated installed app, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close remaining True Drive processes",
                "Relaunch the validated True Drive executable",
                "Verify that True Drive remains running"
            }),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.FanatecSoftware,
            "companion-fanatec-process-recovery",
            "Recover Fanatec software",
            "Confirmed app fault and process-set recovery",
            "PitMedic closes the Fanatec app, Control Panel, and FanaLab process set, relaunches the validated installed app, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close the Fanatec app, Control Panel, and FanaLab process set",
                "Relaunch the validated Fanatec executable",
                "Verify that the Fanatec software remains running"
            }),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.LogitechGHub,
            "companion-logitech-ghub-service-recovery",
            "Recover Logitech G HUB",
            "Loading-loop, agent-fault, and updater-service recovery",
            "PitMedic follows Logitech's loading-loop recovery order: close the G HUB UI and agent, restart the G HUB updater service, relaunch G HUB, and verify that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close the G HUB UI and agent without deleting settings",
                "Restart the Logitech G HUB updater service",
                "Relaunch the validated G HUB executable",
                "Verify that G HUB remains running"
            },
            RequiresElevation: true,
            WindowsServiceName: "LGHUBUpdaterService"),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.SimagicSimProManager,
            "companion-simagic-clean-recovery",
            "Recover SIMAGIC SimPro Manager",
            "SimPro 2/3 conflict, daemon, crash, and stale-process recovery",
            "PitMedic closes the known SimPro 2, SimPro 3, and SimPro daemon process set, relaunches the validated installed generation, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close the known SimPro 2, SimPro 3, and daemon processes",
                "Relaunch the validated SimPro executable",
                "Verify that SimPro Manager remains running"
            }),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.AsetekRaceHub,
            "companion-asetek-clean-recovery",
            "Recover Asetek RaceHub",
            "RaceHub app and elevated-helper clean recovery",
            "PitMedic closes the RaceHub app and its known elevated helper, relaunches the validated installed app, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close RaceHub and its known elevated helper",
                "Relaunch the validated RaceHub executable",
                "Verify that RaceHub remains running"
            }),
        new CompanionRecoveryDefinition(
            CompanionSoftwareKind.VrsDirectForce,
            "companion-vrs-clean-recovery",
            "Recover VRS DirectForce",
            "Confirmed configuration-app crash or stale-process recovery",
            "PitMedic closes only the VRS configuration-app process, relaunches the validated installed app, and verifies that it stays running.",
            new[]
            {
                "Confirm every supported simulator is closed",
                "Close the remaining VRS configuration-app process",
                "Relaunch the validated VRS executable",
                "Verify that the VRS app remains running"
            })
    };

    public static CompanionRecoveryDefinition For(CompanionSoftwareKind kind) =>
        Supported.First(item => item.Kind == kind);

    public static bool IsSupportedRepairId(string repairId) =>
        !string.IsNullOrWhiteSpace(repairId)
        && Supported.Any(item => item.RepairId.Equals(repairId, StringComparison.OrdinalIgnoreCase));
}
