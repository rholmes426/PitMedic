# Knowledge Scout review — October 19, 2026

The owner explicitly authorized implementation and publication to both the website
and signed app on October 19. Target release: 1.0.0.9; public baseline: 1.0.0.8.

## Evidence and implemented scope

- [SIMAGIC download center](https://simagic.com/pages/download-center), checked October 19:
  3.2.3, released October 14, fixes some launch failures and shortcuts broken after
  reinstalling. Add exact version/symptom guidance to app recovery explanations,
  references and the public companion guide. Paddle fixes involve firmware and are
  explicitly separate manual vendor procedures. Preserve earlier 3.2.2 guidance.
- [Simucube changelog](https://docs.simucube.com/TunerSoftware/changelog.html), checked
  October 19: 3.2.0, released October 7, adds output-ownership notifications and
  profile export, and documents specific Simucube 3 force-feedback corrections.
  Expand the existing Tuner guidance record; preserve 3.1.4/3.1.5 coverage. Do not
  apply True Drive process recovery to Tuner or ask users to reproduce force spikes.
- [Logitech release notes](https://support.logi.com/hc/en-gb/articles/360048967733-G-HUB-Update-Release-Notes),
  checked October 19: 2026.6.974819, released September 21, fixes G29/G920 input
  missing inside G HUB, Windows-theme tray crashes and updater resource leaks.
  Details unavailable during the previous review are now readable. Add these to
  app recovery explanations/references and the public guide, with manual update
  and symptom-specific retesting. This is a newly verified coverage gap, not a new
  October release. Generic simulator input trouble does not prove this cause.

These are vendor-documented remedies, not locally reproduced repairs. No detector,
automatic repair, permission, process allowlist, or recovery lifecycle state changes.
Companion repair verification dates are unchanged; editorial dates record the new
guidance. The guidance-only Tuner evidence date is refreshed.

## Scout maintenance

Latest report reviewed: issue #9, October 16, 27 enabled sources, eight changed
findings, no source-availability or catalog errors. The registry also retains one
disabled source. The machine-readable review log still stopped at September 14.

Reconcile shipped decisions against the October 5 review/current catalog. Add
explicit no-change decisions for known catalog links and unrelated trading spam.
Narrow discovery filters reject those routes while retaining vendor support,
release notes and actual issue threads. Existing fixes never expire or disappear
because of discovery filtering. Later source changes remain eligible for review.

MOZA's rollback/update-tool listing and Logitech's older memory-fix wording do not
establish that a PitMedic repair causes harm. VRS downloads establish no new remedy.
Fanatec 1.7.0.7 and the new EVO Nürburgring/RaceRoom profile threads remain
needs-evidence: actual content failed retrieval or returned a forum index.

## Other pending work

Include the EventLog 10.0.13 update from PR #152, whose seven CI checks passed before
this batch. Full combined Windows validation is required again for the release.
Tuner executable discovery and reliable SIMAGIC firmware-state detection remain
investigations. Analytics-only dependency/deployment work stays separate.

## Validation and publication

Local library generation/consistency, lifecycle/source validation, Scout regression
tests and release/theme tests run before submission. Required Build Validation,
protected signing/installer verification, publication PR and live Pages/updater/
download checksum verification must pass before reporting the release published.
