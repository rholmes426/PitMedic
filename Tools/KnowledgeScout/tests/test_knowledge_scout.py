from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


REPO_ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = REPO_ROOT / "Tools/KnowledgeScout/knowledge_scout.py"
SPEC = importlib.util.spec_from_file_location("knowledge_scout", MODULE_PATH)
assert SPEC and SPEC.loader
scout = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(scout)


class KnowledgeScoutTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.registry = json.loads((REPO_ROOT / "Knowledge/source-registry.json").read_text(encoding="utf-8"))
        cls.lifecycle = json.loads((REPO_ROOT / "Knowledge/lifecycle.json").read_text(encoding="utf-8"))

    def one_source_registry(self) -> dict:
        registry = dict(self.registry)
        registry["sources"] = [dict(self.registry["sources"][0])]
        return registry

    def test_repository_catalog_is_complete_and_policy_is_safe(self) -> None:
        problems = scout.catalog_validation(REPO_ROOT, self.registry, self.lifecycle)
        self.assertEqual([], problems)
        self.assertEqual(scout.implemented_ids(REPO_ROOT), {entry["id"] for entry in self.lifecycle["entries"]})

    def test_changed_page_surfaces_new_candidate_and_harm_language(self) -> None:
        registry = self.one_source_registry()
        source = registry["sources"][0]
        prior = {
            "version": 1,
            "sources": {
                source["id"]: {
                    "hash": scout.content_hash("older support page"),
                    "links": [],
                    "status": "ok",
                }
            },
        }

        def fake_fetch(url: str, allowed_hosts: set[str]) -> tuple[str, str]:
            return (
                '<html><body><p>This update can cause data loss. Do not use it.</p>'
                '<a href="/new-crash-fix">New crash fix and workaround</a></body></html>',
                url,
            )

        report, _, actionable = scout.build_report(
            REPO_ROOT,
            registry,
            self.lifecycle,
            prior,
            fetcher=fake_fetch,
        )
        self.assertTrue(actionable)
        self.assertIn("new candidate", report)
        self.assertIn("data loss", report)
        self.assertIn("Only reviewed, credible evidence", report)

    def test_first_run_captures_baseline_without_calling_every_link_new(self) -> None:
        registry = self.one_source_registry()

        def fake_fetch(url: str, allowed_hosts: set[str]) -> tuple[str, str]:
            return ('<a href="/crash-fix">Crash fix</a>', url)

        report, _, _ = scout.build_report(
            REPO_ROOT,
            registry,
            self.lifecycle,
            {"version": 1, "sources": {}},
            fetcher=fake_fetch,
        )
        self.assertIn("first-run source baselines captured", report)
        self.assertNotIn("**Le Mans Ultimate**: new candidate", report)

    def test_source_failure_explicitly_keeps_existing_fixes_active(self) -> None:
        registry = self.one_source_registry()

        def failed_fetch(url: str, allowed_hosts: set[str]) -> tuple[str, str]:
            raise TimeoutError("fixture timeout")

        report, _, actionable = scout.build_report(
            REPO_ROOT,
            registry,
            self.lifecycle,
            {"version": 1, "sources": {}},
            fetcher=failed_fetch,
        )
        self.assertTrue(actionable)
        self.assertIn("existing fixes remain active", report)
        self.assertIn("never retires a fix", report)

    def test_review_due_is_a_reminder_not_a_state_change(self) -> None:
        lifecycle = {
            "entries": [
                {
                    "id": "example-repair",
                    "status": "active",
                    "lastVerified": "2025-01-01",
                    "reviewCadenceDays": 30,
                }
            ]
        }
        reminders = scout.review_reminders(lifecycle, scout.date(2026, 9, 1))
        self.assertEqual(1, len(reminders))
        self.assertIn("current state remains `active`", reminders[0])

    def test_embedded_state_round_trips(self) -> None:
        state = {"version": 1, "sources": {"one": {"hash": "abc"}}}
        with tempfile.TemporaryDirectory() as temp_dir:
            report = Path(temp_dir) / "prior.md"
            report.write_text(f"<!-- pitmedic-knowledge-state:{scout.encode_state(state)} -->", encoding="utf-8")
            self.assertEqual(state, scout.load_prior_state(report))


if __name__ == "__main__":
    unittest.main()
