"""共用官方 Python reference 載入、tensor trace 與 native parity helper。"""

from __future__ import annotations

import hashlib
import importlib.metadata
import json
import os
import platform
import tempfile
from pathlib import Path
from typing import Any

import numpy as np
import torch
from laya import Agent
from laya.common import QTYPES, build_sequence, collate_items, render_options, serialize_state, temp_bucket
from safetensors import safe_open


EXPECTED_SOURCE_FILES = (
    "model.safetensors",
    "encoder/config.json",
    "rl_agent_config.json",
    "tokenizer/tokenizer.json",
    "tokenizer/tokenizer_config.json",
)
LOCK_PATH = Path(__file__).with_name("toolchain.lock.json")


def sha256_file(path: Path) -> str:
    """以固定 chunk size 計算 reference asset 的 SHA-256。"""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def read_toolchain_lock(lock_path: Path = LOCK_PATH) -> dict[str, Any]:
    """讀取固定 source checkpoint 與 runtime package lock。"""
    value = json.loads(lock_path.read_text(encoding="utf-8"))
    if not isinstance(value, dict):
        raise ValueError(f"Toolchain lock must be a JSON object: {lock_path}")
    return value


def _validate_manifest_against_lock(manifest: dict[str, Any], lock: dict[str, Any]) -> None:
    """驗證 source manifest 的 identity、檔案集合與 lock metadata 完全一致。"""
    checkpoint = lock.get("checkpoint")
    if not isinstance(checkpoint, dict):
        raise ValueError("Toolchain lock has no checkpoint object")
    for field in ("repository", "revision", "sourceUrl"):
        if manifest.get(field) != checkpoint.get(field):
            raise ValueError(f"Source manifest does not match toolchain lock at {field}")

    expected_files = checkpoint.get("files")
    actual_files = manifest.get("files")
    if not isinstance(expected_files, dict) or not isinstance(actual_files, dict):
        raise ValueError("Source manifest files do not match toolchain lock")
    if set(actual_files) != set(expected_files):
        raise ValueError("Source manifest file set does not match toolchain lock")
    for relative, expected in expected_files.items():
        actual = actual_files[relative]
        if not isinstance(actual, dict) or actual.get("status") != "verified":
            raise ValueError(f"Source manifest does not match toolchain lock at {relative} status")
        if actual.get("sizeBytes") != expected.get("sizeBytes") or \
                str(actual.get("sha256", "")).lower() != str(expected.get("sha256", "")).lower():
            raise ValueError(f"Source manifest does not match toolchain lock at {relative}")


def read_snapshot_manifest(source_root: Path, lock: dict[str, Any] | None = None) -> dict[str, Any]:
    """讀取並檢查 acquisition manifest 的固定 repository/revision 與 file metadata。"""
    path = source_root / "source-manifest.json"
    if not path.is_file():
        raise FileNotFoundError(f"Snapshot manifest not found: {path}")
    manifest = json.loads(path.read_text(encoding="utf-8"))
    if manifest.get("status") != "verified":
        raise ValueError(f"Snapshot manifest is not verified: {path}")
    _validate_manifest_against_lock(manifest, lock or read_toolchain_lock())
    return manifest


def validate_snapshot(
    source_root: Path,
    manifest: dict[str, Any],
    lock: dict[str, Any] | None = None,
) -> dict[str, str]:
    """重算所有 source asset size/hash，拒絕修改後的 reference snapshot。"""
    locked = lock or read_toolchain_lock()
    _validate_manifest_against_lock(manifest, locked)
    verified: dict[str, str] = {}
    files = locked["checkpoint"]["files"]
    for relative in EXPECTED_SOURCE_FILES:
        path = source_root / relative
        if not path.is_file():
            raise FileNotFoundError(f"Snapshot asset not found: {path}")
        actual = sha256_file(path)
        expected = files[relative]
        actual_size = path.stat().st_size
        if actual_size != expected["sizeBytes"]:
            raise ValueError(
                f"Snapshot asset size mismatch for {relative}: expected {expected['sizeBytes']}, actual {actual_size}"
            )
        if actual.lower() != expected["sha256"].lower():
            raise ValueError(f"Snapshot asset hash mismatch for {relative}: expected {expected['sha256']}, actual {actual}")
        verified[relative] = actual
    return verified


