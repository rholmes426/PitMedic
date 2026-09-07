using PitMedic.Models;

namespace PitMedic.Services;

public static class SimulatorNavigationPolicy
{
    public static GameKind? SelectForStatusChange(GameKind game, bool wasRunning, bool isRunning)
        => isRunning && !wasRunning ? game : null;
}
