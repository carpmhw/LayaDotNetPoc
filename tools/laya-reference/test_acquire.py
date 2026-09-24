"""acquisition helper 的缺檔、hash 與固定 identity 負向測試。"""

from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from acquire import parse_args, validate_snapshot


class AcquisitionTests(unittest.TestCase):
    """驗證 acquisition 不會靜默使用替代 source。"""

    def test_missing_asset_is_reported_as_blocked_input(self) -> None:
        """缺少 lock 宣告檔案時回傳明確 missing 清單。"""
        with tempfile.TemporaryDirectory() as directory:
            files, missing = validate_snapshot(
                Path(directory),
                {
                    "checkpoint": {
                        "repository": "fixed/repository",
                        "revision": "fixed-revision",
                        "files": {"model.safetensors": {"sizeBytes": 4, "sha256": "abcd"}},
                    }
                },
            )
        self.assertEqual(files, {})
        self.assertEqual(missing, ["model.safetensors"])

    def test_changed_asset_fails_hash_validation(self) -> None:
        """檔案內容改變時拒絕使用 snapshot。"""
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.safetensors"
            path.write_bytes(b"changed")
            with self.assertRaisesRegex(ValueError, "SHA-256 mismatch"):
                validate_snapshot(
                    Path(directory),
                    {
                        "checkpoint": {
                            "repository": "fixed/repository",
                            "revision": "fixed-revision",
                            "files": {"model.safetensors": {"sizeBytes": 7, "sha256": "abcd"}},
                        }
                    },
                )

    def test_cli_has_no_floating_repository_or_revision_override(self) -> None:
        """CLI 僅接受輸出與 offline 選項，固定 repository/revision 不可覆寫。"""
        args = parse_args(["--offline", "--output", "snapshot"])
        self.assertTrue(args.offline)
        self.assertEqual(args.output, "snapshot")
        self.assertFalse(hasattr(args, "repository"))
        self.assertFalse(hasattr(args, "revision"))


if __name__ == "__main__":
    unittest.main()
