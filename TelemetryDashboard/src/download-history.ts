import { loadGitHubDownloadData, type GitHubDownloadData, type DownloadAsset } from "./github-downloads";

export type DownloadDay = {
  day: string;
  downloads: number | null;
  total: number | null;
  status: "recorded" | "partial" | "baseline" | "missing";
  verifiedAt?: string;
};
export type DownloadHistory = { available: boolean; daily: DownloadDay[]; trackingSince?: string };
type SnapshotRow = { day: string; verified_at: string; assets_json: string; total_downloads: number };
const DAY_MS = 86400_000;
const BOUNDARY_TOLERANCE_MS = 90 * 60_000;

export async function saveDownloadObservation(db: D1Database, data: GitHubDownloadData): Promise<void> {
  if (!data.available || data.stale || !data.verifiedAt || !data.assets) return;
  // Immutable, timestamp-keyed samples make duplicate/cross-region reads safe.
  // All deltas are calculated in observation order, never by request arrival.
  await db.prepare(`INSERT OR IGNORE INTO github_download_snapshots
    (verified_at, day, assets_json, total_downloads) VALUES (?, ?, ?, ?)`)
    .bind(data.verifiedAt, data.verifiedAt.slice(0, 10), JSON.stringify(data.assets), data.totalDownloads).run();
}

export async function loadDownloadHistory(db: D1Database, now = new Date()): Promise<DownloadHistory> {
  const end = now.toISOString().slice(0, 10);
  const start = new Date(Date.parse(`${end}T00:00:00Z`) - 30 * DAY_MS).toISOString().slice(0, 10);
  try {
    const [rows, first] = await db.batch<SnapshotRow>([
      db.prepare("SELECT * FROM github_download_snapshots WHERE day >= ? AND day <= ? ORDER BY verified_at").bind(start, end),
      db.prepare("SELECT day FROM github_download_snapshots ORDER BY verified_at LIMIT 1"),
    ]);
    return { available: true, daily: buildDownloadDays(rows!.results, now), trackingSince: first?.results[0]?.day };
  } catch {
    console.warn(JSON.stringify({ event: "github_download_history_unavailable" }));
    return { available: false, daily: [] };
  }
}

export async function loadTrackedDownloads(
  db: D1Database, now = new Date(), fetcher: typeof fetch = fetch,
): Promise<{ downloads: GitHubDownloadData; history: DownloadHistory }> {
  let downloads = await loadGitHubDownloadData(fetcher, caches.default, now);
  try { await saveDownloadObservation(db, downloads); }
  catch { console.warn(JSON.stringify({ event: "github_download_snapshot_failed" })); }
  // The edge cache can be evicted. A durable sample still supplies an honest,
  // dated cumulative total during an upstream outage.
  if (!downloads.available) {
    try {
      const last = await db.prepare("SELECT * FROM github_download_snapshots ORDER BY verified_at DESC LIMIT 1").first<SnapshotRow>();
      if (last) downloads = { available: true, totalDownloads: last.total_downloads, verifiedAt: last.verified_at, stale: true };
    } catch { /* History availability is reported below. */ }
  }
  return { downloads, history: await loadDownloadHistory(db, now) };
}

export function buildDownloadDays(rows: SnapshotRow[], now: Date): DownloadDay[] {
  const byDay = new Map<string, DownloadDay>();
  const highWater = new Map<number, number>();
  let previous: SnapshotRow | undefined;
  for (const row of [...rows].sort((a, b) => a.verified_at.localeCompare(b.verified_at))) {
    if (Date.parse(row.verified_at) > now.getTime()) continue;
    const files = JSON.parse(row.assets_json) as DownloadAsset[];
    const midnight = Date.parse(`${row.day}T00:00:00Z`);
    const closeToBoundary = !!previous && midnight - Date.parse(previous.verified_at) <= BOUNDARY_TOLERANCE_MS;
    const sameDay = previous?.day === row.day;
    const comparable = !!previous && (sameDay || closeToBoundary);
    const current = byDay.get(row.day) ?? { day: row.day, downloads: null, total: null,
      status: closeToBoundary ? "recorded" : "baseline" };
    let delta = 0;
    let changed = previous ? (JSON.parse(previous.assets_json) as DownloadAsset[]).some(asset => !files.some(file => file.id === asset.id)) : false;
    for (const asset of files) {
      const old = highWater.get(asset.id);
      if (old !== undefined) {
        if (asset.count < old) changed = true;
        else if (comparable) delta += asset.count - old;
      } else if (comparable && asset.createdAt && asset.createdAt >= previous!.verified_at) delta += asset.count;
      else if (previous) changed = true;
      highWater.set(asset.id, Math.max(old ?? 0, asset.count));
    }
    if (comparable) {
      current.downloads = (current.downloads ?? 0) + delta;
      if (current.status === "baseline" || changed) current.status = "partial";
    } else if (changed) current.status = "partial";
    current.total = row.total_downloads;
    current.verifiedAt = row.verified_at;
    byDay.set(row.day, current);
    previous = row;
  }
  const today = now.toISOString().slice(0, 10);
  const midnight = Date.parse(`${today}T00:00:00Z`);
  return Array.from({ length: 30 }, (_, index) => {
    const dayStart = midnight + (index - 29) * DAY_MS;
    const day = new Date(dayStart).toISOString().slice(0, 10);
    const row = byDay.get(day);
    if (!row) return { day, downloads: null, total: null, status: "missing" };
    if (row.status === "recorded" && (day === today || Date.parse(row.verifiedAt!) < dayStart + DAY_MS - BOUNDARY_TOLERANCE_MS)) row.status = "partial";
    return row;
  });
}
