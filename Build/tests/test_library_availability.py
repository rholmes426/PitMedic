import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("library_generator", ROOT / "Tools/DiagnosticLibrary/generate.py")
generator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generator)

NEW_IDS = {"lmu-controller-config", "iracing-socket-connection", "iracing-spotter-loading-error-63"}
AC_IDS = {"ac-video-config", "ac-controls-config", "ac-steam-content"}
WINDOWS_IDS = {"windows-integrity-check", "windows-system-file-integrity", "windows-dns-cache-recovery",
               "windows-audio-service-recovery", "windows-time-sync", "windows-thermal-limiting",
               "windows-network-health", "windows-background-health"}


class LibraryAvailabilityTests(unittest.TestCase):
    def setUp(self):
        self.version = generator.RELEASE_VERSION

    def tearDown(self):
        generator.RELEASE_VERSION = self.version

    def test_windows_section_is_gated_and_routes_users_to_the_correct_tools(self):
        for version, available in (("1.0.0.13", False), ("1.0.0.14", True)):
            with self.subTest(version=version), tempfile.TemporaryDirectory() as directory:
                generator.RELEASE_VERSION = version
                root = Path(directory)
                generator.generate(root / "library", root / "sitemap.xml")
                index = (root / "library/index.html").read_text(encoding="utf-8")
                sitemap = (root / "sitemap.xml").read_text(encoding="utf-8")
                self.assertEqual(available, 'id="windows"' in index)
                for guide in WINDOWS_IDS:
                    path = root / "library" / guide / "index.html"
                    self.assertEqual(available, path.exists())
                    self.assertEqual(available, f"/diagnostic-library/{guide}/" in sitemap)
                    if available:
                        page = path.read_text(encoding="utf-8")
                        if guide == "windows-thermal-limiting":
                            self.assertIn("Review your thermal report", page)
                        elif guide == "windows-network-health":
                            self.assertIn("Review your network report", page)
                        elif guide == "windows-background-health":
                            self.assertIn("Review Windows health findings", page)
                        else:
                            self.assertIn("Open Home, choose Windows repair tools", page)

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

    def test_rfactor2_section_is_published_with_its_first_supported_release(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for version, expected in (("1.0.0.12", False), ("1.0.0.13", True)):
                with self.subTest(version=version):
                    generator.RELEASE_VERSION = version
                    destination = root / version
                    sitemap = root / f"sitemap-{version}.xml"
                    generator.generate(destination, sitemap)
                    index = (destination / "index.html").read_text(encoding="utf-8")
                    self.assertEqual('id="rfactor2"' in index, expected)
                    for guide in ("rfactor2-steam-content", "rfactor2-trace-log", "rfactor2-shader-cache"):
                        self.assertEqual((destination / guide / "index.html").is_file(), expected)
                        self.assertEqual(guide in sitemap.read_text(encoding="utf-8"), expected)
                    home = (ROOT / "website" / "index.html").read_text(encoding="utf-8")
                    lineup = generator.sync_simulator_lineup(home, generator.load_entries())
                    self.assertEqual('href="/diagnostic-library/#rfactor2"' in lineup, expected)
                    self.assertEqual(lineup, generator.sync_simulator_lineup(lineup, generator.load_entries()))
                    if expected:
                        page = (destination / "rfactor2-steam-content" / "index.html").read_text(encoding="utf-8")
                        self.assertIn("Start this check in PitMedic", page)
                        self.assertIn("Verify Steam files", page)
                        cache = (destination / "rfactor2-shader-cache" / "index.html").read_text(encoding="utf-8")
                        self.assertIn("Start this check in PitMedic", cache)
                        self.assertIn("Restore last backup", cache)

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
        self.assertNotIn('href="/diagnostic-library/#assetto-corsa"', generator.sync_simulator_lineup(home, before))
        generator.RELEASE_VERSION = "1.0.0.13"
        after = generator.load_entries()
        self.assertEqual(AC_IDS, {x["id"] for x in after if x["product"] == "Assetto Corsa"})
        self.assertFalse(AC_IDS & {x["id"] for x in before})
        updated = generator.sync_simulator_lineup(home, after)
        self.assertIn('href="/diagnostic-library/#assetto-corsa"', updated)
        self.assertIn('href="/diagnostic-library/#assetto-corsa-evo"', updated)
        self.assertIn('href="/diagnostic-library/#assetto-corsa-competizione"', updated)
        self.assertEqual(updated, generator.sync_simulator_lineup(updated, after))

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
