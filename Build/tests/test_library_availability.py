import importlib.util
from pathlib import Path
import unittest
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("library_generator", ROOT / "Tools/DiagnosticLibrary/generate.py")
generator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generator)

NEW_IDS = {"lmu-controller-config", "iracing-socket-connection", "iracing-spotter-loading-error-63"}
AC_IDS = {"ac-video-config", "ac-controls-config", "ac-steam-content"}


class LibraryAvailabilityTests(unittest.TestCase):
    def setUp(self):
        self.version = generator.RELEASE_VERSION

    def tearDown(self):
        generator.RELEASE_VERSION = self.version

    def test_unreleased_repairs_are_not_advertised_with_the_previous_download(self):
        generator.RELEASE_VERSION = "1.0.0.11"
        previous = {entry["id"] for entry in generator.load_entries()}
        self.assertTrue(previous.isdisjoint(NEW_IDS))
        self.assertIn("iracing-track-loading-errors", previous)

    def test_publishing_the_supporting_version_adds_the_guides_without_losing_existing_pages(self):
        generator.RELEASE_VERSION = "1.0.0.11"
        previous = {entry["id"] for entry in generator.load_entries()}
        generator.RELEASE_VERSION = "1.0.0.12"
        current = {entry["id"] for entry in generator.load_entries()}
        self.assertEqual(NEW_IDS, current - previous)
        self.assertTrue(previous <= current)

    def test_bad_availability_metadata_stops_generation(self):
        item = generator.PUBLIC_CONTENT["lmu-controller-config"]
        original = item["available_from"]
        try:
            item["available_from"] = "pending"
            with self.assertRaises(ValueError):
                generator.load_entries()
        finally:
            item["available_from"] = original

    def test_original_ac_is_published_with_the_supporting_app(self):
        generator.RELEASE_VERSION = "1.0.0.12"
        before = generator.load_entries()
        self.assertTrue({x["id"] for x in before}.isdisjoint(AC_IDS))
        home = (ROOT / "website/index.html").read_text(encoding="utf-8")
        self.assertNotIn('href="/diagnostic-library/#assetto-corsa"', generator.sync_original_ac_lineup(home, before))
        generator.RELEASE_VERSION = "1.0.0.13"
        after = generator.load_entries()
        self.assertEqual(AC_IDS, {x["id"] for x in after} - {x["id"] for x in before})
        updated = generator.sync_original_ac_lineup(home, after)
        self.assertIn('href="/diagnostic-library/#assetto-corsa"', updated)
        self.assertIn('href="/diagnostic-library/#assetto-corsa-evo"', updated)
        self.assertIn('href="/diagnostic-library/#assetto-corsa-competizione"', updated)
        self.assertEqual(updated, generator.sync_original_ac_lineup(updated, after))

    def test_original_ac_group_and_pages_appear_together(self):
        for version, available in (("1.0.0.12", False), ("1.0.0.13", True)):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as temp:
                generator.RELEASE_VERSION = version
                root = Path(temp)
                generator.generate(root / "library", root / "sitemap.xml")
                index = (root / "library/index.html").read_text(encoding="utf-8")
                self.assertEqual(available, 'id="assetto-corsa"' in index)
                for guide in AC_IDS:
                    self.assertEqual(available, (root / "library" / guide / "index.html").exists())
                    self.assertEqual(available, f"/diagnostic-library/{guide}/" in index)
                    self.assertEqual(available, f"/diagnostic-library/{guide}/" in (root / "sitemap.xml").read_text(encoding="utf-8"))
