using PitMedic.Models;
using PitMedic.Services;

internal static class WindowsRepairPolicyTests
{
    public static void Run()
    {
        foreach (var plan in WindowsRepairPolicy.Options)
        {
            var request = new IncidentRecord
            {
                Game = WindowsRepairPolicy.Product,
                Classification = new CrashClassification(ScopedConfigurationRepair.RequestCategory, 100,
                    "Windows troubleshooting", new[] { ScopedConfigurationRepair.RequestPrefix + plan.Id })
            };
            if (!plan.RequiresApproval || !ElevatedRepairPolicy.RequiresElevation(plan.Id)
                || plan.References.Count == 0 || RepairPlanner.TryCreateFromIncident(request)?.Id != plan.Id)
                throw new InvalidOperationException("Windows actions must reconstruct from a valid approved request and use supported references and elevation.");
            if (RepairPlanner.TryCreateFromIncident(request with { Game = "rFactor 2" }) is not null
                || RepairPlanner.TryCreateFromIncident(request with { Classification = request.Classification with { Evidence = new[] { "untrusted", ScopedConfigurationRepair.RequestPrefix + plan.Id } } }) is not null)
                throw new InvalidOperationException("Windows action must reject requests from another product or ambiguous evidence.");
        }
        var generic = new IncidentRecord
        {
            Game = WindowsRepairPolicy.Product,
            Classification = new CrashClassification(CrashClassification.ApplicationFault, 88, "Generic exception", new[] { "0xc0000005" }),
            RecommendedRepair = WindowsRepairPolicy.Options[0]
        };
        if (RepairPlanner.TryCreateFromIncident(generic) is not null || WindowsRepairPolicy.CreatePlan("windows-arbitrary-action") is not null)
            throw new InvalidOperationException("A generic fault or unknown action must not select or elevate a Windows operation.");
        Console.WriteLine("Windows repair request policy tests passed.");
    }
}
