"""以 CPU ONNX Runtime 比較固定官方 reference fixtures。"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np
import onnxruntime as ort


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


def read_json(path: Path) -> dict[str, Any]:
    """讀取 object-root JSON 並拒絕不完整 artifact。"""
    value = json.loads(path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"JSON root must be an object: {path}")
    return value


def sha256_file(path: Path) -> str:
    """以固定 chunk size 計算 artifact SHA-256。"""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def resolve_path(value: str) -> Path:
    """將 repo-relative path 解析為絕對路徑。"""
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def compare_values(
    name: str,
    expected: np.ndarray,
    actual: np.ndarray,
    absolute_tolerance: float,
    relative_tolerance: float,
    valid_mask: np.ndarray | None = None,
) -> dict[str, Any]:
    """比較有限有效值，排除 masked sentinel 並回報最早失敗位置。"""
    if not np.isfinite(absolute_tolerance) or not 0 <= absolute_tolerance <= 1e-4:
        raise ValueError("Absolute tolerance must be finite and at most 1e-4 without investigation provenance")
    if not np.isfinite(relative_tolerance) or relative_tolerance < 0:
        raise ValueError("Relative tolerance must be finite and nonnegative")
    expected_array = np.asarray(expected)
    actual_array = np.asarray(actual)
    result: dict[str, Any] = {
        "name": name,
        "expectedShape": list(expected_array.shape),
        "actualShape": list(actual_array.shape),
        "absoluteTolerance": absolute_tolerance,
        "relativeTolerance": relative_tolerance,
    }
    if expected_array.shape != actual_array.shape:
        return {**result, "passed": False, "failureReason": "shape-mismatch"}

    mask = np.ones(expected_array.shape, dtype=bool) if valid_mask is None else np.asarray(valid_mask, dtype=bool)
    if mask.shape != expected_array.shape:
        return {
            **result,
            "passed": False,
            "failureReason": "valid-mask-shape-mismatch",
            "validMaskShape": list(mask.shape),
        }

    masked_count = int((~mask).sum())
    valid_expected = expected_array[mask].astype(np.float64, copy=False)
    valid_actual = actual_array[mask].astype(np.float64, copy=False)
    result.update({"validCount": int(mask.sum()), "maskedCount": masked_count})
    if valid_expected.size == 0:
        return {**result, "passed": False, "failureReason": "no-valid-values"}
    if not np.isfinite(valid_expected).all() or not np.isfinite(valid_actual).all():
        return {**result, "passed": False, "failureReason": "non-finite-valid-value"}

    absolute_error = np.abs(valid_actual - valid_expected)
    relative_error = absolute_error / np.maximum(np.abs(valid_expected), 1e-12)
    failures = absolute_error > absolute_tolerance
    result.update(
        {
            "maxAbsoluteError": float(np.max(absolute_error)),
            "maxRelativeError": float(np.max(relative_error)),
            "passed": not bool(failures.any()),
        }
    )
    if failures.any():
        valid_indices = np.argwhere(mask)
        first_index = valid_indices[int(np.flatnonzero(failures)[0])].tolist()
        result.update({"failureReason": "value-tolerance", "firstFailureIndex": first_index})
    return result


def fixture_inputs(fixture: dict[str, Any]) -> dict[str, np.ndarray]:
    """從 fixture flatten values 建立 ONNX Runtime 輸入 tensors。"""
    inputs = fixture["trace"]["inputs"]
    shapes = inputs["shapes"]
    return {
        "input_ids": np.asarray(inputs["input_ids"], dtype=np.int64).reshape(shapes["input_ids"]),
        "attention_mask": np.asarray(inputs["attention_mask"], dtype=np.int64).reshape(shapes["attention_mask"]),
        "marker_pos": np.asarray(inputs["marker_pos"], dtype=np.int64).reshape(shapes["marker_pos"]),
        "marker_mask": np.asarray(inputs["marker_mask"], dtype=bool).reshape(shapes["marker_mask"]),
        "qtype": np.asarray(inputs["qtype"], dtype=np.int64).reshape(shapes["qtype"]),
    }


def stable_softmax(values: np.ndarray) -> np.ndarray:
    """以 float64 計算固定 reference probability，避免比較器額外誤差。"""
    shifted = values - np.max(values)
    exponent = np.exp(shifted)
    return exponent / np.sum(exponent)


def calibrated_probability_comparisons(
    fixture: dict[str, Any],
    logits: np.ndarray,
    absolute_tolerance: float,
    relative_tolerance: float,
) -> list[dict[str, Any]]:
    """依 fixture 保存的 temperature 比較每個問題的有效 probabilities。"""
    results: list[dict[str, Any]] = []
    for row, expected in enumerate(fixture["expected"]):
        count = len(expected["probabilities"])
        expected_values = np.asarray(expected["probabilities"], dtype=np.float64)
        actual_values = stable_softmax(logits[row, :count].astype(np.float64) / expected["temperature"])
        results.append(
            compare_values(
                f"probabilities:{expected['questionId']}",
                expected_values,
                actual_values,
                absolute_tolerance,
                relative_tolerance,
            )
        )
    return results


def verify_bundle_files(bundle_root: Path, manifest: dict[str, Any]) -> dict[str, Any]:
    """驗證 manifest 列出的 bundle 檔案存在、大小與 hash 一致。"""
    files = manifest.get("files")
    if not isinstance(files, dict) or not files:
        raise ValueError("Bundle manifest has no file hash map")
    checked: dict[str, Any] = {}
    for relative, metadata in files.items():
        path = (bundle_root / relative).resolve()
        if not str(path).startswith(str(bundle_root.resolve()) + "/") or not path.is_file():
            raise FileNotFoundError(f"Bundle file is missing or outside root: {relative}")
        actual_size = path.stat().st_size
        actual_hash = sha256_file(path)
        if actual_size != metadata.get("size") or actual_hash.lower() != str(metadata.get("sha256", "")).lower():
            raise ValueError(f"Bundle file hash mismatch: {relative}")
        checked[relative] = {"size": actual_size, "sha256": actual_hash}
    external_files = manifest.get("externalDataFiles", [])
    if sorted(external_files) != sorted(
        relative for relative in files if relative not in {"laya.onnx", "laya_config.json", "tokenizer/tokenizer.json", "tokenizer/tokenizer_config.json"}
    ):
        raise ValueError("Bundle externalDataFiles does not match manifest file set")
    return checked


def compare_fixture(
    session: ort.InferenceSession,
    fixture: dict[str, Any],
    absolute_tolerance: float,
    relative_tolerance: float,
) -> dict[str, Any]:
    """執行單一 fixture 並比較 raw logits、activation probabilities 與 calibrated probabilities。"""
    inputs = fixture_inputs(fixture)
    output_values = session.run(["logits", "act_probs"], inputs)
    actual_logits = np.asarray(output_values[0], dtype=np.float64)
    actual_act_probs = np.asarray(output_values[1], dtype=np.float64)
    trace_outputs = fixture["trace"]["outputs"]
    logits_shape = trace_outputs["logitsShape"]
    act_probs_shape = trace_outputs["actProbsShape"]
    expected_logits = np.asarray(trace_outputs["logits"], dtype=np.float64).reshape(logits_shape)
    expected_act_probs = np.asarray(trace_outputs["act_probs"], dtype=np.float64).reshape(act_probs_shape)
    raw_comparison = compare_values(
        "logits",
        expected_logits,
        actual_logits,
        absolute_tolerance,
        relative_tolerance,
        valid_mask=inputs["marker_mask"],
    )
    activation_comparison = compare_values(
        "act_probs",
        expected_act_probs,
        actual_act_probs,
        absolute_tolerance,
        relative_tolerance,
    )
    probability_comparisons = calibrated_probability_comparisons(
        fixture,
        actual_logits,
        absolute_tolerance,
        relative_tolerance,
    )
    selected_matches: list[bool] = []
    for row, expected in enumerate(fixture["expected"]):
        if expected["type"] != "choice":
            selected_matches.append(True)
            continue
        count = len(expected["probabilities"])
        selected_matches.append(int(np.argmax(actual_logits[row, :count])) == expected.get("selectedIndex", 1))
    comparisons = [raw_comparison, activation_comparison, *probability_comparisons]
    return {
        "id": fixture["id"],
        "shapes": fixture["trace"]["inputs"]["shapes"],
        "optionCounts": [len(item["probabilities"]) for item in fixture["expected"]],
        "raw": raw_comparison,
        "activation": activation_comparison,
        "probabilities": probability_comparisons,
        "selectedAnswerMatches": all(selected_matches),
        "passed": all(item["passed"] for item in comparisons) and all(selected_matches),
        "firstFailure": next((item for item in comparisons if not item["passed"]), None),
    }


def parse_args(arguments: list[str]) -> argparse.Namespace:
    """解析 bundle、fixture、tolerance 與 report 路徑。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-root", default="models/laya-multilingual/staging")
    parser.add_argument("--fixture-path", default="test-data/multilingual-parity-fixtures.json")
    parser.add_argument("--output", default="reports/multilingual-export-validation.json")
    parser.add_argument("--absolute-tolerance", type=float, default=1e-4)
    parser.add_argument("--relative-tolerance", type=float, default=0.0)
    return parser.parse_args(arguments)


