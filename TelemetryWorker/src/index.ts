import { rollUpExpiredTokens, validatePayload } from "./usage";

const ACTIVE_PATH = "/v1/active";
const HEALTH_PATH = "/health";
const MAX_BODY_BYTES = 2_048;
const INSTALL_ALERT_TO = "bobbyholmes@gmail.com";
const INSTALL_ALERT_FROM = "notifications@pitmedic.com";

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === HEALTH_PATH && request.method === "GET") {
      await env.DB.prepare("SELECT 1").first();
      return jsonResponse({ status: "ok", protocol: 1 }, 200);
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
      await env.DB.prepare(
        "INSERT OR IGNORE INTO daily_active (day, token, app_version, channel, install_type) VALUES (?, ?, ?, ?, ?)",
      )
        .bind(
          day,
          payload.dailyToken,
          payload.appVersion,
          payload.channel,
          payload.installType,
        )
        .run();

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
    ctx.waitUntil(rollUpExpiredTokens(env.DB));
  },
} satisfies ExportedHandler<Env>;

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
