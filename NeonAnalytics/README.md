# PitMedic Neon analytics

Privacy-first website analytics collector and authenticated dashboard hosted as a
Neon Function. The function uses the branch-provided pooled `DATABASE_URL` and
the aggregate-only schema documented in `../TelemetryWorker/migrations/0003_website_analytics.sql`.

## Routes

- `GET /health` — database-backed health check
- `POST /v1/web-event` — CORS-restricted aggregate website event collector
- `GET /dashboard` — HTTP Basic-authenticated dashboard

The dashboard includes rolling website conversion metrics, page engagement,
traffic sources, internal navigation paths, Search Console visibility and
ranking opportunities, plus a pre/post cohort for the September 7, 2026
priority SEO content expansion. September 7 is treated as a transition day;
the fixed pre-change baseline is September 2–5 and post-change measurement
starts September 8 as Search Console days settle.

The deployment requires `DASHBOARD_USER` and `DASHBOARD_PASSWORD` environment
variables. Never commit their values.
