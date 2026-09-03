import { rollUpExpiredTokens, validatePayload } from "./usage";
import {
  allowedWebsiteOrigin,
  pruneWebAnalytics,
  recordWebEvent,
  validateWebEvent,
  websiteCorsHeaders,
} from "./web";
import {
  buildYouTubeSearchQuery,
  combinationKey,
  selectFastestWebLap,
  type BenchmarkQuery,
  type WebLapCandidate,
} from "./benchmark";

const ACTIVE_PATH = "/v1/active";
const HEALTH_PATH = "/health";
const BENCHMARK_PATH = "/v1/lap-benchmark";
const WEB_EVENT_PATH = "/v1/web-event";
const MAX_BODY_BYTES = 2_048;
const INSTALL_ALERT_TO = "bobbyholmes@gmail.com";
const INSTALL_ALERT_FROM = "notifications@pitmedic.com";

type WorkerEnv = Env & { YOUTUBE_API_KEY?: string };

const worker = {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === HEALTH_PATH && request.method === "GET") {
      await env.DB.prepare("SELECT 1").first();
      return jsonResponse({ status: "ok", protocol: 1 }, 200);
    }

    if (url.pathname === BENCHMARK_PATH) {
      return handleBenchmarkRequest(request, url, env);
    }

    if (url.pathname === WEB_EVENT_PATH) {
      return handleWebEventRequest(request, env);
    }

    if (url.pathname !== ACTIVE_PATH) {
      return jsonResponse({ error: "not_found" }, 404);
    }

    if (request.method !== "POST") {
      return jsonResponse({ error: "method_not_allowed" }, 405, {
        Allow: "POST",
      });
    }

    if (request.headers.has("Origin")) {
      return jsonResponse({ error: "browser_requests_not_accepted" }, 403);
    }

    try {
      const rawPayload = await readBoundedJson(request, MAX_BODY_BYTES);
      const validation = validatePayload(rawPayload);
      if (!validation.ok) {
        return jsonResponse(
          { error: "invalid_payload", detail: validation.reason },
          400,
        );
      }

      const now = new Date();
      const day = now.toISOString().slice(0, 10);
      const month = day.slice(0, 7);
      const payload = validation.value;

      // Keep the same privacy-preserving de-duplication model. The result of the
      // monthly INSERT tells us whether this anonymous installation is newly
      // counted this month; the rotating token itself is never included in email.
      await env.DB.batch([
        env.DB.prepare(
          "INSERT OR IGNORE INTO daily_active (day, token, app_version, channel, install_type) VALUES (?, ?, ?, ?, ?)",
        ).bind(
          day,
          payload.dailyToken,
          payload.appVersion,
          payload.channel,
          payload.installType,
        ),
        env.DB.prepare(
          "UPDATE daily_active SET app_version = ?, channel = ?, install_type = ? WHERE day = ? AND token = ?",
        ).bind(
          payload.appVersion,
          payload.channel,
          payload.installType,
          day,
          payload.dailyToken,
        ),
      ]);

      const monthlyInsert = await env.DB.prepare(
        "INSERT OR IGNORE INTO monthly_active (month, token, app_version, channel, install_type) VALUES (?, ?, ?, ?, ?)",
      )
        .bind(
          month,
          payload.monthlyToken,
          payload.appVersion,
          payload.channel,
          payload.installType,
        )
        .run();

      // A rotating token identifies the same anonymous installation only for
      // this month. Keep its current version/channel/install type up to date
      // without adding another installation or sending another install alert.
      await env.DB.prepare(
        "UPDATE monthly_active SET app_version = ?, channel = ?, install_type = ? WHERE month = ? AND token = ?",
      )
        .bind(
          payload.appVersion,
          payload.channel,
          payload.installType,
          month,
          payload.monthlyToken,
        )
        .run();

      if ((monthlyInsert.meta.changes ?? 0) > 0) {
        const total = await env.DB.prepare(
          "SELECT COUNT(*) AS total FROM monthly_active WHERE month = ?",
        )
          .bind(month)
          .first<{ total: number }>();

        // Alert delivery is deliberately best-effort. A mail-service outage must
        // never make a valid anonymous heartbeat fail or cause extra client data
        // to be collected.
        await sendInstallAlert(env, {
          appVersion: payload.appVersion,
          channel: payload.channel,
          installType: payload.installType,
          month,
          monthlyTotal: Number(total?.total ?? 0),
          detectedAt: now,
        });
      }

      return jsonResponse({ accepted: true }, 202);
    } catch (error) {
      if (error instanceof PayloadTooLargeError) {
        return jsonResponse({ error: "payload_too_large" }, 413);
      }
      if (error instanceof SyntaxError) {
        return jsonResponse({ error: "invalid_json" }, 400);
      }

      console.error(
        JSON.stringify({
          event: "usage_heartbeat_failed",
          errorType: error instanceof Error ? error.name : "UnknownError",
        }),
      );
      return jsonResponse({ error: "temporary_failure" }, 503);
    }
  },

  async scheduled(_controller, env, ctx): Promise<void> {
    ctx.waitUntil(
      Promise.all([
        rollUpExpiredTokens(env.DB),
        pruneWebAnalytics(env.DB),
      ]).then(() => undefined),
    );
  },
} satisfies ExportedHandler<WorkerEnv>;

