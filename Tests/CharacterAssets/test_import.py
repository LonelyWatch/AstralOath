import json, tempfile, unittest, sys
from unittest.mock import patch
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"Tools"/"CharacterAssets"))
import import_characters as importer

class ImportBoundaryTests(unittest.TestCase):
    def test_four_base_ults_play_the_cut_at_the_body_marker(self):
        project = Path(__file__).resolve().parents[2]
        for hero, model, expected_time in (
            ("c1003", "rose", 1.1666), ("c1004", "silk", 0.5666),
            ("c1007", "wildred", 1.4333), ("c1008", "arming", 1.2333),
        ):
            with self.subTest(hero=hero):
                root = project / importer.RES / "Characters" / hero
                graph_id = importer.primary_graphs(hero)[3]
                graph = json.loads((root / "Configs/stagept" / (graph_id + ".graph.json")).read_text("utf-8"))
                skeleton = json.loads((root / "Animations/model" / (model + ".spine.json")).read_text("utf-8"))
                events = skeleton["animations"]["skill3"]["events"]
                self.assertAlmostEqual(next(e["time"] for e in events if e["name"] == "cutin"), expected_time, places=4)
                additions = graph.get("additions", {})
                nodes = additions.get("entities", [])
                self.assertEqual([e["etty"] for e in nodes], ["ONEVENT", "CUTIN"])
                self.assertEqual((nodes[0]["ani"], nodes[0]["name"]), ("skill3", "cutin"))
                self.assertIn({"from": nodes[0]["guid"], "to": nodes[1]["guid"]}, additions["connections"])
                clip = "sk_" + hero + "_3"
                self.assertEqual(nodes[1]["args"], clip)
                self.assertIn("cut/fhd/" + clip + ".webp", [a["source"] for a in graph["assets"]])
                movie = json.loads((root / "Effects/cut/fhd" / (clip + ".cutin.json")).read_text("utf-8"))
                self.assertGreater(len(movie["frames"]), 1)
                for index, frame in enumerate(movie["frames"]):
                    self.assertGreater(frame["durationSeconds"], 0)
                    self.assertTrue((root / "Effects/cut/fhd" / (clip + "_frames") / f"{index:04}.png").is_file())

    def test_character_allowlist_rejects_expansion(self):
        with self.assertRaises(ValueError): importer.select_characters(["c9999"])
        self.assertEqual(len(importer.select_characters(["c1001"])),1)
    def test_base_roster_uses_exact_graph_roots_in_id_order(self):
        self.assertEqual(list(importer.CHARACTERS), [f"c{i}" for i in (*range(1001, 1025), *range(1027, 1041), 1042, 1043, 1044, *range(1046, 1051), 1053, 1054, 1055, 1060, 1062, 1065, 1066, 1067, *range(1069, 1075), 1076, *range(1079, 1084), *range(1085, 1089))])
        self.assertEqual(importer.skill_ids("c1007"), [f"c_wilderd_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.skill_ids("c1009")[-1], "c_charlotte_skill_03_05")
        self.assertEqual(importer.skill_ids("c1010"), [f"c_jerato_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.skill_ids("c1013"), [f"c_cartuja_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.skill_ids("c1014"), [f"c_cedars_skill_{i:02}_new" for i in range(1, 4)])
        self.assertNotIn("stagept/c_cedars_skill_03_new.stg", importer.ANIMATION_CUTINS)
        self.assertEqual(importer.skill_ids("c1017"), [f"c_acha_skill_{i:02}" for i in range(1, 4)])
        self.assertNotIn("stagept/c_acha_skill_03.stg", importer.ANIMATION_CUTINS)
        self.assertEqual(importer.skill_ids("c1018"), [f"c_iterre_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_iterre_skill_03.stg"],
                         ("model/aither.scsp", "skill3", "cutin", "sk_c1018_3"))
        self.assertEqual(importer.skill_ids("c1019"), [f"c_ravi_skill_{i:02}" for i in range(1, 4)])
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_ravi_skill_03.stg"],
                         ("model/ravi.scsp", "skill3_1", "cutin", "sk_c1019_3"))
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_cartuja_skill_03.stg"],
                         ("model/kartua.scsp", "skill3", "cutin", "sk_c1013_3"))
        self.assertEqual(importer.ANIMATION_CUTINS["stagept/c_jerato_skill_03.stg"],
                         ("model/zerato.scsp", "skill3", "cutin", "sk_c1010_3"))

    def test_roster_41_to_50_preserves_ken_skill_variants_and_japanese_banks(self):
        ids = ("c1043", "c1044", "c1046", "c1047", "c1048", "c1049", "c1050", "c1053", "c1054", "c1055")
        self.assertEqual(len(importer.select_characters(ids)), 10)
        self.assertEqual(importer.presentation_ids("c1047"), [
            "c_ken_skill_01_02", "c_ken_skill_02", "c_ken_skill_03", "c_ken_skill_01_01"])
        self.assertEqual(importer.japanese_bank("c1044"), "sound/ja/voc3.audio_ja.bank")
        self.assertEqual(importer.japanese_bank("c1055"), "sound/ja/voc5.audio_ja.bank")

    def test_roster_51_to_60_only_includes_playable_graphs_and_sourced_forms(self):
        src=Path("D:/sevenepic/原始资源")
        if not src.is_dir():
            self.skipTest("Read-only asset source is unavailable.")
        ids=["c1060","c1065","c1066","c1067","c1069","c1070","c1071","c1072","c1073","c1074"]
        heroes=importer.select_characters(ids)
        closure=importer.Closure(src).build(heroes)
        records={row["id"]:row for row in importer.build_catalog(closure,heroes,True)["characters"]}
        self.assertEqual(sum(len(row["skills"]) for row in records.values()),34)
        luna=records["c1066"]["skills"]
        self.assertEqual([r["id"] for r in luna],["c_lunabianca_skill_01_1","c_lunabianca_skill_03","c_lunabianca_skill_01_2","c_lunabianca_skill_01_3"])
        self.assertEqual(luna[1]["displayNameKey"],"character.lunabianca.skill3")
        self.assertEqual(records["c1070"]["skills"][-1]["id"],"c_krau_skill_03_2")
        idol=records["c1067"]["statusForms"][0]
        self.assertEqual(idol["status"],"Idol")
        self.assertEqual(idol["modelAddress"],importer.address("model/tamarinne_t.scsp"))
        self.assertEqual(idol["timelineAddress"],importer.address("model/tamarinne_t.timeline"))
        self.assertEqual(idol["skin"],"default")
        self.assertIn("model/tamarinne_t.scsp",closure.dependencies["stagept/c_tamarinne_skill_03.stg"])
        self.assertIn("model/tamarinne_t.timeline",closure.dependencies["stagept/c_tamarinne_skill_03.stg"])
        self.assertEqual([row["id"] for row in records["c1067"]["skills"] if row.get("modelAddress")==idol["modelAddress"]],["c_tamarinne_skill_01_1","c_tamarinne_skill_02_2"])

    def test_roster_61_to_70_preserves_zeno_passive_and_base_voice_banks(self):
        self.assertEqual(importer.primary_graphs("c1083"), {1: "c_zeno_skill_01", 3: "c_zeno_skill_03"})
        for cid in ("c1076", "c1079", "c1080", "c1081", "c1082", "c1088"):
            self.assertEqual(importer.japanese_bank(cid), "sound/ja/voc3.audio_ja.bank")
        for cid in ("c1083", "c1085", "c1086", "c1087"):
            self.assertEqual(importer.japanese_bank(cid), "sound/ja/voc1.audio_ja.bank")

    def test_roster_61_to_70_keeps_proven_japanese_takes_and_missing_zeno_first_half(self):
        source = Path("D:/sevenepic/原始资源")
        if not source.is_dir():
            self.skipTest("Read-only asset source is unavailable.")
        heroes = importer.select_characters(["c1080", "c1082", "c1083", "c1086", "c1087"])
        catalog = importer.build_catalog(importer.Closure(source).build(heroes), heroes, True)
        self.assertEqual(catalog["missingJapaneseProgrammerKeys"], ["voc_c1083_skill3_4_1", "voc_c1083_skill3_4_1k"])
        self.assertEqual(len(catalog["japaneseProgrammerAliases"]), 11)

    def test_new_roster_keeps_native_graph_names_and_includes_burn_alternates(self):
        expected = {
            "c1020": "schuri", "c1021": "dinggo", "c1022": "ruell", "c1023": "kylon",
            "c1024": "iseria", "c1027": "charles", "c1028": "clarisa", "c1029": "leo",
            "c1030": "yuna", "c1031": "lots",
        }
        for character, model in expected.items():
            with self.subTest(character=character):
                self.assertEqual(importer.CHARACTERS[character][0], model)
                self.assertEqual(importer.skill_ids(character), [f"c_{model}_skill_{i:02}" for i in range(1, 4)])
                expected_alternate = [f"c_{model}_skill_01_2"] if character in ("c1022", "c1024") else []
                self.assertEqual(importer.presentation_ids(character), importer.skill_ids(character) + expected_alternate)

    def test_roster_31_to_40_graphs_follow_the_source_identity(self):
        ids = ["c1032", "c1033", "c1034", "c1035", "c1036", "c1037", "c1038", "c1039", "c1040", "c1042"]
        models = ["maya", "collie", "strage", "fuzzies", "crozet", "dominiel", "sez_gaien", "haste", "serilla", "tywin"]
        for cid, model in zip(ids, models):
            expected = [f"c_{model}_skill_{slot:02}" for slot in (1, 2, 3)]
            if cid == "c1035":
                expected.append("c_fuzzies_skill_03_2")
            with self.subTest(cid=cid):
                self.assertEqual(importer.presentation_ids(cid), expected)

    def test_voice_display_key_normalizes_hyphen_without_touching_sample_identity(self):
        sample = "voc_c1022_sk3_1-1"
        self.assertEqual(importer.voice_display_key(sample), "character.voice.voc_c1022_sk3_1_1")
        self.assertEqual(importer.voice_display_key("voc_c1022_idle"), "character.voice.voc_c1022_idle")
        self.assertEqual(importer.voice_display_key("voc_c1034_skill1_1(J)"), "character.voice.voc_c1034_skill1_1_j")
        with self.assertRaisesRegex(ValueError, "collide"):
            importer.voice_display_keys(["voc_c1034_skill1_1(J)", "voc_c1034_skill1_1_j"])

    def test_animation_index_is_derived_only_for_the_evidenced_graph_and_keeps_raw_fields(self):
        raw = {"entities": [{"etty": "ANI", "ani": "skill{idx}"}]}
        graph = {"source": raw}
        importer.apply_graph_animation_index("stagept/c_iterre_skill_01.stg", graph)
        self.assertEqual(graph, {"source": {"entities": [{"etty": "ANI", "ani": "skill{idx}"}]}, "animationIndex": 1})
        other = {"source": raw}
        importer.apply_graph_animation_index("stagept/c_iterre_m_skill_01.stg", other)
        self.assertEqual(other, {"source": raw})
        with self.assertRaisesRegex(ValueError, "No animation index token"):
            importer.apply_graph_animation_index("stagept/c_iterre_skill_01.stg",
                                                 {"source": {"entities": [{"etty": "ANI", "ani": "skill1"}]}})

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
