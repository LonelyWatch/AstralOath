import tempfile, unittest, sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[2]/"Tools"/"CharacterAssets"))
import import_characters as importer

class ImportBoundaryTests(unittest.TestCase):
    def test_character_allowlist_rejects_expansion(self):
        with self.assertRaises(ValueError): importer.select_characters(["c9999"])
        self.assertEqual(len(importer.select_characters(["c1001"])),1)
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
