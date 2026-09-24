#!/usr/bin/env bash
set -euo pipefail

# 依 preflight→readiness→Phase 2 full 的依賴順序執行驗收，未知錯誤不繼續相依階段。
readonly ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-phase2-acceptance"
readonly OUTPUT_DIR="$ROOT_DIR/reports/phase2-acceptance/$RUN_ID"
readonly STAGE_MANIFEST="$OUTPUT_DIR/stage-manifest.json"
readonly ACCEPTANCE_RECORD="$OUTPUT_DIR/acceptance.json"
readonly COVERAGE_DIR="$OUTPUT_DIR/test-coverage"
readonly REUSE_FROM="${LAYA_ACCEPTANCE_REUSE_FROM:-}"
shopt -s globstar nullglob
mkdir -p "$OUTPUT_DIR" "$COVERAGE_DIR"
cd "$ROOT_DIR"

readonly UNTIL="${2:-full}"
if [[ "${1:-}" != "" && "${1:-}" != "--until" ]]; then
    printf 'ERROR: expected --until readiness or no arguments.\n' >&2
    exit 3
fi
if [[ "$UNTIL" != "full" && "$UNTIL" != "readiness" ]]; then
    printf 'ERROR: unsupported acceptance scope: %s\n' "$UNTIL" >&2
    exit 3
fi

export DOTNET_ROLL_FORWARD="${DOTNET_ROLL_FORWARD:-Major}"
export LAYA_SOURCE_COMMIT="${LAYA_SOURCE_COMMIT:-$(git rev-parse HEAD)}"
if [[ -z "${LAYA_SOURCE_DIRTY+x}" ]]; then
    if git diff --quiet; then export LAYA_SOURCE_DIRTY=false; else export LAYA_SOURCE_DIRTY=true; fi
fi
SOURCE_DIGEST_UNSET=false
if [[ -z "${LAYA_SOURCE_DIGEST+x}" ]]; then
    SOURCE_DIGEST_UNSET=true
fi
export LAYA_SOURCE_DIGEST="${LAYA_SOURCE_DIGEST:-$LAYA_SOURCE_COMMIT}"

printf '{"schemaVersion":1,"runId":"%s","status":"running","stages":[]}\n' \
    "$RUN_ID" > "$STAGE_MANIFEST"

# 計算目前模型、fixture、source 與 dataset 的依賴檔案集合。
dependency_paths() {
    local paths=(
        LayaDotNetPoc.sln
        Directory.*
        src/**/*.cs
        src/**/*.csproj
        src/**/*.json
        tests/**/*.cs
        tests/**/*.csproj
        benchmarks/**/*.cs
        benchmarks/**/*.csproj
        test-data/multilingual-reference-manifest.json
        test-data/multilingual-parity-fixtures.json
        test-data/multilingual-tokenizer-fixtures.json
        test-data/category-label-guideline.md
        test-data/transactions-phase2-manifest.json
        test-data/transactions-phase2.csv
        reports/multilingual-export-validation.json
        reports/multilingual-dotnet-parity.json
        reports/multilingual-readiness.json
        reports/phase2-runs/**/*.json
        models/laya/**/*
        models/laya-multilingual/current-bundle.json
        tools/**/*.cs
        tools/**/*.mjs
        tools/**/*.py
        tools/**/*.sh
        tools/laya-reference/toolchain.lock.json
    )
    local pointer_root
    pointer_root="$(jq -r '.root // empty' models/laya-multilingual/current-bundle.json 2>/dev/null || true)"
    if [[ -n "$pointer_root" ]]; then
        paths+=("$pointer_root"/**/*)
    fi
    printf '%s\0' "${paths[@]}" | while IFS= read -r -d '' path; do
        [[ -f "$path" ]] && printf '%s\0' "$path"
    done | sort -zu
}

# 計算目前模型、fixture、source 與 dataset 的依賴指紋，只在本次驗收計算一次。
DEPENDENCY_FINGERPRINT_CACHE=""
dependency_fingerprint() {
    if [[ -n "$DEPENDENCY_FINGERPRINT_CACHE" ]]; then
        printf '%s\n' "$DEPENDENCY_FINGERPRINT_CACHE"
        return 0
    fi
    local fingerprint
    fingerprint="$(while IFS= read -r -d '' path; do sha256sum "$path"; done < <(dependency_paths) | sha256sum | cut -d' ' -f1)"
    if [[ -z "$fingerprint" ]]; then
        return 1
    fi
    DEPENDENCY_FINGERPRINT_CACHE="$fingerprint"
    printf '%s\n' "$fingerprint"
}

