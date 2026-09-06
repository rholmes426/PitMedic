using System.Text.RegularExpressions;

namespace PitMedic.Services;

public static class LmuLogEvidenceParser
{
    private static readonly Regex InstalledContentRegex = new(
        @"(?:error\s+decompressing\s+file|error\s+loading\s+mesh|error\s+initializing\s+scene\s+file|cube\s+error\s+loading\s+scene\s+file)[^\r\n]{0,1200}?[\\/]Installed[\\/](?<kind>Locations|Vehicles)[\\/](?<name>[^\\/\r\n]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static IReadOnlyList<string> ExtractAffectedInstalledContent(string text)
    {
        var found = new List<string>();
        foreach (Match match in InstalledContentRegex.Matches(text))
        {
            var kind = match.Groups["kind"].Value;
            var name = match.Groups["name"].Value.Trim();
            if (string.IsNullOrWhiteSpace(kind) || string.IsNullOrWhiteSpace(name)) continue;
            if (name.Contains("..", StringComparison.Ordinal)) continue;
            found.Add(Path.Combine(kind, name));
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
