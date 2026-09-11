using Microsoft.Win32;
using System.Management;
using System.Text;

namespace PitMedic.Services;

internal static class DriverPrerequisites
{
    public static bool PawnIORegistered()
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var package = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
            using var driver = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\PawnIO");
            if (package?.GetValue("DisplayVersion") is string && driver is not null) return true;
        }
        return false;
    }

    public static string GetReport()
    {
        var text = new StringBuilder();
        try
        {
            text.AppendLine(PawnIORegistered()
                ? "CPU sensor driver: registered. Live sensor readings still determine whether it is working."
                : "CPU sensor driver is missing or incomplete. Rerun the PitMedic installer to set up PawnIO.");
            using var search = new ManagementObjectSearcher(
                "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            using var devices = search.Get();
            foreach (ManagementObject device in devices)
            {
                using (device)
                    text.AppendLine($"Device needs attention: {device["Name"]} (Windows code {device["ConfigManagerErrorCode"]}). Check Windows Update or your PC manufacturer's support page.");
            }
            using var graphics = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var adapters = graphics.Get();
            foreach (ManagementObject adapter in adapters)
            {
                using (adapter)
                    if (adapter["Name"]?.ToString()?.Contains("Microsoft Basic Display", StringComparison.OrdinalIgnoreCase) == true)
                        text.AppendLine("Generic display driver detected. Install the graphics driver from Windows Update or your PC/GPU manufacturer for GPU monitoring.");
            }
            text.AppendLine("PitMedic includes its .NET runtime. Simulator-specific DirectX and Visual C++ prerequisites are managed by the simulator or its launcher.");
        }
        catch (Exception ex)
        {
            text.AppendLine($"Driver checks could not finish: {ex.Message}");
        }
        return text.ToString();
    }
}
