namespace PitMedic.Services;

internal enum WindowsHealthIssue
{
    AudioServiceStopped, DnsTimeouts, FileSystemCorruption, StorageRetries, HardwareError, DisplayReset, UpdateInstallFailure
}

internal sealed record WindowsHealthEvent(string Provider, int Id, int? Level, DateTimeOffset At);
internal sealed record WindowsHealthFinding(WindowsHealthIssue Issue, int Occurrences, DateTimeOffset At, string Guidance);
internal sealed record WindowsHealthSummary(bool LogAvailable, bool AudioStatusAvailable, IReadOnlyList<WindowsHealthFinding> Findings);

internal static class WindowsHealthPolicy
{
    private sealed record Rule(WindowsHealthIssue Issue, string[] Providers, int? Id, int? MaximumLevel, int Minimum,
        TimeSpan Window, string Guidance)
    {
        public bool Matches(WindowsHealthEvent item) => Providers.Contains(item.Provider, StringComparer.OrdinalIgnoreCase)
            && (Id is null || item.Id == Id)
            && (MaximumLevel is null || item.Level is >= 1 && item.Level <= MaximumLevel);
    }

    private static readonly Rule[] Rules =
    [
        new(WindowsHealthIssue.FileSystemCorruption, ["Microsoft-Windows-Ntfs", "Ntfs"], 55, null, 1, TimeSpan.FromHours(2),
            "Windows logged NTFS corruption (event 55). Back up important files and check the affected volume in Event Viewer before deciding on a disk repair."),
        new(WindowsHealthIssue.StorageRetries, ["Disk"], 153, null, 2, TimeSpan.FromHours(2),
            "Windows repeatedly retried storage I/O (event 153). Back up important files and check the affected drive, connection and vendor diagnostics; this does not establish drive failure."),
        new(WindowsHealthIssue.HardwareError, ["Microsoft-Windows-WHEA-Logger"], null, 2, 1, TimeSpan.FromHours(2),
            "Windows logged a hardware error. Review the WHEA-Logger event in Event Viewer and the device maker's diagnostics before changing hardware or firmware settings."),
        new(WindowsHealthIssue.DisplayReset, ["Display"], 4101, null, 1, TimeSpan.FromHours(2),
            "Windows reported that the display driver stopped responding and recovered (event 4101). Check display-driver update history and whether resets recur under the same workload."),
        new(WindowsHealthIssue.UpdateInstallFailure, ["Microsoft-Windows-WindowsUpdateClient"], 20, null, 1, TimeSpan.FromHours(2),
            "Windows logged an update installation failure (event 20). In Windows Update history, check the failed KB and error code and whether a later attempt succeeded."),
        new(WindowsHealthIssue.DnsTimeouts, ["Microsoft-Windows-DNS-Client"], 1014, null, 2, TimeSpan.FromMinutes(10),
            "Windows logged repeated DNS timeouts (event 1014). Check whether several services fail and whether the configured DNS resolver, router or VPN is reachable; a cache reset is not indicated by timeout evidence alone.")
    ];

    // Fixed, provider-scoped queries; never collect event bodies, device paths, hostnames or IP addresses.
    internal static IEnumerable<string> Queries => Rules.Select(rule =>
    {
        var provider = string.Join(" or ", rule.Providers.Select(name => $"Provider[@Name='{name}']"));
        var id = rule.Id is int value ? $" and EventID={value}" : "";
        var level = rule.MaximumLevel is int maximum ? $" and Level>=1 and Level<={maximum}" : "";
        return $"*[System[({provider}){id}{level} and TimeCreated[timediff(@SystemTime) <= {(long)rule.Window.TotalMilliseconds}]]]";
    });

    public static WindowsHealthSummary Evaluate(IReadOnlyList<WindowsHealthEvent>? events, bool? audioStopped,
        bool previouslyStopped, DateTimeOffset now)
    {
        var findings = new List<WindowsHealthFinding>();
        if (audioStopped == true && previouslyStopped)
            findings.Add(new(WindowsHealthIssue.AudioServiceStopped, 2, now,
                "A Windows audio service configured to start automatically is still stopped. If sound is missing, open Home → Windows repair tools → Start stopped Windows audio services to review and approve the recovery."));

        foreach (var rule in Rules)
        {
            var matches = events?.Where(e => e.At >= now - rule.Window && e.At <= now && rule.Matches(e)).ToArray();
            if (matches is not null && matches.Length >= rule.Minimum)
                findings.Add(new(rule.Issue, matches.Length, matches.Max(e => e.At), rule.Guidance));
        }
        return new(events is not null, audioStopped.HasValue, findings);
    }
}
