from __future__ import annotations

import base64
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
            now=scout.datetime(2025, 1, 1, tzinfo=scout.timezone.utc),
            fetcher=fake_fetch,
        )
        self.assertTrue(actionable)
        self.assertIn("new candidate", report)
        self.assertIn("data loss", report)
        self.assertIn("Only reviewed, credible evidence", report)

    def test_support_page_text_churn_is_quiet_without_a_new_candidate(self) -> None:
        registry = self.one_source_registry()
        source = registry["sources"][0]
        source["reportTextChanges"] = False
        prior = {
            "version": 2,
            "sources": {
                source["id"]: {
                    "hash": scout.content_hash("older support page"),
                    "links": [],
                    "status": "ok",
                }
            },
        }

        def fake_fetch(url: str, allowed_hosts: set[str]) -> tuple[str, str]:
            return ("<html><body>Routine community conversation.</body></html>", url)

        report, _, actionable = scout.build_report(
            REPO_ROOT,
            registry,
            self.lifecycle,
            prior,
            fetcher=fake_fetch,
        )
        self.assertFalse(actionable)
        self.assertNotIn("source text changed", report)

    def test_per_source_candidate_limit_can_expand_to_hard_cap(self) -> None:
        links = [(f"https://lemansultimate.com/fix-{index}", f"Fix {index}") for index in range(50)]
        found = scout.candidate_links(
            links,
            ["fix"],
            {"lemansultimate.com"},
            scout.MAX_LINKS_PER_SOURCE,
        )
        self.assertEqual(scout.MAX_LINKS_PER_SOURCE, len(found))
        with self.assertRaises(ValueError):
            scout.candidate_links(links, ["fix"], {"lemansultimate.com"}, scout.MAX_LINKS_PER_SOURCE + 1)

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

    def test_uncompressed_legacy_state_still_loads(self) -> None:
        state = {"version": 1, "sources": {"legacy": {"hash": "abc"}}}
        encoded = base64.urlsafe_b64encode(json.dumps(state).encode("utf-8")).decode("ascii")
        with tempfile.TemporaryDirectory() as temp_dir:
            report = Path(temp_dir) / "prior.md"
            report.write_text(f"<!-- pitmedic-knowledge-state:{encoded} -->", encoding="utf-8")
            self.assertEqual(state, scout.load_prior_state(report))

    def test_registry_rejects_non_first_party_monitoring_authority(self) -> None:
        registry = self.one_source_registry()
        registry["sources"][0]["authority"] = "crowd-community"
        problems = scout.catalog_validation(REPO_ROOT, registry, self.lifecycle)
        self.assertTrue(any("Invalid source authority" in problem for problem in problems))


    def test_forum_canonicalization_groups_pages_and_preserves_query(self) -> None:
        self.assertEqual(
            "https://forum.reizastudios.com/threads/update.36665/",
            scout.canonical_candidate_url("https://forum.reizastudios.com/forums/news.41/threads/update.36665/page-3"),
        )
        self.assertEqual(
            "https://community.lemansultimate.com/index.php?threads/wheel.17859/",
            scout.canonical_candidate_url("https://community.lemansultimate.com/index.php?threads/wheel.17859/post-99353"),
        )

    def test_unrelated_safety_notices_do_not_hide_real_product_harm(self) -> None:
        self.assertEqual([], scout.relevant_harm_snippets(
            "Important Notice: The Presentation software is no longer supported or maintained by Logitech.",
            "Logitech G HUB",
        ))
        self.assertEqual([], scout.relevant_harm_snippets("penalties for unsafe rejoin", "Le Mans Ultimate"))
        self.assertTrue(scout.relevant_harm_snippets("G HUB update causes data loss.", "Logitech G HUB"))

    def test_cross_source_duplicates_and_disappearing_findings_are_retained_once(self) -> None:
        registry = self.one_source_registry()
        source = registry["sources"][0]
        other = dict(source, id="second-source")
        registry["sources"] = [source, other]
        prior = {"sources": {item["id"]: {"hash": "old", "links": []} for item in registry["sources"]}}
        def fetch(url, hosts):
            return ('<a href="https://lemansultimate.com/new-crash">Crash workaround</a>', url)
        report, state, _ = scout.build_report(REPO_ROOT, registry, self.lifecycle, prior, fetcher=fetch)
        self.assertIn("- 1 new or changed source findings", report)
        self.assertEqual(1, len(state["pendingFindings"]))
        def empty(url, hosts):
            return ("No current links", url)
        _, later, _ = scout.build_report(REPO_ROOT, registry, self.lifecycle, state, fetcher=empty)
        self.assertEqual(state["pendingFindings"], later["pendingFindings"])

    def test_review_resolution_does_not_suppress_later_changes(self) -> None:
        from unittest.mock import patch
        registry = self.one_source_registry()
        url = registry["sources"][0]["url"]
        record = {"url": url, "status": "queued", "reviewedAt": "2026-09-14T19:00:00Z", "note": "Reviewed"}
        old = {"text": "Earlier finding", "url": url, "firstSeen": "2026-09-11T19:00:00Z"}
        newer = {"text": "Later finding", "url": url, "firstSeen": "2026-09-15T19:00:00Z"}
        prior = {"sources": {}, "pendingFindings": {"old": old, "new": newer}}
        with patch.object(scout, "review_records", return_value=[record]):
            _, state, _ = scout.build_report(REPO_ROOT, registry, self.lifecycle, prior, offline=True)
        self.assertNotIn("old", state["pendingFindings"])
        self.assertIn("new", state["pendingFindings"])


