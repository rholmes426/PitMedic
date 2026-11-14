import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("library_generator", ROOT / "Tools/DiagnosticLibrary/generate.py")
generator = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(generator)

NEW_IDS = {"lmu-controller-config", "iracing-socket-connection", "iracing-spotter-loading-error-63"}


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
