# Laya .NET POC

## Purpose

此專案驗證 Laya English 與候選 multilingual ONNX bundle 是否能在不依賴 Python Runtime 的情況下，透過 .NET 8、ONNX Runtime CPU 與本機 Hugging Face tokenizer 完成 Choice／Noul 決策推論。Phase 2 是離線適用性評估，不等於 BankReportImporter production integration。

POC 不修改 BankReportImporter，也不把模型權重提交到 Git。正式整合前必須完成 reference parity、交易資料評估、CPU 效能與記憶體驗證。

## Architecture

`Laya.Console` 建立示範 request，`Laya.Core` 依序處理 state serialization、tokenization、sequence／marker encoding、ONNX Runtime inference、calibration 與結果映射。`Laya.Core.Tests` 驗證不需大型模型的純邏輯與有資產時的整合契約；`Laya.Benchmarks` 測量長生命週期 session 的 cold／warm CPU 行為。

```text
Laya.Console
     |
ILayaDecisionEngine
     |
LayaDecisionEngine
  /     |       \
State  Sequence  LayaOnnxSession
  |       |             |
JSON  Tokenizer    ONNX Runtime CPU
                        |
                   Calibration
```

## Requirements

- .NET SDK 8 or later capable of targeting `net8.0`.
- CPU execution environment supported by ONNX Runtime.
- Local `receptron/laya-onnx` English model bundle for model-dependent tests, Console and benchmarks.
- Python or Node.js is only needed if regenerating offline reference fixtures; it is not a runtime dependency.

## Model Download

模型下載來源、revision、檔案 hash 與預期目錄請參考 [models/README.md](models/README.md)。模型權重必須放在本機 `models/laya`，不可提交 Git。

## Directory Structure

```text
LayaDotNetPoc.sln
src/Laya.Core/
src/Laya.Console/
tests/Laya.Core.Tests/
benchmarks/Laya.Benchmarks/
src/Shared/Configuration/    # Console／Benchmark 共用 profile resolver
models/laya/                 # local, ignored model bundle
models/laya-multilingual/    # verified pointer plus ignored source/weights
test-data/                   # fixtures and transactions
reports/                     # Phase 2 evidence and raw run artifacts
```

## Build

```bash
dotnet restore LayaDotNetPoc.sln
dotnet build LayaDotNetPoc.sln --no-restore
```

## Run

模型準備完成後：

```bash
dotnet run --project src/Laya.Console -- --model-root models/laya
```

缺少模型時，程式 SHALL 以明確錯誤指出缺失資產，不會自動下載模型。

### Profiles

Console、Benchmark 與 Phase 2 tools 共用 `LayaProfileResolver`。解析優先序是明確 `--model-root`、`--profile`、`Laya.Profile` 設定、host English 預設；Core 的 `LayaOptions` 只接受已解析的絕對 root：

```bash
dotnet run --project src/Laya.Console -- --model-root models/laya
dotnet run --project src/Laya.Console -- --profile english --model-root models/laya
dotnet run --project src/Laya.Console -- --profile multilingual --model-root models/laya-multilingual
```

multilingual upstream 本身只有 Transformers/safetensors checkpoint；本機固定 source/toolchain 已產生並驗證 versioned ONNX bundle，詳見 `reports/model-validation.md` 與 `reports/multilingual-readiness.md`。Runtime 不會用 English graph 搭 multilingual tokenizer，也不會自動下載替代模型。

### Multilingual Reproduction

Acquisition 與 export 是明確的離線前置操作；acceptance 不會自動下載或重新 export：

```bash
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh tools/laya-reference/acquire.py \
  --offline --output models/laya-multilingual/source
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh tools/laya-reference/probe.py \
  --source-root models/laya-multilingual/source \
  --output reports/multilingual-reference-probe.json
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/export-laya-multilingual.sh
tools/publish-laya-multilingual.sh
```

`models/laya-multilingual/current-bundle.json` 只會指向 Python 與 .NET parity 都通過的 versioned bundle。驗收分為 `./tools/run-phase2-acceptance.sh --until readiness` 與 default full scope；完整 scope 缺 frozen held-out evidence 時維持 blocked。

交易 CSV 評估使用 11 類 `food`、`transport`、`shopping`、`utilities`、`transfer`、`salary`、`bank_fee`、`investment`、`medical`、`entertainment`、`other`：

```bash
dotnet run --project src/Laya.Console -- --model-root models/laya --evaluate-csv test-data/transactions.csv
```

Phase 1 使用明確七欄／30–50 筆模式；Phase 2 使用九欄／200–500 筆模式，不以檔名猜測 phase：

```bash
dotnet run --project src/Laya.Console -- --profile multilingual --phase phase2 --evaluate-csv test-data/transactions-phase2.csv --dataset-manifest test-data/transactions-phase2-manifest.json --split development
```

