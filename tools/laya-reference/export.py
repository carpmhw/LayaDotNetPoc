"""由已驗證官方 native reference 產生 staging ONNX candidate bundle。"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

import numpy as np
import onnx
import torch
from onnx import TensorProto

from reference import (
    LOCK_PATH,
    copy_snapshot,
    load_agent,
    read_snapshot_manifest,
    read_toolchain_lock,
    sha256_file,
    validate_runtime_toolchain,
    validate_snapshot,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
LOCK_PATH = Path(__file__).with_name("toolchain.lock.json")


class ExportWrapper(torch.nn.Module):
    """只包裝官方 forward，將 act logits 轉成目標 act_probs output。"""

    def __init__(self, model: torch.nn.Module) -> None:
        """建立不改變 encoder/head/temperature 的 export wrapper。"""
        super().__init__()
        self.model = model

    def forward(
        self,
        input_ids: torch.Tensor,
        attention_mask: torch.Tensor,
        marker_pos: torch.Tensor,
        marker_mask: torch.Tensor,
        qtype: torch.Tensor,
    ) -> tuple[torch.Tensor, torch.Tensor]:
        """執行官方 forward 並只輸出 schema 要求的 logits/act_probs。"""
        logits, act_logits = self.model(input_ids, attention_mask, marker_pos, marker_mask, qtype)
        return logits.float(), torch.softmax(act_logits.float(), dim=-1)


def parse_args(arguments: list[str]) -> argparse.Namespace:
    """解析固定 source、staging root 與 exporter 參數。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", default="models/laya-multilingual/source")
    parser.add_argument("--fixture-path", default="test-data/multilingual-parity-fixtures.json")
    parser.add_argument("--output", default="models/laya-multilingual/staging")
    parser.add_argument("--opset", type=int, default=18)
    return parser.parse_args(arguments)


def resolve_path(value: str) -> Path:
    """將 repo-relative path 解析為絕對路徑。"""
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def read_json(path: Path) -> dict[str, Any]:
    """讀取 object-root JSON 並拒絕不完整 fixture。"""
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def tensor_from_fixture(values: list[Any], shape: list[int], dtype: torch.dtype) -> torch.Tensor:
    """將 fixture flatten values 重建成 export sample tensor。"""
    return torch.tensor(values, dtype=dtype).reshape(shape)


def sample_inputs(fixture_path: Path) -> tuple[torch.Tensor, ...]:
    """從獨立 Python fixture 建立含 batch>1 的 export example inputs。"""
    fixtures = read_json(fixture_path).get("fixtures", [])
    if not fixtures:
        raise ValueError(f"No parity fixtures available: {fixture_path}")
    fixture = next(
        (item for item in fixtures if item["trace"]["inputs"]["shapes"]["input_ids"][0] > 1),
        fixtures[0],
    )
    inputs = fixture["trace"]["inputs"]
    shapes = inputs["shapes"]
    return (
        tensor_from_fixture(inputs["input_ids"], shapes["input_ids"], torch.int64),
        tensor_from_fixture(inputs["attention_mask"], shapes["attention_mask"], torch.int64),
        tensor_from_fixture(inputs["marker_pos"], shapes["marker_pos"], torch.int64),
        tensor_from_fixture(inputs["marker_mask"], shapes["marker_mask"], torch.bool),
        tensor_from_fixture(inputs["qtype"], shapes["qtype"], torch.int64),
    )


def dynamic_axes() -> dict[str, dict[int, str]]:
    """宣告 B、S、M 的 legacy ONNX dynamic axes。"""
    return {
        "input_ids": {0: "batch", 1: "sequence"},
        "attention_mask": {0: "batch", 1: "sequence"},
        "marker_pos": {0: "batch", 1: "markers"},
        "marker_mask": {0: "batch", 1: "markers"},
        "qtype": {0: "batch"},
        "logits": {0: "batch", 1: "markers"},
        "act_probs": {0: "batch"},
    }


def dynamic_shapes() -> dict[str, dict[int, Any]]:
    """建立 torch.export 的 B/S/M 動態 shape 約束。"""
    from torch.export import Dim

    batch = Dim("batch", min=1, max=2)
    sequence = Dim("sequence", min=2, max=1024)
    markers = Dim("markers", min=1, max=11)
    return {
        "input_ids": {0: batch, 1: sequence},
        "attention_mask": {0: batch, 1: sequence},
        "marker_pos": {0: batch, 1: markers},
        "marker_mask": {0: batch, 1: markers},
        "qtype": {0: batch},
    }


def graph_iterator(graph: onnx.GraphProto) -> Iterable[onnx.GraphProto]:
    """遞迴列出主 graph 與 control-flow 子 graph。"""
    yield graph
    for node in graph.node:
        for attribute in node.attribute:
            if attribute.type == onnx.AttributeProto.GRAPH:
                yield from graph_iterator(attribute.g)
            elif attribute.type == onnx.AttributeProto.GRAPHS:
                for nested in attribute.graphs:
                    yield from graph_iterator(nested)


