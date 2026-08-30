CREATE TABLE daily_active (
    day TEXT NOT NULL,
    token TEXT NOT NULL,
    app_version TEXT NOT NULL,
    channel TEXT NOT NULL CHECK (channel IN ('preview', 'stable')),
    install_type TEXT NOT NULL CHECK (install_type IN ('installer', 'portable')),
    PRIMARY KEY (day, token)
) WITHOUT ROWID;

CREATE INDEX daily_active_dimensions
    ON daily_active (day, app_version, channel, install_type);

CREATE TABLE monthly_active (
    month TEXT NOT NULL,
    token TEXT NOT NULL,
    app_version TEXT NOT NULL,
    channel TEXT NOT NULL CHECK (channel IN ('preview', 'stable')),
    install_type TEXT NOT NULL CHECK (install_type IN ('installer', 'portable')),
    PRIMARY KEY (month, token)
) WITHOUT ROWID;

CREATE INDEX monthly_active_dimensions
    ON monthly_active (month, app_version, channel, install_type);

CREATE TABLE daily_rollup (
    day TEXT NOT NULL,
    app_version TEXT NOT NULL,
    channel TEXT NOT NULL,
    install_type TEXT NOT NULL,
    active_installations INTEGER NOT NULL CHECK (active_installations >= 0),
    PRIMARY KEY (day, app_version, channel, install_type)
) WITHOUT ROWID;

CREATE TABLE monthly_rollup (
    month TEXT NOT NULL,
    app_version TEXT NOT NULL,
    channel TEXT NOT NULL,
    install_type TEXT NOT NULL,
    active_installations INTEGER NOT NULL CHECK (active_installations >= 0),
    PRIMARY KEY (month, app_version, channel, install_type)
) WITHOUT ROWID;
