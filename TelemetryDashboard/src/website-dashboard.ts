import type { SearchConsoleData, SearchMetricRow } from "./search-console";

export type WebTrendPoint = {
  day: string;
  pageViews: number;
  downloads: number;
};

export type WebPageRow = {
  path: string;
  pageViews: number;
  engagedViews: number;
  downloads: number;
};

export type WebDimensionRow = {
  label: string;
  count: number;
  secondary?: string;
};

export type WebJourneyRow = {
  source: string;
  target: string;
  count: number;
};

export type WebsiteDashboardData = {
  todayPageViews: number;
  sevenDayPageViews: number;
  thirtyDayPageViews: number;
  downloads: number;
  engagementRate: number;
  organicEntries: number;
  daily: WebTrendPoint[];
  topPages: WebPageRow[];
  searchLandings: WebDimensionRow[];
  sources: WebDimensionRow[];
  products: WebDimensionRow[];
  countries: WebDimensionRow[];
  devices: WebDimensionRow[];
  journeys: WebJourneyRow[];
};

type QueryRow = Record<string, unknown>;

export async function loadWebsiteDashboardData(
  db: D1Database,
  now = new Date(),
): Promise<WebsiteDashboardData> {
  const today = isoDay(now);
  const sevenDaysAgo = isoDay(addDays(now, -6));
  const thirtyDaysAgo = isoDay(addDays(now, -29));

  const results = await db.batch<QueryRow>([
    db.prepare(`
      SELECT
        SUM(CASE WHEN event_type = 'page_view' AND day = ? THEN event_count ELSE 0 END) AS today_views,
        SUM(CASE WHEN event_type = 'page_view' AND day >= ? THEN event_count ELSE 0 END) AS seven_day_views,
        SUM(CASE WHEN event_type = 'page_view' THEN event_count ELSE 0 END) AS thirty_day_views,
        SUM(CASE WHEN event_type = 'download' THEN event_count ELSE 0 END) AS downloads,
        SUM(CASE WHEN event_type = 'engaged' THEN event_count ELSE 0 END) AS engaged,
        SUM(CASE WHEN event_type = 'page_view' AND traffic_type = 'search' THEN event_count ELSE 0 END) AS organic
      FROM web_daily_events WHERE day >= ?`).bind(
        today,
        sevenDaysAgo,
        thirtyDaysAgo,
      ),
    db.prepare(`
      SELECT day,
        SUM(CASE WHEN event_type = 'page_view' THEN event_count ELSE 0 END) AS page_views,
        SUM(CASE WHEN event_type = 'download' THEN event_count ELSE 0 END) AS downloads
      FROM web_daily_events WHERE day >= ? GROUP BY day ORDER BY day`).bind(
        thirtyDaysAgo,
      ),
    db.prepare(`
      SELECT path,
        SUM(CASE WHEN event_type = 'page_view' THEN event_count ELSE 0 END) AS page_views,
        SUM(CASE WHEN event_type = 'engaged' THEN event_count ELSE 0 END) AS engaged_views,
        SUM(CASE WHEN event_type = 'download' THEN event_count ELSE 0 END) AS downloads
      FROM web_daily_events WHERE day >= ? GROUP BY path
      HAVING page_views > 0 ORDER BY page_views DESC, path LIMIT 15`).bind(
        thirtyDaysAgo,
      ),
    db.prepare(`
      SELECT path AS label, SUM(event_count) AS total
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'page_view' AND traffic_type = 'search'
      GROUP BY path ORDER BY total DESC, path LIMIT 10`).bind(thirtyDaysAgo),
    db.prepare(`
      SELECT source AS label, SUM(event_count) AS total, traffic_type AS secondary
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'page_view' AND traffic_type != 'internal'
      GROUP BY source, traffic_type ORDER BY total DESC, source LIMIT 12`).bind(
        thirtyDaysAgo,
      ),
    db.prepare(`
      SELECT product AS label, SUM(event_count) AS total
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'page_view'
        AND section IN ('Simulator guide', 'Simulator diagnostic', 'Companion diagnostic')
      GROUP BY product ORDER BY total DESC, product LIMIT 14`).bind(
        thirtyDaysAgo,
      ),
    db.prepare(`
      SELECT country AS label, SUM(event_count) AS total
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'page_view'
      GROUP BY country ORDER BY total DESC, country LIMIT 12`).bind(thirtyDaysAgo),
    db.prepare(`
      SELECT device_type AS label, SUM(event_count) AS total
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'page_view'
      GROUP BY device_type ORDER BY total DESC, device_type`).bind(thirtyDaysAgo),
    db.prepare(`
      SELECT path AS source, target, SUM(event_count) AS total
      FROM web_daily_events
      WHERE day >= ? AND event_type = 'internal_navigation'
      GROUP BY path, target ORDER BY total DESC, path, target LIMIT 12`).bind(
        thirtyDaysAgo,
      ),
  ]);

  const totals = results[0]?.results?.[0] ?? {};
  const pageViews = number(totals.thirty_day_views);
  const engaged = number(totals.engaged);

  return {
    todayPageViews: number(totals.today_views),
    sevenDayPageViews: number(totals.seven_day_views),
    thirtyDayPageViews: pageViews,
    downloads: number(totals.downloads),
    engagementRate: pageViews > 0 ? Math.round((engaged / pageViews) * 1000) / 10 : 0,
    organicEntries: number(totals.organic),
    daily: fillDays(results[1]?.results ?? [], now),
    topPages: (results[2]?.results ?? []).map((row) => ({
      path: string(row.path),
      pageViews: number(row.page_views),
      engagedViews: number(row.engaged_views),
      downloads: number(row.downloads),
    })),
    searchLandings: dimensionRows(results[3]?.results ?? []),
    sources: dimensionRows(results[4]?.results ?? []),
    products: dimensionRows(results[5]?.results ?? []),
    countries: dimensionRows(results[6]?.results ?? []).map((row) => ({
      ...row,
      label: countryName(row.label),
    })),
    devices: dimensionRows(results[7]?.results ?? []).map((row) => ({
      ...row,
      label: titleCase(row.label),
    })),
    journeys: (results[8]?.results ?? []).map((row) => ({
      source: string(row.source),
      target: string(row.target),
      count: number(row.total),
    })),
  };
}

