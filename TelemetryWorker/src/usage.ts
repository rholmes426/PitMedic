const TOKEN_PATTERN = /^[a-f0-9]{64}$/;
const VERSION_PATTERN = /^\d+\.\d+\.\d+\.\d+(?:-[a-z0-9.-]+)?$/i;
const ALLOWED_FIELDS = new Set([
  "protocol",
  "dailyToken",
  "monthlyToken",
  "appVersion",
  "channel",
  "installType",
]);

export type UsagePayload = {
  protocol: 1;
  dailyToken: string;
  monthlyToken: string;
  appVersion: string;
  channel: "preview" | "stable";
  installType: "installer" | "portable";
};

export type ValidationResult =
  | { ok: true; value: UsagePayload }
  | { ok: false; reason: string };

export function validatePayload(candidate: unknown): ValidationResult {
  if (!isObject(candidate))
    return { ok: false, reason: "payload_must_be_an_object" };

  const fields = Object.keys(candidate);
  if (fields.some((field) => !ALLOWED_FIELDS.has(field))) {
    return { ok: false, reason: "unexpected_field" };
  }
  if (fields.length !== ALLOWED_FIELDS.size) {
    return { ok: false, reason: "missing_field" };
  }
  if (candidate.protocol !== 1)
    return { ok: false, reason: "unsupported_protocol" };
  if (!isMatchingString(candidate.dailyToken, TOKEN_PATTERN, 64)) {
    return { ok: false, reason: "invalid_daily_token" };
  }
  if (!isMatchingString(candidate.monthlyToken, TOKEN_PATTERN, 64)) {
    return { ok: false, reason: "invalid_monthly_token" };
  }
  if (!isMatchingString(candidate.appVersion, VERSION_PATTERN, 32)) {
    return { ok: false, reason: "invalid_app_version" };
  }
  if (candidate.channel !== "preview" && candidate.channel !== "stable") {
    return { ok: false, reason: "invalid_channel" };
  }
  if (
    candidate.installType !== "installer" &&
    candidate.installType !== "portable"
  ) {
    return { ok: false, reason: "invalid_install_type" };
  }

  return { ok: true, value: candidate as UsagePayload };
}

export async function rollUpExpiredTokens(
  db: D1Database,
  now = new Date(),
): Promise<void> {
  const currentDay = now.toISOString().slice(0, 10);
  const currentMonth = currentDay.slice(0, 7);

  await db.batch([
    db
      .prepare(
        `
      INSERT OR REPLACE INTO daily_rollup
        (day, app_version, channel, install_type, active_installations)
      SELECT day, app_version, channel, install_type, COUNT(*)
      FROM daily_active
      WHERE day < ?
      GROUP BY day, app_version, channel, install_type
    `,
      )
      .bind(currentDay),
    db.prepare("DELETE FROM daily_active WHERE day < ?").bind(currentDay),
    db
      .prepare(
        `
      INSERT OR REPLACE INTO monthly_rollup
        (month, app_version, channel, install_type, active_installations)
      SELECT month, app_version, channel, install_type, COUNT(*)
      FROM monthly_active
      WHERE month < ?
      GROUP BY month, app_version, channel, install_type
    `,
      )
      .bind(currentMonth),
    db.prepare("DELETE FROM monthly_active WHERE month < ?").bind(currentMonth),
  ]);
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function isMatchingString(
  value: unknown,
  pattern: RegExp,
  maxLength: number,
): value is string {
  return (
    typeof value === "string" &&
    value.length <= maxLength &&
    pattern.test(value)
  );
}
