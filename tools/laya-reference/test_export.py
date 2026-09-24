"""ONNX exporter 的最小 CPU candidate regression test。"""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path

from export import build_manifest, export_candidate


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


class ExportTests(unittest.TestCase):
    """驗證固定 native reference 可以產生 target schema candidate。"""

    def test_cpu_export_does_not_use_unsupported_transformer_fast_path(self) -> None:
        """export 應避開 PyTorch fused TransformerEncoderLayer operator。"""
        with tempfile.TemporaryDirectory(prefix="laya-export-test-") as directory:
            report = export_candidate(
                REPOSITORY_ROOT / "models/laya-multilingual/source",
                REPOSITORY_ROOT / "test-data/multilingual-parity-fixtures.json",
                Path(directory),
                18,
            )
        self.assertEqual(report["status"], "candidate-staged")
        self.assertEqual(report["manifest"]["schemaVersion"], 2)
        self.assertEqual(set(report["manifest"]["schema"]["outputs"]), {"logits", "act_probs"})

    def test_manifest_uses_custom_source_root_for_provenance(self) -> None:
        """custom source root 的 source manifest hash 不可硬編碼預設路徑。"""
        with tempfile.TemporaryDirectory(prefix="laya-export-provenance-") as directory:
            root = Path(directory)
            source_root = root / "custom-source"
            output = root / "candidate"
            source_root.mkdir()
            output.mkdir()
            for relative in (
                "laya.onnx",
                "laya_config.json",
                "tokenizer/tokenizer.json",
                "tokenizer/tokenizer_config.json",
            ):
                path = output / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(relative.encode("utf-8"))
            source_manifest_path = source_root / "source-manifest.json"
            source_manifest = {
                "repository": "convaiinnovations/laya-multilingual",
                "revision": "052592a15d198d9ad47da779604259b10b47b7aa",
                "files": {},
            }
            source_manifest_path.write_text(json.dumps(source_manifest) + "\n", encoding="utf-8")
            fixture_path = root / "fixture.json"
            fixture_path.write_text(json.dumps({
                "reference": {
                    "repository": source_manifest["repository"],
                    "revision": source_manifest["revision"],
                    "sourceManifestFiles": source_manifest["files"],
                    "toolchainLockSha256": sha256(REPOSITORY_ROOT / "tools/laya-reference/toolchain.lock.json"),
                }
            }) + "\n", encoding="utf-8")
            manifest = build_manifest(
                output,
                source_root,
                source_manifest,
                {
                    "max_len": 512,
                    "head_max_len": 192,
                    "temperature": [],
                    "temperature_by_options": {},
                },
                {"inputs": {}, "outputs": {}},
                [],
                18,
                fixture_path,
            )

            self.assertEqual(
                manifest["reference"]["sourceManifestSha256"],
                sha256(source_manifest_path),
            )


def sha256(path: Path) -> str:
    """計算測試 artifact 的 SHA-256。"""
    return hashlib.sha256(path.read_bytes()).hexdigest()


if __name__ == "__main__":
    unittest.main()
