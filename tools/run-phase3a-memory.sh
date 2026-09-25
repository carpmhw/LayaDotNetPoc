#!/usr/bin/env bash
set -euo pipefail

# 依 baseline-first 順序執行 Phase 3A campaign、Docker stage 與 evidence validation。
readonly ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="pilot"
MODE="pilot"
CAMPAIGN_ID=""
OUTPUT_ROOT="$ROOT_DIR/reports/phase3a"
MODEL_ROOT=""
POLICY=""
RESUME=false

# 解析 campaign orchestration 的明確參數。
while (($# > 0)); do
    case "$1" in
        --stage)
            (($# >= 2)) || { printf '缺少 --stage 值。\n' >&2; exit 3; }
            STAGE="$2"
            shift 2
            ;;
        --mode)
            (($# >= 2)) || { printf '缺少 --mode 值。\n' >&2; exit 3; }
            MODE="$2"
            shift 2
            ;;
        --campaign-id)
            (($# >= 2)) || { printf '缺少 --campaign-id 值。\n' >&2; exit 3; }
            CAMPAIGN_ID="$2"
            shift 2
            ;;
        --output-root)
            (($# >= 2)) || { printf '缺少 --output-root 值。\n' >&2; exit 3; }
            OUTPUT_ROOT="$2"
            shift 2
            ;;
        --model-root)
            (($# >= 2)) || { printf '缺少 --model-root 值。\n' >&2; exit 3; }
            MODEL_ROOT="$2"
            shift 2
            ;;
        --policy)
            (($# >= 2)) || { printf '缺少 --policy 值。\n' >&2; exit 3; }
            POLICY="$2"
            shift 2
            ;;
        --resume)
            RESUME=true
            shift
            ;;
        *)
            printf '未知參數：%s\n' "$1" >&2
            exit 3
            ;;
    esac
done

# 驗證 host SDK／tooling、campaign identity 與 stage/mode 組合。
for executable in dotnet node; do
    if ! command -v "$executable" >/dev/null 2>&1; then
        printf 'BLOCKED：找不到必要 host 工具 %s。\n' "$executable" >&2
        exit 2
    fi
done
if [[ ! "$CAMPAIGN_ID" =~ ^[a-z0-9][a-z0-9_-]{2,79}$ ]]; then
    printf 'BLOCKED：--campaign-id 必須是 3-80 字元的小寫 ASCII slug。\n' >&2
    exit 2
fi
if [[ "$MODE" != "pilot" && "$MODE" != "formal" ]]; then
    printf '未知 mode：%s\n' "$MODE" >&2
    exit 3
fi
if [[ "$STAGE" == "all" && "$MODE" != "formal" ]]; then
    printf 'BLOCKED：all stage 僅能以 formal mode 執行；pilot 不得啟動完整驗收矩陣。\n' >&2
    exit 2
fi
if [[ "$MODE" == "formal" && ( -z "$POLICY" || ! -f "$POLICY" ) ]]; then
    printf 'BLOCKED：formal mode 必須提供 frozen memory policy。\n' >&2
    exit 2
fi
if [[ -n "$POLICY" ]]; then
    POLICY="$(realpath "$POLICY")"
fi

# 解析 model root 或 verified current pointer 至固定 versioned bundle。
resolve_model_root() {
    local requested_root="$1"
    local bundle_manifest
    local bundle_root
    if [[ -n "$requested_root" ]]; then
        requested_root="$(realpath "$requested_root")"
        if [[ -f "$requested_root/laya-bundle-manifest.json" ]]; then
            printf '%s' "$requested_root"
            return 0
        fi
        if [[ -f "$requested_root/current-bundle.json" ]]; then
            bundle_root="$(jq -r '.root' "$requested_root/current-bundle.json")"
            bundle_manifest="$ROOT_DIR/$bundle_root/laya-bundle-manifest.json"
            if [[ -f "$bundle_manifest" ]]; then
                realpath "$ROOT_DIR/$bundle_root"
                return 0
            fi
        fi
        printf 'BLOCKED：--model-root 必須是 verified versioned bundle 或其 multilingual pointer directory。\n' >&2
        return 2
    fi

    bundle_root="$(jq -r '.root' "$ROOT_DIR/models/laya-multilingual/current-bundle.json")"
    bundle_manifest="$ROOT_DIR/$bundle_root/laya-bundle-manifest.json"
    if [[ ! -f "$bundle_manifest" ]]; then
        printf 'BLOCKED：current multilingual bundle manifest is missing: %s\n' "$bundle_manifest" >&2
        return 2
    fi
    realpath "$ROOT_DIR/$bundle_root"
}

if [[ "$STAGE" != "aggregate" ]]; then
    MODEL_ROOT="$(resolve_model_root "$MODEL_ROOT")" || exit $?
fi
OUTPUT_ROOT="$(realpath -m "$OUTPUT_ROOT")"
mkdir -p "$OUTPUT_ROOT"

# 建置 net8.0 hosts；主要版本 roll-forward 支援僅安裝 SDK／runtime 10 的開發主機。
export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
dotnet restore "$ROOT_DIR/LayaDotNetPoc.sln"
dotnet build "$ROOT_DIR/LayaDotNetPoc.sln" --no-restore

# 以同一 campaign identity 透過獨立 child process 執行一個 stage。
run_campaign_stage() {
    local stage="$1"
    local -a arguments
    arguments=(--stage "$stage" --campaign-id "$CAMPAIGN_ID" --mode "$MODE" --output "$OUTPUT_ROOT")
    if [[ -n "$MODEL_ROOT" ]]; then
        arguments+=(--model-root "$MODEL_ROOT")
    fi
    if [[ -n "$POLICY" ]]; then
        arguments+=(--policy "$POLICY")
    fi
    if [[ "$RESUME" == "true" ]]; then
        arguments+=(--resume)
    fi
    dotnet run --no-build --project "$ROOT_DIR/benchmarks/Laya.MemoryBenchmarks" -- "${arguments[@]}"
}

# 執行 campaign、Docker deployment 與 aggregate/evidence validation。
case "$STAGE" in
    pilot|baseline|components|lifecycle|arena|shape|concurrency|reproduction)
        run_campaign_stage "$STAGE"
        ;;
    docker)
        [[ "$MODE" == "formal" ]] || { printf 'BLOCKED：Docker target matrix requires formal frozen policy mode.\n' >&2; exit 2; }
        "$ROOT_DIR/tools/validate-phase3a-deployment.sh" \
            --model-root "$MODEL_ROOT" \
            --policy "$POLICY" \
            --output-root "$OUTPUT_ROOT" \
            --campaign-id "$CAMPAIGN_ID"
        ;;
    aggregate)
        dotnet run --no-build --project "$ROOT_DIR/benchmarks/Laya.MemoryBenchmarks" -- \
            --stage aggregate \
            --campaign-id "$CAMPAIGN_ID" \
            --mode pilot \
            --output "$OUTPUT_ROOT"
        node "$ROOT_DIR/tools/validate-phase3a-evidence.mjs" "$OUTPUT_ROOT"
        ;;
    all)
        for stage in baseline components lifecycle arena shape concurrency reproduction; do
            run_campaign_stage "$stage"
        done
        "$ROOT_DIR/tools/validate-phase3a-deployment.sh" \
            --model-root "$MODEL_ROOT" \
            --policy "$POLICY" \
            --output-root "$OUTPUT_ROOT" \
            --campaign-id "$CAMPAIGN_ID"
        dotnet run --no-build --project "$ROOT_DIR/benchmarks/Laya.MemoryBenchmarks" -- \
            --stage aggregate \
            --campaign-id "$CAMPAIGN_ID" \
            --mode pilot \
            --output "$OUTPUT_ROOT"
        node "$ROOT_DIR/tools/validate-phase3a-evidence.mjs" "$OUTPUT_ROOT"
        ;;
    *)
        printf '未知 stage：%s\n' "$STAGE" >&2
        exit 3
        ;;
esac
