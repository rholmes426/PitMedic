import { describe, expect, it, vi } from "vitest";
import { detailParams, detailUrl, renderMetricDetails } from "../src/metric-details";
import { loadSearchConsoleData } from "../src/search-console";

describe("metric drilldowns and search freshness",()=>{
  it("rejects malformed and excessive ranges",()=>{
    for(const query of ['metric=views&start=2026-02-30&end=2026-03-01','metric=views&start=2026-09-09&end=2026-09-01','metric=views&start=2020-01-01&end=2026-09-09','metric=secret&start=2026-09-09&end=2026-09-09']) {
      expect(()=>detailParams(new URL(`https://stats.example/website/details?${query}`))).toThrow();
    }
  });
  it("keeps day, page and source filters and escapes stored labels",async()=>{
    const fetcher=vi.spyOn(globalThis,'fetch').mockImplementation(async(input,init)=>{
      const url=new URL(String(input));
      expect(url.pathname).toBe('/v1/website-details');
      expect(url.searchParams.get('start')).toBe('2026-09-09');
      expect(url.searchParams.get('end')).toBe('2026-09-09');
      expect(url.searchParams.get('page')).toBe('/guide/');
      expect(new Headers(init?.headers).get('Authorization')).toBe('Basic test');
      return Response.json({protocol:1,data:{count:2,pageViews:2,breakdowns:[{dimension:'page',label:'/guide/',secondary:'',count:2}],rows:[{path:'/guide/',source:'<script>alert(1)</script>',traffic_type:'referral',device_type:'desktop',country:'ZZ',target:'',count:2}],journeys:[]}});
    });
    const html=await renderMetricDetails(new URL('https://stats.example'+detailUrl('views','2026-09-09','2026-09-09',{page:'/guide/'})),{NEON_ANALYTICS_URL:'https://neon.example',NEON_ANALYTICS_CREDENTIALS:'test'},new Date('2026-09-09T23:00:00Z'),'');
    expect(html).toContain('2026-09-09 to 2026-09-09');
    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>');
    expect(html).toContain('source=%3Cscript%3E');
    expect(html).toContain('Exact external referring-page URLs are not collected');
    fetcher.mockRestore();
  });
  it("requests yesterday in Pacific time and separates preliminary, pending, and finalized zero days",async()=>{
    const pair=await crypto.subtle.generateKey({name:'RSASSA-PKCS1-v1_5',modulusLength:2048,publicExponent:new Uint8Array([1,0,1]),hash:'SHA-256'},true,['sign','verify']) as CryptoKeyPair;
    const bytes=new Uint8Array(await crypto.subtle.exportKey('pkcs8',pair.privateKey) as ArrayBuffer);
    const key=`-----BEGIN PRIVATE KEY-----\n${btoa(String.fromCharCode(...bytes))}\n-----END PRIVATE KEY-----`;
    const bodies:Record<string,unknown>[]=[];
    const fetcher=vi.spyOn(globalThis,'fetch').mockImplementation(async(input,init)=>{
      if(String(input).includes('oauth2')) return Response.json({access_token:'test-only',expires_in:3600});
      const body=JSON.parse(String(init?.body));bodies.push(body);
      if(body.dimensions?.[0]==='date') return Response.json({metadata:{first_incomplete_date:'2026-09-07'},rows:[{keys:['2026-09-06'],clicks:1,impressions:71,ctr:1/71,position:8.2},{keys:['2026-09-08'],clicks:2,impressions:80,ctr:2/80,position:8}]});
      return Response.json({rows:[]});
    });
    // UTC has rolled to Sept 10, but it is still Sept 9 in California.
    const result=await loadSearchConsoleData({GOOGLE_SERVICE_ACCOUNT_JSON:JSON.stringify({client_email:'test@project.iam.gserviceaccount.com',private_key:key}),SEARCH_CONSOLE_PROPERTY:'https://pitmedic.com/'},new Date('2026-09-10T01:00:00Z'));
    expect(result.available).toBe(true);
    expect(result.periodEnd).toBe('2026-09-08');
    expect(result.daily.find(r=>r.date==='2026-09-08')).toMatchObject({status:'preliminary',clicks:2,impressions:80});
    expect(result.daily.find(r=>r.date==='2026-09-07')).toMatchObject({status:'pending'});
    expect(result.daily.find(r=>r.date==='2026-09-05')).toMatchObject({status:'final',clicks:0,impressions:0});
    expect(bodies.some(b=>b.endDate==='2026-09-08'&&b.dataState==='all')).toBe(true);
    fetcher.mockRestore();
  });
});
