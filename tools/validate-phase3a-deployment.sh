#!/usr/bin/env bash
set -euo pipefail

# 驗證 multilingual verified bundle 在精確十進位 Docker memory limits 下的 CPU 可部署性。
readonly ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
IMAGE="laya-memory-probe:phase3a"
MODEL_ROOT=""
POLICY=""
OUTPUT_ROOT="$ROOT_DIR/reports/phase3a"
CAMPAIGN_ID=""

# 解析 Docker deployment matrix 所需的明確參數。
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
        --policy)
            POLICY="$2"
            shift 2
            ;;
        --output-root)
            OUTPUT_ROOT="$2"
            shift 2
            ;;
        --campaign-id)
            CAMPAIGN_ID="$2"
            shift 2
            ;;
        *)
            printf '未知參數：%s\n' "$1" >&2
            exit 3
            ;;
    esac
done

# 驗證 host 端 Docker、jq、Node 與 SHA-256 工具。
for executable in docker jq node sha256sum realpath getconf; do
    if ! command -v "$executable" >/dev/null 2>&1; then
        printf 'BLOCKED：找不到必要 host 工具 %s。\n' "$executable" >&2
        exit 2
    fi
done

# 驗證 model root 是 versioned multilingual bundle，而非 pointer container 或 English model。
if [[ -z "$MODEL_ROOT" || ! -d "$MODEL_ROOT" ]]; then
    printf 'BLOCKED：--model-root 必須指向本機 verified multilingual versioned bundle。\n' >&2
    exit 2
fi
MODEL_ROOT="$(realpath "$MODEL_ROOT")"
if [[ ! -f "$MODEL_ROOT/laya-bundle-manifest.json" || ! -f "$MODEL_ROOT/laya.onnx" ||
    ! -f "$MODEL_ROOT/laya.onnx.data" || ! -f "$MODEL_ROOT/laya_config.json" ||
    ! -f "$MODEL_ROOT/tokenizer/tokenizer.json" || ! -f "$MODEL_ROOT/tokenizer/tokenizer_config.json" ]]; then
    printf 'BLOCKED：verified bundle 缺少 ONNX、external data、config 或 tokenizer：%s\n' "$MODEL_ROOT" >&2
    exit 2
fi
if ! jq -e '.schemaVersion == 2 and .status == "verified" and .profile == "multilingual" and (.checkpoint.revision | type == "string")' \
    "$MODEL_ROOT/laya-bundle-manifest.json" >/dev/null; then
    printf 'BLOCKED：model manifest 不是 verified multilingual schemaVersion=2 bundle。\n' >&2
    exit 2
fi

# 驗證正式 threshold policy 與固定十進位 3 GB target。
if [[ -z "$POLICY" || ! -f "$POLICY" ]]; then
    printf 'BLOCKED：--policy 必須指向 pilot 後凍結的 memory policy。\n' >&2
    exit 2
fi
POLICY="$(realpath "$POLICY")"
if ! jq -e '.schemaVersion == 1 and .status == "frozen" and .targetMemoryLimitBytes == 3000000000' \
    "$POLICY" >/dev/null; then
    printf 'BLOCKED：policy 必須為 schema 1 frozen policy，target 固定 3000000000 bytes。\n' >&2
    exit 2
fi

# 驗證 campaign ID，避免產生不安全容器名稱或輸出路徑。
if [[ ! "$CAMPAIGN_ID" =~ ^[a-z0-9][a-z0-9_-]{2,79}$ ]]; then
    printf 'BLOCKED：--campaign-id 必須是 3-80 字元的小寫 ASCII slug。\n' >&2
    exit 2
fi

# 確認 Docker daemon 可用後才建立證據目錄或建置 image。
if ! docker info >/dev/null 2>&1; then
    printf 'BLOCKED：Docker daemon 無法連線。\n' >&2
    exit 2
