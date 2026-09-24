"""reference source provenance 與 package archive 驗證測試。"""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

import reference


class ReferenceProvenanceTests(unittest.TestCase):
    """驗證 reference 不會信任 custom root 自己提供的 provenance。"""

    def test_manifest_must_match_locked_checkpoint_files(self) -> None:
        """source manifest 的檔案 metadata 不可覆寫固定 lock。"""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            relative = "model.safetensors"
            payload = b"fixed source"
            (root / relative).write_bytes(payload)
            manifest = {
                "status": "verified",
                "repository": "fixed/repository",
                "revision": "fixed-revision",
                "sourceUrl": "https://example.invalid/fixed-revision",
                "files": {
                    relative: {
                        "sizeBytes": len(payload),
                        "sha256": hashlib.sha256(payload).hexdigest(),
                        "status": "verified",
                    }
                },
            }
            (root / "source-manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "lock"):
                reference.read_snapshot_manifest(root)

    def test_archive_hash_is_required_to_match_expected_distribution(self) -> None:
        """package archive hash 不符時必須拒絕 runtime artifact。"""
        with tempfile.TemporaryDirectory() as directory:
            archive = Path(directory) / "laya-0.3.7-py3-none-any.whl"
            archive.write_bytes(b"wrong wheel")

            self.assertTrue(hasattr(reference, "verify_archive_hash"))
            with self.assertRaisesRegex(ValueError, "SHA-256"):
                reference.verify_archive_hash(archive, "0" * 64)


if __name__ == "__main__":
    unittest.main()
