const READONLY_SCOPE = "https://www.googleapis.com/auth/webmasters.readonly";
const TOKEN_ENDPOINT = "https://oauth2.googleapis.com/token";

export type SearchConsoleEnv = {
  GOOGLE_SERVICE_ACCOUNT_JSON?: string;
  SEARCH_CONSOLE_PROPERTY?: string;
};

export type SearchMetricRow = {
  label: string;
  clicks: number;
  impressions: number;
  ctr: number;
  position: number;
};

export type SearchTrendPoint = {
  date: string;
  clicks: number;
  impressions: number;
};

export type SearchConsoleData = {
  available: boolean;
  message: string;
  periodStart: string;
  periodEnd: string;
  clicks: number;
  impressions: number;
  ctr: number;
  position: number;
  daily: SearchTrendPoint[];
  queries: SearchMetricRow[];
  pages: SearchMetricRow[];
};

type ServiceAccount = {
  client_email: string;
  private_key: string;
};

type ApiRow = {
  keys?: unknown;
  clicks?: unknown;
  impressions?: unknown;
  ctr?: unknown;
  position?: unknown;
};

let cachedToken: { value: string; expiresAt: number } | null = null;

export async function loadSearchConsoleData(
  env: SearchConsoleEnv,
  now = new Date(),
): Promise<SearchConsoleData> {
  const periodEnd = isoDay(addDays(now, -2));
  const periodStart = isoDay(addDays(now, -29));
  const unavailable = (message: string): SearchConsoleData => ({
    available: false,
    message,
    periodStart,
    periodEnd,
    clicks: 0,
    impressions: 0,
    ctr: 0,
    position: 0,
    daily: [],
    queries: [],
    pages: [],
  });

  if (!env.GOOGLE_SERVICE_ACCOUNT_JSON || !env.SEARCH_CONSOLE_PROPERTY) {
    return unavailable("Search Console credentials are not configured yet.");
  }

  try {
    const serviceAccount = parseServiceAccount(env.GOOGLE_SERVICE_ACCOUNT_JSON);
    const token = await accessToken(serviceAccount);
    const [totalsResponse, dailyResponse, queryResponse, pageResponse] =
      await Promise.all([
        querySearchConsole(env.SEARCH_CONSOLE_PROPERTY, token, {
          startDate: periodStart,
          endDate: periodEnd,
          type: "web",
          dataState: "all",
        }),
        querySearchConsole(env.SEARCH_CONSOLE_PROPERTY, token, {
          startDate: periodStart,
          endDate: periodEnd,
          dimensions: ["date"],
          type: "web",
          dataState: "all",
          rowLimit: 100,
        }),
        querySearchConsole(env.SEARCH_CONSOLE_PROPERTY, token, {
          startDate: periodStart,
          endDate: periodEnd,
          dimensions: ["query"],
          type: "web",
          dataState: "all",
          rowLimit: 15,
        }),
        querySearchConsole(env.SEARCH_CONSOLE_PROPERTY, token, {
          startDate: periodStart,
          endDate: periodEnd,
          dimensions: ["page"],
          type: "web",
          dataState: "all",
          rowLimit: 15,
        }),
      ]);

    const totals = metricRows(totalsResponse.rows)[0];
    return {
      available: true,
      message: "",
      periodStart,
      periodEnd,
      clicks: totals?.clicks ?? 0,
      impressions: totals?.impressions ?? 0,
      ctr: totals?.ctr ?? 0,
      position: totals?.position ?? 0,
      daily: metricRows(dailyResponse.rows).map((row) => ({
        date: row.label,
        clicks: row.clicks,
        impressions: row.impressions,
      })),
      queries: metricRows(queryResponse.rows),
      pages: metricRows(pageResponse.rows).map((row) => ({
        ...row,
        label: pagePath(row.label),
      })),
    };
  } catch (error) {
    console.error(
      JSON.stringify({
        event: "search_console_read_failed",
        errorType: error instanceof Error ? error.name : "UnknownError",
      }),
    );
    return unavailable("Google Search Console is temporarily unavailable.");
  }
}

