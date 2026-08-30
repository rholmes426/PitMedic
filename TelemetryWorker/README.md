# PitMedic anonymous usage service

This Worker accepts one explicitly consented active-installation heartbeat per PitMedic installation per UTC day. The accepted JSON contract contains only protocol version, app version, release channel, install type, and rotating daily/monthly tokens.

The service rejects unknown fields so an app bug cannot silently upload diagnostics, file paths, hostnames, or other undeclared data. Request bodies and network identifiers are never written to the database or application logs.

Daily tokens are rolled into aggregate counts after the UTC day closes. Monthly tokens are rolled up after the UTC month closes. Tokens are then deleted; only counts grouped by version, channel, and install type remain.

## Owner install alerts

When a monthly token is inserted for the first time, the Worker can send the PitMedic owner a private notification email. The email contains only the app version, release channel, install type, detection time, and current anonymous monthly-active total. It never includes the rotating token, IP address, hardware information, simulator activity, findings, repairs, diagnostics, or file paths.

The `INSTALL_ALERT_EMAIL` binding is restricted to the configured owner destination. Before production deployment, the destination must be verified in the PitMedic Cloudflare account and `pitmedic.com` must be onboarded to Cloudflare Email Service so `notifications@pitmedic.com` is an authorized sender. Alert delivery is best-effort and never changes whether a valid heartbeat is accepted.

## Local validation

```bash
npm install
npm run types
npm run check
npm run deploy:dry-run
```

## Production deployment

The production Worker is `pitmedic-usage` and uses the `pitmedic-usage` D1 database. Its public heartbeat URL is:

`https://pitmedic-usage.pitmedic-usage-telemetry.workers.dev/v1/active`

After a code or schema change:

1. Authenticate Wrangler to the PitMedic Cloudflare account.
2. Confirm the owner destination is verified and the Email Service sending domain is onboarded when install alerts are enabled.
3. Run `npm run check` and `npm run deploy:dry-run`.
4. Apply pending migrations with `npm run migrate:remote`.
5. Deploy with `npm run deploy`.
6. Verify `GET /health` and the D1 aggregate query.

View aggregate results by running the statements in `queries/summary.sql` through the Cloudflare D1 dashboard or Wrangler.
