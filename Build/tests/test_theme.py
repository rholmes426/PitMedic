"""Keep the shared dark palette readable; run in the existing build test suite."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
XAML = "{http://schemas.microsoft.com/winfx/2006/xaml}"


def luminance(color):
    rgb = [int(color.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    rgb = [v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in rgb]
    return sum(v * weight for v, weight in zip(rgb, (.2126, .7152, .0722)))


def contrast(a, b):
    light, dark = sorted((luminance(a), luminance(b)), reverse=True)
    return (light + .05) / (dark + .05)


class ThemeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.app = ET.parse(ROOT / "Source/PitMedic/App.xaml")
        cls.brushes = {e.attrib[XAML + "Key"]: e.attrib["Color"]
                       for e in cls.app.iter() if e.tag.endswith("SolidColorBrush")}
        cls.css = (ROOT / "website/styles.css").read_text()
        cls.tokens = dict(re.findall(r"--([\w-]+):\s*(#[\da-fA-F]{6})\s*;", cls.css))

    def test_shared_identity(self):
        for web, app in {"background": "BgBrush", "surface": "PanelBrush",
                         "accent": "AccentBrush", "warning": "WarnBrush",
                         "caution": "HotspotBrush", "danger": "DangerBrush"}.items():
            self.assertEqual(self.tokens[web].lower(), self.brushes[app].lower())

    def test_app_text_contrast(self):
        pairs = [(fg, bg) for fg in ("TextBrush", "MutedBrush")
                 for bg in ("BgBrush", "PanelBrush", "Panel2Brush", "InsetBrush",
                            "HoverBrush", "PressedBrush", "AccentSoftBrush")]
        pairs += [("AccentTextBrush", bg) for bg in
                  ("AccentBrush", "AccentHoverBrush", "AccentPressedBrush")]
        pairs += [(prefix + "TextBrush", prefix + "SoftBrush")
                  for prefix in ("Good", "Warn", "Danger", "Info")]
        for fg, bg in pairs:
            with self.subTest(fg=fg, bg=bg):
                self.assertGreaterEqual(contrast(self.brushes[fg], self.brushes[bg]), 4.5)

    def test_web_text_contrast(self):
        pairs = [(fg, bg) for fg in ("heading", "text", "muted", "accent", "success")
                 for bg in ("background", "surface", "surface-raised", "accent-soft")]
        pairs += [("accent-text", bg) for bg in ("accent", "accent-hover", "accent-pressed")]
        for fg, bg in pairs:
            with self.subTest(fg=fg, bg=bg):
                self.assertGreaterEqual(contrast(self.tokens[fg], self.tokens[bg]), 4.5)

    def test_no_undefined_css_tokens(self):
        defined = set(re.findall(r"--([\w-]+)\s*:", self.css))
        used = set(re.findall(r"var\(--([\w-]+)\)", self.css))
        self.assertFalse(used - defined, f"Undefined CSS colors: {used - defined}")

    def test_xaml_resources_resolve(self):
        documents = [ET.parse(p) for p in (ROOT / "Source/PitMedic").glob("*.xaml")]
        keys = {e.attrib[XAML + "Key"] for doc in documents for e in doc.iter()
                if XAML + "Key" in e.attrib}
        for doc in documents:
            for e in doc.iter():
                for value in e.attrib.values():
                    for key in re.findall(r"\{(?:Dynamic|Static)Resource (\w+)\}", value):
                        self.assertIn(key, keys)


if __name__ == "__main__":
    unittest.main()
