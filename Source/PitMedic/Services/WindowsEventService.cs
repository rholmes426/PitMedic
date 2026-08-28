using System.Diagnostics.Eventing.Reader;
using PitMedic.Models;

namespace PitMedic.Services;

public sealed class WindowsEventService
{
    private static readonly HashSet<string> InterestingProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Application Error", "Windows Error Reporting", "Display", "nvlddmkm", "amdwddmg",
        "WHEA-Logger", "Kernel-Power", "Kernel-PnP", "DriverFrameworks-UserMode"
    };

    public IReadOnlyList<WindowsEventEvidence> GetAround(DateTimeOffset center, string executable, TimeSpan window)
    {
        var list = new List<WindowsEventEvidence>();
        foreach (var log in new[] { "Application", "System" })
        {
            try
            {
                var query = new EventLogQuery(log, PathType.LogName) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                for (var i = 0; i < 350; i++)
                {
                    using var record = reader.ReadEvent();
                    if (record is null) break;
                    if (record.TimeCreated is not DateTime created) continue;
                    var when = new DateTimeOffset(created);
                    if (when < center - window) break;
                    if (when > center + window) continue;

                    var provider = record.ProviderName ?? "Unknown";
                    var message = SafeMessage(record);
                    var mentionsGame = message.Contains(executable, StringComparison.OrdinalIgnoreCase);
                    var providerInteresting = InterestingProviders.Contains(provider);
                    var idInteresting = record.Id is 1000 or 1001 or 4101 or 41;

                    if (!mentionsGame && !providerInteresting && !idInteresting) continue;

                    list.Add(new WindowsEventEvidence(
                        log,
                        when,
                        provider,
                        record.Id,
                        record.LevelDisplayName ?? "Unknown",
                        message));
                }
            }
            catch
            {
                // Event log access can fail on restricted systems; incident capture should continue.
            }
        }
        return list.OrderBy(e => e.TimeCreated).ToArray();
    }

    private static string SafeMessage(EventRecord record)
    {
        try { return record.FormatDescription() ?? record.ToXml(); }
        catch { try { return record.ToXml(); } catch { return "Event details unavailable."; } }
    }
}