def verify_archive_hash(path: Path, expected: str) -> str:
    """驗證 locked wheel 或 source distribution 的 SHA-256。"""
    if not path.is_file():
        raise FileNotFoundError(f"Package archive not found: {path}")
    actual = sha256_file(path)
    if actual.lower() != expected.lower():
        raise ValueError(f"Package archive SHA-256 mismatch: expected {expected}, actual {actual}")
    return actual


def _normalise_package_name(value: str) -> str:
    """將 package 名稱轉成 wheel filename 可比較的 canonical form。"""
    return value.replace("-", "_").replace(".", "_").lower()


def _archive_roots(site_root: Path, archive_roots: list[Path] | None) -> list[Path]:
    """取得 package archive 搜尋路徑，避免默默使用未鎖定的 system package。"""
    if archive_roots is not None:
        return [path.resolve() for path in archive_roots]
    configured = os.environ.get("LAYA_REFERENCE_WHEEL_DIR")
    if configured:
        return [Path(value).resolve() for value in configured.split(os.pathsep) if value]
    return [site_root.parent / "laya-reference-wheels", site_root.parent]


def _find_package_archive(
    package: str,
    version: str,
    expected_sha256: str,
    roots: list[Path],
) -> Path:
    """尋找並驗證指定 runtime package 的 wheel archive。"""
    package_prefix = _normalise_package_name(package)
    version_prefix = version.replace("-", "_").lower()
    candidates: list[Path] = []
    for root in roots:
        if not root.is_dir():
            continue
        for path in root.glob("*.whl"):
            parts = path.name[:-4].split("-")
            if len(parts) >= 2 and _normalise_package_name(parts[0]) == package_prefix and \
                    parts[1].lower() == version_prefix:
                candidates.append(path)
    if not candidates:
        raise FileNotFoundError(f"Locked wheel archive is unavailable for {package}=={version}")
    matching = [path for path in candidates if sha256_file(path).lower() == expected_sha256.lower()]
    if len(matching) != 1:
        raise ValueError(f"No unique locked wheel archive matches {package}=={version}")
    return matching[0]


def validate_runtime_toolchain(
    lock: dict[str, Any] | None = None,
    site_root: Path | None = None,
    archive_roots: list[Path] | None = None,
) -> dict[str, Any]:
    """驗證 Python version、isolated distribution location、package versions 與 wheel hashes。"""
    locked = lock or read_toolchain_lock()
    runtime = locked.get("runtime")
    if not isinstance(runtime, dict):
        raise ValueError("Toolchain lock has no runtime object")
    site = (site_root or Path(os.environ.get("LAYA_REFERENCE_SITE", "/tmp/opencode/laya-reference-cpu-site"))).resolve()
    if not site.is_dir():
        raise FileNotFoundError(f"Isolated Python target does not exist: {site}")
    expected_python = runtime.get("python")
    if platform.python_version() != expected_python:
        raise ValueError(f"Python version mismatch: expected {expected_python}, actual {platform.python_version()}")

    packages: dict[str, Any] = {}
    roots = _archive_roots(site, archive_roots)
    for expected in runtime.get("packages", []):
        name = expected["name"]
        distribution = importlib.metadata.distribution(name)
        if distribution.version != expected["version"]:
            raise ValueError(
                f"Package version mismatch for {name}: expected {expected['version']}, actual {distribution.version}"
            )
        location = Path(str(distribution.locate_file(""))).resolve()
        if location != site and site not in location.parents:
            raise ValueError(f"Package {name} is outside isolated target: {location}")
        package_identity: dict[str, Any] = {"version": distribution.version, "location": str(location)}
        if expected.get("sha256"):
            archive = _find_package_archive(name, distribution.version, expected["sha256"], roots)
            package_identity["archive"] = str(archive)
            package_identity["sha256"] = verify_archive_hash(archive, expected["sha256"])
        packages[name] = package_identity

    source_distribution = os.environ.get("LAYA_REFERENCE_SOURCE_DISTRIBUTION")
    if source_distribution:
        expected_source_hash = locked["officialSource"]["sourceDistributionSha256"]
        verify_archive_hash(Path(source_distribution), expected_source_hash)

    return {
        "python": platform.python_version(),
        "platform": runtime.get("platform"),
        "packages": packages,
    }


