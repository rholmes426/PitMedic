export type DashboardAuthEnv = {
  ADMIN_PASSCODE_HASH: string;
  SESSION_SIGNING_KEY: string;
};

const SESSION_COOKIE = "__Host-pitmedic_admin";
const SESSION_LIFETIME_SECONDS = 30 * 24 * 60 * 60;
const MAX_LOGIN_BODY_BYTES = 4096;

type SessionPayload = {
  version: 1;
  expiresAt: number;
};

export async function isAuthenticated(
  request: Request,
  env: DashboardAuthEnv,
  now = new Date(),
): Promise<boolean> {
  if (!hasValidConfiguration(env)) return false;

  const session = readCookie(request.headers.get("Cookie"), SESSION_COOKIE);
  if (!session) return false;

  const separator = session.lastIndexOf(".");
  if (separator <= 0 || separator === session.length - 1) return false;

  const encodedPayload = session.slice(0, separator);
  const providedSignature = session.slice(separator + 1);
  const expectedSignature = await sign(encodedPayload, env.SESSION_SIGNING_KEY);
  if (!constantTimeEqual(providedSignature, expectedSignature)) return false;

  try {
    const payload = JSON.parse(decodeBase64Url(encodedPayload)) as Partial<SessionPayload>;
    return (
      payload.version === 1 &&
      typeof payload.expiresAt === "number" &&
      Number.isSafeInteger(payload.expiresAt) &&
      payload.expiresAt > Math.floor(now.getTime() / 1000)
    );
  } catch {
    return false;
  }
}

export async function handleLogin(
  request: Request,
  env: DashboardAuthEnv,
  now = new Date(),
): Promise<Response> {
  if (request.method === "GET") return loginPage();
  if (request.method !== "POST") {
    return new Response("Method not allowed", {
      status: 405,
      headers: authHeaders({ Allow: "GET, POST" }),
    });
  }

  if (!hasValidConfiguration(env)) {
    return loginPage("Private login is temporarily unavailable.", 503);
  }

  const contentType = request.headers.get("Content-Type") ?? "";
  if (!contentType.toLowerCase().startsWith("application/x-www-form-urlencoded")) {
    return loginPage("Please use the passcode form to sign in.", 415);
  }

  const declaredLength = Number(request.headers.get("Content-Length") ?? "0");
  if (Number.isFinite(declaredLength) && declaredLength > MAX_LOGIN_BODY_BYTES) {
    return loginPage("That sign-in request was too large.", 413);
  }

  const bodyBytes = await request.arrayBuffer();
  if (bodyBytes.byteLength > MAX_LOGIN_BODY_BYTES) {
    return loginPage("That sign-in request was too large.", 413);
  }

  const body = new TextDecoder().decode(bodyBytes);
  const passcode = new URLSearchParams(body).get("passcode") ?? "";
  const providedHash = await sha256Hex(passcode);
  if (!constantTimeEqual(providedHash, env.ADMIN_PASSCODE_HASH.toLowerCase())) {
    return loginPage("That passcode was not accepted.", 401);
  }

  const expiresAt = Math.floor(now.getTime() / 1000) + SESSION_LIFETIME_SECONDS;
  const encodedPayload = encodeBase64Url(
    JSON.stringify({ version: 1, expiresAt } satisfies SessionPayload),
  );
  const signature = await sign(encodedPayload, env.SESSION_SIGNING_KEY);

  return new Response(null, {
    status: 303,
    headers: authHeaders({
      Location: "/dashboard",
      "Set-Cookie": `${SESSION_COOKIE}=${encodedPayload}.${signature}; Path=/; Max-Age=${SESSION_LIFETIME_SECONDS}; Secure; HttpOnly; SameSite=Strict`,
    }),
  });
}

export function handleLogout(): Response {
  return new Response(null, {
    status: 303,
    headers: authHeaders({
      Location: "/login",
      "Set-Cookie": `${SESSION_COOKIE}=; Path=/; Max-Age=0; Secure; HttpOnly; SameSite=Strict`,
    }),
  });
}

export function authHeaders(additional: Record<string, string> = {}): Headers {
  return new Headers({
    "Cache-Control": "no-store, max-age=0",
    "Content-Security-Policy":
      "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'",
    "Cross-Origin-Opener-Policy": "same-origin",
    "Permissions-Policy": "camera=(), microphone=(), geolocation=()",
    "Referrer-Policy": "no-referrer",
    "X-Content-Type-Options": "nosniff",
    "X-Frame-Options": "DENY",
    "X-Robots-Tag": "noindex, nofollow, noarchive",
    ...additional,
  });
}

