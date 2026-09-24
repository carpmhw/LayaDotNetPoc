"""執行固定 multilingual checkpoint 的官方 Python native reference probe。"""

from __future__ import annotations

import argparse
import json
import os
import platform
import sys
import traceback
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from reference import (
    copy_snapshot,
    compare_native_trace,
    load_agent,
    read_snapshot_manifest,
    reference_cases,
    validate_snapshot,
    weight_coverage,
    trace_request,
    validate_runtime_toolchain,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]


def write_report(path: Path, report: dict[str, Any]) -> None:
    """以固定 JSON 格式保存 probe 結果與失敗診斷。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def parse_args(arguments: list[str]) -> argparse.Namespace:
    """解析 source root、輸出報告與固定 CPU probe 參數。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", default="models/laya-multilingual/source")
    parser.add_argument("--output", default="reports/multilingual-reference-probe.json")
    return parser.parse_args(arguments)


def resolve_path(value: str) -> Path:
    """將 repo-relative 路徑解析為絕對路徑。"""
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def run_probe(source_root: Path) -> dict[str, Any]:
    """載入 snapshot、執行所有合成 cases 並回傳 machine-readable 結果。"""
    runtime = validate_runtime_toolchain()
    manifest = read_snapshot_manifest(source_root)
    verified_hashes = validate_snapshot(source_root, manifest)
    temporary = copy_snapshot(source_root, manifest)
    try:
        snapshot = Path(temporary.name)
        agent = load_agent(snapshot)
        coverage = weight_coverage(agent, snapshot)
        cases: list[dict[str, Any]] = []
        for case in reference_cases():
            native = agent.predict(case["state"], case["questions"])
            trace = trace_request(agent, case["state"], case["questions"])
            comparison = compare_native_trace(agent, case["questions"], native, trace)
            cases.append(
                {
                    "name": case["name"],
                    "state": case["state"],
                    "questions": case["questions"],
                    "native": native,
                    "trace": trace,
                    "comparison": comparison,
                }
            )
        passed = coverage["complete"] and all(case["comparison"]["passed"] for case in cases)
        return {
            "schemaVersion": 1,
            "status": "complete" if passed else "failed",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "source": {
                "repository": manifest["repository"],
                "revision": manifest["revision"],
                "manifestFiles": verified_hashes,
            },
            "toolchain": {
                "python": runtime["python"],
                "platform": runtime["platform"],
                "packages": runtime["packages"],
                "laya": runtime["packages"]["laya"]["version"],
                "torch": runtime["packages"]["torch"]["version"],
                "transformers": runtime["packages"]["transformers"]["version"],
                "numpy": runtime["packages"]["numpy"]["version"],
                "threads": int(os.environ.get("LAYA_REFERENCE_THREADS", "1")),
            },
            "configuration": {
                "maxLen": agent.cfg.get("max_len"),
                "headMaxLen": agent.cfg.get("head_max_len"),
                "temperature": agent.temperature_raw,
                "temperatureByOptions": agent.temperature_by_options_raw,
                "device": str(agent.device),
                "dtype": str(agent.dtype),
            },
            "weightCoverage": coverage,
            "cases": cases,
        }
    finally:
        temporary.cleanup()


def main(arguments: list[str] | None = None) -> int:
    """執行 probe 並以 0 complete、2 blocked、1 failed 回傳。"""
    args = parse_args(sys.argv[1:] if arguments is None else arguments)
    source_root = resolve_path(args.source_root)
    output = resolve_path(args.output)
    try:
        report = run_probe(source_root)
        write_report(output, report)
        print(json.dumps({"status": report["status"], "output": str(output)}, ensure_ascii=True))
        return 0 if report["status"] == "complete" else 1
    except (FileNotFoundError, ConnectionError) as exception:
        report = {
            "schemaVersion": 1,
            "status": "blocked",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "error": str(exception),
        }
        write_report(output, report)
        print(f"BLOCKED: {exception}", file=sys.stderr)
        return 2
    except Exception as exception:
        report = {
            "schemaVersion": 1,
            "status": "failed",
            "generatedUtc": datetime.now(timezone.utc).isoformat(),
            "error": str(exception),
            "traceback": traceback.format_exc(),
        }
        write_report(output, report)
        print(f"FAILED: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
