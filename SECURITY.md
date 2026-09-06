# Security policy

PitMedic monitors local diagnostic data, uses a separate read-only helper for protected CPU telemetry, and can perform elevated, user-approved repairs through a different one-shot helper. Security reports should not include passwords, access tokens, full diagnostic archives, or personal data unless specifically requested through a private channel.

## Reporting a vulnerability

Please report suspected security vulnerabilities privately to **robert@pitmedic.com** with `PitMedic security` in the subject line. Include a concise description, affected version, reproduction conditions, and the potential impact. Do not publish exploit details, proof-of-concept code, sensitive logs, credentials, or personal information in a public GitHub issue.

If you need to share a sensitive diagnostic artifact, first describe what it contains and wait for instructions about the minimum data needed.

## Official releases

Official releases are distributed only from the PitMedic repository and pitmedic.com, include a timestamped Authenticode signature, and publish SHA-256 checksums. Unsigned development builds are not official releases.
