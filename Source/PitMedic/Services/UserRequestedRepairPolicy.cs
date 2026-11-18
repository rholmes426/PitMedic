using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>Allowlisted symptom actions, including problems which do not create a crash.</summary>
public static class UserRequestedRepairPolicy
{
    private static readonly ConfigurationRepairOption AcVerification = new(
        "ac-steam-verify", GameKind.AssettoCorsa, "Verify original Assetto Corsa game files",
        "Ask Steam to check the original game's files. Close the game, launcher and Content Manager first. Steam may replace modified stock files; back those up before continuing. This does not install or repair third-party mods.",
        Array.Empty<string>());

    public static IReadOnlyList<ConfigurationRepairOption> OptionsFor(GameKind game) =>
        ScopedConfigurationRepair.Options.Where(x => x.Game == game)
            .Concat(game == GameKind.AssettoCorsa ? new[] { AcVerification } : Array.Empty<ConfigurationRepairOption>()).ToArray();

    public static RepairPlan? CreatePlan(GameKind game, string id)
    {
        if (game != GameKind.AssettoCorsa || id != AcVerification.Id)
            return ScopedConfigurationRepair.CreatePlan(game, id);
        return new RepairPlan
        {
            Id = id, Game = "Assetto Corsa", Title = AcVerification.Title, Summary = AcVerification.Description,
            Safety = RepairSafety.Reversible, RequiresApproval = true, EstimatedMinutes = 8,
            SteamAppId = AssettoCorsaPolicy.SteamAppId,
            Steps = new[] { "Close the game and configuration tools", "Start Steam verification", "Wait for Steam to finish, then test a stock car and track" },
            References = RepairKnowledgeBase.ReferencesForPlan(id)
        };
    }

    public static RepairPlan? FromRequest(IncidentRecord incident)
    {
        if (incident.Classification.Category != ScopedConfigurationRepair.RequestCategory
            || incident.Classification.Evidence.Count != 1) return null;
        var line = incident.Classification.Evidence[0];
        if (!line.StartsWith(ScopedConfigurationRepair.RequestPrefix, StringComparison.Ordinal)) return null;
        var game = GameDefinition.Supported.FirstOrDefault(x => x.DisplayName == incident.Game);
        return game is null ? null : CreatePlan(game.Kind, line[ScopedConfigurationRepair.RequestPrefix.Length..]);
    }
}