export function renderWebsiteDashboard(
  data: WebsiteDashboardData,
  search: SearchConsoleData,
  generatedAt: Date,
  styles: string,
): string {
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>PitMedic Website Analytics</title>
  <style>${styles}</style>
</head>
<body>
  <header>
    <a class="brand" href="/dashboard"><strong>PIT</strong><em>MEDIC</em><span>ANALYTICS</span></a>
    <nav class="dashboard-nav" aria-label="Analytics views"><a href="/dashboard">App usage</a><a class="active" href="/website">Website</a></nav>
    <div class="header-actions"><span class="private-pill">PRIVATE</span><a class="refresh" href="/website">Refresh</a><form method="post" action="/logout"><button class="logout" type="submit">Log out</button></form></div>
  </header>
  <main>
    <section class="intro">
      <div><span class="eyebrow">WEBSITE · LAST 30 DAYS</span><h1>Traffic and content</h1><p>Aggregate page, acquisition, engagement, and signed-installer activity from pitmedic.com.</p></div>
      <div class="timestamp">Updated ${escapeHtml(formatUtc(generatedAt))}</div>
    </section>

    <section class="cards six" aria-label="Website totals">
      ${metricCard("Today", data.todayPageViews, "Page views")}
      ${metricCard("Last 7 days", data.sevenDayPageViews, "Page views")}
      ${metricCard("Last 30 days", data.thirtyDayPageViews, "Page views")}
      ${metricCard("Engagement", `${formatNumber(data.engagementRate)}%`, "30 seconds or 50% scroll")}
      ${metricCard("Organic entries", data.organicEntries, "Search-referred page views")}
      ${metricCard("Downloads", data.downloads, "Signed installer clicks")}
    </section>

    ${renderSearchConsole(search)}

    <section class="panel trend-panel">
      <div class="panel-head"><div><span class="eyebrow">DAILY TREND</span><h2>Page views and downloads</h2></div><div class="legend"><span><i class="views"></i>Views</span><span><i class="downloads"></i>Downloads</span></div></div>
      ${renderTrend(data.daily)}
    </section>

    <section class="panel">
      <div class="panel-head"><div><span class="eyebrow">CONTENT PERFORMANCE</span><h2>Top pages</h2></div><span class="aggregate">30 DAYS</span></div>
      ${renderPages(data.topPages)}
    </section>

    <section class="web-grid two">
      ${dimensionPanel("SEARCH DISCOVERY", "Organic landing pages", data.searchLandings, "No search-referred visits recorded yet.")}
      ${dimensionPanel("ACQUISITION", "Traffic sources", data.sources, "No external entries recorded yet.")}
    </section>

    <section class="web-grid three">
      ${dimensionPanel("INTEREST", "Simulators and software", data.products, "Product interest will appear after page views arrive.")}
      ${dimensionPanel("AUDIENCE", "Countries", data.countries, "Country totals will appear after page views arrive.")}
      ${dimensionPanel("EXPERIENCE", "Device classes", data.devices, "Device totals will appear after page views arrive.")}
    </section>

    <section class="panel">
      <div class="panel-head"><div><span class="eyebrow">CONTENT JOURNEYS</span><h2>Most-used internal links</h2></div><span class="aggregate">30 DAYS</span></div>
      ${renderJourneys(data.journeys)}
    </section>

    <footer><strong>Aggregate-only analytics.</strong> No IP addresses, cookies, local-storage identifiers, full referrer URLs, search terms, or raw user-agent strings are stored. Cloudflare Web Analytics remains enabled separately for unique-visitor and Core Web Vitals reporting.</footer>
  </main>
</body>
</html>`;
}

function renderSearchConsole(data: SearchConsoleData): string {
  if (!data.available) {
    return `<section class="panel search-console"><div class="panel-head"><div><span class="eyebrow">GOOGLE SEARCH CONSOLE</span><h2>Search visibility</h2></div><span class="aggregate">READ ONLY</span></div><div class="empty"><strong>Connection pending</strong><span>${escapeHtml(data.message)}</span></div></section>`;
  }

  return `<section class="panel search-console">
    <div class="panel-head"><div><span class="eyebrow">GOOGLE SEARCH CONSOLE · ${escapeHtml(data.periodStart)} TO ${escapeHtml(data.periodEnd)}</span><h2>Search visibility</h2></div><span class="aggregate">READ ONLY</span></div>
    <div class="search-metrics">
      ${compactMetric("Clicks", formatNumber(data.clicks))}
      ${compactMetric("Impressions", formatNumber(data.impressions))}
      ${compactMetric("CTR", `${formatNumber(data.ctr * 100)}%`)}
      ${compactMetric("Average position", data.position > 0 ? formatNumber(data.position) : "—")}
    </div>
    <div class="search-grid">
      ${renderSearchRows("Top queries", data.queries, false)}
      ${renderSearchRows("Top Google pages", data.pages, true)}
    </div>
  </section>`;
}

function compactMetric(label: string, value: string): string {
  return `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function renderSearchRows(
  title: string,
  rows: SearchMetricRow[],
  paths: boolean,
): string {
  if (rows.length === 0) {
    return `<article><h3>${escapeHtml(title)}</h3>${empty("Search Console has not reported matching data yet.")}</article>`;
  }
  return `<article><h3>${escapeHtml(title)}</h3><div class="table-wrap"><table class="search-table"><thead><tr><th>${paths ? "Page" : "Query"}</th><th>Clicks</th><th>Views</th><th>CTR</th><th>Pos.</th></tr></thead><tbody>${rows
    .map(
      (row) => `<tr><td>${escapeHtml(paths ? pageLabel(row.label) : row.label)}${paths ? `<small class="path">${escapeHtml(row.label)}</small>` : ""}</td><td class="number">${formatNumber(row.clicks)}</td><td class="number">${formatNumber(row.impressions)}</td><td class="number">${formatNumber(row.ctr * 100)}%</td><td class="number">${row.position > 0 ? formatNumber(row.position) : "—"}</td></tr>`,
    )
    .join("")}</tbody></table></div></article>`;
}

function fillDays(rows: QueryRow[], now: Date): WebTrendPoint[] {
  const byDay = new Map(
    rows.map((row) => [
      string(row.day),
      { pageViews: number(row.page_views), downloads: number(row.downloads) },
    ]),
  );
  return Array.from({ length: 30 }, (_, index) => {
    const day = isoDay(addDays(now, index - 29));
    const values = byDay.get(day) ?? { pageViews: 0, downloads: 0 };
    return { day, ...values };
  });
}

function dimensionRows(rows: QueryRow[]): WebDimensionRow[] {
  return rows.map((row) => ({
    label: string(row.label),
    count: number(row.total),
    secondary: typeof row.secondary === "string" ? row.secondary : undefined,
  }));
}

function renderTrend(points: WebTrendPoint[]): string {
  const maximum = Math.max(1, ...points.map((point) => point.pageViews));
  return `<div class="trend-bars">${points
    .map((point) => {
      const viewHeight = Math.max(0, (point.pageViews / maximum) * 100);
      const downloadHeight = Math.max(0, (point.downloads / maximum) * 100);
      return `<div class="trend-day" title="${escapeHtml(formatDay(point.day))}: ${point.pageViews} views, ${point.downloads} downloads"><div class="trend-columns"><i class="views" style="height:${viewHeight.toFixed(2)}%"></i><i class="downloads" style="height:${downloadHeight.toFixed(2)}%"></i></div><span>${escapeHtml(formatShortDay(point.day))}</span></div>`;
    })
    .join("")}</div>`;
}

function renderPages(rows: WebPageRow[]): string {
  if (rows.length === 0) return empty("No website activity recorded yet.");
  return `<div class="table-wrap"><table><thead><tr><th>Page</th><th>Views</th><th>Engaged</th><th>Rate</th><th>Downloads</th></tr></thead><tbody>${rows
    .map((row) => {
      const rate = row.pageViews > 0 ? (row.engagedViews / row.pageViews) * 100 : 0;
      return `<tr><td><a class="path-link" href="https://pitmedic.com${escapeHtml(row.path)}" target="_blank" rel="noreferrer">${escapeHtml(pageLabel(row.path))}</a><small class="path">${escapeHtml(row.path)}</small></td><td class="number">${row.pageViews}</td><td class="number">${row.engagedViews}</td><td class="number">${formatNumber(rate)}%</td><td class="number">${row.downloads}</td></tr>`;
    })
    .join("")}</tbody></table></div>`;
}

function dimensionPanel(
  eyebrow: string,
  title: string,
  rows: WebDimensionRow[],
  emptyMessage: string,
): string {
  const maximum = Math.max(1, ...rows.map((row) => row.count));
  const content = rows.length
    ? `<div class="rank-list">${rows
        .map(
          (row) => {
            const isPath = row.label.startsWith("/");
            const primary = isPath ? pageLabel(row.label) : row.label;
            const secondary = row.secondary ? titleCase(row.secondary) : isPath ? row.label : "";
            return `<div class="rank-row"><div><strong>${escapeHtml(primary)}</strong>${secondary ? `<small>${escapeHtml(secondary)}</small>` : ""}</div><div class="rank-track"><i style="width:${((row.count / maximum) * 100).toFixed(2)}%"></i></div><span>${row.count}</span></div>`;
          },
        )
        .join("")}</div>`
    : empty(emptyMessage);
  return `<article class="panel"><div class="panel-head"><div><span class="eyebrow">${escapeHtml(eyebrow)}</span><h2>${escapeHtml(title)}</h2></div></div>${content}</article>`;
}

function renderJourneys(rows: WebJourneyRow[]): string {
  if (rows.length === 0) return empty("Internal navigation paths will appear after visitors use site links.");
  return `<div class="journeys">${rows
    .map(
      (row) => `<div class="journey"><span>${escapeHtml(pageLabel(row.source))}</span><i>→</i><strong>${escapeHtml(pageLabel(row.target))}</strong><b>${row.count}</b></div>`,
    )
    .join("")}</div>`;
}

function metricCard(label: string, value: number | string, description: string): string {
  return `<article class="metric"><span>${escapeHtml(label)}</span><strong>${typeof value === "number" ? formatNumber(value) : escapeHtml(value)}</strong><small>${escapeHtml(description)}</small></article>`;
}

function empty(message: string): string {
  return `<div class="empty"><strong>Waiting for data</strong><span>${escapeHtml(message)}</span></div>`;
}

function pageLabel(path: string): string {
  if (path === "/") return "Home";
  if (path === "/diagnostic-library/") return "Diagnostic Library";
  const part = path.split("/").filter(Boolean).at(-1) ?? path;
  return titleCase(part.replace(/-/g, " "));
}

function titleCase(value: string): string {
  return value.replace(/\b\w/g, (letter) => letter.toUpperCase());
}

function countryName(code: string): string {
  if (code === "XX") return "Unknown";
  try {
    return new Intl.DisplayNames(["en"], { type: "region" }).of(code) ?? code;
  } catch {
    return code;
  }
}

function number(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : 0;
}

function string(value: unknown): string {
  return typeof value === "string" ? value : "";
}

function addDays(value: Date, days: number): Date {
  return new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate() + days));
}

function isoDay(value: Date): string {
  return value.toISOString().slice(0, 10);
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 }).format(value);
}

function formatDay(value: string): string {
  return new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", year: "numeric", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}

function formatShortDay(value: string): string {
  return new Intl.DateTimeFormat("en-US", { month: "numeric", day: "numeric", timeZone: "UTC" }).format(new Date(`${value}T00:00:00Z`));
}

function formatUtc(value: Date): string {
  return new Intl.DateTimeFormat("en-US", { year: "numeric", month: "short", day: "numeric", hour: "numeric", minute: "2-digit", timeZone: "UTC", timeZoneName: "short" }).format(value);
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"']/g, (character) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[character] ?? character);
}
