Fixes an iRacing repair mismatch after a simulator fault followed by a launcher connection reset. Capture, saved findings and the elevated helper now use the same evidence-based repair selection; the launcher logger name alone no longer triggers a UI-cache repair.

Older iRacing findings are reassessed before repair, including clearing unsupported stale recommendations. Independent helper validation, narrow allowlists, backups and required approval remain in place.

Includes compatible maintenance dependencies and build actions. Vitest 5 remains deferred because the Cloudflare test plugin requires Vitest 4. The website also has clearer search titles, headings and direct troubleshooting-guide links.