function parseServiceAccount(value: string): ServiceAccount {
  const candidate: unknown = JSON.parse(value);
  if (!candidate || typeof candidate !== "object") {
    throw new Error("Invalid service-account credentials.");
  }
  const record = candidate as Record<string, unknown>;
  if (
    typeof record.client_email !== "string" ||
    !record.client_email.endsWith(".iam.gserviceaccount.com") ||
    typeof record.private_key !== "string" ||
    !record.private_key.includes("BEGIN PRIVATE KEY")
  ) {
    throw new Error("Incomplete service-account credentials.");
  }
  return {
    client_email: record.client_email,
    private_key: record.private_key,
  };
}

async function accessToken(account: ServiceAccount): Promise<string> {
  if (cachedToken && cachedToken.expiresAt > Date.now() + 60_000) {
    return cachedToken.value;
  }

  const now = Math.floor(Date.now() / 1000);
  const header = base64Url(JSON.stringify({ alg: "RS256", typ: "JWT" }));
  const claims = base64Url(
    JSON.stringify({
      iss: account.client_email,
      scope: READONLY_SCOPE,
      aud: TOKEN_ENDPOINT,
      iat: now,
      exp: now + 3600,
    }),
  );
  const unsigned = `${header}.${claims}`;
  const privateKey = await crypto.subtle.importKey(
    "pkcs8",
    pemBytes(account.private_key),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign(
    "RSASSA-PKCS1-v1_5",
    privateKey,
    new TextEncoder().encode(unsigned),
  );
  const assertion = `${unsigned}.${base64UrlBytes(new Uint8Array(signature))}`;

  const response = await fetch(TOKEN_ENDPOINT, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer",
      assertion,
    }),
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok) throw new Error(`OAuth token request failed: ${response.status}`);
  const result: unknown = await response.json();
  if (!result || typeof result !== "object") throw new Error("Invalid OAuth response.");
  const token = (result as Record<string, unknown>).access_token;
  const expiresIn = (result as Record<string, unknown>).expires_in;
  if (typeof token !== "string") throw new Error("OAuth token was missing.");
  cachedToken = {
    value: token,
    expiresAt: Date.now() + (typeof expiresIn === "number" ? expiresIn : 3600) * 1000,
  };
  return token;
}

async function querySearchConsole(
  property: string,
  token: string,
  body: Record<string, unknown>,
): Promise<{ rows?: ApiRow[] }> {
  const endpoint = `https://www.googleapis.com/webmasters/v3/sites/${encodeURIComponent(property)}/searchAnalytics/query`;
  const response = await fetch(endpoint, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${token}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(10_000),
  });
  if (!response.ok) throw new Error(`Search Console query failed: ${response.status}`);
  const result: unknown = await response.json();
  return result && typeof result === "object" ? (result as { rows?: ApiRow[] }) : {};
}

function metricRows(rows: ApiRow[] | undefined): SearchMetricRow[] {
  return (rows ?? []).map((row) => ({
    label: Array.isArray(row.keys) && typeof row.keys[0] === "string" ? row.keys[0] : "Total",
    clicks: finite(row.clicks),
    impressions: finite(row.impressions),
    ctr: finite(row.ctr),
    position: finite(row.position),
  }));
}

function pagePath(value: string): string {
  try {
    const url = new URL(value);
    return `${url.pathname}${url.search}`;
  } catch {
    return value;
  }
}

function pemBytes(value: string): ArrayBuffer {
  const base64 = value
    .replace(/-----BEGIN PRIVATE KEY-----/g, "")
    .replace(/-----END PRIVATE KEY-----/g, "")
    .replace(/\s/g, "");
  const binary = atob(base64);
  const bytes = Uint8Array.from(binary, (character) => character.charCodeAt(0));
  return bytes.buffer;
}

function base64Url(value: string): string {
  return base64UrlBytes(new TextEncoder().encode(value));
}

function base64UrlBytes(value: Uint8Array): string {
  let binary = "";
  for (const byte of value) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

function finite(value: unknown): number {
  return typeof value === "number" && Number.isFinite(value) && value >= 0 ? value : 0;
}

function addDays(value: Date, days: number): Date {
  return new Date(Date.UTC(value.getUTCFullYear(), value.getUTCMonth(), value.getUTCDate() + days));
}

function isoDay(value: Date): string {
  return value.toISOString().slice(0, 10);
}
