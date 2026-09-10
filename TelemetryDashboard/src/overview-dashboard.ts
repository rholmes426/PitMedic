import { detailUrl, metricLink, webRange } from "./metric-details";
import { downloadDescription, type GitHubDownloadData } from "./github-downloads";
import type { SearchConsoleData } from "./search-console";
import type { DashboardData } from "./usage-dashboard";
import type { WebsiteDashboardData } from "./website-dashboard";

export function renderOverviewDashboard(
  usage: DashboardData,
  website: WebsiteDashboardData,
  search: SearchConsoleData,
  downloads: GitHubDownloadData,
  generatedAt: Date,
  styles: string,
): string {
  const range = webRange(generatedAt);
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>PitMedic Analytics Overview</title>
  <style>${styles}</style>
</head>
<body>
  <header>
    <a class="brand" href="/dashboard"><strong>PIT</strong><em>MEDIC</em><span>ANALYTICS</span></a>
    <nav class="dashboard-nav" aria-label="Dashboard tabs"><a class="active" aria-current="page" href="/dashboard">Overview</a><a href="/app">App usage</a><a href="/website">Website &amp; search</a></nav>
    <div class="header-actions"><span class="private-pill">PRIVATE</span><a class="refresh" href="/dashboard">Refresh</a><form method="post" action="/logout"><button class="logout" type="submit">Log out</button></form></div>
  </header>
  <main>
    <section class="intro">
      <div><span class="eyebrow">PITMEDIC ANALYTICS</span><h1>Everything at a glance</h1><p>App adoption, downloads, website activity, and Google search visibility in one private dashboard.</p></div>
      <div class="timestamp">Updated ${escapeHtml(formatUtc(generatedAt))}</div>
    </section>

    <section class="cards six" aria-label="Combined analytics totals">
      ${metricCard("Active today", usage.today, "Opted-in installations", "/app")}
      ${metricCard("Active this month", usage.thisMonth, "Opted-in installations", "/app")}
      ${metricCard("GitHub downloads", downloads.available ? downloads.totalDownloads : "—", downloadDescription(downloads), "/app")}
      ${metricCard("Website views", website.thirtyDayPageViews, "Last 30 days", detailUrl("views",range.start,range.end))}
      ${metricCard("Google clicks", search.available ? search.clicks : "—", search.available ? "Search Console period" : "Connection unavailable", detailUrl("google-clicks",search.periodStart,search.periodEnd))}
      ${metricCard("Google impressions", search.available ? search.impressions : "—", search.available ? "Search Console period" : "Connection unavailable", detailUrl("google-impressions",search.periodStart,search.periodEnd))}
    </section>

    <section class="overview-grid">
      <article class="panel overview-card">
        <div class="panel-head"><div><span class="eyebrow">APP</span><h2>Usage and distribution</h2></div><a class="panel-link" href="/app">Open app usage</a></div>
        <div class="overview-stats"><div><span>30-day daily average</span><strong>${formatNumber(usage.thirtyDayAverage)}</strong></div><div><span>Active versions</span><strong>${formatNumber(usage.currentVersions)}</strong></div><div><span>Package downloads</span><strong>${downloads.available ? formatNumber(downloads.totalDownloads) : "—"}</strong><small>${escapeHtml(downloadDescription(downloads))}</small></div></div>
        <p>Review daily and monthly adoption, active versions, release channels, and installation types.</p>
      </article>
      <article class="panel overview-card">
        <div class="panel-head"><div><span class="eyebrow">WEBSITE &amp; SEARCH</span><h2>Reach and discovery</h2></div><a class="panel-link" href="/website">Open website &amp; search</a></div>
        <div class="overview-stats"><div><span>7-day views</span><strong>${metricLink(website.sevenDayPageViews,detailUrl("views",webRange(generatedAt,7).start,range.end))}</strong></div><div><span>Engagement</span><strong>${metricLink(`${formatNumber(website.engagementRate)}%`,detailUrl("engaged",webRange(generatedAt,30).start,range.end))}</strong></div><div><span>Organic entries</span><strong>${metricLink(website.organicEntries,detailUrl("organic",webRange(generatedAt,30).start,range.end))}</strong></div></div>
        <p>${search.available ? `Google reported ${formatNumber(search.clicks)} clicks from ${formatNumber(search.impressions)} impressions.` : escapeHtml(search.message)}</p>
      </article>
    </section>

    <footer><strong>Private, aggregate analytics.</strong> Use the tabs above for detailed app, website, and search reporting.</footer>
  </main>
</body>
</html>`;
}

function metricCard(label: string, value: number | string, description: string, href:string): string {
  return `<article class="metric"><span>${escapeHtml(label)}</span><strong>${metricLink(value,href)}</strong><small>${escapeHtml(description)}</small></article>`;
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 }).format(value);
}

function formatUtc(value: Date): string {
  return `${new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
  }).format(value)} UTC`;
}

function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    (character) =>
      ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;",
      })[character] ?? character,
  );
}
