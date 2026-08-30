# PitMedic private usage dashboard

This Worker renders read-only aggregate counts from the `pitmedic-usage` D1 database. It never selects or returns the raw daily or monthly rotating-token columns.

The dashboard is deliberately separate from the public heartbeat Worker. It has its own server-side passcode gate so protecting the dashboard never blocks PitMedic clients and does not depend on the Cloudflare account dashboard's human-verification flow.

## Deployment safety sequence

1. Keep `workers_dev` set to `false` for the first shell deployment.
2. Set `ADMIN_PASSCODE_HASH` to the SHA-256 hash of a high-entropy administrator passcode.
3. Set `SESSION_SIGNING_KEY` to an independent high-entropy secret of at least 32 characters.
4. Enable the production `workers.dev` route only after the authentication tests pass.
5. Verify an unauthenticated browser is redirected to `/login`, an incorrect passcode is rejected, and logging out expires the session cookie.

The passcode itself is never stored in Worker configuration. Successful authentication creates a signed, secure, HTTP-only, same-site cookie that expires after 30 days. Neither secret is returned to the browser. Cloudflare Access can be placed in front later as an additional layer, but it is not required for this single-administrator dashboard.

Run `npm run check` before deployment. The tests verify that the rendered response contains only aggregate counts and never exposes raw rotating tokens.
