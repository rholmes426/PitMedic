CREATE TABLE web_daily_events (
    day TEXT NOT NULL,
    event_type TEXT NOT NULL CHECK (event_type IN ('page_view', 'engaged', 'download', 'internal_navigation')),
    path TEXT NOT NULL,
    target TEXT NOT NULL,
    section TEXT NOT NULL,
    product TEXT NOT NULL,
    traffic_type TEXT NOT NULL CHECK (traffic_type IN ('direct', 'internal', 'search', 'social', 'referral')),
    source TEXT NOT NULL,
    country TEXT NOT NULL,
    device_type TEXT NOT NULL CHECK (device_type IN ('desktop', 'mobile', 'tablet', 'unknown')),
    event_count INTEGER NOT NULL CHECK (event_count >= 0),
    PRIMARY KEY (day, event_type, path, target, section, product, traffic_type, source, country, device_type)
) WITHOUT ROWID;

CREATE INDEX web_daily_events_period_type
    ON web_daily_events (day, event_type);

CREATE INDEX web_daily_events_path
    ON web_daily_events (path, day);
