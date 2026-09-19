namespace PitMedic.Models { public sealed class AppSettings { public bool? ShareAnonymousUsage { get; set; } } }
namespace PitMedic {
    internal static class AppInfo {
        public const string Version = "1.0.0.2", ReleaseChannel = "stable", AnonymousUsageEndpoint = "https://example.invalid/v1/active";
    }
}
namespace PitMedic.Services {
    public sealed class SettingsService {
        public PitMedic.Models.AppSettings Current { get; } = new();
        public event Action<PitMedic.Models.AppSettings>? SettingsChanged;
        public void Set(bool enabled) { Current.ShareAnonymousUsage = enabled; SettingsChanged?.Invoke(Current); }
    }
    public static class AppPaths {
        public static string Root { get; } = Path.Combine(Path.GetTempPath(), "pitmedic-usage-test-" + Guid.NewGuid());
        public static string SettingsFile => Path.Combine(Root, "settings.json");
        public static string AppLog => Path.Combine(Root, "app.log");
        public static string StatsFile => Path.Combine(Root, "stats.json");
        public static string AnonymousUsageKeyFile => Path.Combine(Root, "anonymous-usage.key");
        public static string AnonymousUsageStateFile => Path.Combine(Root, "anonymous-usage-state.json");
    }
    public static class AppLog { public static void Write(string message) { } }
}
