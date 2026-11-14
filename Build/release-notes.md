PitMedic now opens the main window and Finding Review when a simulator crash dump or matching Windows application fault is captured without a repair to offer. Additional findings remain available in history without opening duplicate review windows.

Adds Repair Tools on the iRacing and Le Mans Ultimate pages for specific problems that can occur without a crash:

- iRacing connection Errors 10057, 10035 and 10054: a backed-up, one-key core.ini connection test.
- iRacing Loading Error 63: preserve app.ini and let iRacing regenerate it, with an explicit settings-reset warning.
- Le Mans Ultimate controls: preserve and reset only the four vendor-documented controller files, with a remapping warning.

Each new configuration repair requires approval, retains its recovery files and offers Restore last backup. Restore preserves the current configuration before replacing it. Locked, ambiguous, unsupported and linked paths stop the operation. File operations are verified; the result is marked for a functional retest rather than assuming the original symptom is resolved.

Adds exact iRacing Loading Error 77 routing. PitMedic isolates content only when one car/track folder is identified in that error's evidence; otherwise it starts supported verification without removing guessed folders. Downloads must finish before retesting.

Updates the Diagnostic Library with three matching repair guides and Error 77 coverage. New repair pages are published with the supporting app version. Scout remains manual, using the expanded source inventory when the owner requests a review.
