import { type WebsiteAnalyticsEnv } from "./website-dashboard";
import { loadSearchConsoleData, type SearchConsoleEnv, type SearchMetricRow } from "./search-console";

export const esc = (v: string): string => v.replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
const num = (v: number) => new Intl.NumberFormat("en-US",{maximumFractionDigits:1}).format(v);
export function detailUrl(metric: string, start: string, end: string, filters: Record<string,string> = {}): string {
  return `/website/details?${new URLSearchParams({metric,start,end,...filters})}`;
}
export function metricLink(value: string | number, href: string): string {
  return `<a class="metric-link" href="${esc(href)}">${esc(typeof value === "number" ? num(value) : value)}</a>`;
}
export function webRange(now: Date, days = 30): {start:string;end:string} {
  const end = now.toISOString().slice(0,10);
  return {end,start:new Date(Date.parse(end)-86400000*(days-1)).toISOString().slice(0,10)};
}
export class InvalidDetailRequest extends Error {}
export function detailParams(url: URL): URLSearchParams {
  const q = url.searchParams;
  const day = (v:string|null) => !!v && /^\d{4}-\d{2}-\d{2}$/.test(v) && Number.isFinite(Date.parse(v)) && new Date(v).toISOString().slice(0,10)===v;
  const start=q.get("start"),end=q.get("end");
  if (!day(start)||!day(end)||start!>end!||Date.parse(end!)-Date.parse(start!)>365*86400000) throw new InvalidDetailRequest("Choose a valid date range of up to 366 days.");
  if (!["views","organic","engaged","downloads","navigation","google-clicks","google-impressions","google-ctr","google-position"].includes(q.get("metric")||"")) throw new InvalidDetailRequest("Choose a supported metric.");
  const clean = new URLSearchParams();
  for (const key of ["metric","start","end","page","source","device","country","product","target","traffic","query"]) {
    const value = q.get(key);
    if (value!==null) {
      if(value.length>512) throw new InvalidDetailRequest("Filter is too long.");
      if(key==='page' && (!value.startsWith('/')||value.startsWith('//')||/[?#]/.test(value))) throw new InvalidDetailRequest("Invalid page filter.");
      clean.set(key,value);
    }
  }
  return clean;
}
const labels: Record<string,string> = {views:"Page views",organic:"Organic entries",engaged:"Engagement",downloads:"Installer clicks",navigation:"Internal link clicks","google-clicks":"Google clicks","google-impressions":"Google impressions","google-ctr":"Google CTR","google-position":"Google average position"};
type Breakdown = {dimension:string;label:string;secondary:string;count:number};
type DetailRow = {path:string;source:string;traffic_type:string;device_type:string;country:string;target:string;count:number};
type DetailData = {count:number;pageViews:number;breakdowns:Breakdown[];rows:DetailRow[];journeys:{path:string;target:string;count:number}[]};

export async function renderMetricDetails(url:URL,env:WebsiteAnalyticsEnv & SearchConsoleEnv,now:Date,styles:string):Promise<string> {
  const q=detailParams(url),metric=q.get('metric')!,start=q.get('start')!,end=q.get('end')!;
  const google=metric.startsWith('google-');
  const filtered=(key:string,value:string) => {const next=new URLSearchParams(q);next.set(key,value);return `/website/details?${next}`;};
  const link=(value:string|number,key:string,label:string) => metricLink(value,filtered(key,label));
  let content:string;
  if(google) {
    const data=await loadSearchConsoleData(env,now,{start,end,page:q.get('page')||undefined,query:q.get('query')||undefined});
    if(!data.available) content=`<div class="empty">${esc(data.message)}</div>`;
    else {
      const pending=data.daily.every(r=>r.status==='pending');
      const value=metric==='google-clicks'?data.clicks:metric==='google-impressions'?data.impressions:metric==='google-ctr'?`${num(data.ctr*100)}%`:data.position?num(data.position):'—';
      const searchTable=(title:string,rows:SearchMetricRow[],dimension?:string) => `<section class="panel"><h2>${title}</h2><div class="table-wrap"><table><thead><tr><th>${dimension==='query'?'Query':'Dimension'}</th><th>Clicks</th><th>Impressions</th><th>CTR</th><th>Position</th></tr></thead><tbody>${rows.map(r=>`<tr><td>${esc(r.label)}</td><td class="number">${dimension?link(r.clicks,dimension,r.label):num(r.clicks)}</td><td class="number">${dimension?link(r.impressions,dimension,r.label):num(r.impressions)}</td><td class="number">${num(r.ctr*100)}%</td><td class="number">${r.position?num(r.position):'—'}</td></tr>`).join('')||'<tr><td colspan="5">No reported rows for this selection.</td></tr>'}</tbody></table></div></section>`;
      content=`<section class="panel"><span class="eyebrow">${esc(labels[metric] || metric)}</span><h2>${pending?'Awaiting Google data':esc(String(value))}</h2><p>${data.daily.some(r=>r.status!=='final')?'This range includes preliminary or awaiting data. Google may revise it.':'Finalized date range.'} Lower average position is better.</p><p>Google omits some queries for privacy. Page impressions can exceed site impressions when several pages appear in one search; these breakdowns may not sum to the site total.</p></section>${searchTable('Landing pages',data.pages,'page')}${searchTable('Visible search queries',data.queries,'query')}${searchTable('Countries',data.countries||[])}${searchTable('Devices',data.devices||[])}`;
    }
  } else {
    const response=await fetch(`${env.NEON_ANALYTICS_URL.replace(/\/+$/,'')}/v1/website-details?${q}`,{headers:{Authorization:`Basic ${env.NEON_ANALYTICS_CREDENTIALS.trim()}`},signal:AbortSignal.timeout(20000)});
    if(!response.ok) throw new Error('Website details are temporarily unavailable');
    const payload=await response.json() as {protocol:number;data:DetailData};
    if(payload.protocol!==1 || !payload.data || !Array.isArray(payload.data.rows)||!Array.isArray(payload.data.breakdowns)) throw new Error('Invalid website details');
    const data=payload.data;
    const breakdown=(dimension:string,title:string) => `<section class="panel"><h2>${title}</h2><div class="table-wrap"><table><thead><tr><th>${title}</th><th>Count</th><th>Share</th></tr></thead><tbody>${data.breakdowns.filter(r=>r.dimension===dimension).map(r=>`<tr><td>${esc(r.label||'Unknown')} ${r.secondary?`<small>${esc(r.secondary)}</small>`:''}</td><td class="number">${dimension==='day'?metricLink(Number(r.count),detailUrl(metric,r.label,r.label,Object.fromEntries([...q].filter(([k])=>!['metric','start','end'].includes(k))))):link(Number(r.count),dimension,r.label)}</td><td class="number">${data.count?num(Number(r.count)/Number(data.count)*100):0}%</td></tr>`).join('')||'<tr><td colspan="3">No activity for this selection.</td></tr>'}</tbody></table></div></section>`;
    content=`<section class="panel"><span class="eyebrow">${esc(labels[metric] || metric)}</span><h2>${metric==='engaged'?`${num(data.pageViews?data.count/data.pageViews*100:0)}%` :num(Number(data.count))}</h2><p>${metric==='engaged'?`${num(Number(data.count))} engaged views / ${num(Number(data.pageViews))} page views. `:''}Counts are events, not unique people.</p></section>
    <div class="web-grid two">${breakdown('page','Pages')}${breakdown('source','Referring domains and sources')}</div>
    <section class="panel"><h2>Pages and their traffic sources</h2><p>External referrals show domains. Exact external referring-page URLs are not collected.</p><div class="table-wrap"><table><thead><tr><th>Page</th><th>Source</th><th>Type</th><th>Device</th><th>Count</th></tr></thead><tbody>${data.rows.map(r=>`<tr><td>${esc(r.path)}${r.target?`<small>Target: ${esc(r.target)}</small>`:''}</td><td>${esc(r.source)}</td><td>${esc(r.traffic_type)}</td><td>${esc(r.device_type)}</td><td class="number">${metricLink(Number(r.count),detailUrl(metric,start,end,{...Object.fromEntries(q),page:r.path,source:r.source,traffic:r.traffic_type,device:r.device_type,country:r.country,target:r.target}))}</td></tr>`).join('')||'<tr><td colspan="5">No matching activity.</td></tr>'}</tbody></table></div></section>
    <div class="web-grid two">${breakdown('device','Devices')}${breakdown('country','Countries')}</div>${breakdown('day','Daily counts')}
    <section class="panel"><h2>Internal links clicked</h2><p>These are recorded link clicks during the selected range, not inferred visitor sessions.</p><div class="table-wrap"><table><thead><tr><th>From page</th><th>To page</th><th>Clicks</th></tr></thead><tbody>${data.journeys.map(r=>`<tr><td>${esc(r.path)}</td><td>${esc(r.target)}</td><td>${metricLink(Number(r.count),detailUrl('navigation',start,end,{page:r.path,target:r.target}))}</td></tr>`).join('')||'<tr><td colspan="3">No recorded internal link clicks.</td></tr>'}</tbody></table></div></section>`;
  }
  const filters=[...q].filter(([k])=>!['metric','start','end'].includes(k)).map(([k,v])=>`<span>${esc(k)}: ${esc(v)} ${metricLink('Remove',(()=>{const next=new URLSearchParams(q);next.delete(k);return `/website/details?${next}`;})())}</span>`).join(' · ');
  return `<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>${esc(labels[metric] || metric)} · PitMedic Analytics</title><style>${styles}</style></head><body><header><a class="brand" href="/dashboard"><strong>PIT</strong><em>MEDIC</em><span>ANALYTICS</span></a><nav class="dashboard-nav"><a href="/dashboard">Overview</a><a href="/app">App usage</a><a href="/website">Website &amp; search</a></nav><span class="private-pill">PRIVATE</span></header><main><section class="intro"><div><a class="panel-link" href="/website">Back to website &amp; search</a><h1>${esc(labels[metric] || metric)}</h1><p>${esc(start)} to ${esc(end)} · ${google?'Pacific time':'UTC'} daily aggregates</p><p>${filters}</p></div></section><section class="panel"><form method="get" action="/website/details">${[...q].filter(([k])=>!['start','end'].includes(k)).map(([k,v])=>`<input type="hidden" name="${esc(k)}" value="${esc(v)}">`).join('')}<label>From <input type="date" name="start" required value="${start}"></label> <label>To <input type="date" name="end" required value="${end}"></label> <button type="submit">Apply dates</button></form></section>${content}<footer>Private daily aggregates. No visitor identifiers or full external referring URLs.</footer></main></body></html>`;
}
