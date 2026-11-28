"""Source-pixel and consumer regressions; this is not a Unity/Native render test."""
from pathlib import Path
import hashlib
import re
import unittest
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
TEXTURES = ROOT / 'Assets/Game/Modules/Home/Res/Textures/Inventory/More'
SOURCE = Path('D:/sevenepic/原始资源/img/renew_glow_quick.png')
GENERATOR = ROOT / 'Assets/Game/Editor/ArenaUiBuilder.HomeMoreLayout.cs'


def linear(value):
    return value / 12.92 if value <= .04045 else ((value + .055) / 1.055) ** 2.4


def srgb(value):
    return 12.92 * value if value <= .0031308 else 1.055 * value ** (1 / 2.4) - .055


def active_image():
    code = GENERATOR.read_text(encoding='utf-8').split('private static void DrawHomeNavigationBackground', 1)[1]
    name = re.search(r'HomeMoreArt\("([^"]+)"\)', code)[1]
    return Image.open(TEXTURES / (name + '.png')).convert('RGBA')


class HomeNavigationCoverageTests(unittest.TestCase):
    def assert_source_white_transfer(self, xy, source_rgba, expected_alpha):
        self.assertEqual(Image.open(SOURCE).convert('RGBA').getpixel(xy), source_rgba)
        actual = active_image().getpixel(xy)
        self.assertEqual(actual[:3], source_rgba[:3], 'Source RGB must be preserved.')
        self.assertEqual(actual[3], expected_alpha, 'Linear coverage must retain the source white-reference contrast.')
        target = sum((1 - source_rgba[3] / 255 + source_rgba[3] / 255 * c / 255) * 255 for c in source_rgba[:3]) / 3
        observed = sum(srgb(1 - actual[3] / 255 + actual[3] / 255 * linear(c / 255)) * 255 for c in actual[:3]) / 3
        self.assertLessEqual(abs(observed - target), 1.5, 'Quantized neutral-gray white-reference mean must match within 1.5/255.')

    def test_source_350_360_retains_white_reference_contrast(self):
        self.assert_source_white_transfer((350, 360), (36, 40, 45, 164), 214)

    def test_source_430_544_retains_white_reference_contrast(self):
        self.assert_source_white_transfer((430, 544), (42, 51, 63, 226), 245)

    def test_original_and_expanded_drawer_stay_unchanged(self):
        self.assertEqual(hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
                         hashlib.sha256((TEXTURES / 'renew_glow_quick.png').read_bytes()).hexdigest())
        drawer = GENERATOR.read_text(encoding='utf-8').split('private static GameObject BuildHomeMoreMenu()', 1)[1]
        self.assertIn('Paint(drawerArt, HomeMoreArt("renew_glow_quick"), Color.white)', drawer)

    def test_source_rgb_transparency_and_offscreen_pixels_are_retained(self):
        source = Image.open(SOURCE).convert('RGBA')
        actual = active_image()
        self.assertEqual(actual.size, (1000, 720))
        self.assertEqual(actual.getpixel((438, 544)), (132, 138, 145, 241), 'Original bright ornament core must be untouched.')
        changed = 0
        for index, (original, result) in enumerate(zip(source.getdata(), actual.getdata())):
            self.assertEqual(original[:3], result[:3], 'No source RGB pixel may change.')
            if index % 1000 >= 462 or original[3] in (0, 255):
                self.assertEqual(original, result, 'Offscreen/transparent/opaque source must be untouched.')
            if original != result:
                changed += 1
                self.assertGreaterEqual(result[3], original[3], 'Do not erase or lighten source backing.')
        self.assertGreater(changed, 100000, 'The active collapsed consumer must use the coverage variant.')

    def test_continuous_source_has_no_hard_palette_correction_edge(self):
        source = list(Image.open(SOURCE).convert('RGBA').getdata())
        actual = list(active_image().getdata())
        correction = [out[3] - original[3] for original, out in zip(source, actual)]
        worst = 0
        for y in range(720):
            for x in range(461):
                index = y * 1000 + x
                neighbors = [index + 1] + ([index + 1000] if y < 719 else [])
                for other in neighbors:
                    if max(abs(a - b) for a, b in zip(source[index], source[other])) <= 2:
                        worst = max(worst, abs(correction[index] - correction[other]))
        self.assertLessEqual(worst, 3, 'Do not introduce a hard correction boundary on the continuous source gradient.')


if __name__ == '__main__':
    unittest.main(verbosity=2)
