Fixes missed iRacing server-entry failures recorded in eos_anticheat_errors.txt. Detects Unknown file version errors for tracks, cars, and other files, retaining the rejected filename even when the user exits the simulator normally.

Prioritizes content rejection over generic Error 73 and offers approval-gated Steam content validation for Steam installations. Direct or unlocated installations retain the diagnosis without automatic file deletion. Preserves protection against stale log entries, duplicate callbacks, and updater-time false alarms.

Validated with 18 new regression checks, the existing release-policy tests, and app and repair-helper Release builds. Steam validation must finish before retrying the affected session.