export default worker;

async function handleWebEventRequest(
  request: Request,
  env: WorkerEnv,
): Promise<Response> {
  const origin = allowedWebsiteOrigin(request);
  if (!origin) return jsonResponse({ error: "origin_not_allowed" }, 403);
  const cors = websiteCorsHeaders(origin);

  if (request.method === "OPTIONS") {
    return new Response(null, { status: 204, headers: cors });
  }
  if (request.method !== "POST") {
    return jsonResponse(
      { error: "method_not_allowed" },
      405,
      { ...cors, Allow: "POST, OPTIONS" },
    );
  }

  try {
    const rawPayload = await readBoundedJson(request, MAX_BODY_BYTES);
    const validation = validateWebEvent(rawPayload);
    if (!validation.ok) {
      return jsonResponse(
        { error: "invalid_payload", detail: validation.reason },
        400,
        cors,
      );
    }
    await recordWebEvent(env.DB, validation.value, request);
    return new Response(null, { status: 204, headers: cors });
  } catch (error) {
    if (error instanceof PayloadTooLargeError) {
      return jsonResponse({ error: "payload_too_large" }, 413, cors);
    }
    if (error instanceof SyntaxError) {
      return jsonResponse({ error: "invalid_json" }, 400, cors);
    }
    console.error(
      JSON.stringify({
        event: "website_analytics_failed",
        errorType: error instanceof Error ? error.name : "UnknownError",
      }),
    );
    return jsonResponse({ error: "temporary_failure" }, 503, cors);
  }
}

