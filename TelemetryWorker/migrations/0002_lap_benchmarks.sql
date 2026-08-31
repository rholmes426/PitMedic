CREATE TABLE IF NOT EXISTS official_lap_benchmarks (
  combination_key TEXT PRIMARY KEY,
  sim TEXT NOT NULL,
  track TEXT NOT NULL,
  layout TEXT NOT NULL DEFAULT '',
  car TEXT NOT NULL,
  lap_seconds REAL NOT NULL CHECK (lap_seconds >= 20 AND lap_seconds <= 1800),
  source_name TEXT NOT NULL,
  source_url TEXT NOT NULL,
  verified_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS lap_benchmark_cache (
  combination_key TEXT PRIMARY KEY,
  available INTEGER NOT NULL CHECK (available IN (0, 1)),
  lap_seconds REAL,
  source_kind TEXT NOT NULL,
  source_name TEXT NOT NULL,
  source_url TEXT,
  confidence TEXT NOT NULL,
  checked_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_lap_benchmark_cache_expiry
  ON lap_benchmark_cache(expires_at);
