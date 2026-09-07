const RELEASES_URL =
  "https://api.github.com/repos/rholmes426/PitMedic/releases?per_page=100";

export type GitHubDownloadData = {
  available: boolean;
  totalDownloads: number;
};

export async function loadGitHubDownloadData(
  fetcher: typeof fetch = fetch,
): Promise<GitHubDownloadData> {
  try {
    const response = await fetcher(RELEASES_URL, {
      headers: {
        Accept: "application/vnd.github+json",
        "User-Agent": "PitMedic-Usage-Dashboard",
        "X-GitHub-Api-Version": "2022-11-28",
      },
    });
    if (!response.ok) return unavailableDownloads();
    return summarizeGitHubReleases(await response.json());
  } catch {
    return unavailableDownloads();
  }
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
