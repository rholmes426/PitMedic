import { validatePayload } from "./usage";

export function validateFirstLaunch(value: unknown, now = new Date()): value is {
  protocol: 1; eventToken: string; day: string; appVersion: string;
  channel: "stable" | "preview"; installType: "installer" | "portable";
} {
  if (!value || typeof value !== "object" || Array.isArray(value)) return false;
  const v = value as Record<string, unknown>;
  const fields = ["protocol", "eventToken", "day", "appVersion", "channel", "installType"];
  if (Object.keys(v).length !== fields.length || Object.keys(v).some(k => !fields.includes(k))) return false;
  if (typeof v.day !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(v.day)) return false;
  const day = new Date(v.day + "T00:00:00Z");
  if (!Number.isFinite(day.getTime()) || day.toISOString().slice(0, 10) !== v.day) return false;
  const today = new Date(now.toISOString().slice(0, 10) + "T00:00:00Z").getTime();
  const age = today - day.getTime();
  if (age < 0 || age > 90 * 86400000) return false;
  return validatePayload({ protocol: v.protocol, dailyToken: v.eventToken, monthlyToken: v.eventToken,
    appVersion: v.appVersion, channel: v.channel, installType: v.installType }).ok;
}
