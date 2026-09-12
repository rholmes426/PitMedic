Fix an iRacing update false positive: the status-only CheckIfAntiCheatInstalledForIRacing message no longer creates a failed-launch finding. EAC diagnostic signatures are suppressed while the updater is active and during its existing settling interval; update-time lines are consumed without being replayed later. Fresh launch failures after updating remain detectable.

Saved iRacing findings supported solely by this installation probe are reassessed as status checks, with their original evidence retained and obsolete repair recommendations removed. Both repair entry points reject these status-only findings. Findings with independent fault evidence are preserved.

Regression coverage includes the reported message, missed updater detection, active updates, post-update failures, log rotation, unrelated graphics faults, and saved-finding reassessment.