function loginPage(message?: string, status = 200): Response {
  const notice = message
    ? `<p class="notice" role="alert">${escapeHtml(message)}</p>`
    : "";
  const html = `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="robots" content="noindex,nofollow,noarchive">
  <title>PitMedic Private Dashboard</title>
  <style>
    :root{color-scheme:light;--navy:#0d2b49;--orange:#f47a20;--ink:#112a42;--muted:#65788b;--line:#dbe4ec;--soft:#f3f6f9}
    *{box-sizing:border-box}body{min-height:100vh;margin:0;display:grid;place-items:center;background:var(--soft);color:var(--ink);font-family:Inter,ui-sans-serif,system-ui,-apple-system,"Segoe UI",sans-serif;padding:24px}.card{width:min(100%,430px);background:#fff;border:1px solid var(--line);border-radius:18px;box-shadow:0 18px 55px rgba(17,42,66,.11);overflow:hidden}.stripe{height:5px;background:var(--orange)}main{padding:34px}.brand{font-size:23px;letter-spacing:-.6px;color:var(--navy)}.brand strong,.brand em{font-weight:850}.brand em{font-style:normal;color:var(--orange)}.private{float:right;margin-top:3px;border:1px solid var(--line);border-radius:999px;padding:5px 8px;color:var(--muted);font-size:10px;font-weight:800;letter-spacing:1px}h1{margin:30px 0 8px;color:var(--navy);font-size:27px;letter-spacing:-.6px}p{margin:0 0 22px;color:var(--muted);font-size:14px;line-height:1.5}label{display:block;margin-bottom:7px;color:var(--navy);font-size:12px;font-weight:800;text-transform:uppercase;letter-spacing:.5px}input{width:100%;height:46px;border:1px solid #bdccd9;border-radius:9px;padding:0 13px;color:var(--ink);font:inherit;outline:none}input:focus{border-color:var(--orange);box-shadow:0 0 0 3px rgba(244,122,32,.15)}button{width:100%;height:46px;margin-top:13px;border:0;border-radius:9px;background:var(--orange);color:#10263d;font:inherit;font-weight:850;cursor:pointer}.notice{margin:-6px 0 16px;padding:10px 12px;border-radius:8px;background:#fff3e9;color:#8a3f0c;font-weight:700;font-size:13px}.foot{margin-top:20px;color:#7b8b9a;font-size:11px;text-align:center}
  </style>
</head>
<body>
  <section class="card">
    <div class="stripe"></div>
    <main>
      <div><span class="brand"><strong>PIT</strong><em>MEDIC</em></span><span class="private">PRIVATE</span></div>
      <h1>Usage dashboard</h1>
      <p>Enter the private administrator passcode to view anonymous, aggregated PitMedic usage.</p>
      ${notice}
      <form method="post" action="/login">
        <label for="passcode">Administrator passcode</label>
        <input id="passcode" name="passcode" type="password" autocomplete="current-password" required autofocus>
        <button type="submit">Open dashboard</button>
      </form>
      <div class="foot">No app data is collected by this sign-in page.</div>
    </main>
  </section>
</body>
</html>`;

  return new Response(html, {
    status,
    headers: authHeaders({ "Content-Type": "text/html; charset=utf-8" }),
  });
}

function hasValidConfiguration(env: DashboardAuthEnv): boolean {
  return (
    /^[a-f0-9]{64}$/i.test(env.ADMIN_PASSCODE_HASH ?? "") &&
    typeof env.SESSION_SIGNING_KEY === "string" &&
    env.SESSION_SIGNING_KEY.length >= 32
  );
}

async function sha256Hex(value: string): Promise<string> {
  const digest = await crypto.subtle.digest(
    "SHA-256",
    new TextEncoder().encode(value),
  );
  return Array.from(new Uint8Array(digest), (byte) =>
    byte.toString(16).padStart(2, "0"),
  ).join("");
}

async function sign(value: string, signingKey: string): Promise<string> {
  const key = await crypto.subtle.importKey(
    "raw",
    new TextEncoder().encode(signingKey),
    { name: "HMAC", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign(
    "HMAC",
    key,
    new TextEncoder().encode(value),
  );
  return bytesToBase64Url(new Uint8Array(signature));
}

function encodeBase64Url(value: string): string {
  return bytesToBase64Url(new TextEncoder().encode(value));
}

function decodeBase64Url(value: string): string {
  const base64 = value.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(Math.ceil(base64.length / 4) * 4, "=");
  const binary = atob(padded);
  return new TextDecoder().decode(
    Uint8Array.from(binary, (character) => character.charCodeAt(0)),
  );
}

function bytesToBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function readCookie(cookieHeader: string | null, name: string): string | null {
  if (!cookieHeader) return null;
  for (const pair of cookieHeader.split(";")) {
    const separator = pair.indexOf("=");
    if (separator < 0) continue;
    if (pair.slice(0, separator).trim() === name) {
      return pair.slice(separator + 1).trim();
    }
  }
  return null;
}

function constantTimeEqual(left: string, right: string): boolean {
  if (left.length !== right.length) return false;
  let mismatch = 0;
  for (let index = 0; index < left.length; index += 1) {
    mismatch |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return mismatch === 0;
}

function escapeHtml(value: string): string {
  return value.replace(
    /[&<>"']/g,
    (character) =>
      ({
        "&": "&amp;",
        "<": "&lt;",
        ">": "&gt;",
        '"': "&quot;",
        "'": "&#39;",
      })[character] ?? character,
  );
}
