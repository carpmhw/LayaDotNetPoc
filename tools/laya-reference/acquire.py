"""固定 multilingual checkpoint 的顯式 acquisition 與 snapshot hash 驗證。"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from huggingface_hub import HfApi, hf_hub_download


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
LOCK_PATH = Path(__file__).with_name("toolchain.lock.json")
MANIFEST_NAME = "source-manifest.json"


def read_json(path: Path) -> dict[str, Any]:
    """讀取 JSON 檔並在格式錯誤時回報固定路徑。"""
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exception:
        raise RuntimeError(f"Could not read JSON lock or manifest: {path}: {exception}") from exception
    if not isinstance(value, dict):
        raise RuntimeError(f"JSON root must be an object: {path}")
    return value


def sha256_file(path: Path) -> str:
    """以串流方式計算單一 asset 的 SHA-256。"""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def resolve_output(value: str) -> Path:
    """將 acquisition 輸出路徑解析為絕對路徑，不允許空路徑。"""
    if not value.strip():
        raise ValueError("Output path must not be empty.")
    return (REPOSITORY_ROOT / value).resolve() if not Path(value).is_absolute() else Path(value).resolve()


def expected_files(lock: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """取得 lock 宣告的固定 checkpoint 檔案集合。"""
    files = lock.get("checkpoint", {}).get("files")
    if not isinstance(files, dict) or not files:
        raise RuntimeError("toolchain.lock.json has no checkpoint file list.")
    return files


def validate_manifest_metadata(manifest: dict[str, Any], lock: dict[str, Any], files: dict[str, Any]) -> None:
    """驗證既有 source manifest 不可偏離固定 checkpoint lock。"""
    checkpoint = lock["checkpoint"]
    for field in ("repository", "revision", "sourceUrl"):
        if manifest.get(field) != checkpoint.get(field):
            raise ValueError(f"Snapshot manifest does not match toolchain lock at {field}.")
    declared = manifest.get("files")
    if not isinstance(declared, dict) or set(declared) != set(files):
        raise ValueError("Snapshot manifest file set does not match toolchain lock.")
    for relative, expected in files.items():
        actual = declared[relative]
        if actual.get("status") != "verified" or \
                actual.get("sizeBytes") != expected.get("sizeBytes") or \
                str(actual.get("sha256", "")).lower() != str(expected.get("sha256", "")).lower():
            raise ValueError(f"Snapshot manifest does not match toolchain lock at {relative}.")


def validate_snapshot(
    output: Path,
    lock: dict[str, Any],
    require_manifest: bool = False,
) -> tuple[dict[str, Any], list[str]]:
    """驗證本機 snapshot 的存在、大小、內容 hash 與可選 manifest。"""
    manifest_path = output / MANIFEST_NAME
    missing: list[str] = []
    files: dict[str, Any] = {}
    for relative, expected in expected_files(lock).items():
        path = output / relative
        if not path.is_file():
            missing.append(relative)
            continue
        actual_size = path.stat().st_size
        actual_sha = sha256_file(path)
        expected_size = expected.get("sizeBytes")
        expected_sha = expected.get("sha256")
        if expected_size is not None and actual_size != expected_size:
            raise ValueError(
                f"Asset size mismatch for {relative}: expected {expected_size}, actual {actual_size}."
            )
        if expected_sha is not None and actual_sha.lower() != expected_sha.lower():
            raise ValueError(f"Asset SHA-256 mismatch for {relative}: expected {expected_sha}, actual {actual_sha}.")
        files[relative] = {
            "sizeBytes": actual_size,
            "sha256": actual_sha,
            "status": "verified",
        }

    if manifest_path.is_file():
        existing = read_json(manifest_path)
        validate_manifest_metadata(existing, lock, files)
    elif require_manifest:
        missing.append(MANIFEST_NAME)

    return files, missing


def write_manifest(output: Path, lock: dict[str, Any], files: dict[str, Any], command: str) -> None:
    """保存不含自身 hash 的可重算 snapshot provenance manifest。"""
    manifest = {
        "schemaVersion": 1,
        "status": "verified",
        "repository": lock["checkpoint"]["repository"],
        "revision": lock["checkpoint"]["revision"],
        "sourceUrl": lock["checkpoint"]["sourceUrl"],
        "acquiredUtc": datetime.now(timezone.utc).isoformat(),
        "command": command,
        "files": files,
    }
    (output / MANIFEST_NAME).write_text(
        json.dumps(manifest, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )


def acquire_online(output: Path, lock: dict[str, Any]) -> dict[str, Any]:
    """只從固定 Hugging Face revision 下載 lock 宣告的 source assets。"""
    checkpoint = lock["checkpoint"]
    repository = checkpoint["repository"]
    revision = checkpoint["revision"]
    info = HfApi().model_info(repository, revision=revision)
    if getattr(info, "sha", None) != revision:
        raise RuntimeError(f"Hugging Face resolved revision changed: expected {revision}, got {getattr(info, 'sha', None)}")

    output.mkdir(parents=True, exist_ok=True)
    for relative in expected_files(lock):
        hf_hub_download(
            repo_id=repository,
            filename=relative,
            revision=revision,
            local_dir=str(output),
        )

    files, missing = validate_snapshot(output, lock)
    if missing:
        raise RuntimeError(f"Acquisition did not produce required files: {', '.join(missing)}")
    write_manifest(output, lock, files, f"acquire.py --output {output}")
    return files


def parse_args(arguments: list[str]) -> argparse.Namespace:
    """解析 acquisition 的 online/offline 與輸出路徑參數。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", default="models/laya-multilingual/source")
    parser.add_argument("--offline", action="store_true")
    return parser.parse_args(arguments)


def main(arguments: list[str] | None = None) -> int:
    """執行固定 snapshot acquisition 並使用明確 exit code 分類結果。"""
    args = parse_args(sys.argv[1:] if arguments is None else arguments)
    output = resolve_output(args.output)
    lock = read_json(LOCK_PATH)
    try:
        if args.offline:
            files, missing = validate_snapshot(output, lock, require_manifest=True)
            if missing:
                print(f"BLOCKED: missing snapshot assets: {', '.join(missing)}", file=sys.stderr)
                return 2
            print(json.dumps({"status": "verified", "files": files}, ensure_ascii=True, indent=2))
            return 0

        files = acquire_online(output, lock)
        print(json.dumps({"status": "acquired", "files": files}, ensure_ascii=True, indent=2))
        return 0
    except FileNotFoundError as exception:
        print(f"BLOCKED: required acquisition asset is unavailable: {exception}", file=sys.stderr)
        return 2
    except (ConnectionError, TimeoutError) as exception:
        print(f"BLOCKED: fixed source acquisition could not complete: {exception}", file=sys.stderr)
        return 2
    except ValueError as exception:
        print(f"FAILED: fixed source asset validation failed: {exception}", file=sys.stderr)
        return 1
    except Exception as exception:
        print(f"FAILED: fixed source acquisition failed: {exception}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
