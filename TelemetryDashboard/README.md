# PitMedic private usage dashboard

This Worker renders read-only aggregate counts from the `pitmedic-usage` D1 database. The **App usage** view shows opted-in active installations. The **Website** view shows page trends, engagement, organic entries, signed-installer clicks, top content, referring sites, simulator and companion-software interest, broad country/device totals, and internal navigation paths. It never selects or returns the raw daily or monthly rotating-token columns.

Website records are already daily aggregates. The dashboard has no visitor/session record and cannot display IP addresses, cookies, local-storage identifiers, full referrer URLs, search terms, or raw user-agent strings.

The Website view can also read aggregate search-performance data directly from the Google Search Console API: clicks, impressions, click-through rate, average position, top queries, and top Google landing pages. This connection is read-only and does not store Search Console rows in D1.

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