fi
mkdir -p "$OUTPUT_ROOT/docker/$CAMPAIGN_ID"
OUTPUT_ROOT="$(realpath "$OUTPUT_ROOT")"
readonly RESULT_ROOT="$OUTPUT_ROOT/docker/$CAMPAIGN_ID"
if [[ -e "$RESULT_ROOT/phase3a-docker-memory.json" || -e "$RESULT_ROOT/phase3a-docker-memory.md" ]]; then
    printf 'BLOCKED：此 campaign 已有 Docker summary；請使用新的 --campaign-id，避免覆寫 evidence。\n' >&2
    exit 2
fi
if ! docker image inspect "$IMAGE" >/dev/null 2>&1; then
    docker build --tag "$IMAGE" --file "$ROOT_DIR/tools/Laya.MemoryProbe/Dockerfile" "$ROOT_DIR"
fi

# 擷取 host source identity；run evidence output 不納入 dirty-source hash。
SOURCE_IDENTITY="$(node "$ROOT_DIR/tools/phase3a-source-identity.mjs" "$ROOT_DIR")"
SOURCE_COMMIT="$(jq -r '.commit' <<<"$SOURCE_IDENTITY")"
SOURCE_DIRTY="$(jq -r '.dirty' <<<"$SOURCE_IDENTITY")"
SOURCE_DIRTY_IDENTITY="$(jq -r '.dirtyIdentity // empty' <<<"$SOURCE_IDENTITY")"
CONTAINER_RUNTIME="docker-$(docker version --format '{{.Server.Version}}')"
PAGE_SIZE_BYTES="$(getconf PAGESIZE)"
readonly SOURCE_COMMIT SOURCE_DIRTY SOURCE_DIRTY_IDENTITY CONTAINER_RUNTIME
readonly PAGE_SIZE_BYTES

# 將 host cgroup counters 與 process VmRSS 直接由 /proc 讀取，不在容器內額外啟動 sampler process。
sample_container_memory() {
    local container_name="$1"
    local result_dir="$2"
    local container_pid
    local cgroup_line
    local hierarchy
    local controllers
    local cgroup_path
    local cgroup_dir
    local memory_current
    local memory_peak
    local swap_current
    local rss_bytes="null"
    local sample_time

    container_pid="$(docker inspect --format '{{.State.Pid}}' "$container_name" 2>/dev/null || true)"
    if [[ ! "$container_pid" =~ ^[1-9][0-9]*$ || ! -r "/proc/$container_pid/cgroup" ]]; then
        sample_time="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
        printf '%s,null,null,null,null,null,container-pid-or-cgroup-unavailable\n' "$sample_time" \
            >>"$result_dir/container-memory.csv"
        return 0
    fi

    while IFS=: read -r hierarchy controllers cgroup_path; do
        if [[ "$hierarchy" == "0" ]]; then
            break
        fi
    done < "/proc/$container_pid/cgroup"
    cgroup_dir="/sys/fs/cgroup/${cgroup_path#/}"
    memory_current="$(read_cgroup_value "$cgroup_dir/memory.current")"
    memory_peak="$(read_cgroup_value "$cgroup_dir/memory.peak")"
    swap_current="$(read_cgroup_value "$cgroup_dir/memory.swap.current")"
    while IFS= read -r status_line; do
        if [[ "$status_line" == VmRSS:* ]]; then
            local rss_kilobytes
            local rss_unit
            read -r _ rss_kilobytes rss_unit <<<"$status_line"
            if [[ "$rss_kilobytes" =~ ^[0-9]+$ && "$rss_unit" == "kB" ]]; then
                rss_bytes="$((rss_kilobytes * 1024))"
            fi
            break
        fi
    done < "/proc/$container_pid/status"
    sample_time="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    local unavailable_reason=""
    if [[ "$memory_current" == "null" || "$memory_peak" == "null" || "$swap_current" == "null" || "$rss_bytes" == "null" ]]; then
        unavailable_reason="one-or-more-cgroup-or-process-counters-unavailable"
    fi
    printf '%s,%s,%s,%s,%s,%s,%s\n' "$sample_time" "$container_pid" "$memory_current" \
        "$memory_peak" "$swap_current" "$rss_bytes" "$unavailable_reason" >>"$result_dir/container-memory.csv"
}

