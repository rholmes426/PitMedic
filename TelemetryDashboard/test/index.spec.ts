import { env } from "cloudflare:workers";
import { beforeEach, describe, expect, it, vi } from "vitest";
import worker, { type DashboardEnv } from "../src/index";
import { summarizeGitHubReleases } from "../src/github-downloads";
import { loadDashboardData } from "../src/usage-dashboard";
import { loadWebsiteDashboardData } from "../src/website-dashboard";

const DAILY_TOKEN = "a".repeat(64);
const MONTHLY_TOKEN = "b".repeat(64);
const IncomingRequest = Request<unknown, IncomingRequestCfProperties>;
const dashboardEnv = env as unknown as DashboardEnv;
const websiteSummary = {
  protocol: 1,
  generatedAt: new Date().toISOString(),
  data: {
    todayPageViews: 8,
    sevenDayPageViews: 8,
    thirtyDayPageViews: 8,
    downloads: 2,
    engagementRate: 62.5,
    organicEntries: 8,
    daily: [{ day: new Date().toISOString().slice(0, 10), pageViews: 8, downloads: 2 }],
    topPages: [{ path: "/diagnostic-library/iracing-helper-service/", pageViews: 8, engagedViews: 5, downloads: 2 }],
    searchLandings: [{ label: "/diagnostic-library/iracing-helper-service/", count: 8 }],
    sources: [{ label: "Google", count: 8, secondary: "search" }],
    products: [{ label: "iRacing", count: 8 }],
    countries: [{ label: "US", count: 8 }],
    devices: [{ label: "desktop", count: 8 }],
    journeys: [{ source: "/diagnostic-library/", target: "/diagnostic-library/iracing-helper-service/", count: 3 }],
  },
};

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
  vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
    const url = typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    if (url === "https://neon-analytics.example/v1/website-summary") {
      expect(new Headers(init?.headers).get("Authorization")).toBe("Basic dGVzdDp0ZXN0");
      return Response.json(websiteSummary);
    }
    return new Response("Unavailable", { status: 503 });
  });
  await env.DB.batch([
    env.DB.prepare("DELETE FROM daily_active"),
    env.DB.prepare("DELETE FROM monthly_active"),
    env.DB.prepare("DELETE FROM daily_rollup"),
    env.DB.prepare("DELETE FROM monthly_rollup"),
    env.DB.prepare("DELETE FROM web_daily_events"),
  ]);
});

describe("private aggregate dashboard", () => {
  it("protects metric details and rejects invalid filters after login", async () => {
    const url = "https://stats.example/website/details?metric=views&start=bad&end=2026-09-09";
    const unauthenticated = await worker.fetch(new IncomingRequest(url), dashboardEnv);
    expect(unauthenticated.status).toBe(303);
    const cookie = await authenticatedCookie();
    const response = await worker.fetch(new IncomingRequest(url, {headers:{Cookie:cookie}}), dashboardEnv);
    expect(response.status).toBe(400);
  });

  it("counts only downloadable PitMedic app assets from GitHub releases", () => {
    const downloads = summarizeGitHubReleases([
      {
        assets: [
          { name: "PitMedic-Setup-x64.exe", download_count: 7 },
          { name: "PitMedic-0.6.0.15-win-x64.zip", download_count: 3 },
          { name: "SHA256SUMS.txt", download_count: 40 },
          { name: "PitMedic-installer-manifest.json", download_count: 20 },
        ],
      },
      {
        assets: [
          {
            name: "PitMedic-Setup-x64-UNSIGNED-PREVIEW.exe",
            download_count: 2,
          },
          {
            name: "PitMedic-0.6.0.9-win-x64-UNSIGNED-PREVIEW.zip",
            download_count: 1,
          },
        ],
      },
    ]);

    expect(downloads.available).toBe(true);
    expect(downloads.totalDownloads).toBe(13);
  });

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
      new IncomingRequest("https://stats.example/app", {
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
    expect(html).toContain("PitMedic App Usage Analytics");
    expect(html).toContain('aria-current="page" href="/app"');
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
      new IncomingRequest("https://stats.example/app", {
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

  it("renders detailed aggregate website analytics in a separate private view", async () => {
    const data = await loadWebsiteDashboardData(dashboardEnv);
    expect(data.thirtyDayPageViews).toBe(8);
    expect(data.downloads).toBe(2);
    expect(data.engagementRate).toBe(62.5);
    expect(data.organicEntries).toBe(8);

    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/website", {
        headers: { Cookie: cookie },
      }),
      dashboardEnv,
    );
    const html = await response.text();
    expect(response.status).toBe(200);
    expect(html).toContain("Traffic and content");
    expect(html).toContain("Organic landing pages");
    expect(html).toContain("iRacing");
    expect(html).toContain("United States");
    expect(html).toContain("62.5%");
    expect(html).toContain('aria-current="page" href="/website"');
    expect(html).not.toContain("dailyToken");
    expect(html).not.toContain("User-Agent");
  });

  it("fails closed instead of rendering false zeros when Neon is unavailable", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(new Response("Unavailable", { status: 503 }));
    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/website", { headers: { Cookie: cookie } }),
      dashboardEnv,
    );
    expect(response.status).toBe(503);
    expect(await response.text()).toContain("temporarily unavailable");
  });

  it("combines app, download, website, and search summaries on the overview tab", async () => {
    const cookie = await authenticatedCookie();
    const response = await worker.fetch(
      new IncomingRequest("https://stats.example/dashboard", {
        headers: { Cookie: cookie },
      }),
      dashboardEnv,
    );
    const html = await response.text();

    expect(response.status).toBe(200);
    expect(html).toContain("PitMedic Analytics Overview");
    expect(html).toContain("Everything at a glance");
    expect(html).toContain("GitHub downloads");
    expect(html).toContain("Website views");
    expect(html).toContain("Google clicks");
    expect(html).toContain('aria-current="page" href="/dashboard"');
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
