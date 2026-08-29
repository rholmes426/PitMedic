namespace PitMedic;

internal static class AppInfo
{
    public const string DeveloperName = "Bobby Holmes";
    public const string DeveloperEmail = "bobbyholmes@gmail.com";
    public const string SupportUrl = "https://paypal.me/PitMedicApp";
    public const string ProjectUrl = "https://github.com/rholmes426/PitMedic";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "Unknown";
}
