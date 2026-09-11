import { websiteDetails } from "./website-details";
import { gscSync } from "./gsc-sync";
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
const SEO_CHANGE_DATE = "2026-09-07";
const SEO_POST_START = "2026-09-08";
const SEO_BASELINE_START = "2026-09-02";
const SEO_BASELINE_END = "2026-09-05";
const SEO_CHANGE_PAGES = [
  ["Diagnostic Library", "https://pitmedic.com/diagnostic-library/"],
  ["iRacing guide", "https://pitmedic.com/simulators/iracing/"],
  ["LMU guide", "https://pitmedic.com/simulators/le-mans-ultimate/"],
  ["Missing File Privileges", "https://pitmedic.com/diagnostic-library/iracing-missing-file-privileges/"],
  ["Content File Locked", "https://pitmedic.com/diagnostic-library/iracing-content-file-locked/"],
  ["Loading Error 3", "https://pitmedic.com/diagnostic-library/iracing-loading-error-3/"],
  ["Car Loading Errors", "https://pitmedic.com/diagnostic-library/iracing-car-loading-errors/"],
  ["EasyAntiCheat Error 73", "https://pitmedic.com/diagnostic-library/iracing-eac-error73/"],
  ["MOZA clean recovery", "https://pitmedic.com/diagnostic-library/companion-moza-clean-recovery/"],
] as const;

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

    if (url.pathname === "/v1/gsc-sync" && request.method === "POST") {
      if (!isAuthorized(request)) return json({ error: "unauthorized" }, 401);
      return gscSync(pool, request);
    }

    if (["/v1/website-summary", "/v1/website-details"].includes(url.pathname) && request.method === "GET") {
      if (!isAuthorized(request)) {
        return json({ error: "unauthorized" }, 401, {
          "Cache-Control": "no-store",
          "WWW-Authenticate": 'Basic realm="PitMedic Analytics", charset="UTF-8"',
        });
      }
      return url.pathname === "/v1/website-details" ? websiteDetails(pool, url) : websiteSummary();
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
  const seoValues = SEO_CHANGE_PAGES.map((_, index) => `($${index * 2 + 1}, $${index * 2 + 2})`).join(",");
  const [
    eventTotals,
    webPeriods,
    webTrend,
    topPages,
    trafficSources,
    products,
    navigationPaths,
    gscTrend,
    gscPeriods,
    gscFootprint,
    gscPages,
    gscQueries,
    rankingOpportunities,
    ctrOpportunities,
    seoChangePages,
    gscCountries,
    gscDevices,
    metadata,
  ] = await Promise.all([
    pool.query(`SELECT event_type, SUM(event_count)::bigint AS total FROM web_daily_events WHERE day >= CURRENT_DATE - 29 GROUP BY event_type ORDER BY total DESC`),
    pool.query(`SELECT
      SUM(event_count) FILTER (WHERE day >= CURRENT_DATE - 6 AND event_type='page_view')::bigint AS current_views,
      SUM(event_count) FILTER (WHERE day BETWEEN CURRENT_DATE - 13 AND CURRENT_DATE - 7 AND event_type='page_view')::bigint AS prior_views,
      SUM(event_count) FILTER (WHERE day >= CURRENT_DATE - 6 AND event_type='engaged')::bigint AS current_engaged,
      SUM(event_count) FILTER (WHERE day >= CURRENT_DATE - 6 AND event_type='download')::bigint AS current_downloads,
      SUM(event_count) FILTER (WHERE day >= CURRENT_DATE - 6 AND event_type='page_view' AND traffic_type='search')::bigint AS search_entries
      FROM web_daily_events WHERE day >= CURRENT_DATE - 13`),
    pool.query(`SELECT day::text, event_type, SUM(event_count)::bigint AS total FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type IN ('page_view','engaged') GROUP BY day, event_type ORDER BY day`),
    pool.query(`SELECT path,
      SUM(event_count) FILTER (WHERE event_type='page_view')::bigint AS views,
      SUM(event_count) FILTER (WHERE event_type='engaged')::bigint AS engaged,
      SUM(event_count) FILTER (WHERE event_type='download')::bigint AS downloads
      FROM web_daily_events WHERE day >= CURRENT_DATE - 29 GROUP BY path
      HAVING SUM(event_count) FILTER (WHERE event_type='page_view') > 0
      ORDER BY views DESC, path LIMIT 20`),
    pool.query(`SELECT traffic_type, source, SUM(event_count)::bigint AS visits FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type='page_view' GROUP BY traffic_type, source ORDER BY visits DESC, source LIMIT 20`),
    pool.query(`SELECT section, product,
      SUM(event_count) FILTER (WHERE event_type='page_view')::bigint AS views,
      SUM(event_count) FILTER (WHERE event_type='engaged')::bigint AS engaged,
      SUM(event_count) FILTER (WHERE event_type='download')::bigint AS downloads
      FROM web_daily_events WHERE day >= CURRENT_DATE - 29 GROUP BY section, product
      HAVING SUM(event_count) FILTER (WHERE event_type='page_view') > 0
      ORDER BY views DESC, product LIMIT 20`),
    pool.query(`SELECT path, target, SUM(event_count)::bigint AS clicks
      FROM web_daily_events WHERE day >= CURRENT_DATE - 29 AND event_type='internal_navigation'
      GROUP BY path, target ORDER BY clicks DESC, path, target LIMIT 20`),
    pool.query(`WITH bounds AS (
        SELECT MIN(day) AS start_day,
          COALESCE((SELECT value::date FROM analytics_metadata WHERE key='gsc_settled_through'),MAX(day)) AS end_day
        FROM gsc_daily_metrics WHERE dimension_type='site'
      ), days AS (
        SELECT generate_series(start_day,end_day,INTERVAL '1 day')::date AS day FROM bounds
      )
      SELECT days.day::text, COALESCE(m.clicks,0)::bigint AS clicks, COALESCE(m.impressions,0)::bigint AS impressions,
        COALESCE(m.ctr,0) AS ctr, m.position
      FROM days LEFT JOIN gsc_daily_metrics m ON m.dimension_type='site' AND m.day=days.day ORDER BY days.day`),
    pool.query(`WITH bounds AS (SELECT MAX(day) AS end_day FROM gsc_daily_metrics WHERE dimension_type='site')
      SELECT
        SUM(clicks) FILTER (WHERE day BETWEEN end_day - 6 AND end_day)::bigint AS current_clicks,
        SUM(impressions) FILTER (WHERE day BETWEEN end_day - 6 AND end_day)::bigint AS current_impressions,
        SUM(position*impressions) FILTER (WHERE day BETWEEN end_day - 6 AND end_day) / NULLIF(SUM(impressions) FILTER (WHERE day BETWEEN end_day - 6 AND end_day),0) AS current_position,
        SUM(clicks) FILTER (WHERE day BETWEEN end_day - 13 AND end_day - 7)::bigint AS prior_clicks,
        SUM(impressions) FILTER (WHERE day BETWEEN end_day - 13 AND end_day - 7)::bigint AS prior_impressions,
        SUM(position*impressions) FILTER (WHERE day BETWEEN end_day - 13 AND end_day - 7) / NULLIF(SUM(impressions) FILTER (WHERE day BETWEEN end_day - 13 AND end_day - 7),0) AS prior_position,
        end_day::text
      FROM gsc_daily_metrics, bounds WHERE dimension_type='site' GROUP BY end_day`),
    pool.query(`WITH bounds AS (SELECT MAX(day) AS end_day FROM gsc_daily_metrics WHERE dimension_type='site')
      SELECT dimension_type, COUNT(DISTINCT dimension_value)::bigint AS total
      FROM gsc_daily_metrics, bounds
      WHERE day BETWEEN end_day - 27 AND end_day AND dimension_type IN ('page','query')
      GROUP BY dimension_type`),
    pool.query(`SELECT dimension_value AS page, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, CASE WHEN SUM(impressions)>0 THEN SUM(clicks)::float/SUM(impressions) ELSE 0 END AS ctr, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='page' GROUP BY dimension_value ORDER BY impressions DESC LIMIT 20`),
    pool.query(`SELECT dimension_value AS query, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, CASE WHEN SUM(impressions)>0 THEN SUM(clicks)::float/SUM(impressions) ELSE 0 END AS ctr, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='query' GROUP BY dimension_value ORDER BY impressions DESC LIMIT 20`),
    pool.query(`WITH bounds AS (SELECT MAX(day) AS end_day FROM gsc_daily_metrics WHERE dimension_type='site')
      SELECT dimension_value AS query, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions,
      SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position
      FROM gsc_daily_metrics, bounds WHERE dimension_type='query' AND day BETWEEN end_day - 27 AND end_day
      GROUP BY dimension_value
      HAVING SUM(position*impressions)/NULLIF(SUM(impressions),0) > 3
        AND SUM(position*impressions)/NULLIF(SUM(impressions),0) <= 20
      ORDER BY impressions DESC, position LIMIT 20`),
    pool.query(`WITH bounds AS (SELECT MAX(day) AS end_day FROM gsc_daily_metrics WHERE dimension_type='site')
      SELECT dimension_value AS page, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions,
      SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position
      FROM gsc_daily_metrics, bounds WHERE dimension_type='page' AND day BETWEEN end_day - 27 AND end_day
      GROUP BY dimension_value
      HAVING SUM(clicks)=0 AND SUM(impressions)>=2
        AND SUM(position*impressions)/NULLIF(SUM(impressions),0) <= 10
      ORDER BY impressions DESC, position LIMIT 20`),
    pool.query(`WITH pages(label,url) AS (VALUES ${seoValues})
      SELECT pages.label, pages.url,
        COALESCE(SUM(m.clicks) FILTER (WHERE m.day BETWEEN DATE '${SEO_BASELINE_START}' AND DATE '${SEO_BASELINE_END}'),0)::bigint AS baseline_clicks,
        COALESCE(SUM(m.impressions) FILTER (WHERE m.day BETWEEN DATE '${SEO_BASELINE_START}' AND DATE '${SEO_BASELINE_END}'),0)::bigint AS baseline_impressions,
        SUM(m.position*m.impressions) FILTER (WHERE m.day BETWEEN DATE '${SEO_BASELINE_START}' AND DATE '${SEO_BASELINE_END}') /
          NULLIF(SUM(m.impressions) FILTER (WHERE m.day BETWEEN DATE '${SEO_BASELINE_START}' AND DATE '${SEO_BASELINE_END}'),0) AS baseline_position,
        COALESCE(SUM(m.clicks) FILTER (WHERE m.day >= DATE '${SEO_POST_START}'),0)::bigint AS post_clicks,
        COALESCE(SUM(m.impressions) FILTER (WHERE m.day >= DATE '${SEO_POST_START}'),0)::bigint AS post_impressions,
        SUM(m.position*m.impressions) FILTER (WHERE m.day >= DATE '${SEO_POST_START}') /
          NULLIF(SUM(m.impressions) FILTER (WHERE m.day >= DATE '${SEO_POST_START}'),0) AS post_position
      FROM pages LEFT JOIN gsc_daily_metrics m ON m.dimension_type='page' AND m.dimension_value=pages.url
      GROUP BY pages.label, pages.url ORDER BY baseline_impressions DESC, pages.label`, SEO_CHANGE_PAGES.flat()),
    pool.query(`SELECT dimension_value AS country, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='country' GROUP BY dimension_value ORDER BY impressions DESC`),
    pool.query(`SELECT dimension_value AS device, SUM(clicks)::bigint AS clicks, SUM(impressions)::bigint AS impressions, SUM(position*impressions)/NULLIF(SUM(impressions),0) AS position FROM gsc_daily_metrics WHERE dimension_type='device' GROUP BY dimension_value ORDER BY impressions DESC`),
    pool.query(`SELECT key, value FROM analytics_metadata WHERE key IN ('gsc_settled_through','gsc_last_sync','privacy_model') ORDER BY key`),
  ]);

  const events = new Map(eventTotals.rows.map((row) => [String(row.event_type), Number(row.total)]));
  const pageViews = events.get("page_view") || 0;
  const downloads = events.get("download") || 0;
  const webPeriod = webPeriods.rows[0] || {};
  const currentViews = Number(webPeriod.current_views || 0);
  const priorViews = Number(webPeriod.prior_views || 0);
  const currentEngaged = Number(webPeriod.current_engaged || 0);
  const currentDownloads = Number(webPeriod.current_downloads || 0);
  const searchEntries = Number(webPeriod.search_entries || 0);
  const gsc = gscTrend.rows;
  const gscClicks = gsc.reduce((sum, row) => sum + Number(row.clicks), 0);
  const gscImpressions = gsc.reduce((sum, row) => sum + Number(row.impressions), 0);
  const weightedPosition = gscImpressions
    ? gsc.reduce((sum, row) => sum + Number(row.position) * Number(row.impressions), 0) / gscImpressions
    : 0;
  const gscPeriod = gscPeriods.rows[0] || {};
  const currentSearchImpressions = Number(gscPeriod.current_impressions || 0);
  const priorSearchImpressions = Number(gscPeriod.prior_impressions || 0);
  const currentSearchPosition = nullableNumber(gscPeriod.current_position);
  const priorSearchPosition = nullableNumber(gscPeriod.prior_position);
  const footprint = new Map(gscFootprint.rows.map((row) => [String(row.dimension_type), Number(row.total)]));
  const seoRows = seoChangePages.rows;
  const seoBaselineImpressions = seoRows.reduce((sum, row) => sum + Number(row.baseline_impressions), 0);
  const seoBaselineClicks = seoRows.reduce((sum, row) => sum + Number(row.baseline_clicks), 0);
  const seoPostImpressions = seoRows.reduce((sum, row) => sum + Number(row.post_impressions), 0);
  const seoPostClicks = seoRows.reduce((sum, row) => sum + Number(row.post_clicks), 0);
  const seoBaselinePosition = weightedMetric(seoRows, "baseline_position", "baseline_impressions");
  const seoPostPosition = weightedMetric(seoRows, "post_position", "post_impressions");
  const meta = Object.fromEntries(metadata.rows.map((row) => [String(row.key), String(row.value)]));
  const seoWaiting = !meta.gsc_settled_through || meta.gsc_settled_through < SEO_POST_START;
  const seoPostDays = seoWaiting ? 0 : inclusiveDays(SEO_POST_START,meta.gsc_settled_through);
  const lastSearchActivity = [...gsc].reverse().find((row) => Number(row.impressions)>0)?.day || "—";

  const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PitMedic Analytics</title>
<style>
:root{color-scheme:dark;--bg:#07111d;--panel:#0d1b2a;--line:#20384d;--text:#e8f1f8;--muted:#91a9ba;--cyan:#2dd4bf;--blue:#60a5fa;--amber:#fbbf24;--green:#4ade80;--red:#fb7185}
*{box-sizing:border-box}body{margin:0;background:radial-gradient(circle at top,#10283e 0,#07111d 42%);color:var(--text);font:15px/1.45 Inter,system-ui,sans-serif}
main{max-width:1280px;margin:auto;padding:32px 22px 60px}header{display:flex;align-items:end;justify-content:space-between;gap:20px;margin-bottom:24px}
h1{margin:0;font-size:30px}h2{font-size:18px;margin:0 0 14px}.sub,.muted{color:var(--muted)}
.tabs{display:flex;gap:8px;margin:20px 0}.tab{border:1px solid var(--line);background:#0b1825;color:var(--text);border-radius:999px;padding:8px 14px;cursor:pointer}.tab.active{background:var(--cyan);color:#04120f;border-color:var(--cyan);font-weight:700}
.view{display:none}.view.active{display:block}.grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:14px}.kpi,.panel{background:color-mix(in srgb,var(--panel) 94%,transparent);border:1px solid var(--line);border-radius:14px;box-shadow:0 16px 40px #0003}.kpi{padding:18px}.kpi b{display:block;font-size:28px;margin-top:5px}.panel{padding:18px;margin-top:14px;overflow:auto}.two{display:grid;grid-template-columns:1fr 1fr;gap:14px}
table{border-collapse:collapse;width:100%;min-width:560px}th,td{text-align:left;border-bottom:1px solid var(--line);padding:9px 8px}th{color:var(--muted);font-size:12px;text-transform:uppercase;letter-spacing:.06em}td.num,th.num{text-align:right}
.bar{height:8px;background:#122638;border-radius:5px;overflow:hidden;min-width:100px}.bar i{display:block;height:100%;background:linear-gradient(90deg,var(--blue),var(--cyan));border-radius:5px}.empty{padding:34px;text-align:center;color:var(--muted)}
.callout{border-left:4px solid var(--cyan);padding:12px 14px;background:#0a2230;border-radius:8px;margin-bottom:14px}.good{color:var(--green)}.bad{color:var(--red)}.neutral{color:var(--muted)}.compact{margin-top:8px}.compact .kpi{padding:14px}.compact .kpi b{font-size:22px}
footer{color:var(--muted);margin-top:22px;font-size:13px}@media(max-width:850px){.grid{grid-template-columns:repeat(2,1fr)}.two{grid-template-columns:1fr}header{align-items:start;flex-direction:column}}@media(max-width:460px){.grid{grid-template-columns:1fr}}
</style></head><body><main>
<header><div><h1>PitMedic Analytics</h1><div class="sub">Privacy-first website and Google Search performance</div></div><div class="muted">Last 30 days · refreshed on load</div></header>
<div class="tabs"><button class="tab active" data-view="web">Website</button><button class="tab" data-view="search">Google Search</button></div>
<section id="web" class="view active">
<div class="grid">
${kpi("Page views · 30d", pageViews, `${number(currentViews)} last 7d · ${growthLabel(currentViews,priorViews)}`)}${kpi("Engagement · 7d", currentViews?pct(currentEngaged/currentViews):"—", `${number(currentEngaged)} engaged views`)}${kpi("Download rate · 7d",currentViews?pct(currentDownloads/currentViews):"—",`${number(downloads)} downloads in 30d`)}${kpi("Search entries · 7d",searchEntries,currentViews?`${pct(searchEntries/currentViews)} of views`:"No views yet")}
</div>
<div class="panel"><h2>Daily activity</h2>${webTrend.rows.length ? webTrendTable(webTrend.rows) : empty("Collection starts when the updated website tracker is published.")}</div>
<div class="two">
<div class="panel"><h2>Top pages</h2>${pagePerformanceTable(topPages.rows)}</div>
<div class="panel"><h2>Traffic sources</h2>${trafficTable(trafficSources.rows)}</div>
</div>
<div class="two">
<div class="panel"><h2>Content and products</h2>${productTable(products.rows)}</div>
<div class="panel"><h2>Internal navigation paths</h2>${navigationTable(navigationPaths.rows)}</div>
</div>
</section>
<section id="search" class="view">
<div class="grid">
${kpi("Clicks",gscClicks,`${number(gscPeriod.current_clicks)} in latest 7d`)}${kpi("Impressions",gscImpressions,`${number(currentSearchImpressions)} latest 7d · ${growthLabel(currentSearchImpressions,priorSearchImpressions)}`)}${kpi("Search CTR",gscImpressions?pct(gscClicks/gscImpressions):"0%")}${kpi("Avg. position",weightedPosition?weightedPosition.toFixed(1):"—",positionChangeLabel(currentSearchPosition,priorSearchPosition))}
</div>
<div class="grid compact">
${kpi("Visible pages · 28d",footprint.get("page") || 0)}${kpi("Visible queries · 28d",footprint.get("query") || 0)}${kpi("Ranking opportunities",rankingOpportunities.rows.length,"Queries in positions 4–20")}${kpi("CTR opportunities",ctrOpportunities.rows.length,"Top-10 pages without a click")}
</div>
<div class="panel"><h2>SEO change impact</h2>
<div class="callout"><strong>Priority content expansion · ${SEO_CHANGE_DATE}</strong><br><span class="muted">Baseline ${SEO_BASELINE_START}–${SEO_BASELINE_END}; transition day excluded; post-change measurement begins ${SEO_POST_START}. ${seoWaiting?"Waiting for Google to settle the first post-change day.":`${seoPostDays} post-change day${seoPostDays===1?"":"s"} available.`}</span></div>
<div class="grid compact">${kpi("Baseline impressions",seoBaselineImpressions,`${(seoBaselineImpressions/4).toFixed(1)} per day`)}${kpi("Post impressions",seoWaiting?"Pending":seoPostImpressions,seoWaiting?"No settled post-change data yet":`${seoPostDays? (seoPostImpressions/seoPostDays).toFixed(1):"0.0"} per day`)}${kpi("Baseline CTR",seoBaselineImpressions?pct(seoBaselineClicks/seoBaselineImpressions):"—")}${kpi("Position change",seoWaiting?"Pending":positionChangeValue(seoPostPosition,seoBaselinePosition),seoWaiting?"Lower is better":positionChangeLabel(seoPostPosition,seoBaselinePosition))}</div>
${seoChangeTable(seoRows,seoWaiting)}</div>
<div class="panel"><h2>Daily search performance</h2>${gscTrendTable(gsc)}</div>
<div class="two">
<div class="panel"><h2>Search landing pages</h2>${searchTable(gscPages.rows,"page")}</div>
<div class="panel"><h2>Queries</h2>${searchTable(gscQueries.rows,"query")}</div>
</div>
<div class="two">
<div class="panel"><h2>Striking-distance queries</h2><div class="muted">Queries ranking in positions 4–20, ordered by impressions.</div>${searchTable(rankingOpportunities.rows,"query")}</div>
<div class="panel"><h2>CTR opportunities</h2><div class="muted">Pages averaging position 10 or better with at least two impressions and no clicks.</div>${searchTable(ctrOpportunities.rows,"page")}</div>
</div>
<div class="two">
<div class="panel"><h2>Countries</h2>${searchTable(gscCountries.rows,"country")}</div>
<div class="panel"><h2>Devices</h2>${searchTable(gscDevices.rows,"device")}</div>
</div>
</section>
<footer>Google data current through ${escapeHtml(meta.gsc_settled_through || "—")} · Last search activity ${escapeHtml(lastSearchActivity)} · Daily aggregates only · No cookies, IP addresses, visitor IDs, full referrer URLs, search terms from website visits, or raw user-agent strings are stored.</footer>
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

async function websiteSummary(): Promise<Response> {
  const [totals, daily, topPages, searchLandings, sources, products, countries, devices, journeys] =
    await Promise.all([
      pool.query(`
        SELECT
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view' AND day=CURRENT_DATE), 0)::bigint AS today_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view' AND day>=CURRENT_DATE-6), 0)::bigint AS seven_day_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view'), 0)::bigint AS thirty_day_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='download'), 0)::bigint AS downloads,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='engaged'), 0)::bigint AS engaged,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view' AND traffic_type='search'), 0)::bigint AS organic
        FROM web_daily_events WHERE day>=CURRENT_DATE-29`),
      pool.query(`
        SELECT day::text,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view'), 0)::bigint AS page_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='download'), 0)::bigint AS downloads
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 GROUP BY day ORDER BY day`),
      pool.query(`
        SELECT path,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='page_view'), 0)::bigint AS page_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='engaged'), 0)::bigint AS engaged_views,
          COALESCE(SUM(event_count) FILTER (WHERE event_type='download'), 0)::bigint AS downloads
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 GROUP BY path
        HAVING SUM(event_count) FILTER (WHERE event_type='page_view') > 0
        ORDER BY page_views DESC, path LIMIT 15`),
      pool.query(`SELECT path AS label, SUM(event_count)::bigint AS total
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='page_view' AND traffic_type='search'
        GROUP BY path ORDER BY total DESC, path LIMIT 10`),
      pool.query(`SELECT source AS label, SUM(event_count)::bigint AS total, traffic_type AS secondary
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='page_view' AND traffic_type!='internal'
        GROUP BY source, traffic_type ORDER BY total DESC, source LIMIT 12`),
      pool.query(`SELECT product AS label, SUM(event_count)::bigint AS total
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='page_view'
          AND section IN ('Simulator guide','Simulator diagnostic','Companion diagnostic')
        GROUP BY product ORDER BY total DESC, product LIMIT 14`),
      pool.query(`SELECT country AS label, SUM(event_count)::bigint AS total
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='page_view'
        GROUP BY country ORDER BY total DESC, country LIMIT 12`),
      pool.query(`SELECT device_type AS label, SUM(event_count)::bigint AS total
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='page_view'
        GROUP BY device_type ORDER BY total DESC, device_type`),
      pool.query(`SELECT path AS source, target, SUM(event_count)::bigint AS total
        FROM web_daily_events WHERE day>=CURRENT_DATE-29 AND event_type='internal_navigation'
        GROUP BY path, target ORDER BY total DESC, path, target LIMIT 12`),
    ]);

  const total = totals.rows[0] ?? {};
  const pageViews = Number(total.thirty_day_views || 0);
  const engaged = Number(total.engaged || 0);
  const now = new Date();
  const byDay = new Map(
    daily.rows.map((row) => [
      String(row.day),
      { pageViews: Number(row.page_views || 0), downloads: Number(row.downloads || 0) },
    ]),
  );

  return json({
    protocol: 1,
    generatedAt: now.toISOString(),
    data: {
      todayPageViews: Number(total.today_views || 0),
      sevenDayPageViews: Number(total.seven_day_views || 0),
      thirtyDayPageViews: pageViews,
      downloads: Number(total.downloads || 0),
      engagementRate: pageViews ? Math.round((engaged / pageViews) * 1000) / 10 : 0,
      organicEntries: Number(total.organic || 0),
      daily: Array.from({ length: 30 }, (_, index) => {
        const day = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - 29 + index))
          .toISOString().slice(0, 10);
        return { day, ...(byDay.get(day) ?? { pageViews: 0, downloads: 0 }) };
      }),
      topPages: topPages.rows.map((row) => ({
        path: String(row.path),
        pageViews: Number(row.page_views || 0),
        engagedViews: Number(row.engaged_views || 0),
        downloads: Number(row.downloads || 0),
      })),
      searchLandings: summaryDimensions(searchLandings.rows),
      sources: summaryDimensions(sources.rows),
      products: summaryDimensions(products.rows),
      countries: summaryDimensions(countries.rows),
      devices: summaryDimensions(devices.rows),
      journeys: journeys.rows.map((row) => ({
        source: String(row.source),
        target: String(row.target),
        count: Number(row.total || 0),
      })),
    },
  }, 200, { "Cache-Control": "no-store" });
}

function summaryDimensions(rows: any[]): Array<{ label: string; count: number; secondary?: string }> {
  return rows.map((row) => ({
    label: String(row.label),
    count: Number(row.total || 0),
    ...(row.secondary ? { secondary: String(row.secondary) } : {}),
  }));
}

function kpi(label: string, value: string | number, note = ""): string {
  return `<div class="kpi"><span class="muted">${escapeHtml(label)}</span><b>${escapeHtml(String(value))}</b>${note ? `<small class="muted">${escapeHtml(note)}</small>` : ""}</div>`;
}
function empty(message: string): string { return `<div class="empty">${escapeHtml(message)}</div>`; }
function pct(value: number): string { return `${(value*100).toFixed(1)}%`; }
function number(value: unknown): string { return Number(value || 0).toLocaleString("en-US"); }
function nullableNumber(value: unknown): number | null {
  return value === null || value === undefined || value === "" ? null : Number(value);
}
function growthLabel(current: number, prior: number): string {
  if (!prior) return current ? "new activity" : "no prior activity";
  const change = (current-prior)/prior;
  return `${change>=0?"+":""}${pct(change)} vs prior 7d`;
}
function positionChangeValue(current: number | null, prior: number | null): string {
  if (current === null || prior === null) return "—";
  const improvement = prior-current;
  return `${improvement>=0?"+":""}${improvement.toFixed(1)}`;
}
function positionChangeLabel(current: number | null, prior: number | null): string {
  if (current === null || prior === null) return "Not enough comparison data";
  const improvement = prior-current;
  if (Math.abs(improvement)<0.05) return "No material movement";
  return `${Math.abs(improvement).toFixed(1)} position${Math.abs(improvement)>=1.5?"s":""} ${improvement>0?"better":"worse"} vs prior period`;
}
function weightedMetric(rows: any[], metric: string, weight: string): number | null {
  const usable = rows.filter((row) => nullableNumber(row[metric]) !== null && Number(row[weight])>0);
  const totalWeight = usable.reduce((sum,row)=>sum+Number(row[weight]),0);
  return totalWeight ? usable.reduce((sum,row)=>sum+Number(row[metric])*Number(row[weight]),0)/totalWeight : null;
}
function inclusiveDays(start: string, end: string): number {
  return Math.max(0,Math.floor((Date.parse(`${end}T00:00:00Z`)-Date.parse(`${start}T00:00:00Z`))/86_400_000)+1);
}
function escapeHtml(value: unknown): string {
  return String(value ?? "").replace(/[&<>"']/g, (char) => ({ "&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;" })[char] || char);
}
function pagePerformanceTable(rows: any[]): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>Page</th><th class="num">Views</th><th class="num">Engaged</th><th class="num">Rate</th><th class="num">Downloads</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.path)}</td><td class="num">${number(row.views)}</td><td class="num">${number(row.engaged)}</td><td class="num">${Number(row.views)?pct(Number(row.engaged)/Number(row.views)):"—"}</td><td class="num">${number(row.downloads)}</td></tr>`).join("")}</tbody></table>`;
}
function trafficTable(rows: any[]): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>Type</th><th>Source</th><th class="num">Visits</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.traffic_type)}</td><td>${escapeHtml(row.source)}</td><td class="num">${number(row.visits)}</td></tr>`).join("")}</tbody></table>`;
}
function productTable(rows: any[]): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>Section</th><th>Product</th><th class="num">Views</th><th class="num">Engagement</th><th class="num">Downloads</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.section)}</td><td>${escapeHtml(row.product)}</td><td class="num">${number(row.views)}</td><td class="num">${Number(row.views)?pct(Number(row.engaged)/Number(row.views)):"—"}</td><td class="num">${number(row.downloads)}</td></tr>`).join("")}</tbody></table>`;
}
function navigationTable(rows: any[]): string {
  if (!rows.length) return empty("No internal navigation clicks yet.");
  return `<table><thead><tr><th>From</th><th>To</th><th class="num">Clicks</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.path)}</td><td>${escapeHtml(row.target)}</td><td class="num">${number(row.clicks)}</td></tr>`).join("")}</tbody></table>`;
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
  return `<table><thead><tr><th>Date</th><th class="num">Clicks</th><th class="num">Impressions</th><th class="num">CTR</th><th class="num">Position</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row.day)}</td><td class="num">${number(row.clicks)}</td><td class="num">${number(row.impressions)}</td><td class="num">${pct(Number(row.ctr))}</td><td class="num">${!Number(row.impressions)||row.position===null?"—":Number(row.position).toFixed(1)}</td></tr>`).join("")}</tbody></table>`;
}
function searchTable(rows: any[], label: string): string {
  if (!rows.length) return empty("No data yet.");
  return `<table><thead><tr><th>${escapeHtml(label)}</th><th class="num">Clicks</th><th class="num">Impr.</th><th class="num">Position</th></tr></thead><tbody>${rows.map((row)=>`<tr><td>${escapeHtml(row[label])}</td><td class="num">${number(row.clicks)}</td><td class="num">${number(row.impressions)}</td><td class="num">${row.position===null?"—":Number(row.position).toFixed(1)}</td></tr>`).join("")}</tbody></table>`;
}
function seoChangeTable(rows: any[], waiting: boolean): string {
  if (!rows.length) return empty("No experiment pages configured.");
  return `<table><thead><tr><th>Changed page</th><th class="num">Baseline impr.</th><th class="num">Baseline CTR</th><th class="num">Baseline pos.</th><th class="num">Post impr.</th><th class="num">Post CTR</th><th class="num">Post pos.</th><th class="num">Rank change</th></tr></thead><tbody>${rows.map((row)=>{
    const baselineImpressions=Number(row.baseline_impressions); const postImpressions=Number(row.post_impressions);
    const baselinePosition=nullableNumber(row.baseline_position); const postPosition=nullableNumber(row.post_position);
    const change=baselinePosition!==null&&postPosition!==null?baselinePosition-postPosition:null;
    return `<tr><td><a href="${escapeHtml(row.url)}" rel="noreferrer">${escapeHtml(row.label)}</a></td><td class="num">${number(baselineImpressions)}</td><td class="num">${baselineImpressions?pct(Number(row.baseline_clicks)/baselineImpressions):"—"}</td><td class="num">${baselinePosition===null?"—":baselinePosition.toFixed(1)}</td><td class="num">${waiting?"—":number(postImpressions)}</td><td class="num">${waiting||!postImpressions?"—":pct(Number(row.post_clicks)/postImpressions)}</td><td class="num">${waiting||postPosition===null?"—":postPosition.toFixed(1)}</td><td class="num ${change===null?"neutral":change>=0?"good":"bad"}">${waiting||change===null?"—":`${change>=0?"+":""}${change.toFixed(1)}`}</td></tr>`;
  }).join("")}</tbody></table>`;
}

function json(value: unknown, status = 200, headers: Record<string,string> = {}): Response {
  return new Response(JSON.stringify(value), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", ...headers },
  });
}
