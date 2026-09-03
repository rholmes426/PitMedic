const EVENT_TYPES = new Set([
  "page_view",
  "engaged",
  "download",
  "internal_navigation",
] as const);

const ALLOWED_FIELDS = new Set([
  "protocol",
  "event",
  "path",
  "target",
  "referrer",
]);

const WEBSITE_ORIGINS = new Set([
  "https://pitmedic.com",
  "https://www.pitmedic.com",
]);

export type WebEventType =
  | "page_view"
  | "engaged"
  | "download"
  | "internal_navigation";

export type WebEventPayload = {
  protocol: 1;
  event: WebEventType;
  path: string;
  target: string;
  referrer: string;
};

export type WebValidationResult =
  | { ok: true; value: WebEventPayload }
  | { ok: false; reason: string };

export function allowedWebsiteOrigin(request: Request): string | null {
  const origin = request.headers.get("Origin");
  return origin !== null && WEBSITE_ORIGINS.has(origin) ? origin : null;
}

export function websiteCorsHeaders(origin: string): Record<string, string> {
  return {
    "Access-Control-Allow-Origin": origin,
    "Access-Control-Allow-Methods": "POST, OPTIONS",
    "Access-Control-Allow-Headers": "Content-Type",
    "Access-Control-Max-Age": "86400",
    Vary: "Origin",
  };
}