def external_references(model: onnx.ModelProto) -> list[str]:
    """從 ONNX graph 的 initializer 實際 external_data location 建立去重清單。"""
    references: set[str] = set()
    for graph in graph_iterator(model.graph):
        for initializer in graph.initializer:
            if initializer.data_location != TensorProto.EXTERNAL:
                continue
            for entry in initializer.external_data:
                if entry.key == "location":
                    references.add(entry.value)
    return sorted(references)


def file_metadata(root: Path, relative: str) -> dict[str, Any]:
    """保存 candidate bundle 單一檔案 size 與 SHA-256。"""
    path = root / relative
    return {"size": path.stat().st_size, "sha256": sha256_file(path)}


def validate_schema(model: onnx.ModelProto) -> dict[str, Any]:
    """驗證候選 graph 的目標五輸入／兩輸出名稱、dtype 與 rank。"""
    expected_inputs = {
        "input_ids": (TensorProto.INT64, 2),
        "attention_mask": (TensorProto.INT64, 2),
        "marker_pos": (TensorProto.INT64, 2),
        "marker_mask": (TensorProto.BOOL, 2),
        "qtype": (TensorProto.INT64, 1),
    }
    expected_outputs = {
        "logits": (TensorProto.FLOAT, 2),
        "act_probs": (TensorProto.FLOAT, 2),
    }

    def collect(values: Iterable[onnx.ValueInfoProto], expected: dict[str, tuple[int, int]], direction: str) -> dict[str, Any]:
        """驗證一組 graph value info 的名稱、tensor dtype 與 rank。"""
        result: dict[str, Any] = {}
        for value in values:
            if value.name not in expected:
                continue
            element_type = value.type.tensor_type.elem_type
            dimensions = value.type.tensor_type.shape.dim
            if element_type != expected[value.name][0] or len(dimensions) != expected[value.name][1]:
                raise ValueError(f"ONNX {direction} {value.name} has incompatible dtype/rank.")
            result[value.name] = {
                "dtype": TensorProto.DataType.Name(element_type),
                "rank": len(dimensions),
                "dimensions": [dimension.dim_param or dimension.dim_value for dimension in dimensions],
            }
        missing = sorted(set(expected) - set(result))
        if missing:
            raise ValueError(f"ONNX {direction} is missing required values: {', '.join(missing)}")
        return result

    return {
        "inputs": collect(model.graph.input, expected_inputs, "input"),
        "outputs": collect(model.graph.output, expected_outputs, "output"),
    }


