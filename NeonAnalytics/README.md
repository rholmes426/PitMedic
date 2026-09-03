# PitMedic Neon analytics

Privacy-first website analytics collector and authenticated dashboard hosted as a
Neon Function. The function uses the branch-provided pooled `DATABASE_URL` and
the aggregate-only schema documented in `../TelemetryWorker/migrations/0003_website_analytics.sql`.

## Routes

- `GET /health` — database-backed health check
- `POST /v1/web-event` — CORS-restricted aggregate website event collector
- `GET /dashboard` — HTTP Basic-authenticated dashboard

The deployment requires `DASHBOARD_USER` and `DASHBOARD_PASSWORD` environment
variables. Never commit their values.
