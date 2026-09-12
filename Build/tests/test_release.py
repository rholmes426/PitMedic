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

    def test_prepare_updates_static_simulator_links_and_preserves_unicode(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            sim = root / 'website/simulators/iracing/index.html'
            sim.parent.mkdir(parents=True)
            sim.write_text('Won’t launch? <a href="https://github.com/owner/repo/releases/download/'
                           'v0.6.0.15/PitMedic-Setup-x64.exe">Download v0.6.0.15</a>', encoding='utf-8')
            (root / 'website/update.json').write_text('{"latestVersion":"0.6.0.16"}')
            (root / 'website/index.html').write_text('Current 0.6.0.16 · Download v0.6.0.16', encoding='utf-8')
            stale_readme = release.readme_release(dict(latestVersion='0.6.0.12',
                downloadUrl='https://github.com/owner/repo/releases/download/v0.6.0.12/PitMedic-Setup-x64.exe',
                releaseUrl='https://github.com/owner/repo/releases/tag/v0.6.0.12'))
            (root / 'README.md').write_text('# PitMedic\n\n' + stale_readme + '\n\nHistory: v0.6.0.11\n')
            generator = root / 'Tools/DiagnosticLibrary/generate.py'
            generator.parent.mkdir(parents=True)
            generator.write_text('v0.6.0.16/PitMedic-Setup-x64.exe')
            installer = root / 'signed.exe'
            installer.write_bytes(b'verified signed installer')
            with patch.object(release, 'ROOT', root), patch.object(release.subprocess, 'run'):
                release.prepare('v0.6.0.17', 'owner/repo', installer)
            self.assertIn('Won’t launch?', sim.read_text(encoding='utf-8'))
            self.assertNotIn('0.6.0.15', sim.read_text(encoding='utf-8'))
            self.assertIn('Download v0.6.0.17', sim.read_text(encoding='utf-8'))
            self.assertEqual(json.loads((root / 'website/update.json').read_text())['sha256'],
                             release.digest(installer))
            manifest = json.loads((root / 'website/update.json').read_text())
            readme = (root / 'README.md').read_text()
            release.check_readme(readme, manifest)
            self.assertIn('Current signed release: **0.6.0.17**', readme)
            self.assertIn('History: v0.6.0.11', readme)
            self.assertNotIn('0.6.0.12', readme)
            for broken in [stale_readme, readme.replace('download/v0.6.0.17/', 'download/v0.6.0.12/'),
                           readme.replace('tag/v0.6.0.17', 'tag/v0.6.0.12'), '', readme + readme]:
                with self.subTest(readme=broken), self.assertRaises(RuntimeError):
                    release.check_readme(broken, manifest)
            with patch.object(release, 'ROOT', root), patch.object(release.subprocess, 'run'):
                release.prepare('v0.6.0.17', 'owner/repo', installer)
            self.assertEqual((root / 'README.md').read_text(), readme)


if __name__ == '__main__':
    unittest.main()
