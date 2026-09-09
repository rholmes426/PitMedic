const RELEASES_URL =
  "https://api.github.com/repos/rholmes426/PitMedic/releases?per_page=100";
const CACHE_URL = "https://pitmedic-usage-dashboard.pitmedic-usage-telemetry.workers.dev/internal-cache/github-downloads-v1";
const FRESH_MS = 15 * 60_000;
const RETAIN_MS = 30 * 86400_000;
type DownloadCache = Pick<Cache, "match" | "put">;
type Snapshot = { totalDownloads: number | null; verifiedAt: number; retryAt: number };

export type GitHubDownloadData = {
  available: boolean;
  totalDownloads: number;
  verifiedAt?: string;
  stale?: boolean;
};

export async function loadGitHubDownloadData(
  fetcher: typeof fetch = fetch,
  cache?: DownloadCache,
  now = new Date(),
): Promise<GitHubDownloadData> {
  const timestamp = now.getTime();
  let snapshot: Snapshot | undefined;
  try {
    const stored = await cache?.match(CACHE_URL);
    const value: unknown = stored ? await stored.json() : undefined;
    if (isRecord(value) && (value.totalDownloads === null ||
      (typeof value.totalDownloads === "number" && Number.isSafeInteger(value.totalDownloads) && value.totalDownloads >= 0)) &&
      typeof value.verifiedAt === "number" && Number.isFinite(value.verifiedAt) &&
      typeof value.retryAt === "number" && Number.isFinite(value.retryAt) &&
      value.verifiedAt <= timestamp && (value.totalDownloads === null || timestamp - value.verifiedAt < RETAIN_MS)) {
      snapshot = value as Snapshot;
    }
  } catch {
    console.warn(JSON.stringify({ event: "github_download_cache_read_failed" }));
  }
  const fromSnapshot = (): GitHubDownloadData => snapshot?.totalDownloads != null ? {
    available: true, totalDownloads: snapshot.totalDownloads,
    verifiedAt: new Date(snapshot.verifiedAt).toISOString(),
    stale: timestamp - snapshot.verifiedAt >= FRESH_MS,
  } : unavailableDownloads();
  if (snapshot && (timestamp < snapshot.retryAt ||
    (snapshot.totalDownloads !== null && timestamp - snapshot.verifiedAt < FRESH_MS))) return fromSnapshot();

  let retryAt = timestamp + 5 * 60_000;
  try {
    const response = await fetcher(RELEASES_URL, {
      headers: {
        Accept: "application/vnd.github+json",
        "User-Agent": "PitMedic-Usage-Dashboard",
        "X-GitHub-Api-Version": "2022-11-28",
      },
      signal: AbortSignal.timeout(8_000),
    });
    if (response.ok) {
      const data = summarizeGitHubReleases(await response.json());
      if (data.available) {
        snapshot = { totalDownloads: data.totalDownloads, verifiedAt: timestamp, retryAt: timestamp + FRESH_MS };
        await saveSnapshot(cache, snapshot);
        return fromSnapshot();
      }
      console.warn(JSON.stringify({ event: "github_download_lookup_failed", reason: "invalid_response" }));
    } else {
      const remaining = response.headers.get("X-RateLimit-Remaining");
      const reset = Number(response.headers.get("X-RateLimit-Reset")) * 1000;
      const retryHeader = response.headers.get("Retry-After");
      const retry = retryHeader ? (/^\d+$/.test(retryHeader) ? timestamp + Number(retryHeader) * 1000 : Date.parse(retryHeader)) : 0;
      if (remaining === "0" && Number.isFinite(reset)) retryAt = Math.max(retryAt, reset);
      if (Number.isFinite(retry)) retryAt = Math.max(retryAt, retry);
      console.warn(JSON.stringify({ event: "github_download_lookup_failed", status: response.status,
        rateLimitRemaining: remaining, rateLimitReset: response.headers.get("X-RateLimit-Reset"), retryAt: new Date(retryAt).toISOString() }));
    }
  } catch (error) {
    console.warn(JSON.stringify({ event: "github_download_lookup_failed", errorType: error instanceof Error ? error.name : "UnknownError" }));
  }
  snapshot = { totalDownloads: snapshot?.totalDownloads ?? null, verifiedAt: snapshot?.verifiedAt ?? 0, retryAt };
  await saveSnapshot(cache, snapshot);
  return fromSnapshot();
}

async function saveSnapshot(cache: DownloadCache | undefined, snapshot: Snapshot): Promise<void> {
  try {
    // Only a public package-download total and refresh timestamps are cached.
    await cache?.put(CACHE_URL, Response.json(snapshot, { headers: { "Cache-Control": "public, max-age=2592000" } }));
  } catch {
    console.warn(JSON.stringify({ event: "github_download_cache_write_failed" }));
  }
}

export function downloadDescription(data: GitHubDownloadData): string {
  if (!data.available) return "GitHub count temporarily unavailable; retrying automatically";
  if (!data.verifiedAt) return "Installer and portable ZIPs · not unique users";
  const time = new Intl.DateTimeFormat("en-US", { month: "short", day: "numeric", hour: "numeric", minute: "2-digit", timeZone: "UTC" }).format(new Date(data.verifiedAt));
  return `${data.stale ? "Cached total · last verified" : "Verified"} ${time} UTC${data.stale ? " · refresh pending" : ""}`;
}

export function summarizeGitHubReleases(value: unknown): GitHubDownloadData {
  if (!Array.isArray(value)) return unavailableDownloads();

  let totalDownloads = 0;
  for (const release of value) {
    if (!isRecord(release) || !Array.isArray(release.assets)) continue;
    for (const asset of release.assets) {
      if (!isRecord(asset) || typeof asset.name !== "string") continue;
      if (!isPitMedicAppAsset(asset.name)) continue;
      if (
        typeof asset.download_count !== "number" ||
        !Number.isSafeInteger(asset.download_count) ||
        asset.download_count < 0
      )
        continue;
      totalDownloads += asset.download_count;
    }
  }

  return { available: true, totalDownloads };
}

function isPitMedicAppAsset(name: string): boolean {
  return (
    /^PitMedic-Setup-x64(?:-UNSIGNED-PREVIEW)?\.exe$/i.test(name) ||
    /^PitMedic-\d+(?:\.\d+){3}-win-x64(?:-UNSIGNED-PREVIEW)?\.zip$/i.test(
      name,
    )
  );
}

function unavailableDownloads(): GitHubDownloadData {
  return { available: false, totalDownloads: 0 };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}
