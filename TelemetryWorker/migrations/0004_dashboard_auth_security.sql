CREATE TABLE IF NOT EXISTS dashboard_login_failures (
  client_key TEXT NOT NULL,
  attempted_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_dashboard_login_failures_client_time
  ON dashboard_login_failures (client_key, attempted_at);
