namespace PitMedic.Services;

public static class IRacingRepairSignaturePolicy
{
    private const string EvidencePrefix = "PitMedic diagnostic signature: ";

    public static string? FindKnowledgeSignature(IEnumerable<string> evidence)
    {
        foreach (var line in evidence)
        {
            var start = line.IndexOf(EvidencePrefix, StringComparison.OrdinalIgnoreCase);
            if (start < 0) continue;
            start += EvidencePrefix.Length;
            var end = line.IndexOfAny([']', '\r', '\n'], start);
            var signature = (end >= 0 ? line[start..end] : line[start..]).Trim();
            var knowledgeSignature = MapDiagnosticSignature(signature);
            if (!string.IsNullOrWhiteSpace(knowledgeSignature)) return knowledgeSignature;
        }

        return null;
    }

    public static string MapDiagnosticSignature(string signatureId) => signatureId switch
    {
        "helper-service" => "iracing-helper-service",
        "waiting-service" => "iracing-updater-autoinstall",
        "ui-welcome" => "iracing-ui-safe",
        "ui-render-failure" => "iracing-ui-cache",
        "eac-error-73" or "eac-failure" or "eac-error-10011" => "iracing-eac-error73",
        "verification-failure" => "iracing-update-verification",
        "content-file-locked" => "iracing-content-file-locked",
        "track-loading-error" => "iracing-track-corruption",
        "car-loading-error" => "iracing-car-corruption",
        "loading-error-49" => "iracing-loading-error-49",
        "already-running" => "iracing-trueforce-stale-state",
        "loading-error-3" => "iracing-loading-error-3",
        "createprocessasuser" or "compatibility-mode" => "iracing-compatibility-flags",
        "digital-signature" => "iracing-run-updater",
        "renderer-config" => "iracing-renderer-config",
        _ => string.Empty
    };
}