Phase 2 dataset、guideline、group-aware split 與 hash 位於 `test-data/transactions-phase2-manifest.json`；摘要位於 `reports/dataset-summary.md`。Baseline A 保留原始 prompt、options 順序與四欄 state serialization。只有 baseline 完成後才允許 A/B/C prompt comparison；policy analyzer 的 95%／98% accuracy 與 500ms／3GB 是 POC reference，不是 production SLA。

## Test

```bash
dotnet test LayaDotNetPoc.sln --no-restore
```

不需模型的單元測試可獨立執行；acceptance readiness 會將缺少模型或 reference fixture 以非零狀態阻擋，不能把 skipped 測試算作 parity 通過。

模型準備完成後可執行 engineering readiness 驗收：

```bash
./tools/run-phase2-acceptance.sh --until readiness
```

缺少模型或 parity fixture 時此命令以非零狀態結束；readiness 成功不代表 Phase 2 品質或 Gate 3 通過。

## Phase 2 Reports and Acceptance

每次 Phase 2 run 以唯一 run-id 保存於 `reports/phase2-runs/<run-id>/`，包含 v2 manifest、未 rounding raw results、metrics、artifact index、calibration、Noul 與摘要；錯誤分類輸出固定為根目錄 `misclassified-transactions.csv` 的八欄去識別格式。Supporting reports 與 Gate 3 報告在 `reports/`，未量測值必須寫成未完成／blocked，不填範例數字。

完整驗收依模型→parity→dataset→quality→benchmark→deployment→Gate 3 順序執行，缺資產或必要 evidence 會非零退出：

```bash
./tools/run-phase2-acceptance.sh
```

`tools/validate-phase2-evidence.mjs` 可單獨檢查 12 份 supporting Markdown、manifests、dataset hash、group split、錯誤 CSV、v2 artifact/index/metrics 與 run identity；`--require-held-out` 會要求 frozen-candidate/policy provenance。`tools/export-laya-multilingual.sh` 與 `tools/publish-laya-multilingual.sh` 只接受 parity-gated、可驗證的 bundle。

## Benchmark

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya
```

BenchmarkDotNet warm matrix 預設使用 5 次暖機與 100 次觀測，涵蓋 1／2／5 questions × short／medium／long state；`--metadata` 輸出實際 token 長度與截斷狀態，`--cold-start` 在新程序中分開量測 load／首次 Run／memory，`--collect` 輸出九組 Run-only／end-to-end 的 mean、P50、P95、P99。`--practical` 量測四欄 transaction state 的 200 次 Choice＋Noul request，`--concurrency 1|2|4` 量測共享 engine 的並行 request；所有模式都可用 `--output <path>` 保存 machine-readable JSON：

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --metadata
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --cold-start
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --collect
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --practical --output reports/phase2-benchmark-runs/multilingual-practical.json
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --concurrency 4 --output reports/phase2-benchmark-runs/multilingual-concurrency-4.json
```

Benchmark 結果輸出為本機實測資料，不使用範例數值；`MemoryDiagnoser` 的 managed allocation 與 Process.WorkingSet64／peak working set 分開記錄。

Phase 2 deployment 使用 .NET 8 Linux multi-stage image；模型以 read-only `/models` volume 掛載，報告以獨立 `/reports` volume 寫入，final image 不包含 weights、Python 或 Node.js：

```bash
docker build --tag laya-dotnet-poc:phase2 .
./tools/validate-phase2-deployment.sh --model-root models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728 --profile multilingual
```

驗證 script 會保留 2／3／4 GiB 各自的 raw output；缺 Docker、缺 mount、OOM 或 request failure 都不會冒充通過。

## Phase 3A Memory Investigation

Phase 3A 是離線記憶體診斷工具，不是 BankReportImporter 整合或 production SLA。Probe 固定使用已驗證的 multilingual bundle 與 CPU execution provider；不下載或替換模型。每次 run 以唯一 ID 寫入 `reports/phase3a/runs/<run-id>/`，保存逐行 samples、numeric latency、errors、summary、manifest 與 artifact hashes。既有 run ID 不可覆寫。

### MemoryProbe scenarios

| Scenario | 迴圈工作 | ONNX session |
| --- | --- | --- |
| `tokenizer-only` | 固定 zh／en／mixed fixture 編碼 | 不建立 |
| `sequence-only` | 真實序列建構，包含 serialization／tokenization | 不建立 |
| `tensor-only` | 建立並釋放五項 input arrays／OrtValues | 不建立 |
| `run-only` | 固定 inputs／每 worker RunOptions，只執行 ONNX Run 並釋放 outputs | 一個共享 session |
| `calibration-only` | 固定 raw output 的 calibration／result mapping | 不建立 |
| `full-pipeline` | Serialize → Tokenize → Build → Tensor → Run → Calibration → Map | 一個長生命週期 session |
| `session-recreate` | 每 cycle Create → Run once → Dispose | 每 cycle 新 session |

