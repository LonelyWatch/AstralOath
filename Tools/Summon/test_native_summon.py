import json
from statistics import median
import unittest
from pathlib import Path

from PIL import Image, ImageStat

from ImportSummonPresentation import DEST, ROOTS, collect_visual, validate_music_stream


def featured_frame(seconds):
    folder = DEST / "IseriaSkillLoop"
    layout = json.loads((folder / "loop.json").read_text(encoding="utf-8"))
    index = round(seconds * layout["framesPerSecond"])
    columns, rows = layout["columns"], layout["rows"]
    page, tile = divmod(index, columns * rows)
    width, height, gutter = layout["width"], layout["height"], layout["gutterPixels"]
    x = tile % columns * (width + 2 * gutter) + gutter
    y = tile // columns * (height + 2 * gutter) + gutter
    with Image.open(folder / layout["pages"][page]) as atlas:
        return atlas.crop((x, y, x + width, y + height)).convert("RGB").resize((640, 360))


class NativeSummonPresentation(unittest.TestCase):
    def test_featured_banner_depends_on_the_battle_preview(self):
        folder = DEST / "IseriaSkillLoop"
        loop = (folder / "IseriaSkillLoop.asset").read_text(encoding="utf-8")
        self.assertNotIn("  pages:", loop, "The banner still takes its frames from the JPEG atlas.")
        guid = next(line.split()[1] for line in (folder / "BattleSkillPreviewDefinition.asset.meta")
                    .read_text(encoding="utf-8").splitlines() if line.startswith("guid:"))
        self.assertIn("preview: {fileID: 11400000, guid: " + guid, loop)

    def test_featured_sword_gate_flash_covers_the_full_frame(self):
        for index in (68, 69, 70):
            with self.subTest(frame=index):
                frame = featured_frame(index / 24).convert("L")
                bands = [ImageStat.Stat(frame.crop((160, y, 480, y + 40))).mean[0] for y in (50, 285)]
                self.assertGreater(min(bands), 180, "Both halves of the sword gate must flash.")
                self.assertLess(abs(bands[0] - bands[1]), 20)

    def test_featured_skill_captures_a_slime_as_the_only_enemy(self):
        loop = json.loads((DEST / "IseriaSkillLoop/loop.json").read_text(encoding="utf-8"))
        self.assertEqual("preview.slime", loop.get("sourceTargetModel"))
        self.assertEqual(1, loop["sourceTargetCount"])

    def test_featured_sword_hits_the_visible_slime(self):
        # In the original at 13:33.756 the victim stands under the blade, near (319, 305).
        # The preview uses the ice slime. Its pink cheek is visible until the first impact.
        target_box = (300, 279, 340, 322)
        waiting = featured_frame(5.625).crop(target_box)
        cheek = [(i % waiting.width, i // waiting.width) for i, (r, g, b) in enumerate(waiting.getdata())
                 if b > 100 and r > max(124, b * .78) and g < r * .76]
        self.assertGreater(len(cheek), 12, "The ice slime must be visible where the strike lands.")
        center = (round(median(x for x, _ in cheek)), round(median(y for _, y in cheek)))
        # First HIT at stage 4.7666 s plus the 1.670 s portrait cut.
        hit = featured_frame(6.4366).crop(target_box)
        self.assertGreater(min(hit.getpixel(center)), 210, "The sword's bright core must cross the victim.")

    def test_featured_sword_crest_is_framed_above_the_impact(self):
        # At 13:35.356 the gold crest is above the contact point, with the entire blade in view.
        frame = featured_frame(181 / 24)
        crest = frame.crop((275, 60, 365, 335))
        gold_rows = [60 + i // crest.width for i, (r, g, b) in enumerate(crest.getdata())
                     if 75 < r < 212 and g > b + 23 and r > b + 23 and abs(r - g) < 36]
        self.assertGreater(len(gold_rows), 60)
        self.assertTrue(100 < median(gold_rows) < 230, f"Sword crest is at y={median(gold_rows)}/360.")

    def test_streamed_music_advances_past_the_decoder_buffer(self):
        for name in ("Theme.wav", "Ceremony.wav"):
            with self.subTest(name=name):
                validate_music_stream((DEST / name).read_bytes(), name)

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
