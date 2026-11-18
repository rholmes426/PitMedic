using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>Allowlisted symptom actions, including problems which do not create a crash.</summary>
public static class UserRequestedRepairPolicy
{
    private static readonly ConfigurationRepairOption AcVerification = new(
        "ac-steam-verify", GameKind.AssettoCorsa, "Verify original Assetto Corsa game files",
        "Ask Steam to check the original game's files. Close the game, launcher and Content Manager first. Steam may replace modified stock files; back those up before continuing. This does not install or repair third-party mods.",
        Array.Empty<string>());

    public const string RFactor2VerificationId = "rfactor2-steam-content";
    public const string RFactor2SteamAppId = "365960";

    private static readonly ConfigurationRepairOption RFactor2Verification = new(
        RFactor2VerificationId, GameKind.RFactor2, "Verify Steam files",
        "Ask Steam to check rFactor 2 for missing or damaged game files. Close rFactor 2 and its tools first. Steam may replace manually modified game files. Back up changes you want to keep and wait for Steam to finish before retesting.",
        Array.Empty<string>());

    private static readonly ConfigurationRepairOption[] SteamVerifications = { AcVerification, RFactor2Verification };

    public static IReadOnlyList<ConfigurationRepairOption> OptionsFor(GameKind game) =>
        ScopedConfigurationRepair.Options.Concat(SteamVerifications).Where(x => x.Game == game).ToArray();

    public static RepairPlan? CreatePlan(GameKind game, string id)
    {
        var verification = SteamVerifications.FirstOrDefault(option => option.Game == game && option.Id == id);
        if (verification is null) return ScopedConfigurationRepair.CreatePlan(game, id);
        var displayName = GameDefinition.Supported.Single(definition => definition.Kind == game).DisplayName;
        return new RepairPlan
        {
            Id = id, Game = displayName, Title = verification.Title, Summary = verification.Description,
            Safety = game == GameKind.RFactor2 ? RepairSafety.Significant : RepairSafety.Reversible,
            RequiresApproval = true, EstimatedMinutes = 8,
            SteamAppId = game == GameKind.RFactor2 ? RFactor2SteamAppId : AssettoCorsaPolicy.SteamAppId,
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
