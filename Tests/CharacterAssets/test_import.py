import json, tempfile, unittest, sys
from unittest.mock import patch
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"Tools"/"CharacterAssets"))
import import_characters as importer

class ImportBoundaryTests(unittest.TestCase):
    def test_character_allowlist_rejects_expansion(self):
        with self.assertRaises(ValueError): importer.select_characters(["c9999"])
        self.assertEqual(len(importer.select_characters(["c1001"])),1)
    def test_base_roster_uses_exact_graph_roots_in_id_order(self):
        self.assertEqual(list(importer.CHARACTERS), [f"c100{i}" for i in range(1, 10)] + ["c1010", "c1011", "c1012", "c1013", "c1062"])
        self.assertEqual(importer.skill_ids("c1007"), [f"c_wilderd_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.skill_ids("c1009")[-1], "c_charlotte_skill_03_05")
        self.assertEqual(importer.skill_ids("c1010"), [f"c_jerato_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.skill_ids("c1013"), [f"c_cartuja_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_cartuja_skill_03.stg"],
                         ("model/kartua.scsp", "skill3", "cutin", "sk_c1013_3"))
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_jerato_skill_03.stg"],
                         ("model/zerato.scsp", "skill3", "cutin", "sk_c1010_3"))

    def test_node_animation_uses_bone_only_spani_dependency(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            (root / "stagept").mkdir()
            (root / "spani").mkdir()
            (root / "stagept/c_test.stg").write_text('{"entities":[{"etty":"NODE_ANI","source":"motion.scsp"}]}')
            (root / "spani/motion.scsp").write_bytes(b"synthetic header")
            (root / "spani/motion.atlas").touch()
            closure = importer.Closure(root)
            closure.include("stagept/c_test.stg", "c1001")
            self.assertEqual(closure.dependencies["stagept/c_test.stg"], ["spani/motion.scsp"])
            self.assertEqual(closure.dependencies["spani/motion.scsp"], [])

    def test_mercedes_cutin_is_added_only_for_the_authored_marker(self):
        source = "stagept/c_mercedes_skill_03.stg"
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            for directory in ("stagept", "model", "cut/fhd"):
                (root / directory).mkdir(parents=True, exist_ok=True)
            model = root / "model/mercedes.scsp"
            model.touch()
            clip = root / "cut/fhd/sk_c1005_3.webp"
            clip.touch()
            graph_path = root / source
            original = {"entities": [{"guid": 1, "etty": "ANI", "ani": "skill3"},
                                     {"guid": 2, "etty": "HIT"}]}
            graph_path.write_text(json.dumps(original), encoding="utf-8")
            with patch.object(importer, "decode_scsp", return_value=({"animations": {
                    "skill3": {"events": [{"name": "cutin", "time": 1.3}]}}}, None)):
                closure = importer.Closure(root)
                self.assertEqual(closure.direct(source), ["cut/fhd/sk_c1005_3.webp"])
                self.assertEqual(closure.structures[source], original)
                addon = closure.graph_additions[source]
                self.assertEqual([node["etty"] for node in addon["entities"]], ["ONEVENT", "CUTIN"])
                self.assertEqual(addon["entities"][0]["name"], "cutin")
                self.assertEqual(addon["connections"], [{"from": 3, "to": 4}])

            with patch.object(importer, "decode_scsp", return_value=({"animations": {}}, None)):
                with self.assertRaisesRegex(ValueError, "Missing cut-in animation marker"):
                    importer.Closure(root).direct(source)

            original["entities"].append({"guid": 3, "etty": "CUTIN", "args": "sk_c1005_3"})
            graph_path.write_text(json.dumps(original), encoding="utf-8")
            closure = importer.Closure(root)
            self.assertEqual(closure.direct(source), ["cut/fhd/sk_c1005_3.webp"])
            self.assertNotIn(source, closure.graph_additions)

    def test_existing_paths_survive_shared_ownership_growth(self):
        source = "effect/common.sct"
        owners = {source: {"c1001", "c1002"}}
        original = Path("Characters/c1001")
        self.assertIn("/Characters/c1001/", importer.output_owner_path(source, owners, {source: original}).as_posix())

    def test_source_path_rejects_traversal_absolute_and_drive(self):
        with tempfile.TemporaryDirectory() as folder:
            for path in ["../secret", "/secret", "C:/secret", "effect/../../secret", "effect\\..\\secret"]:
                with self.subTest(path=path), self.assertRaises(ValueError): importer.safe_source(Path(folder),path)
    def test_missing_page_is_not_guessed_from_atlas_filename(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);(root/"effect").mkdir();(root/"effect/foo.sct").write_bytes(b"data")
            (root/"effect/foo.atlas").write_text("\nrenamed.sct\nsize: 1,1\nformat: RGBA8888\nfilter: Linear,Linear\nrepeat: none\n")
            with self.assertRaises(FileNotFoundError):importer.atlas_pages(root,"effect/foo.atlas")
    def test_atlas_file_alias_follows_shared_atlas(self):
        with tempfile.TemporaryDirectory() as folder:
            root=Path(folder);(root/"effect").mkdir()
            (root/"effect/alias.atlas").write_text("shared.atlas\n")
            (root/"effect/shared.atlas").write_text("\nshared.sct\nsize: 1,1\n")
            (root/"effect/shared.sct").write_bytes(b"data")
            self.assertEqual(importer.atlas_pages(root,"effect/alias.atlas"),[{"name":"shared.sct","source":"effect/shared.sct"}])
    def test_shared_dependencies_have_one_destination(self):
        owners={"effect/common.sct":{"c1001","c1005"},"model/ras.scsp":{"c1001"}}
        self.assertIn("/Shared/",importer.output_owner_path("effect/common.sct",owners).as_posix())
        self.assertIn("/Characters/c1001/",importer.output_owner_path("model/ras.scsp",owners).as_posix())

if __name__=="__main__":unittest.main()