# 將不可讀 cgroup counter 明確保存為 null，而非偽造零值。
read_cgroup_value() {
    local path="$1"
    local value
    if [[ -r "$path" ]]; then
        IFS= read -r value <"$path" || true
        if [[ "$value" =~ ^[0-9]+$ ]]; then
            printf '%s' "$value"
            return 0
        fi
    fi
    printf 'null'
}

# 執行一個具名容器 run、週期採集 external counters，並在清除前保存 inspect／OOM／exit evidence。
run_container_probe() {
    local limit_bytes="$1"
    local case_name="$2"
    local request_count="$3"
    local replicate="$4"
    local load_only="$5"
    local campaign_token
    local run_id
    local container_name
    local result_dir
    local -a probe_arguments
    local -a docker_arguments
    local docker_pid
    local docker_exit
    local actual_memory="null"
    local actual_swap="null"
    local effective_cgroup_memory="null"
    local effective_cgroup_swap="null"
    local oom_killed="false"
    local container_exit="null"
    local status="blocked"
    local terminal_outcome="host-error"
    local run_manifest_path
    local samples_path
    local latency_path
    local completed_requests=0
    local request_errors=0
    local integrity_failures=0
    local run_manifest_status="missing"
    local landed_cgroup_samples=0

    campaign_token="$(printf '%s' "$CAMPAIGN_ID" | sha256sum | cut -c1-8)"
    run_id="p3a-${CAMPAIGN_ID:0:24}-${campaign_token}-docker-${limit_bytes}-${case_name}-${request_count}-${replicate}"
    container_name="$run_id"
    result_dir="$RESULT_ROOT/$run_id"
    if [[ -e "$result_dir" ]]; then
        printf 'BLOCKED：run evidence directory already exists and will not be overwritten: %s\n' "$result_dir" >&2
        return 1
    fi
    mkdir -p "$result_dir"
    printf 'timestamp_utc,container_pid,memory_current_bytes,memory_peak_bytes,memory_swap_current_bytes,process_vmrss_bytes,unavailable_reason\n' \
        >"$result_dir/container-memory.csv"

    if docker container inspect "$container_name" >/dev/null 2>&1; then
        printf 'BLOCKED：container name already exists and will not be overwritten: %s\n' "$container_name" >&2
        printf '{"schemaVersion":1,"runId":"%s","status":"blocked","reason":"container-name-exists"}\n' \
            "$run_id" >"$result_dir/deployment-result.json"
        return 0
    fi

    if [[ "$load_only" == "true" ]]; then
        probe_arguments=(--load-only)
    else
        probe_arguments=(
            --scenario full-pipeline
            --requests "$request_count"
            --concurrency 1
            --sample-every 50
            --cpu-arena on
            --warmup 5
            --state short
            --questions 1
            --idle-seconds 0
            --idle-omission-reason "Docker request-limit matrix run; idle stabilization is measured in the native soak stage."
        )
    fi
    probe_arguments+=(
        --profile multilingual
        --model-root /models
        --output /reports
        --run-id "$run_id"
        --mode formal
        --policy /policy/memory-policy.json
    )

    docker_arguments=(
        run
        --name "$container_name"
        --memory "$limit_bytes"
        --memory-swap "$limit_bytes"
        --read-only
        --tmpfs /tmp:rw,noexec,nosuid,size=64m
        --mount "type=bind,source=$MODEL_ROOT,target=/models,readonly"
        --mount "type=bind,source=$POLICY,target=/policy/memory-policy.json,readonly"
        --mount "type=bind,source=$OUTPUT_ROOT,target=/reports"
        --env "CONTAINER_RUNTIME=$CONTAINER_RUNTIME"
        --env "LAYA_SOURCE_COMMIT=$SOURCE_COMMIT"
        --env "LAYA_SOURCE_DIRTY=$SOURCE_DIRTY"
        --env "LAYA_SOURCE_DIRTY_IDENTITY=$SOURCE_DIRTY_IDENTITY"
        --env "LAYA_CAMPAIGN_ID=$CAMPAIGN_ID"
    )

    set +e
    docker "${docker_arguments[@]}" "$IMAGE" "${probe_arguments[@]}" \
        >"$result_dir/stdout.log" 2>"$result_dir/stderr.log" &
    docker_pid=$!
    while kill -0 "$docker_pid" 2>/dev/null; do
        local running
        running="$(docker inspect --format '{{.State.Running}}' "$container_name" 2>/dev/null || true)"
        if [[ "$running" == "true" ]]; then
            docker stats --no-stream --format '{{json .}}' "$container_name" \
                >>"$result_dir/docker-stats.ndjson" 2>/dev/null || true
            sample_container_memory "$container_name" "$result_dir" || true
        fi
        sleep 1
    done
    if wait "$docker_pid"; then
        docker_exit=0
    else
        docker_exit=$?
    fi
    set -e

    if docker inspect --format '{{json .}}' "$container_name" >"$result_dir/container-inspect.json" 2>"$result_dir/inspect.stderr.log"; then
        actual_memory="$(docker inspect --format '{{.HostConfig.Memory}}' "$container_name")"
        actual_swap="$(docker inspect --format '{{.HostConfig.MemorySwap}}' "$container_name")"
        oom_killed="$(docker inspect --format '{{.State.OOMKilled}}' "$container_name")"
        container_exit="$(docker inspect --format '{{.State.ExitCode}}' "$container_name")"
        if [[ "$actual_memory" == "$limit_bytes" && "$actual_swap" == "$limit_bytes" ]]; then
            if [[ "$oom_killed" == "true" ]]; then
                status="terminal-failure"
                terminal_outcome="oom"
            elif [[ "$docker_exit" -ne 0 || "$container_exit" -ne 0 ]]; then
                status="terminal-failure"
                terminal_outcome="crash"
            else
                status="complete"
                terminal_outcome="completed"
            fi
        fi
    else
        docker_exit="${docker_exit:-125}"
        status="blocked"
        terminal_outcome="host-error"
    fi

    run_manifest_path="$OUTPUT_ROOT/runs/$run_id/manifest.json"
    if [[ -f "$run_manifest_path" ]]; then
        run_manifest_status="$(jq -r '.status // "incomplete"' "$run_manifest_path")"
        completed_requests="$(jq -r '.actualCounts.completedRequests // 0' "$run_manifest_path")"
        request_errors="$(jq -r '.actualCounts.errors // 0' "$run_manifest_path")"
        integrity_failures="$(jq -r '.actualCounts.integrityFailures // 0' "$run_manifest_path")"
        effective_cgroup_memory="$(jq -r '.environment.memoryLimitBytes // "null"' "$run_manifest_path")"
        effective_cgroup_swap="$(jq -r '.environment.memorySwapLimitBytes // "null"' "$run_manifest_path")"
    fi
    samples_path="$OUTPUT_ROOT/runs/$run_id/phase3a-memory-samples.csv"
    latency_path="$OUTPUT_ROOT/runs/$run_id/phase3a-memory-latency.csv"
    if [[ -f "$result_dir/container-memory.csv" ]]; then
        landed_cgroup_samples="$(wc -l <"$result_dir/container-memory.csv")"
    fi
    if [[ "$actual_memory" != "$limit_bytes" || "$actual_swap" != "$limit_bytes" ]]; then
        status="blocked"
        terminal_outcome="host-error"
    elif [[ "$oom_killed" == "true" || "$docker_exit" -ne 0 || "$container_exit" -ne 0 ]]; then
        if ((landed_cgroup_samples > 1)) || [[ -s "$result_dir/docker-stats.ndjson" ]]; then
            status="terminal-failure"
            if [[ "$oom_killed" == "true" ]]; then
                terminal_outcome="oom"
            else
                terminal_outcome="crash"
            fi
        else
            status="blocked"
            terminal_outcome="host-error"
        fi
    elif [[ "$run_manifest_status" != "complete" || ! -f "$samples_path" || ! -f "$latency_path" ||
        "$completed_requests" -ne "$request_count" ]]; then
        status="blocked"
        terminal_outcome="host-error"
    fi

    jq -n \
        --arg runId "$run_id" \
        --arg containerName "$container_name" \
        --arg case "$case_name" \
        --arg status "$status" \
        --arg terminalOutcome "$terminal_outcome" \
        --argjson limitBytes "$limit_bytes" \
        --argjson requestedRequests "$request_count" \
        --argjson replicate "$replicate" \
        --argjson actualMemoryLimitBytes "${actual_memory:-null}" \
        --argjson actualMemorySwapLimitBytes "${actual_swap:-null}" \
        --argjson effectiveCgroupMemoryLimitBytes "$effective_cgroup_memory" \
        --argjson effectiveCgroupSwapLimitBytes "$effective_cgroup_swap" \
        --argjson pageSizeBytes "$PAGE_SIZE_BYTES" \
        --argjson oomKilled "$oom_killed" \
        --argjson dockerExitCode "$docker_exit" \
        --argjson containerExitCode "$container_exit" \
        --argjson completedRequests "$completed_requests" \
        --argjson errors "$request_errors" \
        --argjson integrityFailures "$integrity_failures" \
        --arg inspectPath "$result_dir/container-inspect.json" \
        --arg stdoutPath "$result_dir/stdout.log" \
        --arg stderrPath "$result_dir/stderr.log" \
        --arg statsPath "$result_dir/docker-stats.ndjson" \
        --arg cgroupPath "$result_dir/container-memory.csv" \
        --arg runManifestPath "$run_manifest_path" \
        --arg samplesPath "$samples_path" \
        --arg latencyPath "$latency_path" \
        --arg runManifestStatus "$run_manifest_status" \
        '{schemaVersion:1,runId:$runId,containerName:$containerName,case:$case,status:$status,terminalOutcome:$terminalOutcome,limitBytes:$limitBytes,requestedRequests:$requestedRequests,replicate:$replicate,actualMemoryLimitBytes:$actualMemoryLimitBytes,actualMemorySwapLimitBytes:$actualMemorySwapLimitBytes,effectiveCgroupMemoryLimitBytes:$effectiveCgroupMemoryLimitBytes,effectiveCgroupSwapLimitBytes:$effectiveCgroupSwapLimitBytes,pageSizeBytes:$pageSizeBytes,oomKilled:$oomKilled,dockerExitCode:$dockerExitCode,containerExitCode:$containerExitCode,completedRequests:$completedRequests,errors:$errors,integrityFailures:$integrityFailures,inspectPath:$inspectPath,stdoutPath:$stdoutPath,stderrPath:$stderrPath,dockerStatsRawPath:$statsPath,cgroupSamplesPath:$cgroupPath,runManifestPath:$runManifestPath,runManifestStatus:$runManifestStatus,samplesPath:$samplesPath,latencyPath:$latencyPath}' \
        >"$result_dir/deployment-result.json"

    # 保存完 inspection 與 cgroup data 後才能刪除已停止 container。
    docker rm "$container_name" >/dev/null 2>&1 || true
}

