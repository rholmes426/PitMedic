PitMedic 1.0 adds installation-time driver checks to prevent missing CPU temperature readings on new PCs.

Setup detects missing or incomplete PawnIO registration, downloads the pinned official signed driver installer, verifies its SHA-256, and installs it before starting PitMedic's sensor service. Download or installation failures stop setup with retry guidance; restart-required results are respected. Existing registered installations are preserved, and uninstalling PitMedic leaves this shared driver installed.

Setup and Settings also identify Windows-reported device problems and the generic Microsoft display driver. Settings provides Windows Update access and hardware-specific driver guidance. Sensor reports distinguish a missing CPU driver from unavailable readings with a registered driver. PitMedic continues to include its .NET runtime.

Includes the previous CPU reading validation, iRacing updater repair guard, and refreshed desktop/tray icons.
