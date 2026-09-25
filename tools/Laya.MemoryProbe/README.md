# Laya MemoryProbe

`Laya.MemoryProbe` 是 Phase 3A multilingual CPU 記憶體調查工具。它沿用 Core 的 model validation、profile resolver、tokenizer、sequence builder、tensor owner、ONNX session 與 calibration，不會連網下載模型。Probe 與 campaign host target `net8.0`；執行前需安裝可執行目標 runtime 的 .NET，或在僅有 .NET 10 的開發主機使用 `DOTNET_ROLL_FORWARD=Major`。正式量測須記錄實際 runtime，不能混合不同 runtime 的 replicate。

## 準備模型與執行 Probe

`--profile multilingual` 是必要範圍；`--model-root` 可指向 verified versioned bundle，或 `models/laya-multilingual` verified pointer directory。沒有完整 bundle 時，工具會回報資產路徑並以非零狀態結束；不會下載替代模型。直接執行 200-request pilot 範例：

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --project tools/Laya.MemoryProbe -- \
  --scenario full-pipeline \
  --requests 200 \
  --warmup 5 \
  --concurrency 1 \
  --sample-every 50 \
  --cpu-arena on \
  --state short \
  --questions 1 \
  --workload phase3a-short-transaction \
  --idle-seconds 0 \
  --profile multilingual \
  --model-root models/laya-multilingual \
  --mode pilot \
  --output reports/phase3a \
  --run-id phase3a-pilot-manual-YYYYMMDD
```

這個 standalone run 僅供工具／環境 pilot，不是正式 baseline 或 Gate evidence。Campaign pilot 的固定矩陣、source identity 與 resume 應由 `tools/run-phase3a-memory.sh` 建立：

```bash
./tools/run-phase3a-memory.sh \
  --stage pilot \
  --mode pilot \
  --campaign-id phase3a-pilot-YYYYMMDD \
  --model-root models/laya-multilingual \
  --output-root reports/phase3a
