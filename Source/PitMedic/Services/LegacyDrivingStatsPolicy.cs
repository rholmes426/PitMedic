using System.Text.Json;

namespace PitMedic.Services;

internal static class LegacyDrivingStatsPolicy
{
    private static readonly string[] BestLapFields =
    [
        "LastSessionBestLap",
        "LastBestLapKey",
        "BestLaps"
    ];

    public static bool ContainsBestLapData(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        foreach (var rootProperty in root.EnumerateObject())
        {
            if (!rootProperty.Name.Equals("Games", StringComparison.OrdinalIgnoreCase)
                || rootProperty.Value.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var game in rootProperty.Value.EnumerateObject())
            {
                if (game.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var property in game.Value.EnumerateObject())
                {
                    if (BestLapFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        return true;
                }
            }
        }

        return false;
    }
}
