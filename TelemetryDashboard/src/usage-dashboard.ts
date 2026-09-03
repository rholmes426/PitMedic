export type TrendPoint = {
  period: string;
  activeInstallations: number;
};

export type UsageBreakdown = {
  appVersion: string;
  channel: string;
  installType: string;
  activeInstallations: number;
};

export type DashboardData = {
  today: number;
  thisMonth: number;
  thirtyDayAverage: number;
  currentVersions: number;
  daily: TrendPoint[];
  monthly: TrendPoint[];
  currentMonthBreakdown: UsageBreakdown[];
};

type QueryRow = {
  period?: unknown;
  app_version?: unknown;
  channel?: unknown;
  install_type?: unknown;
  total?: unknown;
};

const DAILY_SQL = `
  WITH combined AS (
    SELECT day AS period, active_installations AS total
    FROM daily_rollup
    WHERE day >= ?
    UNION ALL
    SELECT day AS period, COUNT(*) AS total
    FROM daily_active
    WHERE day >= ?
    GROUP BY day, app_version, channel, install_type
  )
  SELECT period, SUM(total) AS total
  FROM combined
  GROUP BY period
  ORDER BY period`;

const MONTHLY_SQL = `
  WITH combined AS (
    SELECT month AS period, active_installations AS total
    FROM monthly_rollup
    WHERE month >= ?
    UNION ALL
    SELECT month AS period, COUNT(*) AS total
    FROM monthly_active
    WHERE month >= ?
    GROUP BY month, app_version, channel, install_type
  )
  SELECT period, SUM(total) AS total
  FROM combined
  GROUP BY period
  ORDER BY period`;

const BREAKDOWN_SQL = `
  WITH combined AS (
    SELECT app_version, channel, install_type, active_installations AS total
    FROM monthly_rollup
    WHERE month = ?
    UNION ALL
    SELECT app_version, channel, install_type, COUNT(*) AS total
    FROM monthly_active
    WHERE month = ?
    GROUP BY app_version, channel, install_type
  )
  SELECT app_version, channel, install_type, SUM(total) AS total
  FROM combined
  GROUP BY app_version, channel, install_type
  ORDER BY total DESC, app_version, channel, install_type`;

export async function loadDashboardData(
  db: D1Database,
  now = new Date(),
): Promise<DashboardData> {
  const today = isoDay(now);
  const currentMonth = today.slice(0, 7);
  const firstDailyPeriod = isoDay(addUtcMonthsAndDays(now, 0, -29));
  const firstMonthlyPeriod = isoDay(addUtcMonthsAndDays(now, -11, 0)).slice(
    0,
    7,
  );

  const results = await db.batch<QueryRow>([
    db.prepare(DAILY_SQL).bind(firstDailyPeriod, firstDailyPeriod),
    db.prepare(MONTHLY_SQL).bind(firstMonthlyPeriod, firstMonthlyPeriod),
    db.prepare(BREAKDOWN_SQL).bind(currentMonth, currentMonth),
  ]);

  const daily = fillPeriods(
    readTrendRows(results[0]),
    buildDayPeriods(now, 30),
  );
  const monthly = fillPeriods(
    readTrendRows(results[1]),
    buildMonthPeriods(now, 12),
  );
  const currentMonthBreakdown = readBreakdownRows(results[2]);
  const totalThirtyDays = daily.reduce(
    (sum, point) => sum + point.activeInstallations,
    0,
  );

  return {
    today:
      daily.find((point) => point.period === today)?.activeInstallations ?? 0,
    thisMonth: currentMonthBreakdown.reduce(
      (sum, row) => sum + row.activeInstallations,
      0,
    ),
    thirtyDayAverage: Math.round((totalThirtyDays / 30) * 10) / 10,
    currentVersions: new Set(currentMonthBreakdown.map((row) => row.appVersion))
      .size,
    daily,
    monthly,
    currentMonthBreakdown,
  };
}

