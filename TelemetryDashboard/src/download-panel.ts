import type { DownloadDay, DownloadHistory } from "./download-history";

export function renderDownloadPanel(history?: DownloadHistory): string {
  if (!history?.available) return `<section class="panel download-panel" id="github-downloads"><h2>Daily GitHub downloads</h2><p class="data-note">Download history is temporarily unavailable. The cumulative count above is shown separately.</p></section>`;
  const observed = history.daily.filter(row => row.downloads !== null);
  const sum = (days: number) => {
    const rows = history.daily.slice(-days).filter(row => row.downloads !== null);
    return rows.length ? number(rows.reduce((total, row) => total + row.downloads!, 0)) : "—";
  };
  const max = Math.max(1, ...observed.map(row => row.downloads!));
  return `<section class="panel download-panel" id="github-downloads" aria-label="Daily GitHub downloads">
    <div class="panel-head"><div><span class="eyebrow">GITHUB RELEASES · LAST 30 UTC DAYS</span><h2>Daily GitHub downloads</h2></div><a class="panel-link" href="/app/downloads.csv">Export CSV</a></div>
    <div class="search-metrics download-summary"><div><span>Today · so far</span><strong>${sum(1)}</strong></div><div><span>7-day observed total</span><strong>${sum(7)}</strong></div><div><span>30-day observed total</span><strong>${sum(30)}</strong></div></div>
    <p class="data-note">Hourly snapshots of installer and portable ZIP counters, including repeat downloads. Increases are assigned to the UTC day they are observed; downloads near midnight may fall on the next day. ${history.trackingSince ? `Tracking began ${history.trackingSince}. ` : ""}Earlier daily history is unavailable.</p>
    <div class="download-bars" aria-label="Last 14 days of observed downloads">${history.daily.slice(-14).map(row => `<div class="download-bar" title="${row.day}: ${row.downloads === null ? status(row) : `${number(row.downloads)} observed · ${status(row)}`}"><span class="download-value">${row.downloads === null ? "—" : number(row.downloads)}</span><div class="download-track"><i style="height:${row.downloads === null ? 0 : Math.max(row.downloads > 0 ? 3 : 0, row.downloads / max * 100)}%"></i></div><span>${row.day.slice(5).replace("-", "/")}</span></div>`).join("")}</div>
    <div class="table-wrap bounded-table" tabindex="0" role="region" aria-label="Daily download counts"><table><thead><tr><th scope="col">Date (UTC)</th><th scope="col" class="number">Downloads observed</th><th scope="col" class="number">GitHub total</th><th scope="col">Coverage</th></tr></thead><tbody>${[...history.daily].reverse().map(row => `<tr><td>${row.day}</td><td class="number">${row.downloads === null ? "—" : number(row.downloads)}</td><td class="number">${row.total === null ? "—" : number(row.total)}</td><td><span class="tag${row.status !== "recorded" ? " subdued" : ""}">${status(row)}</span></td></tr>`).join("")}</tbody></table></div>
    <p class="data-note">“Partial” covers today, the first tracking day, a collection gap, or changed release assets. Totals include only observed increases; missing days are never counted as zero. GitHub totals reflect currently available release files and can decrease when files are removed.</p>
  </section>`;
}

export function downloadHistoryCsv(history: DownloadHistory): string {
  return ["date_utc,downloads_observed,github_total,coverage,last_verified_utc", ...[...history.daily].reverse().map(row =>
    [row.day, row.downloads ?? "", row.total ?? "", row.status, row.verifiedAt ?? ""].join(","))].join("\r\n") + "\r\n";
}

function status(row: DownloadDay): string {
  return ({ recorded: "Recorded", partial: "Partial", baseline: "Baseline captured", missing: "Not recorded" })[row.status];
}
function number(value: number): string { return new Intl.NumberFormat("en-US").format(value); }
