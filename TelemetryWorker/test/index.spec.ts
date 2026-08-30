import { env } from "cloudflare:workers";
import { beforeEach, describe, expect, it } from "vitest";
import worker from "../src/index";
import { rollUpExpiredTokens, validatePayload } from "../src/usage";

const TOKEN_A = "a".repeat(64);
const TOKEN_B = "b".repeat(64);
const IncomingRequest = Request<unknown, IncomingRequestCfProperties>;

function payload(
  overrides: Record<string, unknown> = {},
): Record<string, unknown> {
  return {
    protocol: 1,
    dailyToken: TOKEN_A,
    monthlyToken: TOKEN_B,
    appVersion: "0.6.0.0",
    channel: "preview",
    installType: "portable",
    ...overrides,
  };
}

async function post(body: unknown): Promise<Response> {
  const request = new IncomingRequest("https://usage.example/v1/active", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  return worker.fetch(request, env);
}

beforeEach(async () => {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM daily_active"),
    env.DB.prepare("DELETE FROM monthly_active"),
    env.DB.prepare("DELETE FROM daily_rollup"),
    env.DB.prepare("DELETE FROM monthly_rollup"),
  ]);
});

describe("payload validation", () => {
  it("accepts only the documented contract", () => {
    expect(validatePayload(payload()).ok).toBe(true);
    expect(validatePayload(payload({ hostname: "not-allowed" }))).toEqual({
      ok: false,
      reason: "unexpected_field",
    });
  });

  it("rejects permanent-looking or malformed tokens", () => {
    expect(validatePayload(payload({ dailyToken: "persistent-id" }))).toEqual({
      ok: false,
      reason: "invalid_daily_token",
    });
  });
});

describe("active installation endpoint", () => {
  it("deduplicates repeat heartbeats", async () => {
    expect((await post(payload())).status).toBe(202);
    expect((await post(payload())).status).toBe(202);

    const daily = await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM daily_active",
    ).first<{ count: number }>();
    const monthly = await env.DB.prepare(
      "SELECT COUNT(*) AS count FROM monthly_active",
    ).first<{ count: number }>();
    expect(daily?.count).toBe(1);
    expect(monthly?.count).toBe(1);
  });

  it("does not accept browser-originated or extra data", async () => {
    const response = await worker.fetch(
      new IncomingRequest("https://usage.example/v1/active", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          Origin: "https://example.com",
        },
        body: JSON.stringify(payload()),
      }),
      env,
    );
    expect(response.status).toBe(403);

    const extra = await post(payload({ computerName: "never-store-this" }));
    expect(extra.status).toBe(400);
  });

  it("rolls expired tokens into aggregate counts and removes the tokens", async () => {
    await env.DB.batch([
      env.DB.prepare("INSERT INTO daily_active VALUES (?, ?, ?, ?, ?)").bind(
        "2026-08-28",
        TOKEN_A,
        "0.6.0.0",
        "preview",
        "portable",
      ),
      env.DB.prepare("INSERT INTO monthly_active VALUES (?, ?, ?, ?, ?)").bind(
        "2026-07",
        TOKEN_B,
        "0.6.0.0",
        "preview",
        "portable",
      ),
    ]);

    await rollUpExpiredTokens(env.DB, new Date("2026-08-29T04:00:00Z"));

    expect(
      (
        await env.DB.prepare(
          "SELECT COUNT(*) AS count FROM daily_active",
        ).first<{ count: number }>()
      )?.count,
    ).toBe(0);
    expect(
      (
        await env.DB.prepare(
          "SELECT COUNT(*) AS count FROM monthly_active",
        ).first<{ count: number }>()
      )?.count,
    ).toBe(0);
    expect(
      (
        await env.DB.prepare(
          "SELECT active_installations FROM daily_rollup",
        ).first<{ active_installations: number }>()
      )?.active_installations,
    ).toBe(1);
    expect(
      (
        await env.DB.prepare(
          "SELECT active_installations FROM monthly_rollup",
        ).first<{ active_installations: number }>()
      )?.active_installations,
    ).toBe(1);
  });
});