class InlineReviewTests(unittest.TestCase):
    def setUp(self):
        self.now = scout.datetime(2026, 9, 18, tzinfo=scout.timezone.utc)
        self.url = "https://lemansultimate.com/crash-fix"
        self.pending = {"one": {"url": self.url, "text": "**Le Mans Ultimate**: candidate", "firstSeen": "2026-09-15T00:00:00Z"}}
        self.registry = {"allowedHosts": ["lemansultimate.com"]}
        self.calls = []

    def fetch(self, url, hosts):
        self.calls.append(url)
        return ("<p>Fixed a crash by restarting the application. " + "Detailed vendor release context. " * 8 + "</p>", url)

    def review(self, **kwargs):
        return scout.review_pending_findings(self.pending, self.registry, kwargs.pop("previous", {}), [], self.now, fetcher=kwargs.pop("fetcher", self.fetch), **kwargs)

    def test_backlog_is_fetched_once_and_not_resolved(self):
        self.pending["duplicate"] = dict(self.pending["one"])
        result = self.review()
        self.assertEqual([self.url], self.calls)
        self.assertEqual("candidate-remedy", result[self.url]["status"])
        self.assertEqual(2, len(self.pending))
        self.assertIn("still require review", result[self.url]["reason"])
        self.assertEqual(result, self.review(previous=result))
        self.assertEqual(1, len(self.calls))

    def test_redirect_and_challenge_never_become_remedies(self):
        result = self.review(fetcher=lambda u,h: ("Fixed " * 100, "https://lemansultimate.com/"))
        self.assertEqual("needs-evidence", result[self.url]["status"])
        result = self.review(fetcher=lambda u,h: ("Verify you are human. Fixed " * 100, u))
        self.assertEqual("needs-evidence", result[self.url]["status"])

    def test_allowlist_failure_and_offline_do_not_fetch(self):
        self.pending["bad"] = {"url": "https://example.com/fix", "firstSeen": "2026-09-15T00:00:00Z"}
        result = self.review(offline=True)
        self.assertEqual([], self.calls)
        result = self.review()
        self.assertEqual([self.url], self.calls)
        self.assertEqual("needs-evidence", result["https://example.com/fix"]["status"])

    def test_scan_cache_reused_and_new_findings_invalidate_review_cache(self):
        raw = self.fetch(self.url, set())
        self.calls.clear()
        result = self.review(page_cache={self.url: raw})
        self.assertEqual([], self.calls)
        self.pending["one"]["firstSeen"] = "2026-09-19T00:00:00Z"
        self.now += scout.timedelta(days=1)
        self.review(previous=result)
        self.assertEqual([self.url], self.calls)

    def test_fetch_budget_retains_overflow_and_rotates_to_unchecked(self):
        from unittest.mock import patch
        self.pending["two"] = {"url": "https://lemansultimate.com/z-fix", "firstSeen": "2026-09-15T00:00:00Z"}
        with patch.object(scout, "INLINE_REVIEW_LIMIT", 1):
            result = self.review()
            self.assertEqual("deferred", result["https://lemansultimate.com/z-fix"]["status"])
            self.review(previous=result)
        self.assertEqual(2, len(self.calls))

    def test_safety_takes_priority_and_fetch_failures_remain_pending(self):
        result = self.review(fetcher=lambda u,h: ("This workaround causes data loss. " * 20, u))
        self.assertEqual("safety-review-required", result[self.url]["status"])
        def fail(u,h):
            raise TimeoutError("timeout")
        result = self.review(fetcher=fail)
        self.assertEqual("needs-evidence", result[self.url]["status"])
        self.assertEqual(1, len(self.pending))


if __name__ == "__main__":
    unittest.main()