async function handleBenchmarkRequest(
  request: Request,
  url: URL,
  env: WorkerEnv,
): Promise<Response> {
  if (request.method !== "GET") {
    return jsonResponse({ error: "method_not_allowed" }, 405, { Allow: "GET" });
  }

  const query = readBenchmarkQuery(url.searchParams);
  if (!query) return jsonResponse({ error: "invalid_combination" }, 400);

  try {
    const key = combinationKey(query);
    const official = await env.DB.prepare(
      "SELECT lap_seconds, source_name, source_url, verified_at FROM official_lap_benchmarks WHERE combination_key = ?",
    )
      .bind(key)
      .first<{
        lap_seconds: number;
        source_name: string;
        source_url: string;
        verified_at: string;
      }>();
    if (official) {
      return benchmarkResponse({
        available: true,
        lapSeconds: official.lap_seconds,
        sourceKind: "official",
        sourceName: official.source_name,
        sourceUrl: official.source_url,
        confidence: "official",
        checkedAt: official.verified_at,
      });
    }

    const now = new Date();
    const cached = await env.DB.prepare(
      "SELECT available, lap_seconds, source_kind, source_name, source_url, confidence, checked_at FROM lap_benchmark_cache WHERE combination_key = ? AND expires_at > ?",
    )
      .bind(key, now.toISOString())
      .first<{
        available: number;
        lap_seconds: number | null;
        source_kind: string;
        source_name: string;
        source_url: string | null;
        confidence: string;
        checked_at: string;
      }>();
    if (cached) {
      return benchmarkResponse({
        available: cached.available === 1,
        lapSeconds: cached.lap_seconds,
        sourceKind: cached.source_kind,
        sourceName: cached.source_name,
        sourceUrl: cached.source_url,
        confidence: cached.confidence,
        checkedAt: cached.checked_at,
      });
    }

    const benchmark = env.YOUTUBE_API_KEY
      ? await searchYouTube(query, env.YOUTUBE_API_KEY)
      : null;
    const checkedAt = now.toISOString();
    const expiresAt = new Date(
      now.getTime() + (benchmark ? 7 : 1) * 24 * 60 * 60 * 1_000,
    ).toISOString();
    const result = benchmark
      ? {
          available: true,
          ...benchmark,
          checkedAt,
        }
      : {
          available: false,
          lapSeconds: null,
          sourceKind: "",
          sourceName: "",
          sourceUrl: null,
          confidence: "",
          checkedAt,
        };

    await env.DB.prepare(
      "INSERT INTO lap_benchmark_cache (combination_key, available, lap_seconds, source_kind, source_name, source_url, confidence, checked_at, expires_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(combination_key) DO UPDATE SET available = excluded.available, lap_seconds = excluded.lap_seconds, source_kind = excluded.source_kind, source_name = excluded.source_name, source_url = excluded.source_url, confidence = excluded.confidence, checked_at = excluded.checked_at, expires_at = excluded.expires_at",
    )
      .bind(
        key,
        result.available ? 1 : 0,
        result.lapSeconds,
        result.sourceKind,
        result.sourceName,
        result.sourceUrl,
        result.confidence,
        checkedAt,
        expiresAt,
      )
      .run();
    return benchmarkResponse(result);
  } catch (error) {
    console.error(
      JSON.stringify({
        event: "lap_benchmark_failed",
        errorType: error instanceof Error ? error.name : "UnknownError",
      }),
    );
    return jsonResponse({ error: "temporary_failure" }, 503);
  }
}

function readBenchmarkQuery(params: URLSearchParams): BenchmarkQuery | null {
  const query = {
    sim: params.get("sim")?.trim() ?? "",
    track: params.get("track")?.trim() ?? "",
    layout: params.get("layout")?.trim() ?? "",
    car: params.get("car")?.trim() ?? "",
  };
  if (
    query.sim.length < 1 ||
    query.sim.length > 80 ||
    query.track.length < 1 ||
    query.track.length > 120 ||
    query.layout.length > 120 ||
    query.car.length < 1 ||
    query.car.length > 120
  ) {
    return null;
  }
  return query;
}

async function searchYouTube(
  query: BenchmarkQuery,
  apiKey: string,
): Promise<ReturnType<typeof selectFastestWebLap>> {
  const search = buildYouTubeSearchQuery(query);
  const endpoint = new URL("https://www.googleapis.com/youtube/v3/search");
  endpoint.searchParams.set("part", "snippet");
  endpoint.searchParams.set("type", "video");
  endpoint.searchParams.set("maxResults", "25");
  endpoint.searchParams.set("safeSearch", "moderate");
  endpoint.searchParams.set("q", search);
  endpoint.searchParams.set("key", apiKey);

  const response = await fetch(endpoint, {
    headers: { Accept: "application/json" },
  });
  if (!response.ok) throw new Error(`YouTube search failed: ${response.status}`);
  const body: unknown = await response.json();
  if (!isYouTubeSearchResponse(body)) return null;
  const candidates: WebLapCandidate[] = body.items.map((item) => ({
    title: item.snippet.title,
    description: item.snippet.description,
    channelTitle: item.snippet.channelTitle,
    videoId: item.id.videoId,
  }));
  return selectFastestWebLap(query, candidates);
}

