import json
from pathlib import Path
import sys
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "Tools" / "CharacterAssets"))
from ExportRosterQueue import build_queue, read_configured_ids, verify_source


class RosterQueueTests(unittest.TestCase):
    @staticmethod
    def source(last=1105):
        rows = [{"code": f"c{number}", "name": f"角色{number}"} for number in range(last, 1000, -1)]
        rows.extend([{"code": "c0001", "name": "演出记录"}, {"code": "c2001", "name": "其他编号段"}])
        return {"zh-CN": rows}

    def test_numeric_order_preserves_the_first_ten_and_counts_angelica_once(self):
        data = build_queue(self.source(), {"c1001", "c1062", "c1010"})
        self.assertEqual(len(data), 100)
        self.assertEqual([item["id"] for item in data[:11]],
                         [f"c{number}" for number in range(1001, 1010)] + ["c1062", "c1010"])
        self.assertEqual(data[-1]["id"], "c1100")
        self.assertEqual([item["ordinal"] for item in data], list(range(1, 101)))
        self.assertEqual(data[10], {"id": "c1010", "name": "角色1010", "ordinal": 11, "status": "configured"})
        self.assertEqual(data[11]["status"], "pending")
        self.assertEqual(sum(item["id"] == "c1062" for item in data), 1)

    def test_refuses_incomplete_roster_or_required_hero(self):
        with self.assertRaisesRegex(ValueError, "100"):
            build_queue(self.source(last=1099), set())
        missing_angelica = self.source()
        missing_angelica["zh-CN"] = [x for x in missing_angelica["zh-CN"] if x["code"] != "c1062"]
        with self.assertRaisesRegex(ValueError, "c1062"):
            build_queue(missing_angelica, set())

    def test_refuses_duplicate_or_malformed_selected_entry(self):
        source = self.source()
        source["zh-CN"].append({"code": "c1001", "name": "重复"})
        with self.assertRaisesRegex(ValueError, "c1001"):
            build_queue(source, set())
        source = self.source()
        next(row for row in source["zh-CN"] if row["code"] == "c1010")["name"] = None
        with self.assertRaisesRegex(ValueError, "c1010"):
            build_queue(source, set())

    def test_csv_validation_and_source_digest(self):
        self.assertEqual(read_configured_ids("##,id,nameKey\n,c1001,test\n,c1010,test\n"), {"c1001", "c1010"})
        with self.assertRaisesRegex(ValueError, "c1001"):
            read_configured_ids("##,id\n,c1001\n,c1001\n")
        raw = json.dumps(self.source(), ensure_ascii=False).encode("utf-8")
        with self.assertRaisesRegex(ValueError, "SHA-256"):
            verify_source(raw, "0" * 64)


if __name__ == "__main__":
    unittest.main()
