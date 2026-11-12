import hashlib
import json
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / 'Tools' / 'CharacterAssets'))
import import_characters as importer


class ImportCurvePolicyTests(unittest.TestCase):
    def verify(self, conversion):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            path = importer.RES / 'Characters/c1005/Animations/model/mercedes.spine.json'
            output = root / path
            output.parent.mkdir(parents=True)
            output.write_bytes(b'{}')
            manifest = {'entries': [{'address': 'characterlab/source/model/mercedes.scsp/json',
                'path': path.as_posix(), 'source': 'model/mercedes.scsp', 'type': 'spine-json',
                'outputSha256': hashlib.sha256(b'{}').hexdigest(), 'dependencies': [], 'conversion': conversion}]}
            importer.verify_manifest(root, manifest)

    def test_old_conversion_is_rejected_even_when_file_hash_matches(self):
        with self.assertRaisesRegex(ValueError, 'curve'):
            self.verify({'spineVersion': '2.1.27', 'converterVersion': 'astraloath-scsp/2'})

    def test_curve_semantics_marker_and_report_are_required_together(self):
        for fields in ({'curveTimelines': {}}, {'curveSemantics': 'scsp-v2-sentinel/1'},
                       {'curveSemantics': 'unknown', 'curveTimelines': {}},
                       {'curveSemantics': 'scsp-v2-sentinel/1', 'curveTimelines': []}):
            with self.subTest(fields=fields), self.assertRaisesRegex(ValueError, 'curve'):
                self.verify({'spineVersion': '2.1.27', **fields})

    def test_audited_empty_timeline_set_is_valid(self):
        self.verify({'spineVersion': '2.1.27', 'curveSemantics': 'scsp-v2-sentinel/1', 'curveTimelines': {}})

    def test_spine38_does_not_use_v2_sentinels(self):
        self.verify({'spineVersion': '3.8.99'})
