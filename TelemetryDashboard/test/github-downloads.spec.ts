import { describe, expect, it, vi } from "vitest";
import { loadGitHubDownloadData, downloadDescription } from "../src/github-downloads";

function memoryCache() {
  let stored: Response | undefined;
  return {
    match: vi.fn(async () => stored?.clone()),
    put: vi.fn(async (_key: RequestInfo | URL, response: Response) => { stored = response.clone(); }),
  };
}
const releases = (count: number) => Response.json([{ assets: [{ name: 'PitMedic-Setup-x64.exe', download_count: count }] }]);
const at = (minutes: number) => new Date(Date.parse('2026-09-09T23:00:00Z') + minutes * 60000);

describe('GitHub download availability', () => {
  it('shares a verified total, keeps it during throttling, respects reset and refreshes afterward', async () => {
    const cache = memoryCache();
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(releases(61))
      .mockResolvedValueOnce(new Response('rate limited', { status: 403, headers: { 'X-RateLimit-Remaining': '0', 'X-RateLimit-Reset': String(at(60).getTime()/1000) } }))
      .mockResolvedValueOnce(releases(62));
    const fresh = await loadGitHubDownloadData(fetcher, cache, at(0));
    expect(fresh).toMatchObject({ available: true, totalDownloads: 61, stale: false });
    await loadGitHubDownloadData(fetcher, cache, at(1));
    expect(fetcher).toHaveBeenCalledTimes(1);
    const stale = await loadGitHubDownloadData(fetcher, cache, at(16));
    expect(stale).toMatchObject({ available: true, totalDownloads: 61, stale: true, verifiedAt: fresh.verifiedAt });
    expect(downloadDescription(stale)).toContain('Cached total · last verified');
    await loadGitHubDownloadData(fetcher, cache, at(30));
    expect(fetcher).toHaveBeenCalledTimes(2);
    expect(await loadGitHubDownloadData(fetcher, cache, at(61))).toMatchObject({ totalDownloads: 62, stale: false });
  });

  it('retains the last successful count on network and malformed-response failures', async () => {
    const cache = memoryCache();
    const fetcher = vi.fn<typeof fetch>().mockResolvedValueOnce(releases(61))
      .mockRejectedValueOnce(new TypeError('network unavailable'))
      .mockResolvedValueOnce(Response.json({ error: 'unexpected' }));
    await loadGitHubDownloadData(fetcher, cache, at(0));
    expect(await loadGitHubDownloadData(fetcher, cache, at(16))).toMatchObject({ available: true, totalDownloads: 61, stale: true });
    expect(await loadGitHubDownloadData(fetcher, cache, at(22))).toMatchObject({ available: true, totalDownloads: 61, stale: true });
  });

  it('does not invent a count on a cold-cache failure and pauses repeated requests', async () => {
    const cache = memoryCache();
    const fetcher = vi.fn<typeof fetch>().mockResolvedValue(new Response('unavailable', { status: 503 }));
    expect(await loadGitHubDownloadData(fetcher, cache, at(0))).toMatchObject({ available: false });
    expect(await loadGitHubDownloadData(fetcher, cache, at(1))).toMatchObject({ available: false });
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
});
