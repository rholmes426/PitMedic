using PitMedic.Models;

namespace PitMedic.Services;

public static class CompanionSoftwareKnowledgeBase
{
    public static IReadOnlyList<RepairReference> ReferencesFor(CompanionSoftwareKind kind) => kind switch
    {
        CompanionSoftwareKind.MozaPitHouse => new[]
        {
            Official("MOZA Pit House user manual", "MOZA Support", "https://support.mozaracing.com/en/support/solutions/articles/70000625635-moza-pit-house-user-manual",
                "MOZA documents the required Visual C++ runtime, updater components, and supported software installation flow."),
            Official("MOZA Pit House FAQs", "MOZA Support", "https://support.mozaracing.com/en/support/solutions/articles/70000627928-moza-pit-house-faqs",
                "MOZA documents its crash/error-report workflow and device-driver checks.")
        },
        CompanionSoftwareKind.SimucubeTrueDrive => new[]
        {
            Official("Simucube 2 True Drive releases", "Granite Devices", "https://granitedevices.com/wiki/Simucube_2_True_Drive_releases",
                "Granite Devices documents True Drive startup/runtime prerequisites and fixes included in current releases."),
            Community("True Drive update not starting", "Granite Devices Community", "https://community.granitedevices.com/t/true-drive-update-not-starting/10772",
                "A confirmed Windows blocked-file case was repaired by unblocking the downloaded executable.")
        },
        CompanionSoftwareKind.FanatecSoftware => new[]
        {
            Official("Fanatec app telemetry no longer working", "Fanatec Support", "https://help.fanatec.com/hc/en-us/articles/47862678424593-The-game-telemetry-function-of-the-Fanatec-app-is-no-longer-working",
                "Fanatec documents restarting the app as the first recovery when its telemetry feed stops or the app crashes.")
        },
        CompanionSoftwareKind.LogitechGHub => new[]
        {
            Official("G HUB freezes while loading", "Logitech Support", "https://support.logi.com/hc/en-ca/articles/360036179173-G-HUB-freezes-while-loading-and-logo-animation-loops",
                "Logitech documents closing the G HUB agent/UI processes before restarting the software."),
            Official("G HUB install/uninstall/update troubleshooting", "Logitech Support", "https://support.logi.com/hc/en-150/articles/360023192454-G-HUB-Install-Uninstall-Update-Troubleshooting",
                "Logitech documents reinstallation when process restart is not enough; PitMedic does not remove settings automatically.")
        },
        CompanionSoftwareKind.SimagicSimProManager => new[]
        {
            Official("SIMAGIC software download center", "SIMAGIC", "https://simagic.com/pages/download-center",
                "SIMAGIC publishes current SimPro Manager builds and supported device families."),
            Community("SimPro Manager suddenly turning off", "SIMAGIC Community", "https://www.reddit.com/r/Simagic/comments/1qgzd6o/simpro_manager_suddenly_turning_off_and_not/",
                "Users repeatedly report a hidden/stale SimPro process preventing relaunch; closing the stale process restores the app.")
        },
        CompanionSoftwareKind.AsetekRaceHub => new[]
        {
            Official("RaceHub troubleshooting", "Asetek Racing", "https://www.asetek.com/simsports/knowledge-base/troubleshooting/",
                "Asetek lists restarting RaceHub and updating it as the first software-side recovery steps."),
            Official("RaceHub log files", "Asetek Racing", "https://www.asetek.com/simsports/knowledge-base/how-to-find-serial-number-and-racehub-log-files/",
                "Asetek recommends collecting RaceHub logs from the affected session for unresolved issues.")
        },
        CompanionSoftwareKind.VrsDirectForce => new[]
        {
            Official("VRS DirectForce Pro", "Virtual Racing School", "https://vrs.racing/hardware",
                "VRS provides the supported DirectForce configuration software and current hardware guidance.")
        },
        _ => Array.Empty<RepairReference>()
    };

    private static RepairReference Official(string title, string source, string url, string note) => new()
    {
        Title = title,
        Source = source,
        Url = url,
        Note = note,
        IsOfficial = true
    };

    private static RepairReference Community(string title, string source, string url, string note) => new()
    {
        Title = title,
        Source = source,
        Url = url,
        Note = note,
        IsOfficial = false
    };
}
