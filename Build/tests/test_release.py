import importlib.util
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


class ReleaseTests(unittest.TestCase):
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
