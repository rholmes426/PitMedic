import type pg from "pg";

// Every filter is bound as a parameter; only these aggregate columns can be selected.
export async function websiteDetails(pool: pg.Pool, url: URL): Promise<Response> {
  const q = url.searchParams;
  const start = q.get("start") || "";
  const end = q.get("end") || "";
  const validDay = (v: string) => /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().slice(0,10) === v;
  const metric = q.get("metric") || "views";
  const event = ({ views: "page_view", organic: "page_view", engaged: "engaged", downloads: "download", navigation: "internal_navigation" } as Record<string,string>)[metric];
  if (!validDay(start) || !validDay(end) || start > end || (Date.parse(end)-Date.parse(start))/86400000 > 365 || !event) {
    return Response.json({ error: "invalid_range_or_metric" }, { status: 400, headers: { "Cache-Control": "no-store" } });
  }
  const values: string[] = [start, end];
  const conditions = ["day BETWEEN $1::date AND $2::date"];
  for (const [key, column] of Object.entries({ page: "path", source: "source", device: "device_type", country: "country", product: "product", target: "target", traffic: "traffic_type" })) {
    const value = q.get(key);
    if (value !== null) {
      if (value.length > 512) return Response.json({error:"invalid_filter"},{status:400});
      values.push(value); conditions.push(`${column}=$${values.length}`);
    }
  }
  if (metric === "organic") conditions.push("traffic_type='search'");
  values.push(event);
  const eventParam = `$${values.length}`;
  // One snapshot supplies the headline and every breakdown, including the exact page/source combinations.
  const result = await pool.query(`WITH scoped AS (
    SELECT * FROM web_daily_events WHERE ${conditions.join(" AND ")}
  ), selected AS (SELECT * FROM scoped WHERE event_type=${eventParam}),
  totals AS (SELECT COALESCE(SUM(event_count),0)::bigint AS count FROM selected),
  breakdown AS (
    SELECT 'page' AS dimension, path AS label, '' AS secondary, SUM(event_count)::bigint AS count FROM selected GROUP BY path
    UNION ALL SELECT 'source', source, traffic_type, SUM(event_count)::bigint FROM selected GROUP BY source,traffic_type
    UNION ALL SELECT 'device', device_type, '', SUM(event_count)::bigint FROM selected GROUP BY device_type
    UNION ALL SELECT 'country', country, '', SUM(event_count)::bigint FROM selected GROUP BY country
    UNION ALL SELECT 'product', product, '', SUM(event_count)::bigint FROM selected GROUP BY product
    UNION ALL SELECT 'day', day::text, '', SUM(event_count)::bigint FROM selected GROUP BY day
  ), combinations AS (
    SELECT path,source,traffic_type,device_type,country,target,SUM(event_count)::bigint AS count
    FROM selected GROUP BY path,source,traffic_type,device_type,country,target
  ), journeys AS (
    SELECT path, target, SUM(event_count)::bigint AS count FROM scoped WHERE event_type='internal_navigation'
    GROUP BY path,target ORDER BY count DESC,path,target
  ) SELECT jsonb_build_object(
    'count',(SELECT count FROM totals),
    'pageViews',(SELECT COALESCE(SUM(event_count),0) FROM scoped WHERE event_type='page_view'),
    'breakdowns',COALESCE((SELECT jsonb_agg(b ORDER BY dimension,count DESC,label) FROM breakdown b),'[]'::jsonb),
    'rows',COALESCE((SELECT jsonb_agg(c ORDER BY count DESC,path,source) FROM combinations c),'[]'::jsonb),
    'journeys',COALESCE((SELECT jsonb_agg(j) FROM journeys j),'[]'::jsonb)
  ) AS data`, values);
  return Response.json({ protocol: 1, start, end, metric, timeZone: "UTC", data: result.rows[0].data }, { headers: { "Cache-Control": "no-store" } });
}
