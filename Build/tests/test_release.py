import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

BUILD = Path(__file__).resolve().parents[1]


def load(name, file):
    spec = importlib.util.spec_from_file_location(name, BUILD / file)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


release = load('release', 'release.py')
ci = load('ci_scope', 'ci-scope.py')
recovery = load('recovery', 'verify-signed-run.py')


class ReleaseTests(unittest.TestCase):
    def test_finds_draft_across_release_pages(self):
        draft = dict(tag_name='v0.6.0.17', draft=True, assets=[])
        with patch.object(release, 'gh', return_value=json.dumps([
                [dict(tag_name='v0.6.0.16')], [draft]])) as api:
            self.assertEqual(release.find_release('v0.6.0.17', 'owner/repo'), draft)
            self.assertEqual(api.call_args.args,
                             ('api', '--paginate', '--slurp', 'repos/owner/repo/releases'))
        with patch.object(release, 'gh', return_value='[[]]'), self.assertRaises(RuntimeError):
            release.find_release('v0.6.0.17', 'owner/repo')

    def test_recovery_requires_exact_successful_signing(self):
        run = dict(path='.github/workflows/publish-release.yml', event='workflow_dispatch',
                   head_branch='main', status='completed', head_sha='approved')
        ref = dict(object=dict(sha='approved'))
        jobs = [dict(name='sign / build-sign-verify', conclusion='success')]
        artifacts = [dict(name='PitMedic-v0.6.0.17-signed-release', expired=False)]
        recovery.validate('v0.6.0.17', run, ref, jobs, artifacts)
        for changed in [dict(head_sha='other'), dict(event='pull_request'),
                        dict(path='.github/workflows/ci.yml')]:
            with self.assertRaises(RuntimeError):
                recovery.validate('v0.6.0.17', run | changed, ref, jobs, artifacts)
        with self.assertRaises(RuntimeError):
            recovery.validate('v0.6.0.17', run, ref, [], artifacts)
        with self.assertRaises(RuntimeError):
            recovery.validate('v0.6.0.17', run, ref, jobs, [artifacts[0] | dict(expired=True)])

    def test_metadata_only_skips_windows(self):
        self.assertEqual(ci.scope(['website/update.json']), dict(build=False, knowledge=True, worker=False, dashboard=False))

    def test_unknown_and_release_inputs_force_all(self):
        for path in ['unknown/config', 'Build/release.py', '.github/workflows/ci.yml', 'Source/PitMedic/MainWindow.xaml']:
            self.assertTrue(all(ci.scope([path]).values()))

    def test_dashboard_scope(self):
        self.assertEqual(ci.scope(['TelemetryDashboard/src/index.ts']), dict(build=False, knowledge=False, worker=False, dashboard=True))

    def test_assets_are_immutable_and_retry_identical(self):
        with tempfile.TemporaryDirectory() as tmp:
            file = Path(tmp) / 'installer.exe'
            file.write_bytes(b'signed')
            assets = [dict(name=file.name, digest='sha256:' + release.digest(file))]
            release.check_assets([file], dict(draft=False, assets=assets))
            with self.assertRaises(RuntimeError):
                release.check_assets([file], dict(draft=False, assets=[]))
            assets[0]['digest'] = 'sha256:wrong'
            with self.assertRaises(RuntimeError):
                release.check_assets([file], dict(draft=True, assets=assets))

    def test_downgrade_rejected_before_writes(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / 'website').mkdir()
            manifest = root / 'website/update.json'
            manifest.write_text('{"latestVersion":"0.6.0.16"}')
            with patch.object(release, 'ROOT', root), self.assertRaises(RuntimeError):
                release.prepare('v0.6.0.15', 'owner/repo', root / 'missing.exe')
            self.assertEqual(manifest.read_text(), '{"latestVersion":"0.6.0.16"}')


if __name__ == '__main__':
    unittest.main()
