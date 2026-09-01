namespace PitMedic.Models;

public enum CompanionSoftwareKind
{
    MozaPitHouse,
    SimucubeTrueDrive,
    FanatecSoftware,
    LogitechGHub,
    SimagicSimProManager,
    AsetekRaceHub,
    VrsDirectForce
}

public sealed record CompanionSoftwareDefinition(
    CompanionSoftwareKind Kind,
    string DisplayName,
    string ExecutableName,
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> RecoveryProcessNames,
    IReadOnlyList<string> InstallDisplayNames,
    IReadOnlyList<string> DefaultExecutablePaths)
{
    public static IReadOnlyList<CompanionSoftwareDefinition> Supported { get; } = new[]
    {
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.MozaPitHouse,
            "MOZA Pit House",
            "MOZA Pit House.exe",
            new[] { "MOZA Pit House", "MOZAPitHouse" },
            new[] { "MOZA Pit House", "MOZAPitHouse" },
            new[] { "MOZA Pit House" },
            DefaultPaths(Path.Combine("MOZA Pit House", "MOZA Pit House.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.SimucubeTrueDrive,
            "Simucube True Drive",
            "Simucube 2 True Drive.exe",
            new[] { "Simucube 2 True Drive", "True Drive" },
            new[] { "Simucube 2 True Drive", "True Drive" },
            new[] { "Simucube 2 True Drive", "Simucube True Drive" },
            DefaultPaths(
                Path.Combine("Simucube", "Simucube 2 True Drive.exe"),
                Path.Combine("Granite Devices", "Simucube 2 True Drive.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.FanatecSoftware,
            "Fanatec software",
            "FanatecControlPanel.exe",
            new[] { "FanatecControlPanel", "Fanatec App", "FanatecApp", "FanaLab" },
            new[] { "FanatecControlPanel", "Fanatec App", "FanatecApp", "FanaLab" },
            new[] { "Fanatec Control Panel", "Fanatec App", "Fanatec Wheel", "FanaLab" },
            DefaultPaths(
                Path.Combine("Fanatec", "Fanatec Wheel", "ui", "FanatecControlPanel.exe"),
                Path.Combine("Fanatec", "Fanatec App", "Fanatec App.exe"),
                Path.Combine("Fanatec", "FanaLab", "FanaLab.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.LogitechGHub,
            "Logitech G HUB",
            "lghub.exe",
            new[] { "lghub", "lghub_agent" },
            new[] { "lghub_agent", "lghub" },
            new[] { "Logitech G HUB", "Logitech G HUB Software" },
            DefaultPaths(
                Path.Combine("LGHUB", "lghub.exe"),
                Path.Combine("LGHUB", "lghub_agent.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.SimagicSimProManager,
            "SIMAGIC SimPro Manager",
            "simpro.exe",
            new[] { "simpro", "SimProManager2", "SimPro Manager 2", "SimProManager3", "SimProManager" },
            new[] { "simpro", "SimProManager2", "SimPro Manager 2", "SimProManager3", "SimProManager", "SimProDaemon" },
            new[] { "SimPro Manager", "SimProManager" },
            DefaultPaths(
                Path.Combine("simpro", "bin", "simpro.exe"),
                Path.Combine("SIMAGIC", "SimProManager2", "SimProManager2.exe"),
                Path.Combine("SimProManager2", "SimProManager2.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.AsetekRaceHub,
            "Asetek RaceHub",
            "RaceHub.exe",
            new[] { "RaceHub" },
            new[] { "RaceHub", "RaceHubElevater" },
            new[] { "RaceHub", "Asetek SimSports" },
            DefaultPaths(
                Path.Combine("Asetek SimSports", "RaceHub", "Application Folder", "RaceHub.exe"),
                Path.Combine("Asetek SimSports", "RaceHub™", "Application Folder", "RaceHub.exe"))),
        new CompanionSoftwareDefinition(
            CompanionSoftwareKind.VrsDirectForce,
            "VRS DirectForce",
            "VRSOne.exe",
            new[] { "VRSOne", "VRS DirectForce Pro Config Tool" },
            new[] { "VRSOne", "VRS DirectForce Pro Config Tool" },
            new[] { "VRS One", "VRS DirectForce", "DirectForce Pro" },
            DefaultPaths(
                Path.Combine("VRS", "VRS One", "VRSOne.exe"),
                Path.Combine("VRS", "DirectForce Pro", "VRSOne.exe")))
    };

    private static IReadOnlyList<string> DefaultPaths(params string[] relativePaths)
    {
        var paths = new List<string>();
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void Add(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return;
            foreach (var relativePath in relativePaths)
                paths.Add(Path.Combine(root, relativePath));
        }
    }
}

public sealed record CompanionSoftwareStatus(
    CompanionSoftwareKind Kind,
    string DisplayName,
    bool IsDetected,
    bool IsRunning);
