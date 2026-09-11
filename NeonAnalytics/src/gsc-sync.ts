import type pg from "pg";

const LIMIT = 8 * 1024 * 1024;
const DIMENSIONS = new Set(["site", "page", "query", "country", "device"]);

type Row = {
  day: string; dimensionType: string; dimensionValue: string;
  clicks: number; impressions: number; position: number;
};
type Payload = {
  protocol: 1; source: "google-search-console-api"; generatedAt: string;
  settledThrough: string; firstIncompleteDate: string | null;
  rows: Row[]; cohort: Record<string, unknown>;
};

export async function gscSync(pool: pg.Pool, request: Request): Promise<Response> {
  try {
    const declared = Number(request.headers.get("Content-Length") || "0");
    if (declared > LIMIT) return response({ error: "payload_too_large" }, 413);
    const raw = await request.text();
    if (new TextEncoder().encode(raw).byteLength > LIMIT) {
      return response({ error: "payload_too_large" }, 413);
    }
    const payload = validate(JSON.parse(raw) as unknown);
    reconcileSource(payload.rows);
    return response({ protocol: 1, success: true, ...(await write(pool, payload)) });
  } catch (error) {
    console.error(JSON.stringify({
      event: "gsc_sync_write_failed",
      errorType: error instanceof Error ? error.name : "UnknownError"
    }));
    return response({ error: "invalid_or_unreconciled_gsc_sync" }, 422);
  }
}

async function write(pool: pg.Pool, payload: Payload): Promise<Record<string, unknown>> {
  const client = await pool.connect();
  try {
    await client.query("BEGIN");
    const days = payload.rows.filter((row) => row.dimensionType === "site").map((row) => row.day);
    const existing = await client.query(
      "SELECT day::text,dimension_value FROM gsc_daily_metrics " +
      "WHERE dimension_type='site' AND day=ANY($1::date[]) ORDER BY day,dimension_value",
      [days]
    );
    const keys = new Map<string, string[]>();
    for (const item of existing.rows) {
      const day = String(item.day);
      keys.set(day, [...(keys.get(day) ?? []), String(item.dimension_value)]);
    }
    const legacySiteKeys: Array<{ day: string; key: string }> = [];
    for (const [day, values] of keys) {
      if (values.length > 1) throw new Error("Duplicate site totals already exist for " + day + ".");
      if (values[0] !== "all") {
        const incoming = payload.rows.find((row) => row.day === day && row.dimensionType === "site");
        if (incoming) incoming.dimensionValue = values[0];
        legacySiteKeys.push({ day, key: values[0] });
      }
    }

    const previous = await client.query(
      "SELECT value FROM analytics_metadata WHERE key='gsc_seo_expansion_2026_09_07'"
    );
    const syncedAt = new Date(payload.generatedAt);
    await client.query(
      "INSERT INTO gsc_daily_metrics " +
      "(day,dimension_type,dimension_value,clicks,impressions,ctr,position,synced_at) " +
      "SELECT * FROM unnest($1::date[],$2::text[],$3::text[],$4::bigint[],$5::bigint[]," +
      "$6::double precision[],$7::double precision[],$8::timestamptz[]) " +
      "ON CONFLICT(day,dimension_type,dimension_value) DO UPDATE SET " +
      "clicks=EXCLUDED.clicks,impressions=EXCLUDED.impressions,ctr=EXCLUDED.ctr," +
      "position=EXCLUDED.position,synced_at=EXCLUDED.synced_at",
      [
        payload.rows.map((row) => row.day),
        payload.rows.map((row) => row.dimensionType),
        payload.rows.map((row) => row.dimensionValue),
        payload.rows.map((row) => row.clicks),
        payload.rows.map((row) => row.impressions),
        payload.rows.map((row) => row.impressions ? row.clicks / row.impressions : 0),
        payload.rows.map((row) => row.position),
        payload.rows.map(() => syncedAt)
      ]
    );

    const mismatch = await client.query(
      "WITH expected AS (SELECT * FROM unnest(" +
      "$1::date[],$2::text[],$3::text[],$4::bigint[],$5::bigint[],$6::double precision[]) " +
      "AS e(day,dimension_type,dimension_value,clicks,impressions,position)) " +
      "SELECT COUNT(*)::int AS count FROM expected e LEFT JOIN gsc_daily_metrics a " +
      "USING(day,dimension_type,dimension_value) WHERE a.day IS NULL OR a.clicks<>e.clicks " +
      "OR a.impressions<>e.impressions OR ABS(a.position-e.position)>0.000000001 " +
      "OR ABS(a.ctr-(CASE WHEN e.impressions>0 THEN e.clicks::double precision/e.impressions ELSE 0 END))>0.000000001",
      [
        payload.rows.map((row) => row.day),
        payload.rows.map((row) => row.dimensionType),
        payload.rows.map((row) => row.dimensionValue),
        payload.rows.map((row) => row.clicks),
        payload.rows.map((row) => row.impressions),
        payload.rows.map((row) => row.position)
      ]
    );
    if (Number(mismatch.rows[0]?.count) !== 0) throw new Error("Stored rows did not match Google.");

    const start = payload.rows.map((row) => row.day).sort()[0];
    const counts = await client.query(
      "SELECT dimension_type,COUNT(*)::int AS rows,SUM(clicks)::bigint AS clicks," +
      "SUM(impressions)::bigint AS impressions FROM gsc_daily_metrics " +
      "WHERE day BETWEEN $1::date AND $2::date GROUP BY dimension_type ORDER BY dimension_type",
      [start, payload.settledThrough]
    );
    const validation = {
      source: payload.source,
      generatedAt: payload.generatedAt,
      settledThrough: payload.settledThrough,
      incomingRows: payload.rows.length,
      expectedCounts: Object.fromEntries([...DIMENSIONS].map((dimension) => [
        dimension, payload.rows.filter((row) => row.dimensionType === dimension).length
      ])),
      storedCounts: counts.rows.map((row) => ({
        dimension: String(row.dimension_type), rows: Number(row.rows),
        clicks: Number(row.clicks), impressions: Number(row.impressions)
      })),
      mismatches: 0,
      legacySiteKeys
    };

    if (previous.rows[0]?.value) {
      await metadata(client, "gsc_previous_seo_expansion_2026_09_07", String(previous.rows[0].value), syncedAt);
    }
    await metadata(client, "gsc_seo_expansion_2026_09_07", JSON.stringify(payload.cohort), syncedAt);
    await metadata(client, "gsc_sync_validation", JSON.stringify(validation), syncedAt);
    await metadata(client, "gsc_source", payload.source, syncedAt);
    await metadata(client, "gsc_settled_through", payload.settledThrough, syncedAt);
    await metadata(client, "gsc_last_sync", payload.generatedAt, syncedAt);
    await client.query("COMMIT");
    return validation;
  } catch (error) {
    await client.query("ROLLBACK");
    throw error;
  } finally {
    client.release();
  }
}