if [[ "$SOURCE_DIGEST_UNSET" == true && "$LAYA_SOURCE_DIRTY" == true ]]; then
    export LAYA_SOURCE_DIGEST="$(dependency_fingerprint)"
fi

# 回收可被重用 stage 的 TRX 與 coverage artifact，避免只複製 log 造成後續 stage 讀不到輸入。
copy_reused_artifacts() {
    local name="$1"
    local previous_log="$2"
    local previous_directory
    previous_directory="$(dirname "$previous_log")"
    case "$name" in
        *-test)
            local category="${name%-test}"
            [[ -f "$previous_directory/${category}.trx" ]] || return 1
            cp "$previous_directory/${category}.trx" "$OUTPUT_DIR/${category}.trx"
            ;;
        *-coverage)
            local coverage_category="${name%-coverage}"
            [[ -f "$previous_directory/test-coverage/${coverage_category}.json" ]] || return 1
            cp "$previous_directory/test-coverage/${coverage_category}.json" \
                "$COVERAGE_DIR/${coverage_category}.json"
            ;;
        readiness_report)
            # readiness report 寫到 repository 固定路徑，不能只靠舊 log 宣稱已重用。
            return 1
            ;;
    esac
}

# 將 stage command、原始 exit、dependency fingerprint 與 log path 寫入 manifest。
record_stage() {
    local name="$1"
    local status="$2"
    local exit_code="$3"
    local started="$4"
    local finished="$5"
    local command="$6"
    local log_path="$7"
    local fingerprint
    fingerprint="$(dependency_fingerprint)"
    local artifact_paths='[]'
    case "$name" in
        *-test)
            local category="${name%-test}"
            artifact_paths="$(jq -nc --arg path "$OUTPUT_DIR/${category}.trx" '[$path]')"
            ;;
        *-coverage)
            local coverage_category="${name%-coverage}"
            artifact_paths="$(jq -nc --arg path "$COVERAGE_DIR/${coverage_category}.json" '[$path]')"
            ;;
        readiness_report)
            artifact_paths="$(jq -nc --arg json "$ROOT_DIR/reports/multilingual-readiness.json" \
                --arg markdown "$ROOT_DIR/reports/multilingual-readiness.md" '[$json, $markdown]')"
            ;;
    esac
    local temporary="$STAGE_MANIFEST.tmp"
    jq --arg name "$name" --arg status "$status" --argjson exitCode "$exit_code" \
        --arg started "$started" --arg finished "$finished" --arg command "$command" \
        --arg logPath "$log_path" --arg fingerprint "$fingerprint" --argjson artifactPaths "$artifact_paths" \
        '.stages += [{name:$name,status:$status,exitCode:$exitCode,startedUtc:$started,finishedUtc:$finished,command:$command,dependencyFingerprint:$fingerprint,logPath:$logPath,artifactPaths:$artifactPaths}]' \
        "$STAGE_MANIFEST" > "$temporary"
    mv "$temporary" "$STAGE_MANIFEST"
}

# 依 command 與 dependency fingerprint 重用上一個成功 stage，保留新的歷史 run。
try_reuse_stage() {
    local name="$1"
    local command_string="$2"
    local log_path="$3"
    if [[ "$name" == "full_final_inventory" ]]; then
        return 1
    fi
    if [[ -z "$REUSE_FROM" || ! -f "$REUSE_FROM" ]]; then
        return 1
    fi

    local fingerprint
    fingerprint="$(dependency_fingerprint)"
    local previous
    previous="$(jq -c --arg name "$name" '[.stages[] | select(.name == $name) | select(.status == "complete" or .status == "reused")] | last // empty' "$REUSE_FROM" 2>/dev/null)"
    if [[ -z "$previous" || "$(jq -r '.command' <<<"$previous")" != "$command_string" ||
        "$(jq -r '.dependencyFingerprint' <<<"$previous")" != "$fingerprint" ]]; then
        return 1
    fi

    local previous_log
    previous_log="$(jq -r '.logPath' <<<"$previous")"
    if [[ ! -f "$previous_log" ]]; then
        return 1
    fi

    if ! copy_reused_artifacts "$name" "$previous_log"; then
        return 1
    fi
    cp "$previous_log" "$log_path"
    local timestamp
    timestamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    record_stage "$name" "reused" 0 "$timestamp" "$timestamp" "$command_string" "$log_path"
    printf '== %s (reused, exit 0) ==\n' "$name"
    cat "$log_path"
    return 0
}

