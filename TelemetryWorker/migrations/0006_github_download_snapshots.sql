-- Public release-asset counters only. No installation or visitor identifiers.
CREATE TABLE IF NOT EXISTS github_download_snapshots (
  verified_at TEXT PRIMARY KEY,
  day TEXT NOT NULL,
  assets_json TEXT NOT NULL,
  total_downloads INTEGER NOT NULL CHECK(total_downloads >= 0)
);
CREATE INDEX IF NOT EXISTS github_download_snapshots_day ON github_download_snapshots(day);
