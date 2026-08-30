import { env } from "cloudflare:workers";
import { beforeEach, describe, expect, it } from "vitest";
import worker, { type DashboardEnv } from "../src/index";
import { loadDashboardData } from "../src/usage-dashboard";

const DAILY_TOKEN = "a".repeat(64);
const MONTHLY_TOKEN = "b".repeat(64);
const IncomingRequest = Request<unknown, IncomingRequestCfProperties>;
const dashboardEnv = env as unknown as DashboardEnv;

async function authenticatedCookie(): Promise<string> {
  const response = await worker.fetch(
    new IncomingRequest("https://stats.example/login", {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({ passcode: "pitmedic-test-passcode" }),
    }),
    dashboardEnv,
  );
  expect(response.status).toBe(303);
  const setCookie = response.headers.get("Set-Cookie");
  expect(setCookie).toContain("Secure");
  expect(setCookie).toContain("HttpOnly");
  return setCookie?.split(";", 1)[0] ?? "";
}

beforeEach(async () => {
  await env.DB.batch([
    env.DB.prepare("DELETE FROM daily_active"),
    env.DB.prepare("DELETE FROM monthly_active"),
    env.DB.prepare("DELETE FROM daily_rollup"),
    env.DB.prepare("DELETE FROM monthly_rollup"),
  ]);
});

describe("private aggregate dashboard", () => {
  it("renders aggregate totals and never exposes raw rotating tokens", async () => {
    const today = new Date().toISOString().slice(0, 10);
    const month = today.slice(0, 7);
    await env.DB.batch([
      env.DB.prepare("INSERT INTO daily_active VALUES (?, ?, ?, ?, ?)").bind(
        today,
        DAILY_TOKEN,
        "0.6.0.0",
        "preview",
        "portable",
      ),
      env.DB.prepare("INSERT INTO monthly_active VALUES (?, ?, ?, ?, ?)").bind(
        month,
        MONTHLY_TOKEN,
        "0.6.0.0",
        "preview",
        "portable",
      ),
    ]);

    const data = await loadDashboardData(env.DB);
    expect(data.today).toBe(1);
    expect(data.thisMonth).toBe(1);

    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard", {
        headers: { Cookie: cookie },
      }),
      dashboardEnv,
    );
    const html = await response.text();

    expect(response.status).toBe(200);
    expect(response.headers.get("Cache-Control")).toContain("no-store");
    expect(response.headers.get("Content-Security-Policy")).toContain(
      "default-src 'none'",
    );
    expect(html).toContain("PitMedic Usage Dashboard");
    expect(html).toContain("0.6.0.0");
    expect(html).toContain("portable");
    expect(html).not.toContain(DAILY_TOKEN);
    expect(html).not.toContain(MONTHLY_TOKEN);
    expect(html).not.toContain("dailyToken");
    expect(html).not.toContain("monthlyToken");
  });

  it("combines expired rollups with current aggregate activity", async () => {
    const now = new Date();
    const yesterday = new Date(
      Date.UTC(
        now.getUTCFullYear(),
        now.getUTCMonth(),
        now.getUTCDate() - 1,
      ),
    );
    const previousMonth = new Date(
      Date.UTC(now.getUTCFullYear(), now.getUTCMonth() - 1, 1),
    );
    const yesterdayPeriod = yesterday.toISOString().slice(0, 10);
    const previousMonthPeriod = previousMonth.toISOString().slice(0, 7);

    await env.DB.batch([
      env.DB.prepare("INSERT INTO daily_rollup VALUES (?, ?, ?, ?, ?)").bind(
        yesterdayPeriod,
        "0.5.0.0",
        "preview",
        "installer",
        4,
      ),
      env.DB.prepare("INSERT INTO monthly_rollup VALUES (?, ?, ?, ?, ?)").bind(
        previousMonthPeriod,
        "0.5.0.0",
        "preview",
        "installer",
        8,
      ),
    ]);

    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard", {
        headers: { Cookie: cookie },
      }),
      dashboardEnv,
    );
    const html = await response.text();
    expect(response.status).toBe(200);
    expect(html).toContain(
      new Intl.DateTimeFormat("en-US", {
        month: "short",
        day: "numeric",
        timeZone: "UTC",
      }).format(yesterday),
    );
    expect(html).toContain(
      new Intl.DateTimeFormat("en-US", {
        month: "short",
        year: "2-digit",
        timeZone: "UTC",
      }).format(previousMonth),
    );
  });

  it("exposes no write endpoint", async () => {
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard", {
        method: "POST",
      }),
      dashboardEnv,
    );
    expect(response.status).toBe(405);
    expect(response.headers.get("Allow")).toBe("GET");
  });

  it("requires the private passcode and rejects forged sessions", async () => {
    const anonymous = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard"),
      dashboardEnv,
    );
    expect(anonymous.status).toBe(303);
    expect(anonymous.headers.get("Location")).toBe("/login");

    const rejected = await worker.fetch(
      new IncomingRequest("https://stats.example/login", {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({ passcode: "wrong-passcode" }),
      }),
      dashboardEnv,
    );
    expect(rejected.status).toBe(401);
    expect(rejected.headers.get("Set-Cookie")).toBeNull();

    const forged = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard", {
        headers: { Cookie: "__Host-pitmedic_admin=forged.session" },
      }),
      dashboardEnv,
    );
    expect(forged.status).toBe(303);
    expect(forged.headers.get("Location")).toBe("/login");
  });

  it("logs out by expiring the private session cookie", async () => {
    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/logout", {
        method: "POST",
        headers: { Cookie: cookie },
      }),
      dashboardEnv,
    );
    expect(response.status).toBe(303);
    expect(response.headers.get("Location")).toBe("/login");
    expect(response.headers.get("Set-Cookie")).toContain("Max-Age=0");
  });
});
