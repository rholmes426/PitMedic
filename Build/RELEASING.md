# PitMedic publishing

Routine merges never tag, sign, or publish releases. CI retains complete unsigned
validation packages for testing. Artifacts on a public repository are not private;
do not put credentials or confidential data in them.

## Approved release

1. Obtain the owner's explicit approval for a signed public release.
2. Run `python Build/set-version.py X.X.X.X`, update `Build/release-notes.md`
   (the single release-note source), and synchronize shipped documentation.
   Submit the release inputs through a PR and merge after required checks.
3. Dispatch **Publish approved signed release** on main, entering the approved
   version. It validates all components, validates every project version field,
   creates an exact lightweight tag and draft, then calls protected Azure signing.
4. Signing verifies binaries and installer, retains the signed artifact, uploads
   without replacing existing files, and publishes only once uploads succeed.
5. The generated website PR is opened automatically. Its required CI is explicitly
   dispatched because events authored with GITHUB_TOKEN do not start ordinary CI.
   Review and merge through normal branch protection. No bypass or auto-approval.
6. Pages verifies the actual public homepage, updater, installer URL and downloaded
   SHA-256, with bounded propagation retries and an Actions summary.

## Setup and recovery

The built-in workflow token needs contents/pull-requests/actions write as declared
in the publication job. Repository settings must allow GitHub Actions to create
pull requests. If blocked, stop and request an authorized settings change; never
use an admin bypass or substitute credentials.

For upload/PR failures, choose **Re-run failed jobs**, preserving the successful
signed artifact. Do not rerun signing: new timestamps produce different bytes.
Already-uploaded identical assets are reused; differing assets are rejected.
An existing publication branch is reused only if regenerated files match.
Published releases cannot be replaced or augmented by this workflow.

If the publication code itself needs correction, merge its fix through a PR,
then dispatch **Resume verified signed publication** on main with the existing
release tag and original approved-release run ID. It checks the exact tagged
commit, successful signing job and retained artifact before downloading the
original bytes. It applies the corrected publication script to the tagged
release notes, then opens the usual protected website PR. It never creates a
new tag or runs signing again.

For a Pages verification failure, rerun the failed deployment after diagnosing
the mismatch; never change the checksum to conceal a failed check.

Releases are serialized with cancellation disabled. Do not queue multiple
different release versions: finish publication before approving another.
Do not change or move an existing tag.

CI uses conservative path selection: app/release/workflow changes and unknown
files run all jobs; website changes run knowledge/library checks; dashboard and
Worker changes run their own checks. Release dispatch always runs full validation.
The required Build Validation gate accepts skipped jobs only when the scope
explicitly did not require them.
