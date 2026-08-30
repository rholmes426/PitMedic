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

## Optional anonymous app-usage count

PitMedic v0.6.0.0 can attempt one anonymous active-installation count at most once per UTC day. This is **off by default** and begins only after the user explicitly chooses **Share anonymous usage**. The choice and a preview of the complete payload are also available in Settings.

The request contains exactly:

- Protocol version.
- PitMedic version.
- Release channel (`preview` or `stable`).
- Installation type (`installer` or `portable`).
- A random-looking token that changes every UTC day.
- A separate random-looking token that changes every UTC month.

The tokens are derived from a random secret stored only in the current user's local PitMedic folder. The secret is never transmitted. Tokens contain no name, email address, Windows user name, computer name, hardware identifier, file path, diagnostic, finding, repair, simulator activity, session duration, or permanent installation identifier.

The service keeps current daily and monthly tokens only long enough to prevent duplicate counts. After the UTC day or month closes, it replaces the tokens with totals grouped by app version, release channel, and installation type, then deletes the raw tokens. Only those aggregate totals remain. The PitMedic service does not write request bodies, tokens, IP addresses, or user-agent strings to its database or application logs. Cloudflare necessarily processes network connection information, including the source IP address, to deliver and protect the service under Cloudflare's own privacy terms.

The project administrator can view these aggregate totals in a private, access-controlled dashboard. The dashboard cannot submit usage records and never reads or displays the rotating tokens. It reports only active-installation counts and their app-version, release-channel, and installation-type breakdowns.

Turning anonymous usage off immediately deletes the local secret and last-send state. Uninstalling PitMedic also removes those two usage-count files while leaving the user's diagnostic history untouched. Turning sharing on later creates a new unlinkable secret. Previously created aggregate counts cannot identify an installation and therefore cannot be individually removed.

## Other network behavior

Diagnostics, findings, repairs, hardware telemetry, and simulator activity are never sent to the PitMedic project. The installed read-only sensor service writes current CPU telemetry only to `%ProgramData%\PitMedic\sensor.json` on the same computer. Ordinary users can read but not change this service-owned file, and PitMedic ignores stale samples.

By default, PitMedic reads a small public version file from `pitmedic.com` at most once per UTC day to check whether an update is available. The request has no body, app-usage token, diagnostic, or permanent identifier. Automatic checks can be turned off in Settings, and PitMedic never downloads or installs an update without the user choosing the Download button. As with any website request, the hosting providers necessarily process connection information such as the source IP address to deliver and protect the file.

Choosing **Support PitMedic** opens the project's hosted PayPal page in the user's default browser. PitMedic does not receive or process payment details, and no donor identity or contribution information is written into the app.

Some user-approved repairs can ask Windows or Steam to perform actions such as validating installed game files. Those third-party applications operate under their own privacy policies.

## Website analytics

The PitMedic website uses Cloudflare Web Analytics to understand aggregate website traffic and performance. It can report page views, visited paths, referral sources, approximate country, general browser and device information, and site-performance measurements.

Cloudflare Web Analytics does not use cookies or local storage, fingerprint visitors, or track people across websites. Website analytics are separate from the installed PitMedic application: the website does not use PitMedic's app-usage tokens, app-usage consent does not affect website analytics, and the PitMedic project does not combine website analytics with app-usage counts or local PitMedic data. Cloudflare processes website requests under its [privacy policy](https://www.cloudflare.com/privacypolicy/).

## Future changes

Any future diagnostic-upload or additional cloud feature must be separately opt-in, show the user what will be shared, redact unnecessary personal identifiers, and update this statement before release.
