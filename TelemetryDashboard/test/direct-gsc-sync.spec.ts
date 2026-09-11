import { afterEach, describe, expect, it, vi } from "vitest";
import { SEO_COHORT_URLS, syncSettledSearchConsole } from "../src/direct-gsc-sync";

afterEach(() => vi.restoreAllMocks());

describe("free direct Search Console sync", () => {
  it("stops before Google's incomplete date and sends only daily aggregates", async () => {
    const pair = await crypto.subtle.generateKey(
      { name: "RSASSA-PKCS1-v1_5", modulusLength: 2048, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
      true,
      ["sign", "verify"]
    ) as CryptoKeyPair;
    const bytes = new Uint8Array(await crypto.subtle.exportKey("pkcs8", pair.privateKey));
    const key = "-----BEGIN PRIVATE KEY-----\n" + btoa(String.fromCharCode(...bytes)) + "\n-----END PRIVATE KEY-----";
    let payload: any;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (input, init) => {
      const url = String(input);
      if (url.includes("oauth2.googleapis.com")) {
        return Response.json({ access_token: "direct-test-token", expires_in: 3600 });
      }
      if (url.includes("urlInspection")) {
        return Response.json({ inspectionResult: { indexStatusResult: {
          verdict: "PASS", coverageState: "Submitted and indexed",
          indexingState: "INDEXING_ALLOWED", lastCrawlTime: "2026-09-08T12:00:00Z"
        } } });
      }
      if (url.includes("searchAnalytics/query")) {
        const body = JSON.parse(String(init?.body));
        if (body.dataState === "all") {
          return Response.json({ metadata: { first_incomplete_date: "2026-09-10" }, rows: [] });
        }
        const dimensions = (body.dimensions ?? []).join(",");
        const totals = [
          { keys: ["2026-09-02"], clicks: 0, impressions: 10, position: 8 },
          { keys: ["2026-09-09"], clicks: 1, impressions: 20, position: 9 }
        ];
        if (dimensions === "date") return Response.json({ rows: totals });
        if (dimensions === "date,page") return Response.json({ rows: totals.map((row) => ({
          ...row, keys: [row.keys[0], SEO_COHORT_URLS[0]]
        })) });
        if (dimensions === "date,query") return Response.json({ rows: [
          { keys: ["2026-09-09", "pitmedic"], clicks: 1, impressions: 5, position: 2 }
        ] });
        if (dimensions === "date,country") return Response.json({ rows: totals.map((row) => ({
          ...row, keys: [row.keys[0], "usa"]
        })) });
        if (dimensions === "date,device") return Response.json({ rows: totals.map((row) => ({
          ...row, keys: [row.keys[0], "DESKTOP"]
        })) });
        if (dimensions === "date,page,query") return Response.json({ rows: [
          { keys: ["2026-09-09", SEO_COHORT_URLS[0], "pitmedic"], clicks: 1, impressions: 5, position: 2 }
        ] });
      }
      if (url === "https://neon.example/v1/gsc-sync") {
        expect(new Headers(init?.headers).get("Authorization")).toBe("Basic dGVzdDp0ZXN0");
        payload = JSON.parse(String(init?.body));
        return Response.json({ protocol: 1, success: true, mismatches: 0 });
      }
      return new Response("unexpected request", { status: 500 });
    });

    const result = await syncSettledSearchConsole({
      GOOGLE_SERVICE_ACCOUNT_JSON: JSON.stringify({
        client_email: "test@project.iam.gserviceaccount.com", private_key: key
      }),
      SEARCH_CONSOLE_PROPERTY: "https://pitmedic.com/",
      NEON_ANALYTICS_URL: "https://neon.example",
      NEON_ANALYTICS_CREDENTIALS: "dGVzdDp0ZXN0"
    }, new Date("2026-09-11T17:00:00Z"));

    expect(result.settledThrough).toBe("2026-09-09");
    expect(payload.source).toBe("google-search-console-api");
    expect(payload.firstIncompleteDate).toBe("2026-09-10");
    expect(payload.rows.filter((row: any) => row.dimensionType === "site")).toHaveLength(8);
    expect(payload.rows.find((row: any) => row.day === "2026-09-08" && row.dimensionType === "site"))
      .toMatchObject({ clicks: 0, impressions: 0, position: 0, dimensionValue: "all" });
    expect(payload.rows[0]).not.toHaveProperty("ctr");
    expect(payload.cohort.indexedPages).toBe(9);
  });
});
