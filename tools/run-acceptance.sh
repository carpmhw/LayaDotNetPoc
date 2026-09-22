#!/usr/bin/env bash

# 執行包含模型依賴的完整 POC 驗收；缺資產時以 PARTIAL 並回傳非零狀態。
set -u

DOTNET_BIN="${DOTNET_BIN:-dotnet}"
MODEL_ROOT="${LAYA_MODEL_ROOT:-models/laya}"
failed=0

# 執行單一步驟並保留最後的失敗狀態。
run_step() {
    local name="$1"
    shift
    printf '\n== %s ==\n' "$name"
    if ! "$@"; then
        printf 'FAIL: %s\n' "$name" >&2
        failed=1
    fi
}

run_step "build" "$DOTNET_BIN" build LayaDotNetPoc.sln --no-restore

required_files=(
    "$MODEL_ROOT/laya.onnx"
    "$MODEL_ROOT/laya.onnx.data"
    "$MODEL_ROOT/laya_config.json"
    "$MODEL_ROOT/tokenizer/tokenizer.json"
    "$MODEL_ROOT/tokenizer/tokenizer_config.json"
    "test-data/parity-fixtures.json"
)
missing=0
for file in "${required_files[@]}"; do
    if [[ ! -f "$file" ]]; then
        printf 'PARTIAL: missing %s\n' "$file" >&2
        missing=1
    fi
done

if [[ "$missing" -eq 1 ]]; then
    printf '\nPARTIAL: model or reference prerequisites are missing; Gate 1/2 and integration were not run.\n' >&2
    exit 2
fi

run_step "tests" "$DOTNET_BIN" test LayaDotNetPoc.sln --no-restore
run_step "console-evaluation" "$DOTNET_BIN" run --project src/Laya.Console --no-restore -- --model-root "$MODEL_ROOT" --evaluate-csv test-data/transactions.csv
run_step "cold-start" "$DOTNET_BIN" run -c Release --project benchmarks/Laya.Benchmarks --no-restore -- --model-root "$MODEL_ROOT" --cold-start
run_step "warm-collection" "$DOTNET_BIN" run -c Release --project benchmarks/Laya.Benchmarks --no-restore -- --model-root "$MODEL_ROOT" --collect

if [[ "$failed" -ne 0 ]]; then
    printf '\nFAIL: one or more acceptance steps failed.\n' >&2
    exit 1
fi

printf '\nPASS: complete model-backed POC acceptance finished.\n'
