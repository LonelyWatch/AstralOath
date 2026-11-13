import sys
import unittest
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools" / "CharacterAssets"))
import import_characters as importer

class JapaneseBankSelectionTests(unittest.TestCase):
    def test_existing_roster_order_is_identical(self):
        self.assertEqual(importer.selected_banks(["c1001", "c1018"]), (
            "sound/master.bank", "sound/master.strings.bank", "sound/sfx1.bank", "sound/voc.event.bank",
            "sound/ja/voc1.audio_ja.bank", "sound/gerneal.bank",
        ))
    def test_voc3_is_opt_in_and_bank_is_shared(self):
        ravi = importer.japanese_bank("c1019")
        self.assertEqual(ravi, "sound/ja/voc3.audio_ja.bank")
        self.assertEqual(importer.selected_banks(["c1019", "c1001", "c1018"]).count(ravi), 1)
        self.assertNotIn(importer.DEFAULT_JAPANESE_BANK, importer.selected_banks(["c1019"]))
        self.assertEqual(importer.output_owner_path(ravi, {ravi: {"c1019"}}).as_posix(),
                         "Assets/Game/Modules/CharacterPresentation/Res/Shared/Sounds/" + ravi)
    def test_closure_only_marks_voc3_for_ravi_and_supports_no_banks(self):
        closure = importer.Closure(Path.cwd())
        with patch.object(importer, "safe_source") as paths:
            paths.return_value.is_file.return_value = False
            with patch.object(closure, "include") as include:
                characters=importer.select_characters(["c1001", "c1019"])
                closure.build(characters, True)
                registrations={entry.args for entry in include.call_args_list if entry.args[0].endswith(".bank")}
                self.assertIn((importer.DEFAULT_JAPANESE_BANK,"c1001"),registrations)
                self.assertIn((importer.japanese_bank("c1019"),"c1019"),registrations)
                self.assertNotIn((importer.japanese_bank("c1019"),"c1001"),registrations)
                include.reset_mock()
                closure.build(characters, False)
                self.assertFalse(any(entry.args[0].endswith(".bank") for entry in include.call_args_list))

if __name__ == "__main__":
    unittest.main()