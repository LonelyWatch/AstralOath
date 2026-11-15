import unittest
from pathlib import Path

from PIL import Image


STAGE = (Path(__file__).resolve().parents[2]
         / 'Assets/Game/Modules/BattleScene/Res/Textures/Backgrounds/Rta021/rta_021.png')


class RtaStageCompositionTests(unittest.TestCase):
    def test_central_cords_connect_to_visible_floor_light(self):
        # This band is the center of the native m_3 floor light in the baked crop.
        # Putting the m_5 backdrop in front of the floor obscures the entire band.
        with Image.open(STAGE) as stage:
            band = stage.convert('RGB').crop((1020, 626, 1220, 634))
            red_pixels = sum(red - green > 30 for red, green, _ in band.getdata())
            self.assertGreater(red_pixels, band.width * band.height // 3)