def write_laya_config(source_root: Path, output: Path) -> dict[str, Any]:
    """將 upstream rl_agent_config 原樣映射到既有 laya_config schema。"""
    source = read_json(source_root / "rl_agent_config.json")
    config = {
        "max_len": source["max_len"],
        "head_max_len": source["head_max_len"],
        "temperature": source["temperature"],
        "temperature_by_options": source["temperature_by_options"],
    }
    (output / "laya_config.json").write_text(json.dumps(config, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
    return config


def copy_runtime_assets(source_root: Path, output: Path) -> None:
    """複製 tokenizer config 到 staging，不改寫 source snapshot。"""
    for relative in ("tokenizer/tokenizer.json", "tokenizer/tokenizer_config.json"):
        destination = output / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source_root / relative, destination)


def validate_fixture_reference(fixture_path: Path, source_manifest: dict[str, Any]) -> dict[str, Any]:
    """驗證 parity fixture 與 source snapshot 使用相同 checkpoint/toolchain。"""
    fixture = read_json(fixture_path)
    reference = fixture.get("reference")
    lock = read_toolchain_lock()
    expected = lock["checkpoint"]
    if not isinstance(reference, dict) or \
            reference.get("repository") != source_manifest.get("repository") or \
            reference.get("revision") != source_manifest.get("revision") or \
            reference.get("sourceManifestFiles") != source_manifest.get("files") or \
            reference.get("toolchainLockSha256") != sha256_file(LOCK_PATH):
        raise ValueError("Parity fixture reference does not match source checkpoint or toolchain lock")
    if reference.get("repository") != expected["repository"] or reference.get("revision") != expected["revision"]:
        raise ValueError("Parity fixture reference does not match locked checkpoint")
    return {
        "path": str(fixture_path),
        "sha256": sha256_file(fixture_path),
    }


def build_manifest(
    output: Path,
    source_root: Path,
    source_manifest: dict[str, Any],
    config: dict[str, Any],
    schema: dict[str, Any],
    external_files: list[str],
    opset: int,
    fixture_path: Path,
) -> dict[str, Any]:
    """建立不含自身 hash 循環的 candidate bundle manifest v2。"""
    file_names = ["laya.onnx", "laya_config.json", "tokenizer/tokenizer.json", "tokenizer/tokenizer_config.json"] + external_files
    files = {name: file_metadata(output, name) for name in file_names}
    lock_hash = sha256_file(LOCK_PATH)
    fixture = validate_fixture_reference(fixture_path, source_manifest)
    source_manifest_path = source_root / "source-manifest.json"
    return {
        "schemaVersion": 2,
        "status": "candidate-staged",
        "profile": "multilingual",
        "checkpoint": {
            "repository": source_manifest["repository"],
            "revision": source_manifest["revision"],
        },
        "reference": {
            "toolchainLockSha256": lock_hash,
            "sourceManifestSha256": sha256_file(source_manifest_path),
            "fixtureSha256": fixture["sha256"],
        },
        "export": {
            "opset": opset,
            "exporter": "torch.onnx.export",
            "dynamo": True,
            "attentionBackend": "torch-mha-fastpath-disabled-during-export",
            "computeDtype": "float32",
            "externalData": bool(external_files),
            "dynamicAxes": dynamic_axes(),
        },
        "schema": schema,
        "calibration": {
            "max_len": config["max_len"],
            "head_max_len": config["head_max_len"],
            "temperature": config["temperature"],
            "temperature_by_options": config["temperature_by_options"],
            "status": "upstream-unfitted-preserved",
        },
        "externalDataFiles": external_files,
        "files": files,
    }


def export_candidate(source_root: Path, fixture_path: Path, output: Path, opset: int) -> dict[str, Any]:
    """執行 CPU FP32 export、checker 與 candidate manifest 產生。"""
    source_manifest = read_snapshot_manifest(source_root)
    validate_snapshot(source_root, source_manifest)
    validate_runtime_toolchain()
    if (output / "laya.onnx").exists() or (output / "laya-bundle-manifest.json").exists():
        raise FileExistsError(f"Refusing to overwrite existing staging candidate: {output}")
    output.mkdir(parents=True, exist_ok=True)
    temporary = copy_snapshot(source_root, source_manifest)
    try:
        agent = load_agent(Path(temporary.name))
        inputs = sample_inputs(fixture_path)
        wrapper = ExportWrapper(agent.model).eval()
        fastpath_enabled = torch.backends.mha.get_fastpath_enabled()
        torch.backends.mha.set_fastpath_enabled(False)
        try:
            with torch.inference_mode():
                torch.onnx.export(
                    wrapper,
                    inputs,
                    str(output / "laya.onnx"),
                    input_names=["input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype"],
                    output_names=["logits", "act_probs"],
                    opset_version=opset,
                    dynamo=True,
                    external_data=True,
                    dynamic_shapes=dynamic_shapes(),
                    do_constant_folding=True,
                    training=torch.onnx.TrainingMode.EVAL,
                    verbose=False,
                )
        finally:
            torch.backends.mha.set_fastpath_enabled(fastpath_enabled)
        model = onnx.load(str(output / "laya.onnx"), load_external_data=False)
        onnx.checker.check_model(str(output / "laya.onnx"), full_check=False)
        schema = validate_schema(model)
        external_files = external_references(model)
        for relative in external_files:
            path = (output / relative).resolve()
            if not path.is_file() or not str(path).startswith(str(output.resolve()) + "/"):
                raise ValueError(f"ONNX graph references missing or out-of-root external data: {relative}")
        config = write_laya_config(source_root, output)
        copy_runtime_assets(source_root, output)
        manifest = build_manifest(output, source_root, source_manifest, config, schema, external_files, opset, fixture_path)
        (output / "laya-bundle-manifest.json").write_text(
            json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
            encoding="utf-8",
        )
        return {
            "schemaVersion": 1,
            "status": "candidate-staged",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "stagingRoot": str(output),
            "manifest": manifest,
        }
    finally:
        temporary.cleanup()


def main(arguments: list[str] | None = None) -> int:
    """執行 export candidate 並保存 complete 或 failed machine report。"""
    args = parse_args(sys.argv[1:] if arguments is None else arguments)
    source_root = resolve_path(args.source_root)
    fixture_path = resolve_path(args.fixture_path)
    output = resolve_path(args.output)
    report_path = output / "export-report.json"
    try:
        report = export_candidate(source_root, fixture_path, output, args.opset)
        report_path.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"status": report["status"], "output": str(output)}, ensure_ascii=True))
        return 0
    except FileNotFoundError as exception:
        report = {"schemaVersion": 1, "status": "blocked", "error": str(exception)}
        output.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(f"BLOCKED: {exception}", file=sys.stderr)
        return 2
    except Exception as exception:
        report = {
            "schemaVersion": 1,
            "status": "failed",
            "error": str(exception),
            "traceback": traceback.format_exc(),
        }
        output.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(f"FAILED: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