async function metadata(client: pg.PoolClient, key: string, value: string, at: Date): Promise<void> {
  await client.query(
    "INSERT INTO analytics_metadata(key,value,updated_at) VALUES($1,$2,$3) " +
    "ON CONFLICT(key) DO UPDATE SET value=EXCLUDED.value,updated_at=EXCLUDED.updated_at",
    [key, value, at]
  );
}

function validate(value: unknown): Payload {
  const payload = object(value);
  if (payload.protocol !== 1 || payload.source !== "google-search-console-api" ||
      !day(payload.settledThrough) || !timestamp(payload.generatedAt) ||
      !(payload.firstIncompleteDate === null || day(payload.firstIncompleteDate)) ||
      !Array.isArray(payload.rows) || !record(payload.cohort)) {
    throw new Error("Invalid sync envelope.");
  }
  const rows: Row[] = payload.rows.map((value) => {
    const row = object(value);
    if (!day(row.day) || typeof row.dimensionType !== "string" || !DIMENSIONS.has(row.dimensionType) ||
        typeof row.dimensionValue !== "string" || !row.dimensionValue || row.dimensionValue.length > 2048 ||
        !whole(row.clicks) || !whole(row.impressions) || !finite(row.position) ||
        Number(row.clicks) > Number(row.impressions)) {
      throw new Error("Invalid aggregate row.");
    }
    if (row.dimensionType === "site" && row.dimensionValue !== "all") {
      throw new Error("Incoming site totals must use all.");
    }
    return {
      day: row.day, dimensionType: row.dimensionType, dimensionValue: row.dimensionValue,
      clicks: Number(row.clicks), impressions: Number(row.impressions), position: Number(row.position)
    };
  });
  const unique = new Set(rows.map((row) => [row.day, row.dimensionType, row.dimensionValue].join("\u0000")));
  if (!rows.length || unique.size !== rows.length ||
      rows.some((row) => row.day > String(payload.settledThrough))) {
    throw new Error("Missing, duplicate, or incomplete aggregate rows.");
  }
  return {
    protocol: 1,
    source: "google-search-console-api",
    generatedAt: String(payload.generatedAt),
    settledThrough: String(payload.settledThrough),
    firstIncompleteDate: payload.firstIncompleteDate === null ? null : String(payload.firstIncompleteDate),
    rows,
    cohort: payload.cohort as Record<string, unknown>
  };
}

function reconcileSource(rows: Row[]): void {
  const grouped = new Map<string, Row[]>();
  for (const row of rows) grouped.set(row.day, [...(grouped.get(row.day) ?? []), row]);
  for (const [date, dayRows] of grouped) {
    const site = dayRows.filter((row) => row.dimensionType === "site");
    if (site.length !== 1) throw new Error("Expected one site row for " + date + ".");
    for (const dimension of ["country", "device"]) {
      const subset = dayRows.filter((row) => row.dimensionType === dimension);
      if (subset.reduce((sum, row) => sum + row.clicks, 0) !== site[0].clicks ||
          subset.reduce((sum, row) => sum + row.impressions, 0) !== site[0].impressions) {
        throw new Error(dimension + " totals do not reconcile for " + date + ".");
      }
    }
  }
}

function object(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : {};
}
function record(value: unknown): boolean {
  return !!value && typeof value === "object" && !Array.isArray(value);
}
function whole(value: unknown): boolean {
  return typeof value === "number" && Number.isSafeInteger(value) && value >= 0;
}
function finite(value: unknown): boolean {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
}
function day(value: unknown): value is string {
  return typeof value === "string" && /^\d{4}-\d{2}-\d{2}$/.test(value) &&
    new Date(value + "T12:00:00Z").toISOString().slice(0, 10) === value;
}
function timestamp(value: unknown): value is string {
  return typeof value === "string" && !Number.isNaN(Date.parse(value));
}
function response(value: unknown, status = 200): Response {
  return Response.json(value, { status, headers: { "Cache-Control": "no-store" } });
}
