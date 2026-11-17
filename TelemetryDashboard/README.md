# PitMedic private usage dashboard

This Worker renders one private, read-only analytics dashboard with three tabs. **Overview** combines the headline app, download, website, and Google Search Console totals. **App usage** reads opted-in active installations from Cloudflare D1 and the public GitHub download count for PitMedic installer and portable ZIP assets. GitHub downloads are not unique-user counts. **Website & search** reads the website aggregates from the Neon collector that receives pitmedic.com events, and shows page trends, engagement, organic entries, signed-installer clicks, top content, referring sites, simulator and companion-software interest, broad country/device totals, internal navigation paths, and Search Console performance. It never selects or returns the raw daily or monthly rotating-token columns.

Website records are already daily aggregates. The dashboard has no visitor/session record and cannot display IP addresses, cookies, local-storage identifiers, full referrer URLs, search terms, or raw user-agent strings.

The Website view can also read aggregate search-performance data directly from the Google Search Console API: clicks, impressions, click-through rate, average position, top queries, and top Google landing pages. This connection is read-only and does not store Search Console rows in D1.

The server-to-server website request uses the Neon dashboard's existing HTTP Basic credentials. Store only the base64 encoding of `username:password` as the dashboard Worker secret `NEON_ANALYTICS_CREDENTIALS`; never commit it. If Neon is unavailable or rejects the credentials, the dashboard returns an error instead of presenting misleading zero totals.

## Google Search Console connection

1. Enable the Google Search Console API in a dedicated Google Cloud project.
2. Create a service account with no Google Cloud IAM role and generate a JSON key.
3. Add the service-account email as a restricted user of the `https://pitmedic.com/` Search Console property.
4. Store the complete JSON key only as the dashboard Worker secret `GOOGLE_SERVICE_ACCOUNT_JSON`.
5. Keep `SEARCH_CONSOLE_PROPERTY` set to `https://pitmedic.com/` in `wrangler.jsonc`.

The service account requests only `https://www.googleapis.com/auth/webmasters.readonly`. Never commit the JSON key or copy it into Worker variables, logs, tests, or documentation.

The dashboard is deliberately separate from the public heartbeat Worker. It has its own server-side passcode gate so protecting the dashboard never blocks PitMedic clients and does not depend on the Cloudflare account dashboard's human-verification flow.

## Deployment safety sequence

1. Keep `workers_dev` set to `false` for the first shell deployment.
2. Set `ADMIN_PASSCODE_HASH` to the SHA-256 hash of a high-entropy administrator passcode.
3. Set `SESSION_SIGNING_KEY` to an independent high-entropy secret of at least 32 characters.
4. Enable the production `workers.dev` route only after the authentication tests pass.
5. Verify an unauthenticated browser is redirected to `/login`, an incorrect passcode is rejected, and logging out expires the session cookie.

The passcode itself is never stored in Worker configuration. Successful authentication creates a signed, secure, HTTP-only, same-site cookie that expires after 30 days. Neither secret is returned to the browser. Cloudflare Access can be placed in front later as an additional layer, but it is not required for this single-administrator dashboard.

Run `npm run check` before deployment. The tests verify that the rendered response contains only aggregate counts and never exposes raw rotating tokens.


## Free settled-data sync

The dashboard Worker uses the existing read-only service account to synchronize
finalized daily Search Console aggregates to the PitMedic Neon analytics database.
Two UTC cron entries cover daylight-saving changes, while the handler runs only
at 9 AM America/Los_Angeles. It refreshes site, page, query, country, and device
aggregates, checks the nine-page September 7 cohort with Google URL Inspection,
and never finalizes a date at or after Google's first incomplete date.

This replaces GSC Wizard with Google's official Search Console API and requires
no additional paid analytics service.

## Daily GitHub downloads

The App usage tab shows a 30-day table, 14-day chart, observed daily/7-day/30-day
increases, coverage labels, and an authenticated CSV export. Public release-asset
counter snapshots are stored in D1 by verified UTC timestamp. An hourly Worker
cron collects them even when the dashboard is closed; normal app/overview loads
can refresh the snapshot too. This does not add any client or visitor tracking.

GitHub exposes cumulative counters rather than download event timestamps. Daily
increases are bucketed by observation day (UTC), so near-midnight downloads can
fall on the following day. The first snapshot is a baseline, not today's downloads;
earlier history cannot be recovered. Missing days remain blank. Days without a
near-midnight baseline or closing observation, the current day, and changed asset
counters are marked partial. New release assets, deletion, counter decreases,
repeat requests, and out-of-order observations do not inflate or erase previously
observed increases. Available-release cumulative totals may decrease after deletion.

Release pagination must finish successfully before a snapshot is accepted. The
existing GitHub rate-limit retry and stale-cache behavior is retained, with a
durable last-verified total as fallback after edge-cache eviction. Apply migration
`0006_github_download_snapshots.sql` before deploying the dashboard (the existing
analytics deployment workflow applies all migrations first).
