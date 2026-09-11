# PitMedic Neon analytics

Privacy-first website analytics collector and authenticated dashboard hosted as a
Neon Function. The function uses the branch-provided pooled `DATABASE_URL` and
the aggregate-only schema documented in `../TelemetryWorker/migrations/0003_website_analytics.sql`.

## Routes

- `GET /health` — database-backed health check
- `POST /v1/web-event` — CORS-restricted aggregate website event collector
- `GET /v1/website-summary` — HTTP Basic-authenticated aggregate JSON for the private combined dashboard
- `POST /v1/gsc-sync` — authenticated finalized Search Console aggregate upsert and validation
- `GET /dashboard` — HTTP Basic-authenticated dashboard

The dashboard includes rolling website conversion metrics, page engagement,
traffic sources, internal navigation paths, Search Console visibility and
ranking opportunities, plus a pre/post cohort for the September 7, 2026
priority SEO content expansion. September 7 is treated as a transition day;
the fixed pre-change baseline is September 2–5 and post-change measurement
starts September 8 as Search Console days settle.

The deployment requires `DASHBOARD_USER` and `DASHBOARD_PASSWORD` environment
variables. Never commit their values.

`GET /v1/website-details?metric=views&start=YYYY-MM-DD&end=YYYY-MM-DD`
is protected by the same Basic authentication as the summary endpoint. It returns
only daily aggregates with reconciled totals, pages, sources, devices, countries,
and internal link clicks. Optional bound filters: page, source, traffic, device,
country, product, target. Supported website metrics: views, organic, engaged,
downloads, navigation. Website dates are UTC, matching the collector.

The combined Cloudflare dashboard exposes `/website/details` behind its existing
private session login. Google details use live read-only Search Console queries,
include yesterday in Pacific time, and show preliminary/pending status. The Neon
settled-data sync remains separate and should store CTR as clicks/impressions
(0–1), with the existing `all` key for site totals.


The Search Console sync is initiated by the Cloudflare dashboard Worker. It uses
Google's official read-only API and stores only daily site, page, query, country,
and device aggregates. CTR is recomputed as clicks divided by impressions, and
incomplete Google dates are rejected.
