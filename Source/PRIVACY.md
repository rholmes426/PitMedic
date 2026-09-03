# PitMedic privacy statement

Last updated: September 3, 2026

PitMedic is designed to perform monitoring, diagnostics, and repairs locally on the user's Windows computer.

## Information stored locally

PitMedic may store:

- Hardware telemetry used for the live trace and captured findings.
- Simulator process, log, and Windows event evidence related to a detected problem.
- Repair plans, progress, results, and recovery backups.
- Local counters such as sessions monitored, repairs completed, and estimated troubleshooting time saved.
- Simulator-reported personal best laps together with their simulator, track/layout, and car combination.
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

By default, PitMedic reads a small public version file from `pitmedic.com` when the app launches to check whether an update is available. The request has no body, app-usage token, diagnostic, or permanent identifier. Automatic checks can be turned off in Settings, and PitMedic never downloads or installs an update without the user choosing the Download button. As with any website request, the hosting providers necessarily process connection information such as the source IP address to deliver and protect the file.

When a supported simulator reports a valid best lap together with an exact simulator, track/layout, and car combination, PitMedic can ask the PitMedic comparison service for a public benchmark. The request contains only those four public combination names. It does not contain the user's lap time, name, account, app-usage token, diagnostic, hardware telemetry, finding, repair, file path, or permanent identifier. The service checks a curated official-source table first and may use the YouTube Data API to find a clearly labeled exact-combination web lap when no official record is available. Positive and negative results are cached by combination to reduce third-party searches. Cloudflare and the selected public source necessarily process network connection information under their own privacy terms.

Choosing **Support PitMedic** opens the project's hosted PayPal page in the user's default browser. PitMedic does not receive or process payment details, and no donor identity or contribution information is written into the app.

Some user-approved repairs can ask Windows or Steam to perform actions such as validating installed game files. Those third-party applications operate under their own privacy policies.

## Website analytics

The PitMedic website uses Cloudflare Web Analytics to understand aggregate website traffic and performance. It can report page views, visited paths, referral sources, approximate country, general browser and device information, and site-performance measurements.

Cloudflare Web Analytics does not use cookies or local storage, fingerprint visitors, or track people across websites. Website analytics are separate from the installed PitMedic application: the website does not use PitMedic's app-usage tokens, app-usage consent does not affect website analytics, and the PitMedic project does not combine website analytics with app-usage counts or local PitMedic data. Cloudflare processes website requests under its [privacy policy](https://www.cloudflare.com/privacypolicy/).

The website also sends a small first-party event to the PitMedic aggregate analytics service when a page loads, when a visitor remains for 30 seconds or scrolls through at least half of a page, when an internal PitMedic link is selected, or when the signed Windows installer link is selected. The event contains only the event type, the PitMedic page path, an internal destination path or public release version when applicable, and the referring site's hostname. It never contains a full referrer URL, search terms, URL query parameters, IP address, cookie, local-storage identifier, app-usage token, or permanent visitor identifier.

Before storage, the service converts Cloudflare's request-country code and the browser user-agent into broad country and device-class labels. It does not store the source IP address or raw user-agent string. Events are immediately combined into daily counters by page, content category, simulator or companion software, traffic-source category, referring hostname, country, and device class. There is no visitor or session record to inspect or connect to installed-app activity. Aggregate website counters are retained for up to two years and are shown only in the same private, access-controlled dashboard used for aggregate app-usage totals.

## Future changes

Any future diagnostic-upload or additional cloud feature must be separately opt-in, show the user what will be shared, redact unnecessary personal identifiers, and update this statement before release.
