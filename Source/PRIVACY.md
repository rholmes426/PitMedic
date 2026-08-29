# PitMedic privacy statement

Last updated: August 29, 2026

PitMedic is designed to perform monitoring, diagnostics, and repairs locally on the user's Windows computer.

## Information stored locally

PitMedic may store:

- Hardware telemetry used for the live trace and captured findings.
- Simulator process, log, and Windows event evidence related to a detected problem.
- Repair plans, progress, results, and recovery backups.
- Local counters such as sessions monitored, repairs completed, and estimated troubleshooting time saved.
- Application settings and operational logs.

These files remain on the computer unless the user deliberately opens, copies, exports, or shares them.

## Information transmitted

PitMedic v0.5.0.0 does not transmit diagnostics, in-app usage statistics, donor identity, or advertising identifiers to the PitMedic project. The installed read-only sensor service writes current CPU telemetry only to `%ProgramData%\PitMedic\sensor.json` on the same computer. Ordinary users can read but not change this service-owned file, and PitMedic ignores stale samples.

Choosing **Support PitMedic** opens the project's hosted PayPal page in the user's default browser. PitMedic does not receive or process payment details, and no donor identity or contribution information is written into the app.

Some user-approved repairs can ask Windows or Steam to perform actions such as validating installed game files. Those third-party applications operate under their own privacy policies.

## Website analytics

The PitMedic website uses Cloudflare Web Analytics to understand aggregate website traffic and performance. It can report page views, visited paths, referral sources, approximate country, general browser and device information, and site-performance measurements.

Cloudflare Web Analytics does not use cookies or local storage, fingerprint visitors, or track people across websites. Website analytics are separate from the installed PitMedic application; the application does not send diagnostic or usage telemetry to the PitMedic project. Cloudflare processes website requests under its [privacy policy](https://www.cloudflare.com/privacypolicy/).

## Future changes

Any future diagnostic-upload or cloud feature must be opt-in, show the user what will be shared, redact unnecessary personal identifiers, and update this statement before release.
