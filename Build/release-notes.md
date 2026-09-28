Fixes repeated false simulator exits, inflated finding counts, and recurring repair windows. All supported simulators and monitored companion apps now require confirmed process termination; a running or inaccessible process is not treated as a crash.

Automatic simulator repairs wait while the simulator is running. Corroborated false running-process findings from earlier versions are excluded from review and repair without deleting the original diagnostic files. Interrupted captures no longer appear as manual snapshots, and manual snapshots do not inflate the issue count.
