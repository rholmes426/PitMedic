import { timingSafeEqual } from "node:crypto";
import { attachDatabasePool } from "@neon/functions";
import pg from "pg";

const { Pool } = pg;
const pool = new Pool({
  connectionString: process.env.DATABASE_URL,
  max: 5,
  idleTimeoutMillis: 30_000,
});
attachDatabasePool(pool);

const ALLOWED_ORIGINS = new Set([
  "https://pitmedic.com",
  "https://www.pitmedic.com",
]);
const EVENTS = new Set([
  "page_view",
  "engaged",
  "download",
  "internal_navigation",
]);
const MAX_BODY_BYTES = 2_048;

type WebEvent = {
  protocol: 1;
  event: string;
  path: string;
  target: string;
  referrer: string;
};

export default {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/health" && request.method === "GET") {
      await pool.query("SELECT 1");
      return json({ status: "ok", protocol: 1 });
    }

    if (url.pathname === "/v1/web-event") {
      return handleWebEvent(request);
    }

    if ((url.pathname === "/" || url.pathname === "/dashboard") && request.method === "GET") {
      if (!isAuthorized(request)) {
        return new Response("Authentication required", {
          status: 401,
          headers: {
            "WWW-Authenticate": 'Basic realm="PitMedic Analytics", charset="UTF-8"',
            "Cache-Control": "no-store",
          },
        });
      }
      return dashboard();
    }

    return json({ error: "not_found" }, 404);
  },
};

async function handleWebEvent(request: Request): Promise<Response> {
  const origin = request.headers.get("Origin");
  if (!origin || !ALLOWED_ORIGINS.has(origin)) {
    return json({ error: "origin_not_allowed" }, 403);
  }
  const cors = {
    "Access-Control-Allow-Origin": origin,
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    "Access-Control-Max-Age": "86400",
    Vary: "Origin",
  };

  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: cors });
  }
  if (request.method !== "POST") {
    return json({ error: "method_not_allowed" }, 405, { ...cors, Allow: "POST, OPTIONS" });
  }

  try {
    const length = Number(request.headers.get("Content-Length") || "0");
    if (length > MAX_BODY_BYTES) return json({ error: "payload_too_large" }, 413, cors);
    const raw = await request.text();
    if (new TextEncoder().encode(raw).byteLength > MAX_BODY_BYTES) {
      return json({ error: "payload_too_large" }, 413, cors);
    }
    const payload = JSON.parse(raw) as unknown;
    const validated = validateEvent(payload);
    if (!validated) return json({ error: "invalid_payload" }, 400, cors);

    const day = new Date().toISOString().slice(0, 10);
    const path = normalizePath(validated.path);
    const target = validated.target.startsWith("/")
      ? normalizePath(validated.target)
      : validated.target.toLowerCase();
    const content = classifyContent(path);
    const traffic = classifyTraffic(validated.referrer);
    const device = classifyDevice(request.headers.get("User-Agent") || "");

    await pool.query(
      `INSERT INTO web_daily_events
        (day, event_type, path, target, section, product, traffic_type, source, country, device_type, event_count)
       VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'ZZ', $9, 1)
       ON CONFLICT (day, event_type, path, target, section, product, traffic_type, source, country, device_type)
       DO UPDATE SET event_count = web_daily_events.event_count + 1`,
      [day, validated.event, path, target, content.section, content.product, traffic.type, traffic.source, device],
    );
    return new Response(null, { status: 204, headers: cors });
  } catch (error) {
    console.error(JSON.stringify({
      event: "website_analytics_failed",
      errorType: error instanceof Error ? error.name : "UnknownError",
    }));
    return json({ error: "temporary_failure" }, 503, cors);
  }
}

