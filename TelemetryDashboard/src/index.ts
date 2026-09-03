import {
  dashboardStyles,
  loadDashboardData,
  renderDashboard,
} from "./usage-dashboard";
import {
  loadWebsiteDashboardData,
  renderWebsiteDashboard,
} from "./website-dashboard";
import {
  loadSearchConsoleData,
  type SearchConsoleEnv,
} from "./search-console";
import {
  authHeaders,
  handleLogin,
  handleLogout,
  isAuthenticated,
  type DashboardAuthEnv,
} from "./auth";

const DASHBOARD_PATH = "/dashboard";
const WEBSITE_PATH = "/website";
export type DashboardEnv = Env & DashboardAuthEnv & SearchConsoleEnv;

export default {
  async fetch(request, env): Promise<Response> {
    const url = new URL(request.url);

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

    if (url.pathname !== DASHBOARD_PATH && url.pathname !== WEBSITE_PATH) {
      return new Response("Not found", {
        status: 404,
        headers: securityHeaders(),
      });
    }

    try {
      const generatedAt = new Date();
      const html = url.pathname === WEBSITE_PATH
        ? renderWebsiteDashboard(
            await loadWebsiteDashboardData(env.DB, generatedAt),
            await loadSearchConsoleData(env, generatedAt),
            generatedAt,
            dashboardStyles,
          )
        : renderDashboard(await loadDashboardData(env.DB, generatedAt), generatedAt);
      return new Response(html, {
        status: 200,
        headers: securityHeaders({
          "Content-Type": "text/html; charset=utf-8",
        }),
      });
    } catch (error) {
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
} satisfies ExportedHandler<DashboardEnv>;

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
