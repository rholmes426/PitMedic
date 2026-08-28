using PitMedic.Models;

namespace PitMedic.Services;

public interface ILiveLogMonitor
{
    void StartSession(DateTimeOffset started);
    IReadOnlyList<LiveFaultEvidence> Poll();
}