# 執行一個 stage，保留 stdout/stderr 並將失敗原樣回傳給 orchestrator。
run_step() {
    local name="$1"
    shift
    local log_path="$OUTPUT_DIR/$name.log"
    local command_string
    command_string="$(printf '%q ' "$@")"
    if try_reuse_stage "$name" "$command_string" "$log_path"; then
        return 0
    fi
    local started
    started="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    set +e
    "$@" > "$log_path" 2>&1
    local exit_code=$?
    set -e
    local finished
    finished="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    local status="complete"
    if [[ "$exit_code" -eq 2 ]]; then
        status="blocked"
    elif [[ "$exit_code" -ne 0 ]]; then
        status="failed"
    fi
    record_stage "$name" "$status" "$exit_code" "$started" "$finished" "$command_string" "$log_path"
    printf '== %s (%s, exit %s) ==\n' "$name" "$status" "$exit_code"
    cat "$log_path"
    return "$exit_code"
}

# 將 acceptance 最終狀態寫入獨立 record，避免 stage manifest 自我 hash 循環。
finish_acceptance() {
    local status="$1"
    local exit_code="$2"
    jq --arg status "$status" --argjson exitCode "$exit_code" \
        --arg scope "$UNTIL" --arg stageManifest "${STAGE_MANIFEST#"$ROOT_DIR/"}" \
        '{schemaVersion:1,runId:.runId,status:$status,scope:$scope,exitCode:$exitCode,stageManifest:$stageManifest,stages:.stages}' \
        "$STAGE_MANIFEST" > "$ACCEPTANCE_RECORD"
    jq --arg status "$status" '.status = $status' "$STAGE_MANIFEST" > "$STAGE_MANIFEST.tmp"
    mv "$STAGE_MANIFEST.tmp" "$STAGE_MANIFEST"
}

# 以 category 執行 dotnet test，並驗證 list、TRX 與 expected/executed/skipped 計數。
run_category() {
    local category="$1"
    local list_log="$OUTPUT_DIR/${category}-list.log"
    local trx_path="$OUTPUT_DIR/${category}.trx"
    local coverage_path="$COVERAGE_DIR/${category}.json"
    local model_root_env="$ROOT_DIR/models/laya"
    if [[ "$category" == MultilingualTokenizer || "$category" == MultilingualModel ]]; then
        model_root_env="$ROOT_DIR/models/laya"
    fi

    if ! run_step "${category}-list" env LAYA_MODEL_ROOT="$model_root_env" \
        LAYA_MULTILINGUAL_MODEL_ROOT="$MULTILINGUAL_MODEL_ROOT" \
        dotnet test tests/Laya.Core.Tests/Laya.Core.Tests.csproj --no-build --no-restore --list-tests \
        --filter "Category=$category"; then
        return 1
    fi
    if ! run_step "${category}-test" env LAYA_MODEL_ROOT="$model_root_env" \
        LAYA_MULTILINGUAL_MODEL_ROOT="$MULTILINGUAL_MODEL_ROOT" \
        dotnet test tests/Laya.Core.Tests/Laya.Core.Tests.csproj --no-build --no-restore \
        --filter "Category=$category" --logger "trx;LogFileName=$trx_path"; then
        return 1
    fi
    if ! run_step "${category}-coverage" bash -o pipefail -c \
        "node tools/validate-dotnet-test-coverage.mjs --category '$category' --list '$list_log' --trx '$trx_path' | tee '$coverage_path'"; then
        return 1
    fi
}

if ! run_step preflight_restore dotnet restore LayaDotNetPoc.sln; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step preflight_build dotnet build LayaDotNetPoc.sln --no-restore; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step preflight_assets jq -e '
    .status == "verified" and .profile == "multilingual" and
    (.verifiedManifestSha256 | length == 64)
  ' models/laya-multilingual/current-bundle.json; then
    finish_acceptance failed 1
    exit 1