def compare_bundle(args: argparse.Namespace) -> dict[str, Any]:
    """驗證 staging bundle 並執行全部 multilingual fixtures。"""
    bundle_root = resolve_path(args.model_root)
    fixture_path = resolve_path(args.fixture_path)
    manifest = read_json(bundle_root / "laya-bundle-manifest.json")
    fixtures_document = read_json(fixture_path)
    if manifest.get("status") not in {"candidate-staged", "verified"}:
        raise ValueError(f"Bundle is not staged or verified: {manifest.get('status')}")
    if fixtures_document.get("status") != "complete":
        raise ValueError("Parity fixture document is not complete")
    compare_values("tolerance-preflight", np.array([0.0]), np.array([0.0]), args.absolute_tolerance, args.relative_tolerance)
    checked_files = verify_bundle_files(bundle_root, manifest)
    session_options = ort.SessionOptions()
    session_options.intra_op_num_threads = 1
    session_options.inter_op_num_threads = 1
    session_options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    session = ort.InferenceSession(str(bundle_root / "laya.onnx"), sess_options=session_options, providers=["CPUExecutionProvider"])
    expected_inputs = {"input_ids", "attention_mask", "marker_pos", "marker_mask", "qtype"}
    expected_outputs = {"logits", "act_probs"}
    actual_inputs = {item.name for item in session.get_inputs()}
    actual_outputs = {item.name for item in session.get_outputs()}
    if actual_inputs != expected_inputs or actual_outputs != expected_outputs:
        raise ValueError(f"ORT schema mismatch: inputs={sorted(actual_inputs)}, outputs={sorted(actual_outputs)}")

    fixture_results = [
        compare_fixture(session, fixture, args.absolute_tolerance, args.relative_tolerance)
        for fixture in fixtures_document["fixtures"]
    ]
    shape_values = [item["shapes"] for item in fixture_results]
    option_counts = sorted({count for item in fixture_results for count in item["optionCounts"]})
    coverage = {
        "fixtureCount": len(fixture_results),
        "executedCount": len(fixture_results),
        "batchValues": sorted({shape["input_ids"][0] for shape in shape_values}),
        "sequenceValues": sorted({shape["input_ids"][1] for shape in shape_values}),
        "markerValues": sorted({shape["marker_pos"][1] for shape in shape_values}),
        "optionCounts": option_counts,
        "hasBatchGreaterThanOne": any(shape["input_ids"][0] > 1 for shape in shape_values),
        "hasMaxLength1024": any(shape["input_ids"][1] == 1024 for shape in shape_values),
    }
    coverage_requirements = {
        "fixtureCountAtLeast20": coverage["fixtureCount"] >= 20,
        "optionsInclude2_3_11": {2, 3, 11}.issubset(option_counts),
        "batchGreaterThanOne": coverage["hasBatchGreaterThanOne"],
        "maxLength1024": coverage["hasMaxLength1024"],
    }
    passed = all(item["passed"] for item in fixture_results) and all(coverage_requirements.values())
    return {
        "schemaVersion": 1,
        "status": "complete" if passed else "failed",
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "bundle": {
            "root": str(bundle_root),
            "manifestSha256": sha256_file(bundle_root / "laya-bundle-manifest.json"),
            "modelSha256": sha256_file(bundle_root / "laya.onnx"),
            "files": checked_files,
        },
        "fixturePath": str(fixture_path),
        "fixtureSha256": sha256_file(fixture_path),
        "reference": fixtures_document["reference"],
        "tolerance": {
            "absolute": args.absolute_tolerance,
            "relative": args.relative_tolerance,
            "maximumAllowed": 5e-4,
            "acceptanceRule": "absolute-only; relaxation requires investigation and is currently rejected",
        },
        "coverage": coverage,
        "coverageRequirements": coverage_requirements,
        "fixtures": fixture_results,
        "failedFixtures": [item["id"] for item in fixture_results if not item["passed"]],
    }


def main(arguments: list[str] | None = None) -> int:
    """執行 ORT comparison 並保存 complete 或 failed machine report。"""
    args = parse_args(sys.argv[1:] if arguments is None else arguments)
    output = resolve_path(args.output)
    try:
        report = compare_bundle(args)
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({"status": report["status"], "fixtures": report["coverage"]["executedCount"]}, ensure_ascii=True))
        return 0 if report["status"] == "complete" else 1
    except FileNotFoundError as exception:
        report = {"schemaVersion": 1, "status": "blocked", "error": str(exception)}
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(f"BLOCKED: {exception}", file=sys.stderr)
        return 2
    except Exception as exception:
        report = {
            "schemaVersion": 1,
            "status": "failed",
            "error": str(exception),
            "traceback": traceback.format_exc(),
        }
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report, ensure_ascii=True, indent=2) + "\n", encoding="utf-8")
        print(f"FAILED: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
