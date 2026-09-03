using PitMedic.Models;

namespace PitMedic.Services;

public static class RepairCapabilityCatalog
{
    public static IReadOnlyList<RepairCapability> Capabilities { get; } = new[]
    {
        // Le Mans Ultimate
        new RepairCapability("lmu-content", "Damaged track or vehicle content", "Le Mans Ultimate", "Game & Content"),
        new RepairCapability("lmu-shader", "Shader/cache corruption", "Le Mans Ultimate", "Configuration"),
        new RepairCapability("lmu-startup", "DX11/startup configuration corruption", "Le Mans Ultimate", "Configuration"),
        new RepairCapability("lmu-plugins", "Plugin conflicts", "Le Mans Ultimate", "Configuration"),
        new RepairCapability("lmu-eac-capability", "Easy Anti-Cheat problems", "Le Mans Ultimate", "Services & System"),
        new RepairCapability("lmu-reshade", "ReShade / DirectX hook conflicts", "Le Mans Ultimate", "Configuration"),
        new RepairCapability("lmu-clock", "Windows clock synchronization problems", "Le Mans Ultimate", "Services & System"),
        new RepairCapability("lmu-overlay", "MSI Afterburner / RTSS conflicts", "Le Mans Ultimate", "Configuration"),
        new RepairCapability("lmu-ghub", "Logitech G HUB conflicts", "Le Mans Ultimate", "Services & System"),

        // iRacing
        new RepairCapability("iracing-service", "Helper Service / System Not in Service", "iRacing", "Services & System"),
        new RepairCapability("iracing-ui-cache-capability", "Corrupt Electron UI cache", "iRacing", "Configuration"),
        new RepairCapability("iracing-uisafe-capability", "UISafe / Welcome to iRacing problem", "iRacing", "Configuration"),
        new RepairCapability("iracing-eac-capability", "Easy Anti-Cheat failures", "iRacing", "Services & System"),
        new RepairCapability("iracing-update-capability", "Update verification loops/failures", "iRacing", "Game & Content"),
        new RepairCapability("iracing-updater-capability", "Updater waiting for iRacingService", "iRacing", "Services & System"),
        new RepairCapability("iracing-track-capability", "Track loading/content corruption", "iRacing", "Game & Content"),
        new RepairCapability("iracing-car-capability", "Car loading/content corruption", "iRacing", "Game & Content"),
        new RepairCapability("iracing-error49-capability", "Loading Error 49 / Steam track corruption", "iRacing", "Game & Content"),
        new RepairCapability("iracing-lock-capability", "Steam Content File Locked", "iRacing", "Game & Content"),
        new RepairCapability("iracing-privileges-capability", "Steam Missing File Privileges", "iRacing", "Game & Content"),
        new RepairCapability("iracing-renderer-capability", "rendererDX11 configuration corruption", "iRacing", "Configuration"),
        new RepairCapability("iracing-trueforce-capability", "Already running / Logitech TrueForce shutdown conflict", "iRacing", "Services & System"),
        new RepairCapability("iracing-error3-capability", "Loading Error 3 / damaged user configuration", "iRacing", "Configuration"),
        new RepairCapability("iracing-compat-capability", "CreateProcessAsUser / compatibility-mode problems", "iRacing", "Services & System"),
        new RepairCapability("iracing-signature-capability", "Digital Signature Failed / updater failure", "iRacing", "Services & System"),
        new RepairCapability("iracing-windows-capability", "Repeated 0xc0000005 / Windows system-file corruption", "iRacing", "Services & System"),

        // Assetto Corsa EVO
        new RepairCapability("ace-video-capability", "Broken video settings / graphics startup state", "Assetto Corsa EVO", "Configuration"),
        new RepairCapability("ace-profile-capability", "Corrupt ACE user profile / launch state", "Assetto Corsa EVO", "Configuration"),
        new RepairCapability("ace-content-capability", "Missing or damaged Steam game files", "Assetto Corsa EVO", "Game & Content"),

        // RaceRoom Racing Experience
        new RepairCapability("raceroom-browser-capability", "Browser cache / Error 503 / UI loading problems", "RaceRoom", "Configuration"),
        new RepairCapability("raceroom-graphics-capability", "Broken graphics / resolution configuration", "RaceRoom", "Configuration"),
        new RepairCapability("raceroom-shader-capability", "Shader cache corruption", "RaceRoom", "Configuration"),
        new RepairCapability("raceroom-profile-capability", "Damaged RaceRoom user configuration", "RaceRoom", "Configuration"),
        new RepairCapability("raceroom-content-capability", "Missing or damaged Steam game files", "RaceRoom", "Game & Content"),

        // Assetto Corsa Competizione
        new RepairCapability("acc-game-user-settings-capability", "Broken display / startup settings", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-engine-config-capability", "Engine / graphics configuration corruption", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-controls-capability", "Controller / wheel configuration corruption", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-ffb-capability", "Force-feedback settings corruption", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-trueforce-capability", "Logitech TrueForce / manufacturer-extras crash", "Assetto Corsa Competizione", "Services & System"),
        new RepairCapability("acc-control-presets-capability", "Saved control preset causing Controls-menu crash", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-profile-capability", "Damaged ACC user configuration", "Assetto Corsa Competizione", "Configuration"),
        new RepairCapability("acc-content-capability", "Missing or damaged Steam game files", "Assetto Corsa Competizione", "Game & Content"),

        // Automobilista 2
        new RepairCapability("ams2-graphics-capability", "Broken graphics / display configuration", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-vr-capability", "Broken OpenVR / Oculus configuration", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-controller-capability", "Controller / wheel configuration corruption", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-ffb-capability", "Custom FFB configuration corruption", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-tuning-capability", "Stale / incompatible car setup data", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-default-profile-capability", "Damaged default profile settings", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-championship-capability", "Corrupted custom championship state", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-profile-capability", "Damaged AMS2 user profile", "Automobilista 2", "Configuration"),
        new RepairCapability("ams2-content-capability", "Missing or damaged Steam game files", "Automobilista 2", "Game & Content"),

        // Detected companion software
        new RepairCapability("companion-moza-clean-recovery", "Pit House crash / stale-process clean recovery", "MOZA Pit House", "Companion Software"),
        new RepairCapability("companion-simucube-clean-recovery", "True Drive crash / stale-process clean recovery", "Simucube True Drive", "Companion Software"),
        new RepairCapability("companion-fanatec-process-recovery", "Fanatec app / Control Panel / FanaLab recovery", "Fanatec software", "Companion Software"),
        new RepairCapability("companion-logitech-ghub-service-recovery", "G HUB loading-loop / updater-service recovery", "Logitech G HUB", "Companion Software"),
        new RepairCapability("companion-simagic-clean-recovery", "SimPro 2/3 conflict / daemon recovery", "SIMAGIC SimPro Manager", "Companion Software"),
        new RepairCapability("companion-asetek-clean-recovery", "RaceHub app / elevated-helper recovery", "Asetek RaceHub", "Companion Software"),
        new RepairCapability("companion-vrs-clean-recovery", "DirectForce configuration-app recovery", "VRS DirectForce", "Companion Software"),
    };

    public static int AutomatedFixCount => Capabilities.Count;
}
