import { env } from "cloudflare:workers";
import { beforeEach, describe, expect, it } from "vitest";
import { buildDownloadDays, loadDownloadHistory, saveDownloadObservation } from "../src/download-history";
import { renderDownloadPanel, downloadHistoryCsv } from "../src/download-panel";
import type { DownloadAsset } from "../src/github-downloads";
import { loadDashboardData } from "../src/usage-dashboard";

const assets = (count: number): DownloadAsset[] => [{ id: 1, count, createdAt: "2026-08-01T00:00:00Z" }];
const save = (time: string, count: number, files = assets(count)) => saveDownloadObservation(env.DB, {
  available: true, totalDownloads: count, verifiedAt: time, assets: files,
});
beforeEach(async () => { await env.DB.prepare("DELETE FROM github_download_snapshots").run(); });

describe("durable daily download counters", () => {
  it("starts a baseline, keeps public cumulative totals, and never backfills historical zeroes", async () => {
    await save("2026-11-17T17:00:00Z", 248);
    let history = await loadDownloadHistory(env.DB, new Date("2026-11-17T17:30:00Z"));
    expect(history.trackingSince).toBe("2026-11-17");
    expect(history.daily.at(-2)).toMatchObject({ downloads: null, status: "missing" });
    expect(history.daily.at(-1)).toMatchObject({ downloads: null, total: 248, status: "baseline" });
    await save("2026-11-17T18:00:00Z", 251);
    await save("2026-11-17T18:00:00Z", 251);
    await save("2026-11-17T17:30:00Z", 249);
    history = await loadDownloadHistory(env.DB, new Date("2026-11-17T18:30:00Z"));
    expect(history.daily.at(-1)).toMatchObject({ downloads: 3, total: 251, status: "partial" });
    expect(renderDownloadPanel(history)).toContain("Tracking began 2026-11-17");
    expect(downloadHistoryCsv(history)).toContain("2026-11-17,3,251,partial,2026-11-17T18:00:00Z");
    expect(downloadHistoryCsv(history)).toContain("2026-11-16,,,missing,");
  });

  it("calculates adjacent days and flags today, collection gaps and changed assets", async () => {
    await save("2026-11-15T23:05:00Z", 100);
    await save("2026-11-16T00:05:00Z", 101);
    await save("2026-11-16T23:05:00Z", 110);
    let history = await loadDownloadHistory(env.DB, new Date("2026-11-17T12:00:00Z"));
    expect(history.daily.at(-2)).toMatchObject({ downloads: 10, status: "recorded" });
    await save("2026-11-17T12:00:00Z", 120);
    history = await loadDownloadHistory(env.DB, new Date("2026-11-17T12:30:00Z"));
    expect(history.daily.at(-1)).toMatchObject({ downloads: 10, status: "partial" });
    await save("2026-11-19T12:00:00Z", 190);
    await save("2026-11-19T13:00:00Z", 192);
    history = await loadDownloadHistory(env.DB, new Date("2026-11-19T13:30:00Z"));
    expect(history.daily.at(-2)).toMatchObject({ downloads: null, status: "missing" });
    expect(history.daily.at(-1)).toMatchObject({ downloads: 2, status: "partial" });
  });

  it("handles new uploads, deleted files and counter resets without negative counts or inflated replacements", async () => {
    await save("2026-11-16T23:05:00Z", 100);
    await save("2026-11-17T12:00:00Z", 107, [...assets(102), { id: 2, count: 5, createdAt: "2026-11-17T01:00:00Z" }]);
    let history = await loadDownloadHistory(env.DB, new Date("2026-11-17T12:30:00Z"));
    expect(history.daily.at(-1)?.downloads).toBe(7);
    await save("2026-11-17T13:00:00Z", 8, [{ id: 2, count: 8, createdAt: "2026-11-17T01:00:00Z" }]);
    history = await loadDownloadHistory(env.DB, new Date("2026-11-17T13:30:00Z"));
    expect(history.daily.at(-1)).toMatchObject({ downloads: 10, status: "partial" });
    await save("2026-11-17T14:00:00Z", 1, assets(1));
    history = await loadDownloadHistory(env.DB, new Date("2026-11-17T14:30:00Z"));
    expect(history.daily.at(-1)).toMatchObject({ downloads: 10, status: "partial" });
  });

  it("does not persist failed or stale GitHub requests as a new daily observation", async () => {
    await saveDownloadObservation(env.DB, { available: false, totalDownloads: 0 });
    await saveDownloadObservation(env.DB, { available: true, totalDownloads: 500, stale: true, verifiedAt: "2026-11-17T12:00:00Z", assets: assets(500) });
    expect((await loadDownloadHistory(env.DB, new Date("2026-11-17T12:30:00Z"))).trackingSince).toBeUndefined();
  });

  it("keeps UTC day boundaries stable over month/year rollover", () => {
    const days = buildDownloadDays([], new Date("2027-01-01T00:30:00Z"));
    expect(days).toHaveLength(30);
    expect(days.at(-2)?.day).toBe("2026-12-31");
    expect(days.at(-1)?.day).toBe("2027-01-01");
  });

  it("keeps twelve distinct months when the selected date is the 31st", async () => {
    const data = await loadDashboardData(env.DB, new Date("2026-12-31T12:00:00Z"));
    expect(new Set(data.monthly.map(row => row.period)).size).toBe(12);
    expect(data.monthly[0]?.period).toBe("2026-01");
    expect(data.monthly.at(-1)?.period).toBe("2026-12");
  });
});
