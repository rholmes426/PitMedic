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

export type WebsiteAnalyticsEnv = {
  NEON_ANALYTICS_URL: string;
  NEON_ANALYTICS_CREDENTIALS: string;
};

export async function loadWebsiteDashboardData(
  env: WebsiteAnalyticsEnv,
  fetcher: typeof fetch = fetch,
): Promise<WebsiteDashboardData> {
  const baseUrl = env.NEON_ANALYTICS_URL?.replace(/\/+$/, "");
  const credentials = env.NEON_ANALYTICS_CREDENTIALS?.trim();
  if (!baseUrl || !credentials) throw new Error("Website analytics source is not configured");

  const response = await fetcher(`${baseUrl}/v1/website-summary`, {
    headers: { Authorization: `Basic ${credentials}` },
  });
  if (!response.ok) throw new Error(`Website analytics source returned ${response.status}`);

  const payload = await response.json() as { protocol?: unknown; data?: unknown };
  if (payload.protocol !== 1) throw new Error("Unsupported website analytics protocol");
  return validateWebsiteData(payload.data);
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
    <nav class="dashboard-nav" aria-label="Dashboard tabs"><a href="/dashboard">Overview</a><a href="/app">App usage</a><a class="active" aria-current="page" href="/website">Website &amp; search</a></nav>
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
    ${renderSearchDaily(data.daily)}
    <div class="search-grid">
      ${renderSearchRows("Top queries", data.queries, false)}
      ${renderSearchRows("Top Google pages", data.pages, true)}
    </div>
  </section>`;
}

function compactMetric(label: string, value: string): string {
  return `<div><span>${escapeHtml(label)}</span><strong>${escapeHtml(value)}</strong></div>`;
}

function renderSearchDaily(rows: SearchConsoleData["daily"]): string {
  if (rows.length === 0) {
    return `<div style="margin-bottom:22px"><div class="panel-head"><div><span class="eyebrow">DAILY GOOGLE PERFORMANCE</span><h2>Clicks and impressions by date</h2></div></div>${empty("Search Console has not reported daily data yet.")}</div>`;
  }

  const sorted = [...rows].sort((a, b) => b.date.localeCompare(a.date));
  return `<div style="margin-bottom:22px"><div class="panel-head"><div><span class="eyebrow">DAILY GOOGLE PERFORMANCE</span><h2>Clicks and impressions by date</h2></div></div><div class="table-wrap"><table class="search-table"><thead><tr><th>Date</th><th>Clicks</th><th>Impressions</th><th>CTR</th></tr></thead><tbody>${sorted
    .map((row) => {
      const ctr = row.impressions > 0 ? (row.clicks / row.impressions) * 100 : 0;
      return `<tr><td>${escapeHtml(formatDay(row.date))}</td><td class="number">${formatNumber(row.clicks)}</td><td class="number">${formatNumber(row.impressions)}</td><td class="number">${formatNumber(ctr)}%</td></tr>`;
    })
    .join("")}</tbody></table></div></div>`;
}

function renderSearchRows(
  title: string,
  rows: SearchMetricRow[],
  paths: boolean,
): string {
  if (rows.length === 0) {
    return `<article><h3>${escapeHtml(title)}</h3>${empty("Search Console has not reported matching data yet.")}</article>`;
  }
  return `<article><h3>${escapeHtml(title)}</h3><div class="table-wrap"><table class="search-table"><thead><tr><th>${paths ? "Page" : "Query"}</th><th>Clicks</th><th>Impressions</th><th>CTR</th><th>Pos.</th></tr></thead><tbody>${rows
    .map(
      (row) => `<tr><td>${escapeHtml(paths ? pageLabel(row.label) : row.label)}${paths ? `<small class="path">${escapeHtml(row.label)}</small>` : ""}</td><td class="number">${formatNumber(row.clicks)}</td><td class="number">${formatNumber(row.impressions)}</td><td class="number">${formatNumber(row.ctr * 100)}%</td><td class="number">${row.position > 0 ? formatNumber(row.position) : "—"}</td></tr>`,
    )
    .join("")}</tbody></table></div></article>`;
}

function validateWebsiteData(value: unknown): WebsiteDashboardData {
  const data = record(value, "website analytics data");
  const dimensions = (key: string, mapLabel = (label: string) => label): WebDimensionRow[] =>
    array(data[key], key).map((item) => {
      const row = record(item, key);
      return {
        label: mapLabel(text(row.label, `${key}.label`)),
        count: metric(row.count, `${key}.count`),
        ...(row.secondary === undefined ? {} : { secondary: text(row.secondary, `${key}.secondary`) }),
      };
    });

  return {
    todayPageViews: metric(data.todayPageViews, "todayPageViews"),
    sevenDayPageViews: metric(data.sevenDayPageViews, "sevenDayPageViews"),
    thirtyDayPageViews: metric(data.thirtyDayPageViews, "thirtyDayPageViews"),
    downloads: metric(data.downloads, "downloads"),
    engagementRate: metric(data.engagementRate, "engagementRate"),
    organicEntries: metric(data.organicEntries, "organicEntries"),
    daily: array(data.daily, "daily").map((item) => {
      const row = record(item, "daily");
      const day = text(row.day, "daily.day");
      if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) throw new Error("Invalid daily.day");
      return {
        day,
        pageViews: metric(row.pageViews, "daily.pageViews"),
        downloads: metric(row.downloads, "daily.downloads"),
      };
    }),
    topPages: array(data.topPages, "topPages").map((item) => {
      const row = record(item, "topPages");
      return {
        path: text(row.path, "topPages.path"),
        pageViews: metric(row.pageViews, "topPages.pageViews"),
        engagedViews: metric(row.engagedViews, "topPages.engagedViews"),
        downloads: metric(row.downloads, "topPages.downloads"),
      };
    }),
    searchLandings: dimensions("searchLandings"),
    sources: dimensions("sources"),
    products: dimensions("products"),
    countries: dimensions("countries", countryName),
    devices: dimensions("devices", titleCase),
    journeys: array(data.journeys, "journeys").map((item) => {
      const row = record(item, "journeys");
      return {
        source: text(row.source, "journeys.source"),
        target: text(row.target, "journeys.target"),
        count: metric(row.count, "journeys.count"),
      };
    }),
  };
}

function record(value: unknown, label: string): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error(`Invalid ${label}`);
  return value as Record<string, unknown>;
}

function array(value: unknown, label: string): unknown[] {
  if (!Array.isArray(value)) throw new Error(`Invalid ${label}`);
  return value;
}

function metric(value: unknown, label: string): number {
  if (typeof value !== "number" || !Number.isFinite(value) || value < 0) throw new Error(`Invalid ${label}`);
  return value;
}

function text(value: unknown, label: string): string {
  if (typeof value !== "string") throw new Error(`Invalid ${label}`);
  return value;
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
  if (code === "XX" || code === "ZZ") return "Unknown";
  try {
    return new Intl.DisplayNames(["en"], { type: "region" }).of(code) ?? code;
  } catch {
    return code;
  }
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