function isYouTubeSearchResponse(value: unknown): value is {
  items: Array<{
    id: { videoId: string };
    snippet: { title: string; description: string; channelTitle: string };
  }>;
} {
  if (!value || typeof value !== "object" || !("items" in value)) return false;
  const items = (value as { items?: unknown }).items;
  return (
    Array.isArray(items) &&
    items.length <= 50 &&
    items.every((item) => {
      if (!item || typeof item !== "object") return false;
      const record = item as {
        id?: { videoId?: unknown };
        snippet?: {
          title?: unknown;
          description?: unknown;
          channelTitle?: unknown;
        };
      };
      return (
        typeof record.id?.videoId === "string" &&
        typeof record.snippet?.title === "string" &&
        typeof record.snippet.description === "string" &&
        typeof record.snippet.channelTitle === "string"
      );
    })
  );
}

function benchmarkResponse(body: Record<string, unknown>): Response {
  return Response.json(body, {
    status: 200,
    headers: {
      "Cache-Control": "public, max-age=900",
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
    },
  });
}

async function sendInstallAlert(
  env: Env,
  alert: {
    appVersion: string;
    channel: string;
    installType: string;
    month: string;
    monthlyTotal: number;
    detectedAt: Date;
  },
): Promise<void> {
  try {
    const binding = env.INSTALL_ALERT_EMAIL;
    if (!binding) return;

    const detected = alert.detectedAt.toISOString();
    const subject = `PitMedic install detected · v${alert.appVersion}`;
    const text = [
      "A new anonymous PitMedic installation was counted on the Usage Dashboard.",
      "",
      `Version: ${alert.appVersion}`,
      `Channel: ${alert.channel}`,
      `Install type: ${alert.installType}`,
      `Detected: ${detected}`,
      `Monthly active installations (${alert.month}): ${alert.monthlyTotal}`,
      "",
      "No rotating token, IP address, hardware information, simulator activity, findings, or diagnostics are included in this email.",
    ].join("\n");

    await binding.send({
      from: INSTALL_ALERT_FROM,
      to: INSTALL_ALERT_TO,
      subject,
      text,
    });
  } catch (error) {
    console.error(
      JSON.stringify({
        event: "install_alert_email_failed",
        errorType: error instanceof Error ? error.name : "UnknownError",
      }),
    );
  }
}

class PayloadTooLargeError extends Error {
  constructor() {
    super("Request body exceeded the accepted size.");
    this.name = "PayloadTooLargeError";
  }
}

async function readBoundedJson(
  request: Request,
  limit: number,
): Promise<unknown> {
  const contentLength = request.headers.get("Content-Length");
  if (contentLength !== null && Number(contentLength) > limit) {
    throw new PayloadTooLargeError();
  }
  if (request.body === null) throw new SyntaxError("Missing JSON body.");

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let length = 0;

  try {
    while (true) {
      const result = await reader.read();
      if (result.done) break;
      length += result.value.byteLength;
      if (length > limit) throw new PayloadTooLargeError();
      chunks.push(result.value);
    }
  } finally {
    reader.releaseLock();
  }

  const body = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    body.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return JSON.parse(
    new TextDecoder("utf-8", { fatal: true, ignoreBOM: false }).decode(body),
  );
}

function jsonResponse(
  body: Record<string, unknown>,
  status: number,
  additionalHeaders: Record<string, string> = {},
): Response {
  return Response.json(body, {
    status,
    headers: {
      "Cache-Control": "no-store",
      "Content-Type": "application/json; charset=utf-8",
      "X-Content-Type-Options": "nosniff",
      ...additionalHeaders,
    },
  });
}
