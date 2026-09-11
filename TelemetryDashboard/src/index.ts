import { renderMetricDetails, InvalidDetailRequest } from "./metric-details";
import {
  dashboardStyles,
  loadDashboardData,
  renderDashboard,
} from "./usage-dashboard";
import {
  loadWebsiteDashboardData,
  renderWebsiteDashboard,
  type WebsiteAnalyticsEnv,
} from "./website-dashboard";
import {
  loadSearchConsoleData,
  type SearchConsoleEnv,
} from "./search-console";
import { loadGitHubDownloadData } from "./github-downloads";
import { syncSettledSearchConsole } from "./direct-gsc-sync";
import { renderOverviewDashboard } from "./overview-dashboard";
import {
  authHeaders,
  handleLogin,
  handleLogout,
  isAuthenticated,
  type DashboardAuthEnv,
} from "./auth";

const DASHBOARD_PATH = "/dashboard";
const APP_PATH = "/app";
const WEBSITE_PATH = "/website";
const WEBSITE_DASHBOARD_STYLES = `${dashboardStyles}
.search-console .search-table th:not(:first-child){text-align:right}
.search-console .search-table td.number{font-variant-numeric:tabular-nums}
.search-console .search-table:has(th:nth-child(4):last-child){table-layout:fixed}
.search-console .search-table:has(th:nth-child(4):last-child) th:first-child{width:40%}
.search-console .search-table:has(th:nth-child(4):last-child) th:nth-child(2){width:18%}
.search-console .search-table:has(th:nth-child(4):last-child) th:nth-child(3){width:24%}
.search-console .search-table:has(th:nth-child(4):last-child) th:nth-child(4){width:18%}
`;
export type DashboardEnv = Env & DashboardAuthEnv & SearchConsoleEnv & WebsiteAnalyticsEnv;

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);

    if (url.pathname === "/internal/gsc-sync") {
      if (request.method !== "POST") {
        return new Response("Method not allowed", { status: 405, headers: securityHeaders({ Allow: "POST" }) });
      }
      if (!internalSyncAuthorized(request, env)) {
        return new Response("Unauthorized", { status: 401, headers: securityHeaders() });
      }
      try {
        return Response.json(await syncSettledSearchConsole(env), {
          headers: securityHeaders({ "Cache-Control": "no-store" }),
        });
      } catch (error) {
        console.error(JSON.stringify({
          event: "settled_gsc_sync_failed",
          errorType: error instanceof Error ? error.name : "UnknownError",
        }));
        return Response.json({ error: "settled_gsc_sync_failed" }, {
          status: 503,
          headers: securityHeaders({ "Cache-Control": "no-store" }),
        });
      }
    }

    if (url.pathname === "/login") return handleLogin(request, env);

    if (url.pathname === "/logout") {
      if (request.method !== "POST") {
        return new Response("Method not allowed", {
          status: 405,
          headers: securityHeaders({ Allow: "POST" }),
        });
      }
      return handleLogout();
    }

    if (request.method !== "GET") {
      return new Response("Method not allowed", {
        status: 405,
        headers: securityHeaders({ Allow: "GET" }),
      });
    }

    if (!(await isAuthenticated(request, env))) {
      return new Response(null, {
        status: 303,
        headers: securityHeaders({ Location: "/login" }),
      });
    }

    if (url.pathname === "/") {
      return new Response(null, {
        status: 302,
        headers: securityHeaders({ Location: DASHBOARD_PATH }),
      });
    }

    if (
      url.pathname !== DASHBOARD_PATH &&
      url.pathname !== APP_PATH &&
      url.pathname !== WEBSITE_PATH &&
      url.pathname !== "/website/details"
    ) {
      return new Response("Not found", {
        status: 404,
        headers: securityHeaders(),
      });
    }

    try {
      const generatedAt = new Date();
      let html: string;
      if (url.pathname === "/website/details") {
        html = await renderMetricDetails(url, env, generatedAt, WEBSITE_DASHBOARD_STYLES);
      } else if (url.pathname === APP_PATH) {
        html = renderDashboard(
          await loadDashboardData(env.DB, generatedAt),
          await loadGitHubDownloadData(fetch, caches.default, generatedAt),
          generatedAt,
        );
      } else if (url.pathname === WEBSITE_PATH) {
        html = renderWebsiteDashboard(
          await loadWebsiteDashboardData(env),
          await loadSearchConsoleData(env, generatedAt),
          generatedAt,
          WEBSITE_DASHBOARD_STYLES,
        );
      } else {
        const [usage, website, search, downloads] = await Promise.all([
          loadDashboardData(env.DB, generatedAt),
          loadWebsiteDashboardData(env),
          loadSearchConsoleData(env, generatedAt),
          loadGitHubDownloadData(fetch, caches.default, generatedAt),
        ]);
        html = renderOverviewDashboard(
          usage,
          website,
          search,
          downloads,
          generatedAt,
          dashboardStyles,
        );
      }
      return new Response(html, {
        status: 200,
        headers: securityHeaders({
          "Content-Type": "text/html; charset=utf-8",
        }),
      });
    } catch (error) {
      if (error instanceof InvalidDetailRequest) return new Response(error.message, {status:400,headers:securityHeaders()});
      console.error(
        JSON.stringify({
          event: "usage_dashboard_failed",
          errorType: error instanceof Error ? error.name : "UnknownError",
        }),
      );
      return new Response("The dashboard is temporarily unavailable.", {
        status: 503,
        headers: securityHeaders(),
      });
    }
  },
  async scheduled(controller, env, ctx): Promise<void> {
    const localHour = new Intl.DateTimeFormat("en-US", {
      timeZone: "America/Los_Angeles", hour: "2-digit", hourCycle: "h23",
    }).format(new Date(controller.scheduledTime));
    if (localHour !== "09") return;
    ctx.waitUntil(syncSettledSearchConsole(env, new Date(controller.scheduledTime)).then(
      (result) => console.log(JSON.stringify({ event: "settled_gsc_sync_completed", ...result })),
      (error) => console.error(JSON.stringify({
        event: "settled_gsc_sync_failed",
        errorType: error instanceof Error ? error.name : "UnknownError",
      })),
    ));
  },
} satisfies ExportedHandler<DashboardEnv>;

function internalSyncAuthorized(request: Request, env: DashboardEnv): boolean {
  const credentials = env.NEON_ANALYTICS_CREDENTIALS?.trim();
  return !!credentials && request.headers.get("Authorization") === "Basic " + credentials;
}

function securityHeaders(additional: Record<string, string> = {}): Headers {
  return new Headers({
    "Cache-Control": "no-store, max-age=0",
    "Content-Security-Policy":
      "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'self'; frame-ancestors 'none'",
    "Cross-Origin-Opener-Policy": "same-origin",
    "Permissions-Policy": "camera=(), microphone=(), geolocation=()",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "X-Robots-Tag": "noindex, nofollow, noarchive",
    ...additional,
  });
}
