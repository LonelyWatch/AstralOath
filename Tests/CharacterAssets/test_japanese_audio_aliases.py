import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools" / "CharacterAssets"))
import import_characters as importer


class JapaneseProgrammerAliasesTests(unittest.TestCase):
    EVENT = "event:/voc/skill/c_yupine_skill_02"
    OTHER = "event:/voc/skill/c_yupine_skin02_skill_02"
    KEYS = tuple(f"voc_c1016_skill2_{number}" for number in range(1, 4))
    SAMPLES = tuple(f"voc_c1016_skill2_{number}_j" for number in range(1, 4))

    def fixture(self):
        audio = {event: self.KEYS + self.SAMPLES for event in (self.EVENT, self.OTHER)}
        master = {self.EVENT: tuple(f"assets\\voc\\ja_1\\{key}.wav" for key in self.SAMPLES)}
        return audio, set(self.SAMPLES), master

    def test_alias_requires_its_exact_source_event(self):
        audio, bank, master = self.fixture()
        self.assertEqual(importer.verified_japanese_aliases({self.OTHER}, audio, bank, master), {})
        result = importer.verified_japanese_aliases({self.EVENT, self.OTHER}, audio, bank, master)
        self.assertEqual(result, {(self.EVENT, requested): sample for requested, sample in zip(self.KEYS, self.SAMPLES)})

    def test_alias_rejects_a_sample_without_japanese_provenance(self):
        audio, bank, master = self.fixture()
        master[self.EVENT] = tuple(f"assets\\voc\\en_1\\{key}.wav" for key in self.SAMPLES)
        with self.assertRaisesRegex(ValueError, "Invalid source Japanese alias"):
            importer.verified_japanese_aliases({self.EVENT}, audio, bank, master)

    def test_alias_rejects_a_sample_outside_the_loaded_bank(self):
        audio, bank, master = self.fixture()
        bank.remove(self.SAMPLES[0])
        with self.assertRaisesRegex(ValueError, "Invalid source Japanese alias"):
            importer.verified_japanese_aliases({self.EVENT}, audio, bank, master)

    def test_alias_rejects_a_request_outside_the_event(self):
        audio, bank, master = self.fixture()
        audio[self.EVENT] = self.SAMPLES
        with self.assertRaisesRegex(ValueError, "Invalid source Japanese alias"):
            importer.verified_japanese_aliases({self.EVENT}, audio, bank, master)


if __name__ == "__main__":
    unittest.main()