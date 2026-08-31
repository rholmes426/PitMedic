namespace PitMedic;

internal static class AppInfo
{
    public const string ProjectName = "PitMedic Project";
    public const string ContactEmail = "robert@pitmedic.com";
    public const string SupportUrl = "https://paypal.me/PitMedicApp";
    public const string ProjectUrl = "https://github.com/rholmes426/PitMedic";
    public const string PrivacyUrl = "https://github.com/rholmes426/PitMedic/blob/main/Source/PRIVACY.md";
    public const string UpdateManifestUrl = "https://pitmedic.com/update.json";

    public const string AnonymousUsageEndpoint = "https://pitmedic-usage.pitmedic-usage-telemetry.workers.dev/v1/active";
    public const string LapBenchmarkEndpoint = "https://pitmedic-usage.pitmedic-usage-telemetry.workers.dev/v1/lap-benchmark";

    public static string Version =>
        typeof(AppInfo).Assembly.GetName().Version?.ToString() ?? "Unknown";

    public static string ReleaseChannel =>
        typeof(AppInfo).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), inherit: false)
            .OfType<System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "PitMedicReleaseChannel")?.Value
        ?? "preview";
}
