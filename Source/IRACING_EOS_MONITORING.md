# iRacing EOS file-version rejection detection

Queued fix; no public version change or installer deployment.

iRacing can reject a file during server entry, stay open, and exit with code zero when the user quits. EOS records these failures in `Documents/iRacing/logs/eos_anticheat_errors.txt`, which the previous live-monitor candidate filter skipped.

- Watch the EOS text log and retain it, along with `sim_launch.txt`, in incident evidence.
- Recognize `Unknown file version` and distinguish track, car, and other file rejections.
- Prefer the content rejection over a generic Error 73 header, including when the header arrives in an earlier polling cycle. A manual normal exit retains the failure.
- On a detected Steam install, offer validation for iRacing (266410), with a ten-minute estimate and explicit approval. Steam validation checks installed content; it is not limited to the rejected file. Completion still requires Steam to finish and the user to retry the affected session.
- Apply the same precedence when history or the repair helper reconstructs a plan. Do not recommend an anti-cheat reinstall for an explicit file-version rejection.
- For direct or unlocated installs, capture the failure without automated file deletion. The EOS log is cumulative, so historical paths must not be fed to folder-removal repairs.
- Preserve update-time suppression, baseline existing log contents on attachment, and deduplicate repeated callbacks.

Validation: `dotnet run --project Source/PitMedic.MonitoringTests/PitMedic.MonitoringTests.csproj -c Release` uses temporary synthetic logs, never launches Steam, and never edits simulator files. Run the existing ReleaseTests suite and Release builds as well. Live server recovery still requires a user retest.