`--requests` 是正式 measured work，不含暖機；一般 scenario 預設 warmup 5 且至少 5，`session-recreate` 固定 warmup 0。latency buffers 有界，單次 run 最多接受 100,000 requests。預設每 50 個完成 requests 採樣；每 worker 的輸入與 RunOptions 獨立。`--questions` 僅接受 1／2／5；state 為 `short`／`medium`／`long`。預設 workload 來自 `test-data/phase3a/memory-workloads.json`；tokenizer fixture ID 為 `tokenizer-en`、`tokenizer-zh`、`tokenizer-mixed`，calibration fixture ID 為 `calibration-mixed-english-reference`。

### Pilot 與正式 campaign

先執行獨立 pilot，不把 pilot runs 當正式 Gate evidence：

```bash
./tools/run-phase3a-memory.sh \
  --stage pilot \
  --mode pilot \
  --campaign-id phase3a-pilot-YYYYMMDD \
  --model-root models/laya-multilingual \
  --output-root reports/phase3a
```

wrapper 會將 multilingual pointer 解析為 versioned bundle、建置 solution，再由獨立 Probe 子程序執行 stage。`--resume` 僅適用於相同 campaign／stage 且 immutable identity 相同的 campaign runs；不覆寫既有證據。Docker matrix 不會覆寫中斷的 output，需先檢視既有結果並以新 campaign ID 另行執行。

正式 mode 必須提供完整、已凍結的 memory policy。v1 因 managed-heap counter 品質異常、v2 因 shape planner run-ID 修正造成 source identity 變更而保留為歷史 evidence；新的 formal campaign 使用 `reports/phase3a/memory-policy-v3.json`，不得跨版本混跑：

```bash
./tools/run-phase3a-memory.sh \
  --stage all \
  --mode formal \
  --campaign-id phase3a-formal-YYYYMMDD \
  --model-root models/laya-multilingual \
  --policy reports/phase3a/memory-policy-v3.json \
  --output-root reports/phase3a
```

Policy 必須帶 pilot run IDs／artifact hash、freeze time／commit、理由與限制；PrivateMemory／RSS／managed late slope 和 growth budgets、近線性 R²、replicate tolerance、minimum matrix 均須明確填值。target 固定為十進位 `3,000,000,000` bytes。不得以範例門檻、Phase 2 的 WorkingSet 比例或事後調整門檻代替 pilot 校準。

`tools/validate-phase3a-deployment.sh` 使用 2／2.5／3／4 GB 十進位 bytes，設定相同 memory／memory-swap 並驗證 inspect 與 page-rounded cgroup limit。報告分別標記 process VmRSS、cgroup memory counters 與可能含 file cache 的 usage；這些指標不可互相冒稱。Docker effective limit 可能按 host page size round down；無法核對限制或必要 counters 時 evidence blocked。3 GB target 的獨立 5,000-request runs 是部署 Gate；較高限額成功不能抵銷 target OOM。

詳細 CLI、run evidence、resume 限制與驗證命令請見 [`tools/Laya.MemoryProbe/README.md`](tools/Laya.MemoryProbe/README.md)。

## Known Limitations

- English ONNX bundle 是 Phase 1 regression；中文／混合文字測試不代表 multilingual checkpoint 表現。
- multilingual ONNX acquisition、Python ORT、.NET parity 與 engineering readiness 已完成；Gate 3 報告仍區分後續品質 evidence blocked 與模型品質失敗。
- Score、GPU、量化、模型自動下載、訓練與 LLM fallback 不在本階段範圍；Docker 僅提供離線 CPU deployment validation。
- Confidence threshold 是 POC 評估預設值，不是 production policy。
- 缺少 held-out frozen candidate、品質分析、benchmark 或 deployment evidence 時，full Phase 2 維持 blocked，不會由 readiness 自動放行。

## Reference Implementation

Reference revision、bundle hash、tokenizer／prompt／calibration 證據與 fixture 產生步驟記錄在 `test-data/multilingual-reference-manifest.json` 與 `reports/multilingual-readiness.md`。實作不得自行猜測 Laya prompt、Noul encoding 或 calibration formula。

## Future BankReportImporter Integration

只有 English parity、multilingual evaluation、CPU／RAM 與實際 validation dataset 門檻都通過後，才建立 `ITransactionClassificationService` 將 category、probability 與 decision mode 接入 BankReportImporter。BankReportImporter 不應直接引用 ONNX Runtime。
