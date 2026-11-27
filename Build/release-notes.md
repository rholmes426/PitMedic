## Windows & Network Health

- PitMedic automatically checks local adapter state, address, gateway and DNS configuration, and adapter error counters. Home shows evidence-specific next steps without a manual check or a popup.
- Windows checks recommend next steps for stopped automatic audio services, repeated DNS timeouts, selected storage and hardware errors, display-driver resets, and Windows Update installation failures.
- After closing a simulator, open its **Last Session → Network Report** to review locally observed connection signs. These readings do not measure race-server latency or packet loss.

## Windows repair tools

- Home offers approval-gated Windows system-file verification and repair, DNS-cache refresh, stopped audio-service recovery, and clock synchronization. These tools can be used without launching a simulator.
- Background checks never reset adapters, change security or DNS settings, or launch a repair on their own.

## Post-session thermal report

- **Last Session → Thermal Report** records supported Intel CPU thermal-boundary readings and NVIDIA GPU thermal-slowdown signals without notifications or focus changes. Unavailable hardware evidence is identified separately.

Eight matching Windows guides are available in the [Diagnostic Library](https://pitmedic.com/diagnostic-library/#windows). Physical in-race disconnects and hardware-specific thermal behavior are not covered by the automated validation.
