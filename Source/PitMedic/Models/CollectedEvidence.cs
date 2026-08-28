namespace PitMedic.Models;

public sealed record CollectedEvidence(
    int LogFiles,
    int DumpFiles,
    IReadOnlyList<string> CrashHints,
    bool CleanExitDetected,
    IReadOnlyList<string> CleanExitHints,
    IReadOnlyList<string> AffectedInstalledContent,
    IReadOnlyList<string> RepairSignatureIds);
