using System.Text.Json;
using PitMedic.Models;

namespace PitMedic.Services;

public static class IncidentDetailsService
{
    public static IncidentDetailsData Build(IncidentRecord incident)
    {
        var plan = incident.RecommendedRepair ?? RepairPlanner.TryCreateFromIncident(incident);
        var repairAttempted = false;
        var repairInProgress = false;
        var repairCancelled = false;
        var repairFailed = false;
        var resolved = false;
        var resolutionSummary = string.Empty;
        var repairStage = string.Empty;
        var backupFolder = string.Empty;
        DateTimeOffset? repairStarted = null;
        DateTimeOffset? repairUpdated = null;
        var plannedActions = new List<string>();
        var activity = new List<IncidentRepairAction>();
        var references = plan?.References?.ToList() ?? new List<RepairReference>();

        var repairPath = Path.Combine(incident.IncidentFolder, "repair.json");
        if (File.Exists(repairPath))
        {
            repairAttempted = true;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(repairPath));
                var root = doc.RootElement;
                if (root.TryGetProperty("state", out var state))
                {
                    var complete = GetBoolean(state, "IsComplete");
                    var success = GetBoolean(state, "Success");
                    repairInProgress = GetBoolean(state, "IsActive") && !complete;
                    resolved = complete && success;
                    repairStage = GetString(state, "Stage");
                    resolutionSummary = GetString(state, "Message");
                    backupFolder = GetString(state, "BackupFolder");
                    if (state.TryGetProperty("StartedAt", out var startedEl) && startedEl.TryGetDateTimeOffset(out var started))
                        repairStarted = started;

                    repairCancelled = complete && !success
                        && (repairStage.Contains("cancel", StringComparison.OrdinalIgnoreCase)
                            || resolutionSummary.Contains("cancel", StringComparison.OrdinalIgnoreCase));
                    repairFailed = complete && !success && !repairCancelled;
                }

                if (root.TryGetProperty("updated", out var updatedEl) && updatedEl.TryGetDateTimeOffset(out var updated))
                    repairUpdated = updated;

                if (root.TryGetProperty("plan", out var planEl)
                    && planEl.TryGetProperty("Steps", out var stepsEl)
                    && stepsEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var step in stepsEl.EnumerateArray())
                    {
                        var text = step.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) plannedActions.Add(text);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"Finding review repair parse warning: {ex.Message}");
            }
        }

        var historyPath = Path.Combine(incident.IncidentFolder, "repair_history.jsonl");
        if (File.Exists(historyPath))
        {
            repairAttempted = true;
            try
            {
                var activityByStage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(historyPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    using var historyDoc = JsonDocument.Parse(line);
                    var root = historyDoc.RootElement;
                    var stage = GetString(root, "Stage");
                    var message = GetString(root, "Message");
                    if (string.IsNullOrWhiteSpace(stage)) continue;

                    DateTimeOffset? timestamp = null;
                    if (root.TryGetProperty("time", out var timeEl) && timeEl.TryGetDateTimeOffset(out var parsedTime))
                        timestamp = parsedTime;

                    var action = new IncidentRepairAction
                    {
                        Timestamp = timestamp,
                        Title = stage,
                        Detail = message
                    };
                    if (activityByStage.TryGetValue(stage, out var existingIndex))
                        activity[existingIndex] = action;
                    else
                    {
                        activityByStage[stage] = activity.Count;
                        activity.Add(action);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"Finding review history parse warning: {ex.Message}");
            }
        }

        if (activity.Count == 0 && repairAttempted && !string.IsNullOrWhiteSpace(repairStage))
        {
            activity.Add(new IncidentRepairAction
            {
                Timestamp = repairUpdated,
                Title = repairStage,
                Detail = resolutionSummary
            });
        }

        if (plannedActions.Count == 0 && plan is not null)
            plannedActions.AddRange(plan.Steps);

        if (references.Count == 0 && plan is not null)
            references.AddRange(RepairKnowledgeBase.ReferencesForPlan(plan.Id));

        var companion = CompanionSoftwareDefinition.Supported.FirstOrDefault(software =>
            software.DisplayName.Equals(incident.Game, StringComparison.OrdinalIgnoreCase));
        if (references.Count == 0 && companion is not null)
            references.AddRange(CompanionSoftwareKnowledgeBase.ReferencesFor(companion.Kind));

        var outcome = BuildOutcome(incident, plan, repairAttempted, repairInProgress, repairCancelled, repairFailed, resolved, resolutionSummary);
        var resolutionActions = activity.Count > 0
            ? activity.Select(x => string.IsNullOrWhiteSpace(x.Detail) ? x.Title : $"{x.Title} — {x.Detail}").ToArray()
            : plannedActions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        return new IncidentDetailsData
        {
            Incident = incident,
            RepairPlan = plan,
            IsResolved = resolved,
            RepairAttempted = repairAttempted,
            RepairInProgress = repairInProgress,
            RepairCancelled = repairCancelled,
            RepairFailed = repairFailed,
            PlainLanguageExplanation = ExplainInPlainLanguage(incident),
            OutcomeHeadline = outcome.Headline,
            ResolutionSummary = outcome.Summary,
            NextStep = outcome.NextStep,
            BackupFolder = backupFolder,
            RepairStarted = repairStarted,
            RepairUpdated = repairUpdated,
            ResolutionActions = resolutionActions,
            RepairActivity = activity,
            References = references.DistinctBy(x => x.Url, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static (string Headline, string Summary, string NextStep) BuildOutcome(
        IncidentRecord incident,
        RepairPlan? plan,
        bool repairAttempted,
        bool repairInProgress,
        bool repairCancelled,
        bool repairFailed,
        bool resolved,
        string statusMessage)
    {
        var isCompanionSoftware = CompanionSoftwareDefinition.Supported.Any(software =>
            software.DisplayName.Equals(incident.Game, StringComparison.OrdinalIgnoreCase));
        if (resolved)
        {
            return (
                "PitMedic completed the repair",
                string.IsNullOrWhiteSpace(statusMessage)
                    ? "The repair steps completed successfully and the full record was saved with this finding."
                    : statusMessage,
                isCompanionSoftware
                    ? $"Confirm {incident.Game} is responsive, then start the simulator when you are ready. PitMedic will continue monitoring both."
                    : $"Launch {incident.Game} and retry the same activity. PitMedic will continue monitoring the next session.");
        }

        if (repairInProgress)
        {
            return (
                "Repair is currently running",
                string.IsNullOrWhiteSpace(statusMessage) ? "PitMedic is working through the approved repair steps." : statusMessage,
                "Keep PitMedic open until the repair reports that it has completed or needs attention.");
        }

        if (repairCancelled)
        {
            return (
                "The repair was cancelled",
                string.IsNullOrWhiteSpace(statusMessage) ? "PitMedic stopped the repair and restored protected data where possible." : statusMessage,
                "Review the completed actions below before deciding whether to retry the repair.");
        }

        if (repairFailed)
        {
            return (
                "The repair needs attention",
                string.IsNullOrWhiteSpace(statusMessage) ? "PitMedic could not complete every repair step." : statusMessage,
                "Review the activity and preserved evidence below before retrying the repair.");
        }

        if (plan is not null)
        {
            return (
                "A repair is available",
                repairAttempted
                    ? "No repair is currently running. PitMedic preserved the previous repair record and the original finding."
                    : "PitMedic has not changed any files or settings. A guided repair is ready for your approval.",
                $"Review the proposed repair, then choose Repair now when you are ready. Estimated time: about {Math.Max(1, plan.EstimatedMinutes)} minute{(Math.Max(1, plan.EstimatedMinutes) == 1 ? string.Empty : "s")}.");
        }

        return (
            "Finding captured for review",
            "PitMedic preserved the session evidence but did not make any changes to your computer.",
            "Use the evidence below when troubleshooting or sharing the finding with support.");
    }

    private static string ExplainInPlainLanguage(IncidentRecord incident)
    {
        var category = incident.Classification.Category.ToLowerInvariant();
        var isCompanionSoftware = CompanionSoftwareDefinition.Supported.Any(software =>
            software.DisplayName.Equals(incident.Game, StringComparison.OrdinalIgnoreCase));
        if (isCompanionSoftware
            && (category.Contains("application fault")
                || category.Contains("crash dump")
                || category.Contains("abnormal termination")))
            return $"{incident.Game} stopped unexpectedly. PitMedic matched the process exit to Windows fault evidence, a crash dump, or a non-zero exit code, and preserved the surrounding system telemetry. After the simulator is closed, the available recovery closes only that companion app's remaining processes and relaunches its captured executable with approval.";
        if (category.Contains("hardware stability"))
            return "Windows recorded a hardware-level error at about the same time the simulator stopped. That makes system stability the strongest lead, although the event alone does not identify a specific failing part.";
        if (category.Contains("graphics driver") || category.Contains("graphics device") || category.Contains("rendering device"))
            return "The simulator lost reliable access to the graphics system. This usually happens when the graphics driver resets, the GPU stops responding, or the simulator's graphics configuration becomes unusable.";
        if (category.Contains("content read") || category.Contains("decompression"))
            return "Le Mans Ultimate could not read one or more installed track or car files. PitMedic identified the affected content so it can be replaced without resetting unrelated simulator data.";
        if (category.Contains("anti-cheat"))
            return "The simulator could not complete its Easy Anti-Cheat startup check, so the session could not launch normally. PitMedic preserved the related log message and can run the simulator's supported repair workflow.";
        if (category.Contains("track content"))
            return "The simulator reported that required track data could not be loaded or verified. PitMedic preserved the affected-session evidence and can rebuild only the relevant content state.";
        if (category.Contains("car content"))
            return "The simulator reported that required vehicle data could not be loaded or verified. PitMedic preserved the affected-session evidence and can rebuild only the relevant content state.";
        if (category.Contains("configuration") || category.Contains("settings") || category.Contains("profile"))
            return "The simulator reported a problem while loading its saved configuration or profile. PitMedic can protect the existing data and let the simulator generate a clean replacement.";
        if (category.Contains("memory"))
            return "The simulator could not reserve the memory it needed. The captured evidence can help distinguish a temporary memory shortage from a repeatable configuration or content problem.";
        if (category.Contains("thermal"))
            return "Temperatures were unusually high immediately before the simulator stopped. This is a useful warning sign, but PitMedic treats it as correlation rather than proof that heat caused the failure.";
        if (category.Contains("connection"))
            return "The simulator reported that it could not establish or maintain the required session connection. PitMedic preserved the exact session log evidence for review.";
        if (category.Contains("helper service"))
            return "The iRacing interface was open, but its required background service was not running. Without that service, iRacing cannot reliably launch or manage simulator sessions.";
        if (category.Contains("application fault") || category.Contains("crash dump") || category.Contains("fatal"))
            return "Windows or the simulator recorded an unexpected application failure. PitMedic captured the surrounding logs, Windows events, and telemetry so the event is not reduced to a generic crash message.";
        if (category.Contains("abnormal process"))
            return "The simulator returned an error exit code instead of closing normally. There was not enough stronger evidence to name one exact cause, so PitMedic preserved the session for review.";
        if (category.Contains("log indicates"))
            return "The simulator's own current-session log contains a strong failure marker even though Windows did not record a more specific application error.";
        if (category.Contains("unconfirmed"))
            return "The simulator ended without a normal shutdown signal, but the available evidence is not strong enough to claim one specific cause. PitMedic saved the session so it can be compared with any repeat occurrence.";

        return string.IsNullOrWhiteSpace(incident.Classification.Summary)
            ? "PitMedic detected an unusual simulator event and preserved the available evidence for review."
            : incident.Classification.Summary;
    }

    private static bool GetBoolean(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