function validateEvent(value: unknown): WebEvent | null {
  if (!value || typeof value !== "object" || Array.isArray(value)) return null;
  const item = value as Record<string, unknown>;
  const keys = Object.keys(item).sort().join(",");
  if (keys !== "event,path,protocol,referrer,target") return null;
  if (item.protocol !== 1 || typeof item.event !== "string" || !EVENTS.has(item.event)) return null;
  if (!validPath(item.path)) return null;
  if (typeof item.target !== "string" || item.target.length > 180) return null;
  if (item.event === "internal_navigation" && !validPath(item.target)) return null;
  if (item.event === "download" && !/^release:v\d+\.\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$/i.test(item.target)) return null;
  if (!["download", "internal_navigation"].includes(item.event) && item.target !== "") return null;
  if (
    typeof item.referrer !== "string" ||
    item.referrer.length > 120 ||
    (item.referrer !== "" && !/^(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(item.referrer))
  ) return null;
  return item as WebEvent;
}

function validPath(value: unknown): value is string {
  return typeof value === "string" &&
    value.length >= 1 &&
    value.length <= 180 &&
    value.startsWith("/") &&
    !/[?#\\]/.test(value) &&
    /^\/[a-z0-9/_\-.]*$/i.test(value);
}

function normalizePath(value: string): string {
  const normalized = value.replace(/\/{2,}/g, "/").toLowerCase();
  return normalized === "/" || normalized.endsWith("/") ? normalized : `${normalized}/`;
}

function classifyContent(path: string): { section: string; product: string } {
  if (path === "/") return { section: "Home", product: "PitMedic" };
  const simulator = path.match(/^\/simulators\/([^/]+)\//)?.[1];
  if (simulator) return { section: "Simulator guide", product: productName(simulator) };
  const diagnostic = path.match(/^\/diagnostic-library\/([^/]+)\//)?.[1];
  if (diagnostic) {
    const companion = diagnostic.match(/^companion-(moza|simucube|fanatec|logitech-ghub|simagic|asetek|vrs)-/)?.[1];
    const prefix = companion ?? diagnostic.split("-", 1)[0] ?? diagnostic;
    return {
      section: diagnostic.startsWith("companion-") ? "Companion diagnostic" : "Simulator diagnostic",
      product: productName(prefix),
    };
  }
  if (path === "/diagnostic-library/") return { section: "Diagnostic Library", product: "All software" };
  return { section: "Other", product: "PitMedic" };
}

function productName(slug: string): string {
  const names: Record<string, string> = {
    iracing: "iRacing",
    lmu: "Le Mans Ultimate",
    "le-mans-ultimate": "Le Mans Ultimate",
    acc: "Assetto Corsa Competizione",
    "assetto-corsa-competizione": "Assetto Corsa Competizione",
    ams2: "Automobilista 2",
    "automobilista-2": "Automobilista 2",
    ace: "Assetto Corsa EVO",
    "assetto-corsa-evo": "Assetto Corsa EVO",
    raceroom: "RaceRoom Racing Experience",
    moza: "MOZA Pit House",
    simucube: "Simucube True Drive",
    fanatec: "Fanatec software",
    "logitech-ghub": "Logitech G HUB",
    simagic: "SIMAGIC SimPro Manager",
    asetek: "Asetek RaceHub",
    vrs: "VRS DirectForce",
  };
  return names[slug] ?? slug.replace(/-/g, " ");
}

function classifyTraffic(referrer: string): { type: string; source: string } {
  const host = referrer.toLowerCase().replace(/^www\./, "");
  if (!host) return { type: "direct", source: "Direct" };
  if (host === "pitmedic.com") return { type: "internal", source: "PitMedic" };
  const search: Array<[string, string]> = [
    ["google.", "Google"],
    ["bing.com", "Bing"],
    ["duckduckgo.com", "DuckDuckGo"],
    ["search.yahoo.", "Yahoo"],
    ["search.brave.com", "Brave Search"],
  ];
  const searchMatch = search.find(([needle]) => host.includes(needle));
  if (searchMatch) return { type: "search", source: searchMatch[1] };
  const social: Array<[string, string]> = [
    ["reddit.com", "Reddit"],
    ["youtube.com", "YouTube"],
    ["youtu.be", "YouTube"],
    ["facebook.com", "Facebook"],
    ["instagram.com", "Instagram"],
    ["x.com", "X"],
    ["twitter.com", "X"],
    ["discord.com", "Discord"],
    ["discord.gg", "Discord"],
  ];
  const socialMatch = social.find(([needle]) => host === needle || host.endsWith(`.${needle}`));
  return socialMatch
    ? { type: "social", source: socialMatch[1] }
    : { type: "referral", source: host };
}

function classifyDevice(userAgent: string): string {
  const ua = userAgent.toLowerCase();
  if (/ipad|tablet|kindle|silk/.test(ua)) return "tablet";
  if (/mobile|iphone|ipod|android/.test(ua)) return "mobile";
  return ua ? "desktop" : "unknown";
}

function isAuthorized(request: Request): boolean {
  const expectedUser = process.env.DASHBOARD_USER || "";
  const expectedPassword = process.env.DASHBOARD_PASSWORD || "";
  const header = request.headers.get("Authorization") || "";
  if (!expectedUser || !expectedPassword || !header.startsWith("Basic ")) return false;
  let decoded = "";
  try {
    decoded = Buffer.from(header.slice(6), "base64").toString("utf8");
  } catch {
    return false;
  }
  const separator = decoded.indexOf(":");
  if (separator < 0) return false;
  return secureEqual(decoded.slice(0, separator), expectedUser) &&
    secureEqual(decoded.slice(separator + 1), expectedPassword);
}

function secureEqual(actual: string, expected: string): boolean {
  const a = Buffer.from(actual);
  const b = Buffer.from(expected);
  return a.length === b.length && timingSafeEqual(a, b);
}

async function dashboard(): Promise<Response> {
  const [
    eventTotals,
    webTrend,
    topPages,
    trafficSources,
    products,
    gscTrend,
    gscPages,
    gscQueries,
    gscCountries,
    gscDevices,
    metadata,
  ] = await Promise.all([
    pool.query(`SELECT event_type, SUM(event_count)::bigint AS total FROM web_daily_events WHERE day >= CURRENT_DATE - 29 GROUP BY event_type ORDER BY total DESC`),
    pool.query(`SELECT day::text, event_type, SUM(event_count)::bigint AS total FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type IN ('page_view','engaged') GROUP BY day, event_type ORDER BY day`),
    pool.query(`SELECT path, SUM(event_count)::bigint AS views FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type='page_view' GROUP BY path ORDER BY views DESC, path LIMIT 20`),
    pool.query(`SELECT traffic_type, source, SUM(event_count)::bigint AS visits FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type='page_view' GROUP BY traffic_type, source ORDER BY visits DESC, source LIMIT 20`),
    pool.query(`SELECT section, product, SUM(event_count)::bigint AS views FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type='page_view' GROUP BY section, product ORDER BY views DESC, product LIMIT 20`),
    pool.query(`SELECT day::text, clicks, impressions, ctr, position FROM gsc_daily_metrics WHERE dimension_type='site' ORDER BY day`),
    pool.query(`SELECT dimension_value AS page, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, CASE WHEN SUM(impressions)>0 THEN SUM(clicks)::float/SUM(impressions) ELSE 0 END AS ctr, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='page' GROUP BY dimension_value ORDER BY impressions DESC LIMIT 20`),
    pool.query(`SELECT dimension_value AS query, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, CASE WHEN SUM(impressions)>0 THEN SUM(clicks)::float/SUM(impressions) ELSE 0 END AS ctr, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='query' GROUP BY dimension_value ORDER BY impressions DESC LIMIT 20`),
    pool.query(`SELECT dimension_value AS country, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='country' GROUP BY dimension_value ORDER BY impressions DESC`),
    pool.query(`SELECT dimension_value AS device, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='device' GROUP BY dimension_value ORDER BY impressions DESC`),
    pool.query(`SELECT key, value FROM analytics_metadata WHERE key IN ('gsc_settled_through','gsc_last_sync','privacy_model') ORDER BY key`),
  ]);

  const events = new Map(eventTotals.rows.map((row) => [String(row.event_type), Number(row.total)]));
  const pageViews = events.get("page_view") || 0;
  const engaged = events.get("engaged") || 0;
  const downloads = events.get("download") || 0;
  const nav = events.get("internal_navigation") || 0;
  const gsc = gscTrend.rows;
  const gscClicks = gsc.reduce((sum, row) => sum + Number(row.clicks), 0);
  const gscImpressions = gsc.reduce((sum, row) => sum + Number(row.impressions), 0);
  const weightedPosition = gscImpressions
    ? gsc.reduce((sum, row) => sum + Number(row.position) * Number(row.impressions), 0) / gscImpressions
    : 0;
  const meta = Object.fromEntries(metadata.rows.map((row) => [String(row.key), String(row.value)]));

  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PitMedic Analytics</title>
<style>
:root{color-scheme:dark;--bg:#07111d;--panel:#0d1b2a;--line:#20384d;--text:#e8f1f8;--muted:#91a9ba;--cyan:#2dd4bf;--blue:#60a5fa;--amber:#fbbf24}
*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at top,#10283e 0,#07111d 42%);color:var(--text);font:15px/1.45 Inter,system-ui,sans-serif}
main{max-width:1280px;margin:auto;padding:32px 22px 60px}header{display:flex;align-items:end;justify-content:space-between;gap:20px;margin-bottom:24px}
h1{margin:0;font-size:30px}h2{font-size:18px;margin:0 0 14px}.sub,.muted{color:var(--muted)}
.tabs{display:flex;gap:8px;margin:20px 0}.tab{border:1px solid var(--line);background:#0b1825;color:var(--text);border-radius:999px;padding:8px 14px;cursor:pointer}.tab.active{background:var(--cyan);color:#04120f;border-color:var(--cyan);font-weight:700}
.view{display:none}.view.active{display:block}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}.kpi,.panel{background:color-mix(in srgb,var(--panel) 94%,transparent);border:1px solid var(--line);border-radius:14px;box-shadow:0 16px 40px #0003}.kpi{padding:18px}.kpi b{display:block;font-size:28px;margin-top:5px}.panel{padding:18px;margin-top:14px;overflow:auto}.two{display:grid;grid-template-columns:1fr 1fr;gap:14px}
table{border-collapse:collapse;width:100%;min-width:560px}th,td{text-align:left;border-bottom:1px solid var(--line);padding:9px 8px}th{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.06em}td.num,th.num{text-align:right}
.bar{height:8px;background:#122638;border-radius:5px;overflow:hidden;min-width:100px}.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--cyan));border-radius:5px}.empty{padding:34px;text-align:center;color:var(--muted)}
footer{color:var(--muted);margin-top:22px;font-size:13px}@media(max-width:850px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}header{align-items:start;flex-direction:column}}@media(max-width:460px){.grid{grid-template-columns:1fr}}
</style></head><body><main>
<header><div><h1>PitMedic Analytics</h1><div class="sub">Privacy-first website and Google Search performance</div></div><div class="muted">Last 30 days · refreshed on load</div></header>
<div class="tabs"><button class="tab active" data-view="web">Website</button><button class="tab" data-view="search">Google Search</button></div>
<section id="web" class="view active">
<div class="grid">
${kpi("Page views", pageViews)}${kpi("Engaged visits", engaged, pageViews ? pct(engaged/pageViews) + " engagement" : "No data yet")}${kpi("Downloads", downloads)}${kpi("Internal clicks", nav)}
</div>
<div class="panel"><h2>Daily activity</h2>${webTrend.rows.length ? webTrendTable(webTrend.rows) : empty("Collection starts when the updated website tracker is published.")}</div>
<div class="two">
<div class="panel"><h2>Top pages</h2>${rankTable(topPages.rows,"path","views","Views")}</div>
<div class="panel"><h2>Traffic sources</h2>${trafficTable(trafficSources.rows)}</div>
</div>
<div class="panel"><h2>Content and products</h2>${productTable(products.rows)}</div>
</section>
<section id="search" class="view">
<div class="grid">
${kpi("Clicks",gscClicks)}${kpi("Impressions",gscImpressions)}${kpi("Search CTR",gscImpressions?pct(gscClicks/gscImpressions):"0%")}${kpi("Avg. position",weightedPosition?weightedPosition.toFixed(1):"—")}
</div>
<div class="panel"><h2>Daily search performance</h2>${gscTrendTable(gsc)}</div>
<div class="two">
<div class="panel"><h2>Search landing pages</h2>${searchTable(gscPages.rows,"page")}</div>
<div class="panel"><h2>Queries</h2>${searchTable(gscQueries.rows,"query")}</div>
</div>
<div class="two">
<div class="panel"><h2>Countries</h2>${searchTable(gscCountries.rows,"country")}</div>
<div class="panel"><h2>Devices</h2>${searchTable(gscDevices.rows,"device")}</div>
</div>
</section>
<footer>Search Console settled through ${escapeHtml(meta.gsc_settled_through || "—")} · Daily aggregates only · No cookies, IP addresses, visitor IDs, full referrer URLs, search terms from website visits, or raw user-agent strings are stored.</footer>
</main><script>
document.querySelectorAll(".tab").forEach(function(button){button.addEventListener("click",function(){document.querySelectorAll(".tab,.view").forEach(function(el){el.classList.remove("active")});button.classList.add("active");document.getElementById(button.dataset.view).classList.add("active")})});
</script></body></html>`;
  return new Response(html, {
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      "Cache-Control": "no-store",
      "Content-Security-Policy": "default-src 'self'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "X-Frame-Options": "DENY",
    },
  });
}

function kpi(label: string, value: string | number, note = ""): string {
  return `<div class="kpi"><span class="muted">${escapeHtml(label)}</span><b>${escapeHtml(String(value))}</b>${note ? `<small class="muted">${escapeHtml(note)}</small>` : ""}</div>`;
}
function empty(message: string): string { return `<div class="empty">${escapeHtml(message)}</div>`; }
function pct(value: number): string { return `${(value*100).toFixed(1)}%`; }
function number(value: unknown): string { return Number(value || 0).toLocaleString("en-US"); }
function escapeHtml(value: unknown): string {
  return String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;" })[char] || char);
}
function rankTable(rows: any[], label: string, metric: string, title: string): string {
  if (!rows.length) return empty("No data yet.");
  const max = Math.max(...rows.map((row) => Number(row[metric])));
  return `<table><thead><tr><th>${escapeHtml(label)}</th><th class="num">${escapeHtml(title)}</th><th>Share</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row[label])}</td><td class="num">${number(row[metric])}</td><td><div class="bar"><i style="width:${max?Number(row[metric])/max*100:0}%"></i></div></td></tr>`).join("")}</tbody></table>`;
}
function trafficTable(rows: any[]): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>Type</th><th>Source</th><th class="num">Visits</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.traffic_type)}</td><td>${escapeHtml(row.source)}</td><td class="num">${number(row.visits)}</td></tr>`).join("")}</tbody></table>`;
}
function productTable(rows: any[]): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>Section</th><th>Product</th><th class="num">Views</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.section)}</td><td>${escapeHtml(row.product)}</td><td class="num">${number(row.views)}</td></tr>`).join("")}</tbody></table>`;
}
function webTrendTable(rows: any[]): string {
  const dates = new Map<string,{views:number;engaged:number}>();
  for (const row of rows) {
    const current = dates.get(row.day) || { views:0, engaged:0 };
    if (row.event_type === "page_view") current.views = Number(row.total);
    if (row.event_type === "engaged") current.engaged = Number(row.total);
    dates.set(row.day,current);
  }
  return `<table><thead><tr><th>Date</th><th class="num">Views</th><th class="num">Engaged</th><th class="num">Rate</th></tr></thead><tbody>${[...dates].map(([day,row])=>`<tr><td>${escapeHtml(day)}</td><td class="num">${number(row.views)}</td><td class="num">${number(row.engaged)}</td><td class="num">${row.views?pct(row.engaged/row.views):"—"}</td></tr>`).join("")}</tbody></table>`;
}
function gscTrendTable(rows: any[]): string {
  if (!rows.length) return empty("No Search Console data yet.");
  return `<table><thead><tr><th>Date</th><th class="num">Clicks</th><th class="num">Impressions</th><th class="num">CTR</th><th class="num">Position</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.day)}</td><td class="num">${number(row.clicks)}</td><td class="num">${number(row.impressions)}</td><td class="num">${pct(Number(row.ctr))}</td><td class="num">${Number(row.position).toFixed(1)}</td></tr>`).join("")}</tbody></table>`;
}
function searchTable(rows: any[], label: string): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>${escapeHtml(label)}</th><th class="num">Clicks</th><th class="num">Impr.</th><th class="num">Position</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row[label])}</td><td class="num">${number(row.clicks)}</td><td class="num">${number(row.impressions)}</td><td class="num">${row.position===null?"—":Number(row.position).toFixed(1)}</td></tr>`).join("")}</tbody></table>`;
}

function json(value: unknown, status = 200, headers: Record<string,string> = {}): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...headers },
  });
}
