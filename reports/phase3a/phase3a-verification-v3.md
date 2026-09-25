# Phase 3A v3 驗證紀錄

Campaign `phase3a-formal-20260925-v3`；本次報告／evidence 驗證時間為 2026-09-25。該 host 安裝 .NET runtime 10.0.12、沒有 runtime 8；net8.0 test／aggregator 執行採用 `DOTNET_ROLL_FORWARD=Major`，並在相關 manifests 記錄實際 .NET 10 runtime。不同 runtime 不混作 reproduction。

## 建置與測試

| 命令／檢查 | 實際結果 |
|---|---|
| `dotnet build LayaDotNetPoc.sln --no-restore` | 通過；0 warnings、0 errors。 |
| `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore` | 通過；358 passed、0 failed、0 skipped。 |
| `node --test tools/*.test.mjs` | 37 passed、0 failed／cancelled／skipped。首次 120 秒工具上限中斷後，將 command timeout 提高至 300 秒重跑完成；本次耗時約 143 秒。 |
| `bash -n tools/*.sh tools/laya-reference/run.sh` | 全部 scripts 語法檢查通過。 |
| `git diff --check` | 通過；無 whitespace errors。 |

## Aggregate 與 evidence validation

- 以 `DOTNET_ROLL_FORWARD=Major dotnet run --project benchmarks/Laya.MemoryBenchmarks --no-restore -- --stage aggregate --campaign-id phase3a-formal-20260925-v3 --mode formal --policy reports/phase3a/memory-policy-v3.json --output reports/phase3a` 從 raw manifests／samples 重算 combined samples、baseline slopes、Gate JSON 與 machine-generated index。結果 `evidenceStatus=blocked`、`status=null`、`classification=null`、recommendation `DO NOT PROCEED`。
- Supporting Markdown 在 aggregate 後依 raw evidence 補齊；`phase3a-artifact-index.json` 於文件更新後重算 root-level report hashes／sizes。Raw per-run manifest 對應的 artifact hash 由 aggregate 驗證。
- `node tools/validate-phase3a-evidence.mjs reports/phase3a` 回報 `valid=false`、`evidenceStatus=blocked`、`gateStatus=null`、`DO NOT PROCEED`，errors 為 shape stage 未完成、retention partial run completion／identity mismatch、reproduction stage 未完成。這是未完成 shape 與 frozen-slope disagreement 的驗收結果，非測試失敗或 leak 判定。
- v3 reproduction 每組三個 raw slopes 及 pairwise 差異已用 CSV 重算並與 `phase3a-reproducibility.json` 相符。Integrity fixtures 仍未執行；零 integrity counter 不轉換為通過。

## 未完成 evidence

Shape retention 執行只到 2,600／3,000，raw manifest 無完整 artifact index；不得復用 partial run。四組 reproduction 各至少一項 late-slope pairwise difference 超過凍結 25% tolerance。Baseline managed heap 1,000–2,500 interval 有五個 unavailable observations；shape retention／concurrency integrity 等尚未具備的證據使 Gate 保持 blocked。驗證指令完成不代表 blocked campaign 可通過。

## 歷史證據與模型身分

`git status --short -- reports models test-data/reference-manifest.json test-data/multilingual-reference-manifest.json` 僅顯示本 Phase 3A 的新證據目錄；Phase 2 報告、candidate／readiness artifacts、English／multilingual reference manifests 與 multilingual current-bundle pointer 均無工作樹差異。v3 verified bundle `8f37a12e…` 的實際 `laya-bundle-manifest.json` SHA-256 為 `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`，`tokenizer/tokenizer.json` SHA-256 為 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`；與 v3 run manifests 記錄相符。
