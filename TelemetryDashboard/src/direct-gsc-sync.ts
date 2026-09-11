const SCOPE = "https://www.googleapis.com/auth/webmasters.readonly";
const TOKEN_URL = "https://oauth2.googleapis.com/token";
const SYNC_START = "2026-09-02";
const BASELINE_START = "2026-09-02";
const BASELINE_END = "2026-09-05";
const POST_START = "2026-09-08";
const PAGE_SIZE = 25000;

export const SEO_COHORT_URLS = [
  "https://pitmedic.com/diagnostic-library/",
  "https://pitmedic.com/diagnostic-library/iracing-missing-file-privileges/",
  "https://pitmedic.com/diagnostic-library/iracing-content-file-locked/",
  "https://pitmedic.com/diagnostic-library/iracing-loading-error-3/",
  "https://pitmedic.com/diagnostic-library/iracing-car-loading-errors/",
  "https://pitmedic.com/diagnostic-library/iracing-eac-error73/",
  "https://pitmedic.com/diagnostic-library/companion-moza-clean-recovery/",
  "https://pitmedic.com/simulators/iracing/",
  "https://pitmedic.com/simulators/le-mans-ultimate/"
] as const;

type DirectGscEnv = {
  GOOGLE_SERVICE_ACCOUNT_JSON?: string;
  SEARCH_CONSOLE_PROPERTY?: string;
  NEON_ANALYTICS_URL: string;
  NEON_ANALYTICS_CREDENTIALS: string;
};
type Dimension = "site" | "page" | "query" | "country" | "device";
type ApiRow = { keys?: unknown; clicks?: unknown; impressions?: unknown; position?: unknown };
type MetricRow = {
  day: string; dimensionType: Dimension; dimensionValue: string;
  clicks: number; impressions: number; position: number;
};
type Client = {
  query: (body: Record<string, unknown>) => Promise<{ rows?: ApiRow[]; metadata?: { first_incomplete_date?: string } }>;
  inspect: (url: string) => Promise<Record<string, unknown>>;
};

let tokenCache: { value: string; expires: number } | null = null;

export async function syncSettledSearchConsole(env: DirectGscEnv, now = new Date()): Promise<Record<string, unknown>> {
  const client = await clientFor(env);
  const today = pacificDay(now);
  const maturityStart = maxDay(SYNC_START, addDays(today, -14));
  const maturity = await client.query({
    startDate: maturityStart, endDate: today, dimensions: ["date"],
    type: "web", dataState: "all", rowLimit: 400
  });
  const incomplete = validDay(maturity.metadata?.first_incomplete_date)
    ? maturity.metadata?.first_incomplete_date
    : undefined;
  let settledThrough = incomplete ? addDays(incomplete, -1) : undefined;
  if (!settledThrough) {
    const finalDates = await client.query({
      startDate: maturityStart, endDate: today, dimensions: ["date"],
      type: "web", dataState: "final", rowLimit: 400
    });
    settledThrough = (finalDates.rows ?? []).map((row) => key(row, 0)).filter(validDay).sort().at(-1);
  }
  if (!settledThrough || settledThrough < SYNC_START || settledThrough >= today) {
    throw new Error("Google did not provide a safe finalized-data watermark.");
  }

  const specs: Array<{ type: Dimension; dimensions: string[] }> = [
    { type: "site", dimensions: ["date"] },
    { type: "page", dimensions: ["date", "page"] },
    { type: "query", dimensions: ["date", "query"] },
    { type: "country", dimensions: ["date", "country"] },
    { type: "device", dimensions: ["date", "device"] }
  ];
  const dimensionResults = await Promise.all(specs.map(async (spec) => ({
    spec,
    rows: await paged(client, {
      startDate: SYNC_START, endDate: settledThrough,
      dimensions: spec.dimensions, type: "web", dataState: "final"
    })
  })));
  const pageQueryRows = await paged(client, {
    startDate: SYNC_START, endDate: settledThrough,
    dimensions: ["date", "page", "query"],
    dimensionFilterGroups: [{ groupType: "and", filters: [{
      dimension: "page", operator: "includingRegex", expression: cohortRegex()
    }] }],
    type: "web", dataState: "final"
  });
  const indexing = await Promise.all(SEO_COHORT_URLS.map(async (url) => {
    const raw = record(await client.inspect(url));
    const status = record(record(raw.inspectionResult).indexStatusResult);
    const verdict = text(status.verdict) || "VERDICT_UNSPECIFIED";
    const coverageState = text(status.coverageState);
    const lower = coverageState.toLowerCase();
    const currentStatus = /not indexed|unknown to google|discovered|crawled/.test(lower)
      ? "not_indexed"
      : verdict === "PASS" || /submitted and indexed|indexed, not submitted/.test(lower)
        ? "indexed" : "pending";
    return {
      url, status: currentStatus, verdict, coverageState,
      indexingState: text(status.indexingState),
      lastCrawlTime: text(status.lastCrawlTime) || null,
      checkedAt: now.toISOString()
    };
  }));

  const rows = dimensionResults.flatMap((result) => result.rows.map((row) => ({
    day: key(row, 0),
    dimensionType: result.spec.type,
    dimensionValue: result.spec.type === "site" ? "all" : key(row, 1),
    clicks: integer(row.clicks),
    impressions: integer(row.impressions),
    position: metric(row.position)
  })));
  addFinalSiteZeros(rows, SYNC_START, settledThrough);
  validateRows(rows, settledThrough);

  const footprints = pageQueryRows.map((row) => ({
    day: key(row, 0), page: key(row, 1), query: key(row, 2)
  })).filter((row) => validDay(row.day) && row.page && row.query);
  const cohort = cohortSnapshot(rows, footprints, indexing, settledThrough, now);
  const payload = {
    protocol: 1, source: "google-search-console-api", generatedAt: now.toISOString(),
    settledThrough, firstIncompleteDate: incomplete ?? null, rows, cohort
  };
  const response = await postWithRetry(
    env.NEON_ANALYTICS_URL.replace(/\/+$/, "") + "/v1/gsc-sync",
    payload,
    env.NEON_ANALYTICS_CREDENTIALS
  );
  if (!response.ok) throw new Error("Neon GSC sync returned " + response.status + ".");
  return { ...(await response.json() as Record<string, unknown>), settledThrough, source: payload.source };
}

