using PitMedic.Models;

namespace PitMedic.Services;

/// <summary>
/// Controls when copied exit-time logs may be promoted from preserved diagnostic context into
/// a user-visible finding. Copied logs can contain entries from an earlier session, so simulators
/// with live monitors rely on their offset-based current-session evidence instead.
/// </summary>
public static class ExitLogEvidencePolicy
{
    public static CollectedEvidence Normalize(GameKind game, CollectedEvidence evidence)
    {
        if (game == GameKind.LeMansUltimate)
        {
            if (!evidence.CleanExitDetected) return evidence;

            // LMU may emit isolated content/cache warnings during a session that still reaches its
            // complete shutdown sequence. Do not let those warnings offer a destructive-looking
            // content repair after the user completed and exited the session normally.
            return evidence with
            {
                CrashHints = Array.Empty<string>(),
                AffectedInstalledContent = Array.Empty<string>(),
                RepairSignatureIds = Array.Empty<string>()
            };
        }

        // iRacing, ACE, RaceRoom and ACC have offset-based live monitors that see only lines written
        // after PitMedic attaches. AMS2 has no sufficiently reliable support log. Their copied log
        // tails remain in the incident folder for review, but cannot create an incident by themselves.
        return evidence with { CrashHints = Array.Empty<string>() };
    }
}
