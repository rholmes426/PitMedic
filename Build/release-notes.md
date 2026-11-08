Fixes a loop that could report the same simulator shutdown repeatedly, including an Assetto Corsa EVO case where one process filled the incident history. PitMedic keeps the original captures, groups repeated reports into one history entry, and loads the history without blocking the app.

When a simulator window has closed and the same process stalls during shutdown, PitMedic waits briefly and ends the remaining process before continuing the repair. An open or minimized simulator still blocks the repair.

Adds Diagnostic Library guidance for iRacing force feedback with Simucube ActivePedals and a non-Simucube wheelbase, plus guidance for Fanatec App 1.7.1.4, Logitech G HUB 2026.7 and RaceRoom 0.9.9.14–0.9.9.15. The iRacing setting remains a manual check.

Updates the Windows Event Log dependency to 10.0.14.