export function validateWebEvent(candidate: unknown): WebValidationResult {
  if (!isObject(candidate)) {
    return { ok: false, reason: "payload_must_be_an_object" };
  }
  const fields = Object.keys(candidate);
  if (fields.length !== ALLOWED_FIELDS.size) {
    return { ok: false, reason: "missing_or_extra_field" };
  }
  if (fields.some((field) => !ALLOWED_FIELDS.has(field))) {
    return { ok: false, reason: "unexpected_field" };
  }
  if (candidate.protocol !== 1) {
    return { ok: false, reason: "unsupported_protocol" };
  }
  if (
    typeof candidate.event !== "string" ||
    !EVENT_TYPES.has(candidate.event as WebEventType)
  ) {
    return { ok: false, reason: "invalid_event" };
  }
  if (!validPath(candidate.path)) {
    return { ok: false, reason: "invalid_path" };
  }
  if (typeof candidate.target !== "string" || candidate.target.length > 180) {
    return { ok: false, reason: "invalid_target" };
  }
  if (candidate.event === "internal_navigation" && !validPath(candidate.target)) {
    return { ok: false, reason: "invalid_navigation_target" };
  }
  if (
    candidate.event === "download" &&
    !/^release:v\d+\.\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$/i.test(candidate.target)
  ) {
    return { ok: false, reason: "invalid_download_target" };
  }
  if (
    candidate.event !== "download" &&
    candidate.event !== "internal_navigation" &&
    candidate.target !== ""
  ) {
    return { ok: false, reason: "unexpected_target" };
  }
  if (
    typeof candidate.referrer !== "string" ||
    candidate.referrer.length > 120 ||
    (candidate.referrer !== "" &&
      !/^(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)*[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$/i.test(
        candidate.referrer,
      ))
  ) {
    return { ok: false, reason: "invalid_referrer" };
  }

  return { ok: true, value: candidate as WebEventPayload };
}

export async function recordWebEvent(
  db: D1Database,
  payload: WebEventPayload,
  request: Request,
  now = new Date(),
): Promise<void> {
  const day = now.toISOString().slice(0, 10);
  const path = normalizePath(payload.path);
  const target = payload.target.startsWith("/")
    ? normalizePath(payload.target)
    : payload.target.toLowerCase();
  const content = classifyContent(path);
  const traffic = classifyTraffic(payload.referrer);
  const country = normalizeCountry(request.cf?.country);
  const device = classifyDevice(request.headers.get("User-Agent") ?? "");

  await db
    .prepare(
      `INSERT INTO web_daily_events
        (day, event_type, path, target, section, product, traffic_type, source, country, device_type, event_count)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(day, event_type, path, target, section, product, traffic_type, source, country, device_type)
       DO UPDATE SET event_count = event_count + 1`,
    )
    .bind(
      day,
      payload.event,
      path,
      target,
      content.section,
      content.product,
      traffic.type,
      traffic.source,
      country,
      device,
    )
    .run();
}

export async function pruneWebAnalytics(
  db: D1Database,
  now = new Date(),
): Promise<void> {
  const cutoff = new Date(now);
  cutoff.setUTCDate(cutoff.getUTCDate() - 730);
  await db
    .prepare("DELETE FROM web_daily_events WHERE day < ?")
    .bind(cutoff.toISOString().slice(0, 10))
    .run();
}

function validPath(value: unknown): value is string {
  return (
    typeof value === "string" &&
    value.length >= 1 &&
    value.length <= 180 &&
    value.startsWith("/") &&
    !/[?#\\]/.test(value) &&
    /^\/[a-z0-9/_\-.]*$/i.test(value)
  );
}

function normalizePath(value: string): string {
  const normalized = value.replace(/\/{2,}/g, "/").toLowerCase();
  return normalized === "/" || normalized.endsWith("/")
    ? normalized
    : `${normalized}/`;
}

function classifyContent(path: string): { section: string; product: string } {
  if (path === "/") return { section: "Home", product: "PitMedic" };

  const simulator = path.match(/^\/simulators\/([^/]+)\//)?.[1];
  if (simulator) {
    return { section: "Simulator guide", product: productName(simulator) };
  }

  const diagnostic = path.match(/^\/diagnostic-library\/([^/]+)\//)?.[1];
  if (diagnostic) {
    const companion = diagnostic.match(
      /^companion-(moza|simucube|fanatec|logitech-ghub|simagic|asetek|vrs)-/,
    )?.[1];
    const prefix = companion ?? diagnostic.split("-", 1)[0];
    return {
      section: diagnostic.startsWith("companion-")
        ? "Companion diagnostic"
        : "Simulator diagnostic",
      product: productName(prefix),
    };
  }

  if (path === "/diagnostic-library/") {
    return { section: "Diagnostic Library", product: "All software" };
  }
  return { section: "Other", product: "PitMedic" };
}

function productName(slug: string): string {
  return (
    {
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
    }[slug] ?? slug.replace(/-/g, " ")
  );
}

function classifyTraffic(referrer: string): {
  type: "direct" | "internal" | "search" | "social" | "referral";
  source: string;
} {
  const host = referrer.toLowerCase().replace(/^www\./, "");
  if (!host) return { type: "direct", source: "Direct" };
  if (host === "pitmedic.com") return { type: "internal", source: "PitMedic" };

  const search = [
    ["google.", "Google"],
    ["bing.com", "Bing"],
    ["duckduckgo.com", "DuckDuckGo"],
    ["search.yahoo.", "Yahoo"],
    ["search.brave.com", "Brave Search"],
  ] as const;
  const searchMatch = search.find(([needle]) => host.includes(needle));
  if (searchMatch) return { type: "search", source: searchMatch[1] };

  const social = [
    ["reddit.com", "Reddit"],
    ["youtube.com", "YouTube"],
    ["youtu.be", "YouTube"],
    ["facebook.com", "Facebook"],
    ["instagram.com", "Instagram"],
    ["x.com", "X"],
    ["twitter.com", "X"],
    ["discord.com", "Discord"],
    ["discord.gg", "Discord"],
  ] as const;
  const socialMatch = social.find(([needle]) => host === needle || host.endsWith(`.${needle}`));
  if (socialMatch) return { type: "social", source: socialMatch[1] };
  return { type: "referral", source: host };
}

function normalizeCountry(value: unknown): string {
  return typeof value === "string" && /^[A-Z]{2}$/.test(value)
    ? value
    : "XX";
}

function classifyDevice(userAgent: string):
  | "desktop"
  | "mobile"
  | "tablet"
  | "unknown" {
  if (!userAgent) return "unknown";
  if (/ipad|tablet|kindle|silk/i.test(userAgent)) return "tablet";
  if (/mobile|iphone|ipod|android/i.test(userAgent)) return "mobile";
  if (/windows|macintosh|linux|cros/i.test(userAgent)) return "desktop";
  return "unknown";
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
