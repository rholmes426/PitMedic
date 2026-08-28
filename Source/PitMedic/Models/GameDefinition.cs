namespace PitMedic.Models;

public enum GameKind
{
    LeMansUltimate,
    IRacing,
    AssettoCorsaEvo,
    RaceRoom,
    AssettoCorsaCompetizione,
    Automobilista2
}

public sealed record GameDefinition(
    GameKind Kind,
    string DisplayName,
    string ExecutableName,
    IReadOnlyList<string> ProcessNames)
{
    public string ProcessName => ProcessNames[0];

    public static IReadOnlyList<GameDefinition> Supported { get; } = new[]
    {
        new GameDefinition(GameKind.LeMansUltimate, "Le Mans Ultimate", "Le Mans Ultimate.exe",
            new[] { "Le Mans Ultimate", "LeMansUltimate", "LMU" }),
        new GameDefinition(GameKind.IRacing, "iRacing", "iRacingSim64DX11.exe",
            new[] { "iRacingSim64DX11", "iRacingSim64DX11_64" }),
        new GameDefinition(GameKind.AssettoCorsaEvo, "Assetto Corsa EVO", "AssettoCorsaEVO.exe",
            new[] { "AssettoCorsaEVO" }),
        new GameDefinition(GameKind.RaceRoom, "RaceRoom Racing Experience", "RRRE64.exe",
            new[] { "RRRE64", "RRRE" }),
        new GameDefinition(GameKind.AssettoCorsaCompetizione, "Assetto Corsa Competizione", "AC2-Win64-Shipping.exe",
            new[] { "AC2-Win64-Shipping", "AC2" }),
        new GameDefinition(GameKind.Automobilista2, "Automobilista 2", "AMS2AVX.exe",
            new[] { "AMS2AVX", "AMS2" })
    };
}
