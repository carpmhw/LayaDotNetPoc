"""由固定官方 Python reference 產生 tokenizer 與 multilingual parity fixtures。"""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

import numpy as np

from laya.common import QTYPES, render_options, temp_bucket
from reference import (
    compare_native_trace,
    copy_snapshot,
    load_agent,
    read_snapshot_manifest,
    reference_cases,
    sha256_file,
    trace_request,
    validate_snapshot,
    validate_runtime_toolchain,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
TOKENIZER_CASES = (
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
)
NON_WHOLE_UNKNOWN_CASE = "全家 <unk> 便利商店"


def choice(instructions: str, criteria: dict[str, str]) -> dict[str, Any]:
    """建立保持 option order 的 Choice question。"""
    return {"type": "choice", "instructions": instructions, "criteria": criteria}


def noul(instructions: str) -> dict[str, Any]:
    """建立使用官方 false/true fallback 的 Noul question。"""
    return {"type": "noul", "instructions": instructions}


def fixture_cases() -> list[dict[str, Any]]:
    """建立四群各五筆且不使用 Phase 2 交易資料的 fixture request。"""
    cases: list[dict[str, Any]] = []

    english = [
        ("choice-english-two", "merchant state", {"category": choice("Pick one", {"food": "food", "travel": "travel"})}),
        ("choice-english-three", "Uber Eats Taiwan order", {"category": choice("Classify", {"food": "food", "travel": "travel", "retail": "retail"})}),
        ("choice-english-eleven", "AMAZON JP subscription", {"category": choice("Choose one", {name: name for name in ("food", "transport", "shopping", "utilities", "rent", "salary", "transfer", "fees", "cash", "software", "other")})}),
        ("noul-english", "The receipt description is ambiguous", {"needs_review": noul("Is this uncertain?")}),
        ("mixed-english", {"merchant": "NETFLIX.COM", "amount": 12.5}, {"category": choice("Classify", {"food": "food", "other": "other"}), "needs_review": noul("Is this uncertain?")}),
    ]
    chinese = [
        ("choice-chinese-two", "全家便利商店", {"category": choice("選擇分類", {"餐飲": "餐飲", "其他": "其他"})}),
        ("choice-chinese-three", "台灣大車隊 高鐵", {"category": choice("選擇分類", {"餐飲": "餐飲", "交通": "交通", "其他": "其他"})}),
        ("choice-chinese-eleven", "國泰信用卡繳款 銀行手續費", {"category": choice("選擇類別", {name: name for name in ("餐飲", "交通", "購物", "水電", "轉帳", "薪資", "手續費", "投資", "醫療", "娛樂", "其他")})}),
        ("noul-chinese", "這筆薪資轉帳需要確認", {"needs_review": noul("這筆交易是否需要人工確認？")}),
        ("mixed-chinese", {"merchant": "統一超商", "note": "街口支付 JKO"}, {"category": choice("選擇分類", {"餐飲": "餐飲", "購物": "購物", "其他": "其他"}), "needs_review": noul("是否需要確認？")}),
    ]
    mixed = [
        ("choice-mixed-two", "Uber Eats 台灣", {"category": choice("Classify 中英交易", {"food": "餐飲 food", "other": "其他 other"})}),
        ("choice-mixed-three", {"description": "APPLE.COM/BILL", "備註": "信用卡"}, {"category": choice("分類 transaction", {"food": "餐飲", "software": "software", "other": "其他"})}),
        ("choice-mixed-eleven", "FamilyMart Taipei 統一超商", {"category": choice("Choose category", {name: name for name in ("food", "transport", "shopping", "utilities", "transfer", "salary", "fees", "medical", "entertainment", "software", "other")})}),
        ("noul-mixed", "LINE PAY 是否為異常?", {"needs_review": noul("Is this transaction uncertain?")}),
        ("mixed-batch", {"merchant": "7-ELEVEN 信義店", "amount": 99, "currency": "TWD"}, {"category": choice("Classify", {"food": "餐飲", "retail": "零售", "other": "其他"}), "needs_review": noul("Is this uncertain?")}),
    ]
    long = [
        ("long-truncation-two", "transaction details " * 300, {"category": choice("Classify long state", {"food": "food", "other": "other"})}),
        ("long-truncation-three", "模糊交易描述 " * 300, {"category": choice("分類長文字", {"餐飲": "餐飲", "交通": "交通", "其他": "其他"})}),
        ("abbreviation-eleven", "UBER *EATS TW NETFLIX.COM GOOGLE *YOUTUBE 台鐵 TAIPEI", {"category": choice("Choose one", {name: name for name in ("food", "transport", "shopping", "utilities", "transfer", "salary", "fees", "investment", "medical", "entertainment", "other")})}),
        ("noul-abbreviation", "AMZN JP / CAT CC PAY / TPE", {"needs_review": noul("Is this abbreviated description uncertain?")}),
        ("long-mixed-batch", {"description": "薪資轉帳 " * 180, "merchant": "FamilyMart Taipei"}, {"category": choice("Classify long mixed state", {"transfer": "轉帳", "retail": "retail", "other": "other"}), "needs_review": noul("需要確認嗎？")}),
    ]
    for group, entries in (("english", english), ("chinese", chinese), ("mixed", mixed), ("long-fuzzy-abbreviation", long)):
        for name, state, questions in entries:
            cases.append({"id": name, "primaryGroup": group, "state": state, "questions": questions})
    return cases


def source_identity(manifest: dict[str, Any], runtime: dict[str, Any]) -> dict[str, Any]:
    """建立每個 fixture 都引用的固定 source/toolchain identity。"""
    lock_path = Path(__file__).with_name("toolchain.lock.json")
    return {
        "repository": manifest["repository"],
        "revision": manifest["revision"],
        "sourceManifestFiles": manifest["files"],
        "toolchainLockSha256": sha256_file(lock_path),
        "package": "laya",
        "packageVersion": runtime["packages"]["laya"]["version"],
        "torchVersion": runtime["packages"]["torch"]["version"],
        "transformersVersion": runtime["packages"]["transformers"]["version"],
        "runtimePackages": runtime["packages"],
    }


def special_tokens(agent: Any) -> dict[str, Any]:
    """保存 tokenizer 的 special token 字串與 IDs。"""
    names = ("bos", "eos", "cls", "sep", "mask", "pad", "unk")
    return {
        name: {
            "token": getattr(agent.tok, f"{name}_token", None),
            "id": getattr(agent.tok, f"{name}_token_id", None),
        }
        for name in names
    }


def generate_tokenizer_fixtures(agent: Any, identity: dict[str, Any]) -> dict[str, Any]:
    """產生十九個 tokenizer 案例與非整段 unknown 案例的 content IDs。"""
    fixtures = []
    for text in TOKENIZER_CASES:
        token_ids = agent.tok(text, add_special_tokens=False)["input_ids"]
        fixtures.append(
            {
                "text": text,
                "textSha256": hashlib.sha256(text.encode("utf-8")).hexdigest(),
                "contentTokenIds": token_ids,
                "contentTokenCount": len(token_ids),
            }
        )
    unknown_token_ids = agent.tok(NON_WHOLE_UNKNOWN_CASE, add_special_tokens=False)["input_ids"]
    return {
        "schemaVersion": 1,
        "status": "complete",
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "reference": identity,
        "tokenizer": {
            "specialTokens": special_tokens(agent),
            "modelMaxLength": agent.tok.model_max_length,
            "fixtures": fixtures,
            "nonWholeUnknownFixture": {
                "text": NON_WHOLE_UNKNOWN_CASE,
                "textSha256": hashlib.sha256(NON_WHOLE_UNKNOWN_CASE.encode("utf-8")).hexdigest(),
                "contentTokenIds": unknown_token_ids,
                "contentTokenCount": len(unknown_token_ids),
            },
        },
    }


def unrounded_expected(agent: Any, questions: dict[str, dict[str, Any]], trace: dict[str, Any]) -> list[dict[str, Any]]:
    """從 raw logits 建立未 rounding 的 native expected probabilities。"""
    logits = np.asarray(trace["outputs"]["logits"], dtype=np.float64).reshape(trace["outputs"]["logitsShape"])
    act_probs = np.asarray(trace["outputs"]["act_probs"], dtype=np.float64).reshape(trace["outputs"]["actProbsShape"])
    expected: list[dict[str, Any]] = []
    for row, (question_id, definition) in enumerate(questions.items()):
        internal = agent._to_internal(definition)
        options = render_options(internal)
        qtype = QTYPES[internal["t"]]
        temperature = agent.temperature_by_options.get(temp_bucket(qtype, len(options)), agent.temperature[qtype])
        values = logits[row, : len(options)] / temperature
        probabilities = np.exp(values - np.max(values))
        probabilities = probabilities / np.sum(probabilities)
        item = {
            "questionId": question_id,
            "type": internal["t"],
            "options": options,
            "temperature": temperature,
            "probabilities": probabilities.tolist(),
            "actProbability": act_probs[row].tolist(),
        }
        if internal["t"] == "choice":
            item["selectedIndex"] = int(np.argmax(probabilities))
            item["selectedOption"] = list(internal["crit"])[item["selectedIndex"]]
        else:
            item["pTrue"] = float(probabilities[1])
        expected.append(item)
    return expected


def generate_parity_fixtures(agent: Any, identity: dict[str, Any]) -> dict[str, Any]:
    """產生四群各五筆的完整 request、trace、raw outputs 與 expected。"""
    fixtures: list[dict[str, Any]] = []
    for case in fixture_cases():
        native = agent.predict(case["state"], case["questions"])
        trace = trace_request(agent, case["state"], case["questions"])
        comparison = compare_native_trace(agent, case["questions"], native, trace)
        if not comparison["passed"]:
            raise ValueError(f"Native instrumentation changed expected output for {case['id']}: {comparison}")
        fixtures.append(
            {
                "id": case["id"],
                "primaryGroup": case["primaryGroup"],
                "state": case["state"],
                "serializedState": trace["serializedState"],
                "questions": case["questions"],
                "expected": unrounded_expected(agent, case["questions"], trace),
                "nativeRounded": native,
                "trace": trace,
                "comparison": comparison,
                "paddingSentinel": {"inputIds": agent.tok.pad_token_id, "maskedLogit": -10000.0},
            }
        )
    group_order = ["english", "chinese", "mixed", "long-fuzzy-abbreviation"]
    groups = {group: sum(item["primaryGroup"] == group for item in fixtures) for group in group_order}
    if len(fixtures) < 20 or set(groups) != {"english", "chinese", "mixed", "long-fuzzy-abbreviation"} or any(count < 5 for count in groups.values()):
        raise ValueError(f"Fixture coverage is incomplete: count={len(fixtures)}, groups={groups}")
    return {
        "schemaVersion": 1,
        "status": "complete",
        "generatedUtc": datetime.now(timezone.utc).isoformat(),
        "reference": identity,
        "coverage": {"fixtureCount": len(fixtures), "primaryGroups": groups},
        "calibration": {
            "temperature": agent.temperature_raw,
            "temperatureByOptions": agent.temperature_by_options_raw,
            "status": "upstream-unfitted-preserved",
        },
        "fixtures": fixtures,
    }


def parse_args(arguments: list[str]) -> argparse.Namespace:
    """解析 source root 與兩個 fixture output path。"""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", default="models/laya-multilingual/source")
    parser.add_argument("--tokenizer-output", default="test-data/multilingual-tokenizer-fixtures.json")
    parser.add_argument("--parity-output", default="test-data/multilingual-parity-fixtures.json")
    return parser.parse_args(arguments)


def resolve_path(value: str) -> Path:
    """將 repo-relative fixture path 解析為絕對路徑。"""
    path = Path(value)
    return path.resolve() if path.is_absolute() else (REPOSITORY_ROOT / path).resolve()


def write_json(path: Path, value: dict[str, Any]) -> None:
    """建立父目錄並以 UTF-8 pretty JSON 保存 fixture。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def main(arguments: list[str] | None = None) -> int:
    """載入 fixed reference 並輸出 tokenizer/parity fixture collections。"""
    args = parse_args(sys.argv[1:] if arguments is None else arguments)
    source_root = resolve_path(args.source_root)
    runtime = validate_runtime_toolchain()
    manifest = read_snapshot_manifest(source_root)
    validate_snapshot(source_root, manifest)
    temporary = copy_snapshot(source_root, manifest)
    try:
        agent = load_agent(Path(temporary.name))
        identity = source_identity(manifest, runtime)
        write_json(resolve_path(args.tokenizer_output), generate_tokenizer_fixtures(agent, identity))
        write_json(resolve_path(args.parity_output), generate_parity_fixtures(agent, identity))
    finally:
        temporary.cleanup()
    print(json.dumps({"status": "complete", "tokenizerCount": len(TOKENIZER_CASES), "parityCount": 20}, ensure_ascii=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
