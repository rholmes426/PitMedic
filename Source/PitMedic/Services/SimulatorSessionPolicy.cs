using PitMedic.Models;

namespace PitMedic.Services;

public static class SimulatorSessionPolicy
{
    public static SimulatorSessionOutcome OutcomeFor(IncidentRecord? incident)
    {
        if (incident is null) return SimulatorSessionOutcome.NoErrorObserved;
        return incident.Classification.Category.Equals("Unconfirmed simulator exit", StringComparison.OrdinalIgnoreCase)
            ? SimulatorSessionOutcome.ReviewNeeded
            : SimulatorSessionOutcome.ErrorObserved;
    }
}