async function clientFor(env: DirectGscEnv): Promise<Client> {
  if (!env.GOOGLE_SERVICE_ACCOUNT_JSON || !env.SEARCH_CONSOLE_PROPERTY) {
    throw new Error("Search Console credentials are not configured.");
  }
  const account = record(JSON.parse(env.GOOGLE_SERVICE_ACCOUNT_JSON));
  const email = text(account.client_email);
  const privateKey = text(account.private_key);
  if (!email.endsWith(".iam.gserviceaccount.com") || !privateKey.includes("BEGIN PRIVATE KEY")) {
    throw new Error("Invalid Search Console service account.");
  }
  const token = await accessToken(email, privateKey);
  const property = env.SEARCH_CONSOLE_PROPERTY;
  const query = async (body: Record<string, unknown>) => {
    const response = await fetch(
      "https://www.googleapis.com/webmasters/v3/sites/" + encodeURIComponent(property) + "/searchAnalytics/query",
      {
        method: "POST",
        headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(20000)
      }
    );
    if (!response.ok) throw new Error("Search Console query failed: " + response.status);
    return await response.json() as { rows?: ApiRow[]; metadata?: { first_incomplete_date?: string } };
  };
  const inspect = async (inspectionUrl: string) => {
    const response = await fetch("https://searchconsole.googleapis.com/v1/urlInspection/index:inspect", {
      method: "POST",
      headers: { Authorization: "Bearer " + token, "Content-Type": "application/json" },
      body: JSON.stringify({ inspectionUrl, siteUrl: property, languageCode: "en-US" }),
      signal: AbortSignal.timeout(20000)
    });
    if (!response.ok) throw new Error("URL Inspection failed: " + response.status);
    return await response.json() as Record<string, unknown>;
  };
  return { query, inspect };
}

async function accessToken(email: string, privateKey: string): Promise<string> {
  if (tokenCache && tokenCache.expires > Date.now() + 60000) return tokenCache.value;
  const now = Math.floor(Date.now() / 1000);
  const header = base64Url(new TextEncoder().encode(JSON.stringify({ alg: "RS256", typ: "JWT" })));
  const claims = base64Url(new TextEncoder().encode(JSON.stringify({
    iss: email, scope: SCOPE, aud: TOKEN_URL, iat: now, exp: now + 3600
  })));
  const unsigned = header + "." + claims;
  const bytes = Uint8Array.from(
    atob(privateKey.replace(/-----BEGIN PRIVATE KEY-----|-----END PRIVATE KEY-----|\s/g, "")),
    (char) => char.charCodeAt(0)
  );
  const key = await crypto.subtle.importKey(
    "pkcs8", bytes, { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" }, false, ["sign"]
  );
  const signature = new Uint8Array(await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(unsigned)
  ));
  const response = await fetch(TOKEN_URL, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion: unsigned + "." + base64Url(signature)
    }),
    signal: AbortSignal.timeout(20000)
  });
  if (!response.ok) throw new Error("OAuth token request failed: " + response.status);
  const result = record(await response.json());
  const token = text(result.access_token);
  if (!token) throw new Error("OAuth token was missing.");
  tokenCache = { value: token, expires: Date.now() + Number(result.expires_in || 3600) * 1000 };
  return token;
}

async function paged(client: Client, body: Record<string, unknown>): Promise<ApiRow[]> {
  const rows: ApiRow[] = [];
  for (let startRow = 0; ; startRow += PAGE_SIZE) {
    const page = (await client.query({ ...body, rowLimit: PAGE_SIZE, startRow })).rows ?? [];
    rows.push(...page);
    if (page.length < PAGE_SIZE) return rows;
  }
}

function addFinalSiteZeros(rows: MetricRow[], start: string, end: string): void {
  const existing = new Set(rows.filter((row) => row.dimensionType === "site").map((row) => row.day));
  for (let day = start; day <= end; day = addDays(day, 1)) {
    if (!existing.has(day)) rows.push({
      day, dimensionType: "site", dimensionValue: "all",
      clicks: 0, impressions: 0, position: 0
    });
  }
}