# 彙總全部 memory limit run outcomes，計算 target 3 GB 重複穩定性。
write_campaign_summary() {
    local result_directory="$1"
    local summary_json="$2"
    local summary_markdown="$3"
    local runs_file="$result_directory/docker-outcomes.json"

    if compgen -G "$RESULT_ROOT/*/deployment-result.json" >/dev/null; then
        jq -s '.' "$RESULT_ROOT"/*/deployment-result.json >"$runs_file"
    else
        printf '[]\n' >"$runs_file"
    fi

    jq -n \
        --arg campaignId "$CAMPAIGN_ID" \
        --slurpfile runs "$runs_file" \
        '($runs[0]) as $r |
         [$r[] | select(.limitBytes == 3000000000 and .requestedRequests == 5000)] as $target |
         {schemaVersion:1,campaignId:$campaignId,runCount:($r|length),runs:$r,
          targetRunCount:($target|length),
          blockedRunCount:([$r[]|select(.status=="blocked")]|length),
          crashCount:([$r[]|select(.limitBytes == 3000000000 and .terminalOutcome == "crash")]|length),
          oomCount:([$target[]|select(.terminalOutcome == "oom")]|length),
          targetSuccessfulRunCount:([$target[]|select(.status=="complete" and .oomKilled==false and .errors==0 and .integrityFailures==0 and .completedRequests==5000)]|length),
          targetOomCount:([$target[]|select(.oomKilled==true)]|length),
          targetRepeatedOom:(([$target[]|select(.oomKilled==true)]|length)>=2),
          targetStable:(([$target[]|select(.status=="complete" and .oomKilled==false and .errors==0 and .integrityFailures==0 and .completedRequests==5000)]|length)>=2),
          memoryLimitsVerified:(([$r[]|select(.actualMemoryLimitBytes!=.limitBytes or .actualMemorySwapLimitBytes!=.limitBytes or .effectiveCgroupMemoryLimitBytes!=((.limitBytes/.pageSizeBytes|floor)*.pageSizeBytes) or .effectiveCgroupSwapLimitBytes!=0)]|length)==0)}' \
        >"$summary_json"

    {
        printf '# Phase 3A Docker 記憶體矩陣\n\n'
        printf 'Campaign: `%s`\n\n' "$CAMPAIGN_ID"
        printf '| 十進位 memory limit (bytes) | Case | Requests | Status | OOMKilled | Exit | Completed | Errors |\n'
        printf '|---:|---|---:|---|---|---:|---:|---:|\n'
        jq -r '.runs[] | "| \(.limitBytes) | \(.case) | \(.requestedRequests) | \(.status) | \(.oomKilled) | \(.containerExitCode) | \(.completedRequests) | \(.errors) |"' "$summary_json"
        printf '\n'
        jq -r '"3 GB target runs: \(.targetRunCount); successful: \(.targetSuccessfulRunCount); repeated OOM: \(.targetRepeatedOom); stable: \(.targetStable); limits verified: \(.memoryLimitsVerified)"' "$summary_json"
        printf '\n結果僅供 Phase 3A POC Memory Gate，Docker stats raw 欄位不冒稱 process RSS。\n'
    } >"$summary_markdown"
}

readonly MODEL_ROOT_REAL="$MODEL_ROOT"
overall_status=0
result_root="$OUTPUT_ROOT/docker/$CAMPAIGN_ID"
mkdir -p "$result_root"
for limit_bytes in 2000000000 2500000000 3000000000 4000000000; do
    run_container_probe "$limit_bytes" "load-only" 0 1 true || overall_status=1
    for request_count in 100 1000; do
        run_container_probe "$limit_bytes" "full-pipeline" "$request_count" 1 false || overall_status=1
    done
    if [[ "$limit_bytes" == "3000000000" ]]; then
        run_container_probe "$limit_bytes" "full-pipeline" 5000 1 false || overall_status=1
        run_container_probe "$limit_bytes" "full-pipeline" 5000 2 false || overall_status=1
    elif [[ "$limit_bytes" == "4000000000" ]]; then
        run_container_probe "$limit_bytes" "full-pipeline" 5000 1 false || overall_status=1
    fi
done

write_campaign_summary \
    "$result_root" \
    "$result_root/phase3a-docker-memory.json" \
    "$result_root/phase3a-docker-memory.md"
if ! jq -e '.memoryLimitsVerified and .targetStable and (.targetRunCount >= 2) and (.blockedRunCount == 0)' \
    "$result_root/phase3a-docker-memory.json" >/dev/null; then
    overall_status=1
fi
exit "$overall_status"
