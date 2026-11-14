using System.Text.RegularExpressions;
using PitMedic.Models;
namespace PitMedic.Services;

public static class IRacingLoadingError77Policy
{
    public static bool IsError(string text) =>
        Regex.IsMatch(text, @"\bloading\s+error\s*[:#-]?\s*77\b", RegexOptions.IgnoreCase);

    public static RepairPlan Create(IEnumerable<string> evidence, bool steam)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in evidence.Where(IsError))
        {
            if (Regex.IsMatch(line, @"(?:^|[\\/])\.\.(?:[\\/]|$)")) continue;
            foreach (Match match in Regex.Matches(line,
                @"(?<![A-Za-z0-9_-])(?<kind>cars|tracks)[\\/](?<name>[A-Za-z0-9][A-Za-z0-9_-]*)(?=[\\/.\s'""\)\]]|$)", RegexOptions.IgnoreCase))
                paths.Add(match.Groups["kind"].Value.ToLowerInvariant() + "/" + match.Groups["name"].Value);
        }
        if (paths.Count == 1)
            return new RepairPlan
            {
                Id = "iracing-error77-content-reacquire", Game = "iRacing",
                Title = "Replace the content named by Loading Error 77",
                Summary = "Preserve and remove only the single car or track folder named in the Error 77 evidence, then start the supported updater or Steam validation. Retest after downloads finish.",
                Safety = RepairSafety.Significant, RequiresApproval = true, EstimatedMinutes = 10,
                SteamAppId = steam ? "266410" : "",
                AffectedContentRelativePaths = paths.ToArray(),
                Steps = new[] { "Confirm iRacing and updates are closed", "Validate the exact content folder", "Preserve affected content",
                    "Remove only that folder's content", "Start supported reacquisition and retest" },
                References = RepairKnowledgeBase.ReferencesForPlan("iracing-track-index-reset")
                    .Concat(RepairKnowledgeBase.ReferencesForPlan("iracing-car-index-reset")).Distinct().ToArray()
            };
        return new RepairPlan
        {
            Id = "iracing-error77-verify", Game = "iRacing",
            Title = steam ? "Validate iRacing content in Steam" : "Open iRacing updater for Loading Error 77",
            Summary = "The affected car or track could not be identified uniquely. No content folder will be removed. Let the supported updater or Steam validation check the installation, then retry the same session.",
            Safety = RepairSafety.Reversible, RequiresApproval = true, EstimatedMinutes = steam ? 10 : 1,
            SteamAppId = steam ? "266410" : "",
            Steps = new[] { "Confirm iRacing is closed", "Start supported content verification", "Wait for downloads, then retest" },
            References = RepairKnowledgeBase.ReferencesForPlan("iracing-track-index-reset")
        };
    }

    public static string ValidateTarget(string installRoot, string relative)
    {
        if (!Regex.IsMatch(relative, @"^(cars|tracks)/[A-Za-z0-9][A-Za-z0-9_-]*$"))
            throw new InvalidDataException("The affected content path is not a supported car or track folder.");
        var root = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(target))
            throw new DirectoryNotFoundException("The affected content folder was not found. No content was removed.");
        for (var path = target; !string.IsNullOrEmpty(path); path = Path.GetDirectoryName(path))
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Content recovery cannot follow links or junctions.");
        var pending = new Stack<string>();
        pending.Push(target);
        while (pending.Count > 0)
            foreach (var child in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                var attributes = File.GetAttributes(child);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new UnauthorizedAccessException("The affected content contains a link or junction.");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
            }
        return target;
    }
}
