using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PitMedic.Services;

internal static class WindowsVersionInfo
{
    private const int FirstWindows11Build = 22_000;

    public static string GetDisplayName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var productName = key?.GetValue("ProductName") as string;
            var buildText = key?.GetValue("CurrentBuildNumber") as string
                ?? key?.GetValue("CurrentBuild") as string;

            var build = int.TryParse(buildText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedBuild)
                ? parsedBuild
                : Environment.OSVersion.Version.Build;

            var friendlyName = NormalizeProductName(productName, build);
            return build > 0 ? $"{friendlyName} (build {build})" : friendlyName;
        }
        catch
        {
            var build = Environment.OSVersion.Version.Build;
            var friendlyName = NormalizeProductName(RuntimeInformation.OSDescription, build);
            return build > 0 ? $"{friendlyName} (build {build})" : friendlyName;
        }
    }

    private static string NormalizeProductName(string? productName, int build)
    {
        var name = productName?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = build >= FirstWindows11Build ? "Windows 11" : "Windows 10";

        if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase))
            name = name["Microsoft ".Length..];

        // Some Windows 11 systems retain "Windows 10" in this compatibility
        // registry value. The Windows 11 build boundary is authoritative here.
        if (build >= FirstWindows11Build
            && name.StartsWith("Windows 10", StringComparison.OrdinalIgnoreCase))
            name = $"Windows 11{name["Windows 10".Length..]}";

        // RuntimeInformation can return the NT kernel version rather than a
        // product name (for example, "Windows 10.0.22631").
        if (name.StartsWith("Windows 10.0", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Windows 11.0", StringComparison.OrdinalIgnoreCase))
            name = build >= FirstWindows11Build ? "Windows 11" : "Windows 10";

        return name;
    }
}
