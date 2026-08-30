SELECT
    'daily' AS period_type,
    day AS period,
    app_version,
    channel,
    install_type,
    active_installations
FROM daily_rollup
UNION ALL
SELECT
    'daily' AS period_type,
    day AS period,
    app_version,
    channel,
    install_type,
    COUNT(*) AS active_installations
FROM daily_active
GROUP BY day, app_version, channel, install_type
UNION ALL
SELECT
    'monthly' AS period_type,
    month AS period,
    app_version,
    channel,
    install_type,
    active_installations
FROM monthly_rollup
UNION ALL
SELECT
    'monthly' AS period_type,
    month AS period,
    app_version,
    channel,
    install_type,
    COUNT(*) AS active_installations
FROM monthly_active
GROUP BY month, app_version, channel, install_type
ORDER BY period_type, period DESC, app_version, channel, install_type;
