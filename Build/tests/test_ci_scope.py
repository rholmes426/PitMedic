"""Check that repository housekeeping skips unrelated application builds."""

import importlib.util
from pathlib import Path
import unittest


spec = importlib.util.spec_from_file_location(
    'ci_scope_housekeeping', Path(__file__).resolve().parents[1] / 'ci-scope.py')
ci = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ci)

REPOSITORY_ONLY = dict(build=False, knowledge=False, worker=False, dashboard=False)
FULL_VALIDATION = dict.fromkeys(REPOSITORY_ONLY, True)


class HousekeepingScopeTests(unittest.TestCase):
    def test_conversation_archive_cleanup_uses_repository_checks(self):
        paths = ['.gitignore', 'AGENTS.md', 'CONTRIBUTING.md', 'PROJECT_HANDOFF.md',
                 'CHAT_TRANSCRIPTS/PROJECT-HISTORY-INDEX.md', 'CHAT_TRANSCRIPTS/README.md']
        self.assertEqual(ci.scope(paths), REPOSITORY_ONLY)

    def test_scope_classifier_changes_use_the_unconditional_python_tests(self):
        self.assertEqual(ci.scope(['Build/ci-scope.py', 'Build/tests/test_ci_scope.py']),
                         REPOSITORY_ONLY)

    def test_app_changes_in_a_housekeeping_pr_still_require_full_validation(self):
        app_path = 'Source/PitMedic/MainWindow.xaml.cs'
        for paths in (['.gitignore', app_path], [app_path, '.gitignore']):
            with self.subTest(paths=paths):
                self.assertEqual(ci.scope(paths), FULL_VALIDATION)

    def test_release_and_workflow_inputs_still_require_full_validation(self):
        paths = ['Build/release.py', 'Installer/PitMedic.iss',
                 '.github/workflows/ci.yml', 'global.json', '.gitattributes']
        for path in paths:
            with self.subTest(path=path):
                self.assertEqual(ci.scope(['.gitignore', path]), FULL_VALIDATION)

    def test_unknown_paths_and_other_build_tests_remain_conservative(self):
        for path in ['unknown/config', 'Build/new-rule.py', 'Build/tests/new-test.py']:
            with self.subTest(path=path):
                self.assertEqual(ci.scope([path]), FULL_VALIDATION)

    def test_housekeeping_preserves_component_specific_checks(self):
        cases = {
            'website/index.html': dict(REPOSITORY_ONLY, knowledge=True),
            'TelemetryWorker/src/index.ts': dict(REPOSITORY_ONLY, worker=True),
            'TelemetryDashboard/src/index.ts': dict(REPOSITORY_ONLY, dashboard=True),
        }
        for path, expected in cases.items():
            with self.subTest(path=path):
                self.assertEqual(ci.scope(['.gitignore', path]), expected)

    def test_explicit_full_validation_dispatch_still_runs_everything(self):
        self.assertEqual(ci.scope(['Build/']), FULL_VALIDATION)


if __name__ == '__main__':
    unittest.main()
