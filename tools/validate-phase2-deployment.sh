#!/usr/bin/env bash
set -euo pipefail

# 在不把模型烘焙進映像的前提下執行 CPU smoke 與 2/3/4 GiB 記憶體矩陣。
readonly ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="laya-dotnet-poc:phase2"
MODEL_ROOT=""
OUTPUT_ROOT="$ROOT_DIR/reports/deployment-runs"
PROFILE="multilingual"

# 解析部署驗證所需的明確參數。
while (($# > 0)); do
    case "$1" in
        --image)
            IMAGE="$2"
            shift 2
            ;;
        --model-root)
            MODEL_ROOT="$2"
            shift 2
            ;;
        --output-root)
            OUTPUT_ROOT="$2"
            shift 2
            ;;
        --profile)
            PROFILE="$2"
            shift 2
            ;;
        *)
            printf 'Unknown option: %s\n' "$1" >&2
            exit 3
            ;;
    esac
done

if ! command -v docker >/dev/null 2>&1; then
    printf 'BLOCKED: Docker is not installed or not on PATH.\n' >&2
    exit 2
fi

if [[ -z "$MODEL_ROOT" || ! -d "$MODEL_ROOT" ]]; then
    printf 'BLOCKED: --model-root must point to an existing verified model directory.\n' >&2
    exit 2
fi

if [[ ! -f "$MODEL_ROOT/laya.onnx" || ! -f "$MODEL_ROOT/laya_config.json" ||
    ! -f "$MODEL_ROOT/tokenizer/tokenizer.json" || ! -f "$MODEL_ROOT/tokenizer/tokenizer_config.json" ]]; then
    printf 'BLOCKED: model mount is missing the verified ONNX/config/tokenizer assets at %s.\n' "$MODEL_ROOT" >&2
    exit 2
fi

mkdir -p "$OUTPUT_ROOT"
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    docker build --tag "$IMAGE" "$ROOT_DIR"
fi

status=0
for limit in 2g 3g 4g; do
    result_path="$OUTPUT_ROOT/deployment-${limit}.json"
    printf 'Running deployment smoke with memory limit %s...\n' "$limit"
    if docker run --rm \
        --memory "$limit" \
        --read-only \
        --mount "type=bind,source=$(realpath "$MODEL_ROOT"),target=/models,readonly" \
        --mount "type=bind,source=$(realpath "$OUTPUT_ROOT"),target=/reports" \
        "$IMAGE" \
        --profile "$PROFILE" \
        --model-root /models \
        --smoke \
        --smoke-count 100 >"$result_path"; then
        printf 'PASS: %s completed; raw output: %s\n' "$limit" "$result_path"
    else
        code=$?
        printf 'FAILED: %s exit code %s; raw output: %s\n' "$limit" "$code" "$result_path" >&2
        status=1
    fi
done

exit "$status"
