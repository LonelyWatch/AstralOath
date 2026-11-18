import unittest
from pathlib import Path

from ImportSummonPresentation import ROOTS, collect_visual


class NativeSummonPresentation(unittest.TestCase):
    def test_requires_every_native_visual_phase_and_preserves_colors(self):
        source = Path("D:/sevenepic/原始资源")
        if not source.is_dir():
            self.skipTest("Original source is required for the visual provenance check.")
        graph, payloads, _ = collect_visual(source)
        phases = {group["id"]: group["parts"] for group in graph["groups"]}
        self.assertEqual(set(phases), set(ROOTS))
        for phase, color in (("1_blue", "blue"), ("1_yellow1", "yellow"), ("1_violet1", "violet")):
            tracks = {part["source"]: part["animation"] for part in phases[phase]}
            for character in ("n_bibleo", "n_biblika"):
                self.assertIn(character, tracks)
            self.assertEqual(tracks["gacha_eff_1_2"], color)
        statue = phases["2_class"][0]
        self.assertEqual("gacha_eff_2_figure", statue["source"])
        self.assertEqual(35, statue["z"])
        statue_asset = next(item for item in graph["skeletons"] if item["id"] == statue["source"])
        self.assertEqual({"artifact", "assassin", "knight", "mage", "manauser", "ranger", "warrior"},
                         {item["name"] for item in statue_asset["animations"]})
        for phase in ("3_character_bone", "3_artifact_bone", "3_portrait_bone"):
            self.assertEqual(phases[phase][0]["animation"], "intro,loop")
        self.assertIn("gacha_eff_3_intro_1.spine.json", payloads)
        self.assertEqual(2400, next(part["delay"] for part in phases["3_b"]
                                   if part["source"] == "gacha_eff_3_loop_1"))


if __name__ == "__main__":
    unittest.main()
