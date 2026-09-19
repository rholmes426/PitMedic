-- One-time receipts are never joined to daily/monthly activity tokens.
CREATE TABLE IF NOT EXISTS first_launch_receipts (
  event_token TEXT PRIMARY KEY,
  day TEXT NOT NULL,
  app_version TEXT NOT NULL,
  channel TEXT NOT NULL,
  install_type TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS first_launch_totals (
  day TEXT NOT NULL,
  app_version TEXT NOT NULL,
  channel TEXT NOT NULL,
  install_type TEXT NOT NULL,
  launches INTEGER NOT NULL,
  PRIMARY KEY(day, app_version, channel, install_type)
);
CREATE TRIGGER IF NOT EXISTS count_first_launch AFTER INSERT ON first_launch_receipts
BEGIN
  INSERT INTO first_launch_totals(day, app_version, channel, install_type, launches)
  VALUES(NEW.day, NEW.app_version, NEW.channel, NEW.install_type, 1)
  ON CONFLICT(day, app_version, channel, install_type) DO UPDATE SET launches = launches + 1;
END;
