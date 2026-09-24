"""multilingual fixture provenance、coverage 與 Node 混用防護測試。"""

from __future__ import annotations

import json
import os
import subprocess
import unittest
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
TOKENIZER_PATH = REPOSITORY_ROOT / "test-data" / "multilingual-tokenizer-fixtures.json"
PARITY_PATH = REPOSITORY_ROOT / "test-data" / "multilingual-parity-fixtures.json"
EXPECTED_TOKENIZER_TEXTS = {
    "全家便利商店",
    "統一超商",
    "台灣大車隊",
    "國泰信用卡繳款",
    "薪資轉帳",
    "銀行手續費",
    "醫療院所",
    "高鐵",
    "Uber Eats Taiwan",
    "APPLE.COM/BILL",
    "AMAZON JP",
    "NETFLIX.COM",
    "FamilyMart Taipei",
    "7-ELEVEN 信義店",
    "UBER *EATS TW",
    "GOOGLE *YOUTUBE",
    "台鐵 TAIPEI",
    "街口支付 JKO",
    "LINE PAY",
}


def read_json(path: Path) -> dict:
    """讀取 repository fixture JSON。"""
    return json.loads(path.read_text(encoding="utf-8"))


class FixtureTests(unittest.TestCase):
    """驗證 fixture 集合足以作為 multilingual readiness 前置證據。"""

    def test_tokenizer_fixture_contract(self) -> None:
        """確認十九個固定字串、special IDs 與 source provenance 完整。"""
        fixture = read_json(TOKENIZER_PATH)
        self.assertEqual(fixture["schemaVersion"], 1)
        self.assertEqual(fixture["status"], "complete")
        self.assertEqual({item["text"] for item in fixture["tokenizer"]["fixtures"]}, EXPECTED_TOKENIZER_TEXTS)
        self.assertEqual(len(fixture["tokenizer"]["fixtures"]), 19)
        self.assertEqual(fixture["tokenizer"]["specialTokens"]["mask"]["token"], "<mask>")
        self.assertEqual(fixture["reference"]["revision"], "052592a15d198d9ad47da779604259b10b47b7aa")

    def test_parity_fixture_coverage_and_raw_values(self) -> None:
        """確認四群各五筆、未 rounding probabilities 與 masked sentinel evidence。"""
        fixture = read_json(PARITY_PATH)
        self.assertEqual(fixture["schemaVersion"], 1)
        self.assertEqual(fixture["coverage"]["fixtureCount"], 20)
        self.assertEqual(fixture["coverage"]["primaryGroups"], {
            "english": 5,
            "chinese": 5,
            "mixed": 5,
            "long-fuzzy-abbreviation": 5,
        })
        self.assertEqual(fixture["calibration"]["temperatureByOptions"], {})
        self.assertEqual(fixture["calibration"]["status"], "upstream-unfitted-preserved")
        self.assertTrue(any(
            any(len(str(value)) > 6 for value in answer["probabilities"])
            for item in fixture["fixtures"]
            for answer in item["expected"]
        ))
        for item in fixture["fixtures"]:
            self.assertIn("sourceManifestFiles", fixture["reference"])
            self.assertEqual(item["paddingSentinel"]["maskedLogit"], -10000.0)
            self.assertTrue(item["comparison"]["passed"])
            self.assertTrue(item["trace"]["inputs"]["marker_mask"])

    def test_node_generator_rejects_multilingual_profile(self) -> None:
        """即使未缺檔也禁止 Node English reference 產生 multilingual expected。"""
        result = subprocess.run(
            ["node", "tools/generate-reference-fixtures.mjs"],
            cwd=REPOSITORY_ROOT,
            env={**os.environ, "LAYA_PROFILE": "multilingual"},
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Python reference", result.stderr + result.stdout)


if __name__ == "__main__":
    unittest.main()