fi
readonly MULTILINGUAL_MODEL_ROOT="$ROOT_DIR/$(jq -r '.root' models/laya-multilingual/current-bundle.json)"
if ! run_step preflight_python_report jq -e '
    .status == "complete" and .coverage.fixtureCount == 20 and
    .coverage.executedCount == 20 and (.failedFixtures | length) == 0
  ' reports/multilingual-export-validation.json; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step preflight_dotnet_report jq -e '
    .status == "complete" and .expectedCount == 20 and
    .executedCount == 20 and .passedCount == 20 and .skippedCount == 0
  ' reports/multilingual-dotnet-parity.json; then
    finish_acceptance failed 1
    exit 1
fi
readonly MULTILINGUAL_FIXTURE_SHA="$(sha256sum test-data/multilingual-parity-fixtures.json | cut -d' ' -f1)"
if ! run_step preflight_parity_identity jq -e -n \
    --slurpfile reference test-data/multilingual-reference-manifest.json \
    --slurpfile python reports/multilingual-export-validation.json \
    --slurpfile dotnet reports/multilingual-dotnet-parity.json \
    --slurpfile pointer models/laya-multilingual/current-bundle.json \
    --arg fixtureSha "$MULTILINGUAL_FIXTURE_SHA" '
    ($reference[0].runtimeBundle.manifestSha256 == $python[0].bundle.manifestSha256) and
    ($python[0].bundle.manifestSha256 == $dotnet[0].bundleManifestSha256) and
    ($dotnet[0].bundleManifestSha256 == $pointer[0].verifiedManifestSha256) and
    ($python[0].fixtureSha256 == $fixtureSha) and
    ($dotnet[0].fixtureSha256 == $fixtureSha)
  '; then
    finish_acceptance failed 1
    exit 1
fi

if ! run_step readiness_full_tests env LAYA_MODEL_ROOT="$ROOT_DIR/models/laya" \
    LAYA_MULTILINGUAL_MODEL_ROOT="$MULTILINGUAL_MODEL_ROOT" \
    dotnet test tests/Laya.Core.Tests/Laya.Core.Tests.csproj --no-build --no-restore; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step readiness_dataset_selector env LAYA_MODEL_ROOT="$ROOT_DIR/models/laya" \
    LAYA_MULTILINGUAL_MODEL_ROOT="$MULTILINGUAL_MODEL_ROOT" \
    dotnet test tests/Laya.Core.Tests/Laya.Core.Tests.csproj --no-build --no-restore \
    --filter 'FullyQualifiedName~Phase2DatasetSelectorTests'; then
    finish_acceptance failed 1
    exit 1
fi
for category in PureLogic EnglishTokenizer EnglishModel MultilingualTokenizer MultilingualModel; do
    if ! run_category "$category"; then
        finish_acceptance failed 1
        exit 1
    fi
done
if ! run_step readiness_english_smoke env LAYA_MODEL_ROOT="$ROOT_DIR/models/laya" \
    dotnet run --project src/Laya.Console --no-build -- --smoke --smoke-count 1; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step readiness_multilingual_smoke env LAYA_MULTILINGUAL_MODEL_ROOT="$MULTILINGUAL_MODEL_ROOT" \
    dotnet run --project src/Laya.Console --no-build -- --profile multilingual --smoke --smoke-count 1; then
    finish_acceptance failed 1
    exit 1
fi
if ! run_step readiness_report node tools/write-multilingual-readiness.mjs . "$COVERAGE_DIR"; then
    finish_acceptance failed 1
    exit 1
fi

if [[ "$UNTIL" == "readiness" ]]; then
    finish_acceptance complete 0
    printf 'Phase 2 engineering readiness complete. Artifacts: %s\n' "$OUTPUT_DIR"
    exit 0
fi

if [[ ! -d reports/phase2-runs ]]; then
    record_stage full_phase2 blocked 2 "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
        "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "reports/phase2-runs is missing" "$OUTPUT_DIR/full_phase2.log"
    printf 'BLOCKED: readiness is complete but reports/phase2-runs is missing; full Phase 2 remains unexecuted.\n' >&2
    finish_acceptance blocked 2
    exit 2
fi
if run_step full_final_inventory node tools/validate-phase2-evidence.mjs . --require-held-out; then
    finish_acceptance complete 0
    printf 'Phase 2 acceptance complete. Artifacts: %s\n' "$OUTPUT_DIR"
    exit 0
else
    final_exit_code=$?
    case "$final_exit_code" in
        2)
            finish_acceptance blocked 2
            exit 2
            ;;
        1)
            finish_acceptance failed 1
            exit 1
            ;;
        *)
            finish_acceptance failed "$final_exit_code"
            exit "$final_exit_code"
            ;;
    esac
fi