export function renderDashboard(
  data: DashboardData,
  generatedAt: Date,
): string {
  const recentDaily = data.daily.slice(-14);
  const latestMonth = data.monthly.at(-1)?.period ?? "Current month";

  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>PitMedic Usage Dashboard</title>
  <style>${dashboardStyles}</style>
</head>
<body>
  <header>
    <a class="brand" href="/dashboard"><strong>PIT</strong><em>MEDIC</em><span>ANALYTICS</span></a>
    <nav class="dashboard-nav" aria-label="Analytics views"><a class="active" href="/dashboard">App usage</a><a href="/website">Website</a></nav>
    <div class="header-actions"><span class="private-pill">PRIVATE</span><a class="refresh" href="/dashboard">Refresh</a><form method="post" action="/logout"><button class="logout" type="submit">Log out</button></form></div>
  </header>
  <main>
    <section class="intro">
      <div><span class="eyebrow">ANONYMOUS APP USAGE</span><h1>Community overview</h1><p>Privacy-preserving active-installation totals. Only people who explicitly opt in are counted.</p></div>
      <div class="timestamp">Updated ${escapeHtml(formatUtc(generatedAt))}</div>
    </section>

    <section class="cards" aria-label="Current usage totals">
      ${metricCard("Today", data.today, "Daily active installations")}
      ${metricCard("This month", data.thisMonth, "Monthly active installations")}
      ${metricCard("30-day average", data.thirtyDayAverage, "Daily active installations")}
      ${metricCard("Current versions", data.currentVersions, "Versions active this month")}
    </section>

    <section class="grid">
      <article class="panel daily-panel">
        <div class="panel-head"><div><span class="eyebrow">LAST 14 DAYS</span><h2>Daily active installations</h2></div></div>
        ${renderBars(recentDaily, "day")}
      </article>
      <article class="panel monthly-panel">
        <div class="panel-head"><div><span class="eyebrow">LAST 12 MONTHS</span><h2>Monthly reach</h2></div></div>
        ${renderBars(data.monthly, "month")}
      </article>
    </section>

    <section class="panel breakdown">
      <div class="panel-head"><div><span class="eyebrow">${escapeHtml(latestMonth)}</span><h2>Version and installation breakdown</h2></div><span class="aggregate">AGGREGATED ONLY</span></div>
      ${renderBreakdown(data.currentMonthBreakdown)}
    </section>

    <footer><strong>No personal data.</strong> This dashboard never reads or displays rotating tokens, IP addresses, diagnostics, findings, repairs, simulator activity, or hardware details.</footer>
  </main>
</body>
</html>`;
}

function readTrendRows(result: D1Result<QueryRow> | undefined): TrendPoint[] {
  return (result?.results ?? []).flatMap((row) => {
    if (typeof row.period !== "string") return [];
    const total = toNonNegativeNumber(row.total);
    return total === null
      ? []
      : [{ period: row.period, activeInstallations: total }];
  });
}

function readBreakdownRows(
  result: D1Result<QueryRow> | undefined,
): UsageBreakdown[] {
  return (result?.results ?? []).flatMap((row) => {
    if (
      typeof row.app_version !== "string" ||
      typeof row.channel !== "string" ||
      typeof row.install_type !== "string"
    )
      return [];
    const total = toNonNegativeNumber(row.total);
    return total === null
      ? []
      : [
          {
            appVersion: row.app_version,
            channel: row.channel,
            installType: row.install_type,
            activeInstallations: total,
          },
        ];
  });
}

function toNonNegativeNumber(value: unknown): number | null {
  return typeof value === "number" && Number.isFinite(value) && value >= 0
    ? value
    : null;
}

function fillPeriods(rows: TrendPoint[], periods: string[]): TrendPoint[] {
  const totals = new Map(
    rows.map((row) => [row.period, row.activeInstallations]),
  );
  return periods.map((period) => ({
    period,
    activeInstallations: totals.get(period) ?? 0,
  }));
}

function buildDayPeriods(now: Date, count: number): string[] {
  return Array.from({ length: count }, (_, index) =>
    isoDay(addUtcMonthsAndDays(now, 0, index - count + 1)),
  );
}

function buildMonthPeriods(now: Date, count: number): string[] {
  return Array.from({ length: count }, (_, index) =>
    isoDay(addUtcMonthsAndDays(now, index - count + 1, 0)).slice(0, 7),
  );
}

function addUtcMonthsAndDays(value: Date, months: number, days: number): Date {
  return new Date(
    Date.UTC(
      value.getUTCFullYear(),
      value.getUTCMonth() + months,
      value.getUTCDate() + days,
    ),
  );
}

function isoDay(value: Date): string {
  return value.toISOString().slice(0, 10);
}

function metricCard(label: string, value: number, description: string): string {
  return `<article class="metric"><span>${escapeHtml(label)}</span><strong>${escapeHtml(formatNumber(value))}</strong><small>${escapeHtml(description)}</small></article>`;
}

function renderBars(points: TrendPoint[], periodType: "day" | "month"): string {
  const maximum = Math.max(
    1,
    ...points.map((point) => point.activeInstallations),
  );
  return `<div class="bars">${points
    .map((point) => {
      const width = (point.activeInstallations / maximum) * 100;
      return `<div class="bar-row"><span>${escapeHtml(formatPeriod(point.period, periodType))}</span><div class="track"><i style="width:${width.toFixed(2)}%"></i></div><strong>${escapeHtml(formatNumber(point.activeInstallations))}</strong></div>`;
    })
    .join("")}</div>`;
}

function renderBreakdown(rows: UsageBreakdown[]): string {
  if (rows.length === 0)
    return `<div class="empty"><strong>No opted-in activity yet.</strong><span>Counts will appear after a v0.6 tester chooses Share anonymous usage.</span></div>`;

  return `<div class="table-wrap"><table><thead><tr><th>Version</th><th>Channel</th><th>Installation</th><th>Active</th></tr></thead><tbody>${rows
    .map(
      (row) =>
        `<tr><td>${escapeHtml(row.appVersion)}</td><td><span class="tag">${escapeHtml(row.channel)}</span></td><td>${escapeHtml(row.installType)}</td><td class="number">${escapeHtml(formatNumber(row.activeInstallations))}</td></tr>`,
    )
    .join("")}</tbody></table></div>`;
}

function formatNumber(value: number): string {
  return new Intl.NumberFormat("en-US", { maximumFractionDigits: 1 }).format(
    value,
  );
}

function formatPeriod(period: string, periodType: "day" | "month"): string {
  const date = new Date(
    `${period}${periodType === "month" ? "-01" : ""}T00:00:00Z`,
  );
  return new Intl.DateTimeFormat("en-US", {
    month: "short",
    day: periodType === "day" ? "numeric" : undefined,
    year: periodType === "month" ? "2-digit" : undefined,
    timeZone: "UTC",
  }).format(date);
}

function formatUtc(value: Date): string {
  return new Intl.DateTimeFormat("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
    timeZone: "UTC",
    timeZoneName: "short",
  }).format(value);
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

export const dashboardStyles = `
:root{color-scheme:light;--navy:#0d2b49;--navy2:#133b61;--orange:#f47a20;--ink:#112a42;--muted:#65788b;--line:#dbe4ec;--soft:#f3f6f9;--white:#fff}
*{box-sizing:border-box}body{margin:0;background:var(--soft);color:var(--ink);font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif}header{height:72px;background:var(--navy);border-bottom:4px solid var(--orange);display:flex;align-items:center;justify-content:space-between;padding:0 max(24px,calc((100vw - 1180px)/2));color:#fff}.brand{color:#fff;text-decoration:none;font-size:20px;letter-spacing:-.5px}.brand strong{font-weight:850}.brand em{font-style:normal;color:var(--orange);font-weight:850}.brand span{font-size:11px;letter-spacing:1.2px;margin-left:12px;color:#a9c1d8}.header-actions{display:flex;align-items:center;gap:12px}.header-actions form{margin:0}.private-pill,.aggregate{font-size:10px;font-weight:800;letter-spacing:1px;border:1px solid #7691aa;border-radius:999px;padding:6px 9px;color:#c9d9e7}.refresh{background:var(--orange);color:#10263d;text-decoration:none;font-weight:800;border-radius:8px;padding:9px 15px}.logout{border:1px solid #7691aa;border-radius:8px;background:transparent;color:#d8e5ef;font:inherit;font-size:12px;font-weight:750;padding:9px 12px;cursor:pointer}main{max-width:1180px;margin:0 auto;padding:38px 24px 54px}.intro{display:flex;align-items:flex-end;justify-content:space-between;gap:24px;margin-bottom:25px}.eyebrow{display:block;color:#60758a;font-size:11px;font-weight:850;letter-spacing:1px;margin-bottom:7px}.intro h1{font-size:38px;line-height:1.05;margin:0 0 9px;color:var(--navy);letter-spacing:-1px}.intro p{margin:0;color:var(--muted);font-size:15px}.timestamp{font-size:12px;color:var(--muted);white-space:nowrap}.cards{display:grid;grid-template-columns:repeat(4,1fr);gap:14px;margin-bottom:14px}.metric,.panel{background:#fff;border:1px solid var(--line);border-radius:14px;box-shadow:0 7px 24px rgba(25,53,80,.045)}.metric{padding:20px}.metric>span{display:block;color:#60758a;text-transform:uppercase;font-size:11px;font-weight:800;letter-spacing:.5px}.metric>strong{display:block;color:var(--navy);font-size:32px;line-height:1.1;margin:8px 0 5px}.metric>small{color:var(--muted);font-size:12px}.grid{display:grid;grid-template-columns:1.35fr 1fr;gap:14px;margin-bottom:14px}.panel{padding:22px}.panel-head{display:flex;align-items:flex-start;justify-content:space-between;gap:18px;margin-bottom:17px}.panel h2{font-size:20px;color:var(--navy);margin:0}.aggregate{color:#60758a;border-color:var(--line)}.bars{display:grid;gap:8px}.bar-row{display:grid;grid-template-columns:58px 1fr 36px;align-items:center;gap:10px;min-height:15px}.bar-row>span{font-size:11px;color:var(--muted)}.bar-row>strong{font-size:11px;text-align:right;color:var(--navy)}.track{height:8px;background:#edf2f6;border-radius:999px;overflow:hidden}.track i{display:block;height:100%;min-width:0;background:linear-gradient(90deg,var(--orange),#ff9a52);border-radius:inherit}.monthly-panel .track i{background:linear-gradient(90deg,var(--navy2),#2c6a9c)}.table-wrap{overflow-x:auto}table{border-collapse:collapse;width:100%;font-size:13px}th{text-align:left;color:#65798c;text-transform:uppercase;letter-spacing:.5px;font-size:10px;padding:0 14px 10px;border-bottom:1px solid var(--line)}td{padding:13px 14px;border-bottom:1px solid #edf1f4}tbody tr:last-child td{border-bottom:0}.number{text-align:right;font-weight:800;color:var(--navy)}th:last-child{text-align:right}.tag{display:inline-block;background:#eaf1f7;color:var(--navy2);border-radius:999px;padding:4px 8px;font-weight:700;font-size:11px}.empty{padding:30px;text-align:center;border:1px dashed #cbd7e1;border-radius:10px;background:#fafcfd}.empty strong,.empty span{display:block}.empty span{color:var(--muted);font-size:13px;margin-top:7px}footer{font-size:12px;color:var(--muted);text-align:center;padding:23px 10px 0}footer strong{color:var(--navy)}
header{gap:22px}.dashboard-nav{display:flex;align-items:center;gap:5px;margin-left:auto}.dashboard-nav a{color:#bcd0e1;text-decoration:none;font-size:12px;font-weight:750;padding:8px 11px;border-radius:8px}.dashboard-nav a:hover,.dashboard-nav a.active{background:#234966;color:#fff}.cards.six{grid-template-columns:repeat(6,1fr)}.cards.six .metric{padding:17px}.cards.six .metric>strong{font-size:27px}.trend-panel{margin-bottom:14px}.legend{display:flex;gap:13px;color:var(--muted);font-size:11px}.legend span{display:flex;align-items:center;gap:5px}.legend i{display:block;width:9px;height:9px;border-radius:2px}.legend .views,.trend-columns .views{background:var(--orange)}.legend .downloads,.trend-columns .downloads{background:var(--navy2)}.trend-bars{height:190px;display:grid;grid-template-columns:repeat(30,minmax(8px,1fr));align-items:end;gap:5px;border-bottom:1px solid var(--line);padding-top:12px}.trend-day{height:100%;display:grid;grid-template-rows:1fr 22px;min-width:0}.trend-columns{display:flex;align-items:flex-end;justify-content:center;gap:2px;height:100%}.trend-columns i{display:block;width:42%;min-height:0;border-radius:3px 3px 0 0}.trend-day>span{font-size:9px;color:var(--muted);text-align:center;padding-top:6px}.web-grid{display:grid;gap:14px;margin:14px 0}.web-grid.two{grid-template-columns:1fr 1fr}.web-grid.three{grid-template-columns:1.35fr 1fr 1fr}.rank-list{display:grid;gap:11px}.rank-row{display:grid;grid-template-columns:minmax(115px,1fr) 1.4fr 34px;align-items:center;gap:10px}.rank-row strong,.rank-row small{display:block}.rank-row strong{font-size:12px;color:var(--navy)}.rank-row small{font-size:9px;color:var(--muted);margin-top:2px}.rank-row>span{font-size:11px;font-weight:800;color:var(--navy);text-align:right}.rank-track{height:7px;background:#edf2f6;border-radius:999px;overflow:hidden}.rank-track i{display:block;height:100%;background:linear-gradient(90deg,var(--orange),#ff9a52);border-radius:inherit}.path-link{color:var(--navy);font-weight:750;text-decoration:none}.path-link:hover{text-decoration:underline}.path{display:block;color:var(--muted);font-size:10px;margin-top:3px}.journeys{display:grid;grid-template-columns:1fr 1fr;gap:8px 18px}.journey{display:grid;grid-template-columns:1fr 18px 1fr 30px;align-items:center;gap:7px;padding:10px 12px;background:var(--soft);border-radius:8px;font-size:11px}.journey span{color:var(--muted)}.journey i{font-style:normal;color:var(--orange);font-weight:900;text-align:center}.journey strong{color:var(--navy)}.journey b{text-align:right;color:var(--navy)}
header{gap:22px}.dashboard-nav{display:flex;align-items:center;gap:5px;margin-left:auto}.dashboard-nav a{color:#bcd0e1;text-decoration:none;font-size:12px;font-weight:750;padding:8px 11px;border-radius:8px}.dashboard-nav a:hover,.dashboard-nav a.active{background:#234966;color:#fff}.cards.six{grid-template-columns:repeat(6,1fr)}.cards.six .metric{padding:17px}.cards.six .metric>strong{font-size:27px}.search-console{margin-bottom:14px;border-top:3px solid #4285f4}.search-metrics{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:20px}.search-metrics>div{background:var(--soft);border-radius:10px;padding:14px}.search-metrics span,.search-metrics strong{display:block}.search-metrics span{font-size:10px;text-transform:uppercase;letter-spacing:.5px;color:var(--muted);font-weight:800}.search-metrics strong{font-size:24px;color:var(--navy);margin-top:5px}.search-grid{display:grid;grid-template-columns:1fr 1fr;gap:22px}.search-grid h3{font-size:14px;color:var(--navy);margin:0 0 12px}.search-table{font-size:11px}.search-table th,.search-table td{padding-left:8px;padding-right:8px}.trend-panel{margin-bottom:14px}.legend{display:flex;gap:13px;color:var(--muted);font-size:11px}.legend span{display:flex;align-items:center;gap:5px}.legend i{display:block;width:9px;height:9px;border-radius:2px}.legend .views,.trend-columns .views{background:var(--orange)}.legend .downloads,.trend-columns .downloads{background:var(--navy2)}.trend-bars{height:190px;display:grid;grid-template-columns:repeat(30,minmax(8px,1fr));align-items:end;gap:5px;border-bottom:1px solid var(--line);padding-top:12px}.trend-day{height:100%;display:grid;grid-template-rows:1fr 22px;min-width:0}.trend-columns{display:flex;align-items:flex-end;justify-content:center;gap:2px;height:100%}.trend-columns i{display:block;width:42%;min-height:0;border-radius:3px 3px 0 0}.trend-day>span{font-size:9px;color:var(--muted);text-align:center;padding-top:6px}.web-grid{display:grid;gap:14px;margin:14px 0}.web-grid.two{grid-template-columns:1fr 1fr}.web-grid.three{grid-template-columns:1.35fr 1fr 1fr}.rank-list{display:grid;gap:11px}.rank-row{display:grid;grid-template-columns:minmax(115px,1fr) 1.4fr 34px;align-items:center;gap:10px}.rank-row strong,.rank-row small{display:block}.rank-row strong{font-size:12px;color:var(--navy)}.rank-row small{font-size:9px;color:var(--muted);margin-top:2px}.rank-row>span{font-size:11px;font-weight:800;color:var(--navy);text-align:right}.rank-track{height:7px;background:#edf2f6;border-radius:999px;overflow:hidden}.rank-track i{display:block;height:100%;background:linear-gradient(90deg,var(--orange),#ff9a52);border-radius:inherit}.path-link{color:var(--navy);font-weight:750;text-decoration:none}.path-link:hover{text-decoration:underline}.path{display:block;color:var(--muted);font-size:10px;margin-top:3px}.journeys{display:grid;grid-template-columns:1fr 1fr;gap:8px 18px}.journey{display:grid;grid-template-columns:1fr 18px 1fr 30px;align-items:center;gap:7px;padding:10px 12px;background:var(--soft);border-radius:8px;font-size:11px}.journey span{color:var(--muted)}.journey i{font-style:normal;color:var(--orange);font-weight:900;text-align:center}.journey strong{color:var(--navy)}.journey b{text-align:right;color:var(--navy)}
@media(max-width:1050px){.cards.six{grid-template-columns:repeat(3,1fr)}.web-grid.three{grid-template-columns:1fr 1fr}.web-grid.three .panel:first-child{grid-column:1/-1}}
@media(max-width:820px){header{height:auto;min-height:72px;flex-wrap:wrap;padding-top:12px;padding-bottom:12px}.dashboard-nav{order:3;width:100%;margin:0}.cards{grid-template-columns:repeat(2,1fr)}.grid,.web-grid.two,.web-grid.three,.search-grid{grid-template-columns:1fr}.search-metrics{grid-template-columns:1fr 1fr}.web-grid.three .panel:first-child{grid-column:auto}.intro{align-items:flex-start;flex-direction:column}.timestamp{white-space:normal}.journeys{grid-template-columns:1fr}.trend-day>span{display:none}}
@media(max-width:500px){header{padding:0 16px}.brand span{display:none}main{padding:28px 14px}.cards{grid-template-columns:1fr 1fr}.metric{padding:16px}.metric>strong{font-size:26px}.panel{padding:17px}.intro h1{font-size:31px}.private-pill{display:none}}
`;
