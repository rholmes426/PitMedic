using System.Text.Json;

namespace PitMedic.Services;

internal static class LegacyDrivingStatsPolicy
{
    public static bool ContainsDrivingStatsData(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return false;

        foreach (var rootProperty in root.EnumerateObject())
        {
            if (rootProperty.Name.Equals("Games", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