def copy_snapshot(source_root: Path, manifest: dict[str, Any]) -> tempfile.TemporaryDirectory[str]:
    """複製 immutable snapshot 到 temporary root，避免 loader 修改官方檔案。"""
    temporary = tempfile.TemporaryDirectory(prefix="laya-reference-")
    target = Path(temporary.name)
    for relative in EXPECTED_SOURCE_FILES:
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes((source_root / relative).read_bytes())
    (target / "source-manifest.json").write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    return temporary


def load_agent(snapshot_root: Path) -> Agent:
    """以官方 laya Agent、CPU FP32 與 eval mode 載入固定 checkpoint。"""
    torch.manual_seed(0)
    np.random.seed(0)
    torch.set_num_threads(int(os.environ.get("LAYA_REFERENCE_THREADS", "1")))
    agent = Agent(str(snapshot_root), device="cpu")
    agent.model.eval()
    return agent


def weight_coverage(agent: Agent, snapshot_root: Path) -> dict[str, Any]:
    """檢查 safetensors key 與已建立模型參數的完整 coverage。"""
    weights_path = snapshot_root / "model.safetensors"
    with safe_open(str(weights_path), framework="pt", device="cpu") as handle:
        weight_keys = set(handle.keys())
    state_keys = set(agent.model.state_dict())
    parameter_keys = {name for name, _ in agent.model.named_parameters()}
    missing = sorted(state_keys - weight_keys)
    unexpected = sorted(weight_keys - state_keys)
    return {
        "parameterCount": len(parameter_keys),
        "stateKeyCount": len(state_keys),
        "weightKeyCount": len(weight_keys),
        "missingParameterKeys": missing,
        "unexpectedWeightKeys": unexpected,
        "complete": not missing and not unexpected,
    }


def reference_cases() -> list[dict[str, Any]]:
    """建立不依賴 Phase 2 交易資料的 native multilingual probe cases。"""
    return [
        {
            "name": "choice-english-two",
            "state": "merchant state",
            "questions": {
                "category": {
                    "type": "choice",
                    "instructions": "Pick one",
                    "criteria": {"food": "food purchase", "travel": "travel purchase"},
                }
            },
        },
        {
            "name": "choice-chinese-three",
            "state": "全家便利商店 台灣大車隊",
            "questions": {
                "category": {
                    "type": "choice",
                    "instructions": "選擇分類",
                    "criteria": {"餐飲": "餐飲消費", "交通": "交通消費", "其他": "其他交易"},
                }
            },
        },
        {
            "name": "noul-mixed-language",
            "state": {"description": "Uber Eats 台灣", "note": "APPLE.COM/BILL"},
            "questions": {
                "needs_review": {"type": "noul", "instructions": "這筆交易是否需要人工確認？"}
            },
        },
        {
            "name": "mixed-batch-choice-noul",
            "state": {"merchant": "統一超商", "amount": 125.5, "currency": "TWD"},
            "questions": {
                "category": {
                    "type": "choice",
                    "instructions": "Classify this mixed-language transaction",
                    "criteria": {"food": "food", "retail": "retail", "other": "other"},
                },
                "needs_review": {"type": "noul", "instructions": "Is this uncertain?"},
            },
        },
        {
            "name": "choice-eleven-options",
            "state": "AMAZON JP subscription",
            "questions": {
                "category": {
                    "type": "choice",
                    "instructions": "Choose one category",
                    "criteria": {
                        "food": "food",
                        "transport": "transport",
                        "shopping": "shopping",
                        "utilities": "utilities",
                        "rent": "rent",
                        "salary": "salary",
                        "transfer": "transfer",
                        "fees": "fees",
                        "cash": "cash",
                        "software": "software",
                        "other": "other",
                    },
                }
            },
        },
    ]


