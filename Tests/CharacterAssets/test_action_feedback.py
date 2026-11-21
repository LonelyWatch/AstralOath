"""Narrow native feedback payload and provenance regression checks."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import unittest
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "Tools/BattleScene"))
sys.path.insert(0, str(ROOT / "Tools/CharacterAssets"))
class ActionFeedbackImportTests(unittest.TestCase):
    def importer(self):
        path = ROOT / "Tools/BattleScene/ImportActionFeedback.py"
        self.assertTrue(path.is_file(), "Native battle action feedback importer is missing")
        spec = importlib.util.spec_from_file_location("action_feedback_import", path)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module
    def test_only_the_reviewed_native_roots_are_imported(self):
        module = self.importer()
        names = (
            "ui_livepvp_soulburn_eff", "stse_counter", "stse_chain", "stse_addturn",
            "heal_hp_01", "stse_attackpower_up", "stse_defense_up", "stse_immune_weak",
            "stse_speed_up", "stse_evade", "stse_immortality", "stse_defense_reflect", "stse_critup",
        )
        reviewed = [f"effect/{name}.cfx" for name in names]
        manifest = json.loads((module.OUTPUT / "source.json").read_text(encoding="utf-8"))
        self.assertCountEqual(module.ROOTS, reviewed)
        self.assertCountEqual(manifest["roots"], reviewed)

    def test_native_particle_gradients_keep_premultiplied_white_and_green(self):
        from PIL import Image
        module = self.importer()
        manifest = json.loads((module.OUTPUT / "source.json").read_text(encoding="utf-8"))
        textures = [row for row in manifest["files"] if row["source"].startswith("原始资源/particle/")]
        names = ("aura_02", "aura_03", "aura_04", "cross_02", "flash", "heal_cross_01", "pati_01", "ticle_02")
        self.assertCountEqual([row["source"] for row in textures], [f"原始资源/particle/{name}.sct" for name in names])
        for row in textures:
            with self.subTest(source=row["source"]), Image.open(module.OUTPUT / row["asset"]) as image:
                rgba = image.convert("RGBA")
                samples = [pixel for pixel in rgba.getdata() if 5 < pixel[3] < 250]
                self.assertGreater(len(samples), 100)
                if row["source"].endswith("/heal_cross_01.sct"):
                    # SHA-256 of the original SCT1 RGBA8888 payload after LZ4 block decompression.
                    self.assertEqual(hashlib.sha256(rgba.tobytes()).hexdigest(),
                                     "aafad85ed812a2c6cf7609d88219d1e97beb5a4c4e1e13727de2858d78635f9d")
                    self.assertTrue(all(b <= r <= g <= a for r, g, b, a in samples))
                else:
                    self.assertTrue(all(r == g == b == a for r, g, b, a in samples))

    def test_generated_payload_keeps_source_bytes_and_complete_dependencies(self):
        module = self.importer()
        source = Path("D:/sevenepic/原始资源")
        self.assertTrue(source.is_dir(), "Read-only original resource evidence is required")
        payloads = module.collect(source, roots=module.ROOTS)
        manifest = json.loads(payloads["source.json"])
        for row in manifest["files"]:
            with self.subTest(source=row["source"]):
                original = source / row["source"].split("/", 1)[1]
                self.assertEqual(row["sourceSha256"], hashlib.sha256(original.read_bytes()).hexdigest())
                self.assertEqual(row["assetSha256"], hashlib.sha256(payloads[row["asset"]]).hexdigest())
                self.assertTrue(all(path in payloads for path in row["dependencies"]))
                self.assertEqual((module.OUTPUT / row["asset"]).read_bytes(), payloads[row["asset"]])
                if original.suffix == ".atlas": self.assertEqual(payloads[row["asset"]], original.read_bytes())
        addturn = json.loads(payloads["stse_addturn.cfx.json"])["primitive"]
        self.assertEqual(addturn[0]["source"], "stse_addturn_renew")
        self.assertEqual(int(addturn[1]["delay"]), 167)
        soul = json.loads(payloads["ui_livepvp_soulburn_eff.cfx.json"])["primitive"]
        self.assertEqual(soul[0]["ani"], "intro,loop")
        self.assertEqual(soul[0]["loop"], 1)
        healing = json.loads(payloads["Particles/heal_hp_01.particle.json"])["emitters"]
        self.assertEqual([(e["textureFileName"], e["blendFuncSource"], e["blendFuncDestination"]) for e in healing], [
            ("particle/heal_cross_01.sct", 1, 771),
            ("particle/cross_02.sct", 770, 1),
            ("particle/pati_01.sct", 770, 1),
            ("particle/flash.sct", 770, 1),
        ])
if __name__ == "__main__": unittest.main()