```

Campaign script 會 restore／build solution。`--resume` 只接受同一 campaign stage、source、policy、workload、model bundle 與 runtime identity 相符的完成 runs；不會覆寫不完整或身分不同的證據。

## Scenario 與邊界

| `--scenario` | 初始化與 measured operation | 不包含的工作 |
| --- | --- | --- |
| `tokenizer-only` | 載入 tokenizer，以 `--workload tokenizer-en\|tokenizer-zh\|tokenizer-mixed` 編碼固定文字 | 不載入 ONNX session |
| `sequence-only` | 建立真實 sequence；包含 serialization、tokenization 與 Build | 不建立 session、不執行 Run |
| `tensor-only` | 建立／釋放 input arrays 與五項 OrtValues | 不執行 Run |
| `run-only` | 初始化一個 session；每 worker 固定 input tensors／RunOptions，measured loop 只呼叫 Run 並釋放 outputs | 不重建 input、不 tokenize、不 calibration；不含輸出檢查／I/O 的 Run latency |
| `calibration-only` | 使用 `calibration-mixed-english-reference` 固定 raw output 做 calibration 與 result mapping | 不載入 session |
| `full-pipeline` | 執行完整 serialization → tokenization → sequence／tensor → Run → calibration → mapping | 不包含完整交易原文或 response 的 raw evidence |
| `session-recreate` | 每個 cycle Create session → Run once → Dispose | warmup 必須為 0、concurrency 必須為 1 |

`--load-only` 是獨立容器 smoke mode，不是第八種正式 scenario；它載入 verified profile、不執行 requests，也不應被當成 soak 成功。Tokenizer／calibration fixture 及交易 state/question 定義和 hash 見 `test-data/phase3a/memory-workloads.json` 及其 manifest。

## CLI 與量測口徑

- 必填 scenario 參數為 `--scenario`、`--requests`；另支援 `--concurrency`、`--sample-every`、`--warmup`、`--cpu-arena on|off`、`--state short|medium|long`、`--questions 1|2|5`、`--workload`、`--idle-seconds`、`--idle-omission-reason`、`--gc-checkpoints`、`--state-schedule`、`--profile`、`--model-root`、`--mode pilot|formal`、`--policy`、`--output` 與 `--run-id`。
- `--requests` 計算暖機後 measured work；一般 scenario 至少 5 warmup，預設為 5，session-recreate 必須 0。單次 run 的 requests 上限為 100,000，確保預配置 latency window 有界。預設 `--sample-every 50`；並行 checkpoint 以完成數和 barrier 採樣，而非 dispatched work 數。
- `--cpu-arena` 預設 `on`，與正式 Core session factory 使用同一設定。除 `full-pipeline`／`run-only` 外，`--concurrency` 必須為 1。
- `--gc-checkpoints` 只接受 100／500／1000／5000 中不大於 requests 的值；forced GC 是獨立診斷 run，不應併入自然 baseline slope。自然 soak 在 requests 完成後依設定做 idle 與配對採樣；正式 mode 若省略 300 秒 idle，必須提供 `--idle-omission-reason`。
- `--state-schedule short:1000,long:1000,short:1000` 僅支援 `full-pipeline`，所有 segment requests 總數須等於 `--requests`，且不能同時提供固定 `--state`。Schedule 用於 retention control，不得混入固定-shape slope。
- Memory 全部以 bytes 寫入。十進位 GB 使用 1,000,000,000 bytes；GiB 使用 1,073,741,824 bytes。Docker request limit 以整數 bytes 傳入；cgroup effective limit 可能因 host page size round down，manifest 同時保存 requested inspect limits、effective counters 與 page size。
- Linux `/proc`／cgroup 不可用的欄位記錄為 `null` 加原因，不會補零。若 `GC.GetTotalMemory(false)` 回傳負 bytes，也以空欄位與 `managed_heap_unavailable_reason` 記錄，不截成零；該 checkpoint 會使對應 managed slope segment blocked。Process VmRSS、cgroup RSS 類指標與可能含 file cache 的 `memory.current` 是不同口徑。

每次 run 建立新的 `runs/<run-id>/` 子目錄，內容包含 `phase3a-memory-samples.csv`、`phase3a-memory-latency.csv`、`phase3a-memory-errors.csv`、`summary.json` 與 `manifest.json`／artifact hashes。Run ID 重複時拒絕啟動；失敗／取消保留已 flush raw rows 並標示 incomplete、terminal failure 或外部 outcome，不會將 planned request count 當成 completed count。

## Pilot、frozen policy 與 Docker

Pilot 是獨立校準輸入，不能作為 formal Gate run。正式 campaign 需先建立完整 `memory-policy.json`，policy 包含 pilot IDs/hash、freeze source／UTC、rationale／limitations、PrivateMemory／RSS／managed slope 與 growth thresholds、near-linear R²、replicate tolerance 及 minimum matrix。target 固定為十進位 `3,000,000,000` bytes；缺值、非法欄位或非 frozen policy 都會拒絕 formal mode。不得使用未校準的示意門檻。

Policy 凍結後才可執行 formal stage，例如；v1 因負值 managed-heap counter 品質異常、v2 因 shape planner run-ID 修正造成 source identity 變更而保留為歷史 evidence，本機新的 formal campaign 使用 v3 policy：

```bash
./tools/run-phase3a-memory.sh \
  --stage baseline \
  --mode formal \
  --campaign-id phase3a-formal-YYYYMMDD \
  --model-root models/laya-multilingual \
  --policy reports/phase3a/memory-policy-v3.json \
  --output-root reports/phase3a
```

Docker limit matrix 由 `tools/validate-phase3a-deployment.sh` 執行；需 Docker daemon、`jq`、Node.js、`sha256sum`、page-size 工具、verified model mount 及完整 frozen policy。final image 是 .NET 8 runtime／CPU Probe，不帶 Python、Node.js 或權重；`/models` 唯讀掛載，`/reports` 獨立可寫。Host wrapper 在清除容器前保存 inspect、OOM／exit、stdout／stderr、process RSS 與 cgroup samples。缺 Docker、無法核對 effective limit、缺 raw sample 或 terminal failure outcome 無法稽核時，驗收應維持 blocked。

## 驗證

```bash
dotnet build LayaDotNetPoc.sln --no-restore
DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore
node --test tools/*.test.mjs
for script in tools/*.sh tools/laya-reference/run.sh; do bash -n "$script" || break; done
node tools/validate-phase3a-evidence.mjs reports/phase3a
```

最後一個 validator 命令只在必要 formal evidence、hash 與 frozen policy 都齊備時才應通過；目前 pilot 或 smoke-only 輸出不足時，非零／blocked 是預期結果，不等於模型品質失敗。詳見根目錄 [README](../../README.md) 的 Phase 3A 操作摘要。