def trace_request(agent: Agent, state: Any, questions: dict[str, dict[str, Any]]) -> dict[str, Any]:
    """以官方 sequence/collate/model 路徑保存完整離散與 raw tensor trace。"""
    items: list[dict[str, Any]] = []
    for question_id, question_definition in questions.items():
        question = agent._to_internal(question_definition)
        sequence, markers = build_sequence(
            agent.tok,
            state,
            question,
            agent.cfg.get("max_len", 1024),
            agent.cfg.get("head_max_len", 256),
        )
        items.append(
            {
                "questionId": question_id,
                "question": question,
                "ids": sequence,
                "markers": markers,
                "qtype": QTYPES[question["t"]],
            }
        )

    batch = collate_items([items], agent.tok.pad_token_id)
    with torch.inference_mode():
        logits, act_logits = agent.model(
            batch["input_ids"],
            batch["attention_mask"],
            batch["marker_pos"],
            batch["marker_mask"],
            batch["qtype"],
        )

    def tensor_values(value: torch.Tensor) -> list[Any]:
        """將 trace tensor 轉成 JSON 可保存的 scalar list。"""
        return value.detach().cpu().reshape(-1).tolist()

    return {
        "serializedState": serialize_state(state),
        "sequences": [
            {
                "questionId": item["questionId"],
                "tokenIds": item["ids"],
                "markerPositions": item["markers"],
                "qtype": item["qtype"],
            }
            for item in items
        ],
        "inputs": {
            "shapes": {
                "input_ids": list(batch["input_ids"].shape),
                "attention_mask": list(batch["attention_mask"].shape),
                "marker_pos": list(batch["marker_pos"].shape),
                "marker_mask": list(batch["marker_mask"].shape),
                "qtype": list(batch["qtype"].shape),
            },
            "input_ids": tensor_values(batch["input_ids"]),
            "attention_mask": tensor_values(batch["attention_mask"]),
            "marker_pos": tensor_values(batch["marker_pos"]),
            "marker_mask": tensor_values(batch["marker_mask"]),
            "qtype": tensor_values(batch["qtype"]),
        },
        "outputs": {
            "logits": tensor_values(logits),
            "act_logits": tensor_values(act_logits),
            "act_probs": tensor_values(torch.softmax(act_logits.float(), dim=-1)),
            "logitsShape": list(logits.shape),
            "actLogitsShape": list(act_logits.shape),
            "actProbsShape": list(act_logits.shape),
        },
    }


def softmax(values: np.ndarray) -> np.ndarray:
    """以數值穩定方式計算 reference probability。"""
    shifted = values - np.max(values)
    result = np.exp(shifted)
    return result / np.sum(result)


def compare_native_trace(
    agent: Agent,
    questions: dict[str, dict[str, Any]],
    native: dict[str, Any],
    trace: dict[str, Any],
) -> dict[str, Any]:
    """比較 native predict 與 instrumentation 的答案、temperature 與 probabilities。"""
    logits = np.asarray(trace["outputs"]["logits"], dtype=np.float64).reshape(trace["outputs"]["logitsShape"])
    results: list[dict[str, Any]] = []
    for row, (question_id, definition) in enumerate(questions.items()):
        internal = agent._to_internal(definition)
        options = render_options(internal)
        question_type = QTYPES[internal["t"]]
        temperature = agent.temperature_by_options.get(
            temp_bucket(question_type, len(options)),
            agent.temperature[question_type],
        )
        probabilities = softmax(logits[row, : len(options)] / temperature)
        answer = native["answers"][question_id]
        if internal["t"] == "noul":
            expected = float(answer["noul"])
            actual = float(probabilities[1])
            max_error = abs(expected - actual)
            selected_match = True
        else:
            expected_values = np.asarray(
                [answer["probabilities"][option] for option in internal["crit"]],
                dtype=np.float64,
            )
            max_error = float(np.max(np.abs(expected_values - probabilities)))
            selected_match = answer["choice"] == list(internal["crit"])[int(np.argmax(probabilities))]
        results.append(
            {
                "questionId": question_id,
                "type": internal["t"],
                "optionCount": len(options),
                "temperature": temperature,
                "maxAbsoluteProbabilityError": max_error,
                "selectedAnswerMatchesTrace": selected_match,
                "finite": bool(np.isfinite(probabilities).all()),
            }
        )
    return {
        "passed": all(
            item["finite"] and item["selectedAnswerMatchesTrace"] and item["maxAbsoluteProbabilityError"] <= 1e-4
            for item in results
        ),
        "questions": results,
    }