function validateRows(rows: MetricRow[], settledThrough: string): void {
  const keys = new Set<string>();
  for (const row of rows) {
    const id = [row.day, row.dimensionType, row.dimensionValue].join("\u0000");
    if (!validDay(row.day) || row.day < SYNC_START || row.day > settledThrough ||
        !row.dimensionValue || keys.has(id) || row.clicks > row.impressions) {
      throw new Error("Invalid or duplicate aggregate row.");
    }
    keys.add(id);
  }
}

function cohortSnapshot(
  rows: MetricRow[],
  queries: Array<{ day: string; page: string; query: string }>,
  indexing: Array<Record<string, unknown>>,
  settledThrough: string,
  now: Date
): Record<string, unknown> {
  const postEnd = settledThrough >= POST_START ? settledThrough : null;
  const measure = (url: string | null, start: string, end: string | null) => {
    if (!end || end < start) return { clicks: 0, ctr: 0, impressions: 0, perDay: 0, position: null, queries: 0 };
    const matching = rows.filter((row) =>
      row.dimensionType === "page" && row.day >= start && row.day <= end &&
      SEO_COHORT_URLS.some((item) => item === row.dimensionValue) &&
      (!url || row.dimensionValue === url)
    );
    const impressions = matching.reduce((sum, row) => sum + row.impressions, 0);
    const clicks = matching.reduce((sum, row) => sum + row.clicks, 0);
    const footprint = new Set(queries.filter((row) =>
      row.day >= start && row.day <= end && (!url || row.page === url)
    ).map((row) => row.query)).size;
    return {
      clicks, impressions, ctr: impressions ? clicks / impressions : 0,
      perDay: impressions / dayCount(start, end),
      position: impressions
        ? matching.reduce((sum, row) => sum + row.position * row.impressions, 0) / impressions
        : null,
      queries: footprint
    };
  };
  return {
    source: "google-search-console-api",
    generatedAt: now.toISOString(),
    settledThrough,
    baseline: { start: BASELINE_START, end: BASELINE_END, days: 4 },
    post: { start: POST_START, end: postEnd, days: postEnd ? dayCount(POST_START, postEnd) : 0 },
    excludedTransitionDay: "2026-09-07",
    rows: [
      { url: "cohort", baseline: measure(null, BASELINE_START, BASELINE_END), post: measure(null, POST_START, postEnd) },
      ...SEO_COHORT_URLS.map((url) => ({
        url,
        baseline: measure(url, BASELINE_START, BASELINE_END),
        post: measure(url, POST_START, postEnd),
        indexing: indexing.find((item) => item.url === url)
      }))
    ],
    indexedPages: indexing.filter((item) => item.status === "indexed").length,
    notes: [
      "Daily aggregates are read directly from Google's official Search Console API.",
      "Query footprint counts only visible queries; Google can omit anonymized queries.",
      "Position is impressions-weighted; lower is better.",
      "Small samples have low statistical power and do not establish causation."
    ]
  };
}

async function postWithRetry(url: string, payload: unknown, credentials: string): Promise<Response> {
  let response = new Response(null, { status: 503 });
  for (let attempt = 0; attempt < 3; attempt += 1) {
    response = await fetch(url, {
      method: "POST",
      headers: { Authorization: "Basic " + credentials.trim(), "Content-Type": "application/json" },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(45000)
    });
    if (![429, 502, 503, 504].includes(response.status)) return response;
    if (attempt < 2) await new Promise((resolve) => setTimeout(resolve, 5000 * (attempt + 1)));
  }
  return response;
}

function cohortRegex(): string {
  return "^(?:" + SEO_COHORT_URLS.map((url) =>
    url.replace(/[.*+?^$()|[\]\\]/g, "\\$&")
  ).join("|") + ")$";
}
function key(row: ApiRow, index: number): string {
  return Array.isArray(row.keys) && typeof row.keys[index] === "string" ? row.keys[index] : "";
}
function record(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
function text(value: unknown): string { return typeof value === "string" ? value : ""; }
function integer(value: unknown): number {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0 ? value : 0;
}
function metric(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : 0;
}
function validDay(value: unknown): value is string {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    new Date(value + "T12:00:00Z").toISOString().slice(0, 10) === value;
}
function addDays(day: string, amount: number): string {
  const date = new Date(day + "T12:00:00Z");
  date.setUTCDate(date.getUTCDate() + amount);
  return date.toISOString().slice(0, 10);
}
function maxDay(left: string, right: string): string { return left > right ? left : right; }
function dayCount(start: string, end: string): number {
  return Math.floor((Date.parse(end + "T12:00:00Z") - Date.parse(start + "T12:00:00Z")) / 86400000) + 1;
}
function pacificDay(now: Date): string {
  return new Intl.DateTimeFormat("en-CA", {
    timeZone: "America/Los_Angeles", year: "numeric", month: "2-digit", day: "2-digit"
  }).format(now);
}
function base64Url(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
