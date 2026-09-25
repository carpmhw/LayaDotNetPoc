# 專案代理工作指南

本文件適用於整個 `LayaDotNetPoc` 儲存庫；子目錄若另有 `AGENTS.md`，該目錄的工作須同時遵循其規範。

## 溝通與程式風格

- 回覆、工作摘要、文件說明與程式註解使用繁體中文；識別字、CLI 參數及資料契約保留原有英文。
- 所有新增或修改的函數、方法與建構函數都必須有繁體中文註解，包含 private helper 與測試方法。C# 沿用 `/// <summary>`；Python 使用 docstring；Shell／JavaScript 使用鄰接註解。
- C# 沿用四空白縮排、file-scoped namespace、PascalCase 型別／方法、camelCase 區域變數與 `_camelCase` 私有欄位；保留 nullable reference types 與 implicit usings。
- 依循鄰近程式與現有抽象，變更聚焦於任務，避免順便重排、重新格式化或升級無關套件。

## 專案定位與邊界

- 這是 .NET 8 的 Laya Choice／Noul 決策推論 POC，以 ONNX Runtime CPU 與本機 Hugging Face tokenizer 執行。
- English bundle 用於 Phase 1 regression；multilingual bundle 用於 Phase 2 離線適用性評估。Phase 2 並非 BankReportImporter 正式整合。
- 執行時不依賴 Python／Node.js、不連網下載模型。Python／Node.js 僅供離線 reference、export、驗證與報告工具使用。
- Score、GPU、量化、訓練、LLM fallback 與模型自動下載不在既定範圍；若任務涉及擴充，先明確釐清需求與契約。
- 不直接修改或接入 BankReportImporter；未來整合邊界請參閱 `README.md`。

## 先讀哪些檔案

1. `README.md`：用途、執行方式、驗收層級與已知限制。
2. `models/README.md`、`models/laya-multilingual/README.md`：模型來源、bundle 結構與發布方式。
3. `test-data/reference-manifest.json`、`test-data/multilingual-reference-manifest.json`：reference revision、hash 與 fixture 來源。
4. 與任務相關的 `src/` 實作、`tests/Laya.Core.Tests/` 測試及 `reports/` 證據。
5. 若任務指定 OpenSpec change，先讀對應 proposal、design、specs 與 tasks，再依該工作流程更新進度。

`openspec/`、`docs/`、`.opencode/` 目前受 `.gitignore` 排除，可能僅存在於本機。可參考可用的規格，但共用操作指引須能由已追蹤的程式、README 與 manifests 核對，不假設乾淨 checkout 具備本機工作文件。

## 目錄與責任

| 路徑 | 責任 |
| --- | --- |
| `src/Laya.Core/` | 公開決策介面、模型驗證、serialization、tokenization、sequence encoding、推論、calibration 與評估純邏輯 |
| `src/Laya.Console/` | CLI、CSV 輸入、dataset 選取、evaluation orchestration 與報告寫入 |
| `src/Shared/Configuration/LayaProfileResolver.cs` | Console／Benchmark 透過 linked source 共用的 profile resolver，並非獨立專案 |
| `tests/Laya.Core.Tests/` | xUnit 測試，涵蓋 Core 與 Console 契約 |
| `benchmarks/Laya.Benchmarks/` | BenchmarkDotNet、cold start、practical 與 concurrency 量測 |
| `tools/` | reference fixture、multilingual export／publish、acceptance 與 evidence／deployment 驗證 |
| `test-data/` | reference fixtures、交易 CSV、標註規則與 dataset manifests |
| `models/` | 模型說明、bundle metadata／pointer 與本機忽略的權重 |
| `reports/` | 可追溯的 readiness、parity、Phase 2、benchmark 與 deployment 證據 |

## 必須維持的實作契約

### 推論與資源生命週期

- 維持 `ILayaDecisionEngine` → serialization／tokenization／sequence → ONNX inference → calibration／結果映射的分層；CLI 解析與報告 I/O 留在 host。
- `LayaDecisionEngine`／`LayaOnnxSession` 採長生命週期重用，避免每次 request 重新載入模型；維持每個 request 的多 question batch 推論。
- 明確管理 tokenizer、session 與 native tensor／推論結果的釋放，建構失敗也須清理已建立資源。共享 engine 的修改須考慮現有並行測試。
- 錯誤須保留可診斷的資產路徑或 question context，沿用 `Laya.Core/Exceptions` 的例外類型；不得以假答案掩蓋缺檔或推論失敗。

### 模型與 profile

- profile 解析留在 `LayaProfileResolver`；優先序為明確 `--model-root`、`--profile`、`Laya.Profile` 設定、English 預設。Core 的 `LayaOptions` 接收 host 已解析的絕對模型路徑。
- English 預設根目錄是 `models/laya`；multilingual 經 `models/laya-multilingual/current-bundle.json` 指向已驗證的 versioned bundle。
- graph、external data、config、tokenizer、checkpoint revision 與 hash 必須一致；不得混用 English graph 與 multilingual tokenizer，或靜默切換到其他模型。
- acquisition、export、parity、publish 是明確的離線準備步驟；runtime 與 acceptance 不得偷偷下載或重新 export。
- 不手動把 candidate pointer 或驗證狀態改成通過。發布須使用既有 parity-gated 流程。

### Reference parity 與評估

- 不猜測 prompt、特殊 token、marker、截斷、Noul 編碼或 calibration 公式；以固定 revision 的 reference 與 fixtures 為依據。
- 修改 serialization、tokenizer、sequence、tensor shape／dtype 或後處理時，檢查相應分層 parity；不可只為讓測試變綠而改寫 expected output、放寬 tolerance 或略過案例。
- fixture 重建須保留 upstream／toolchain provenance、實際 hash 與重現步驟；multilingual 工具環境依 `tools/laya-reference/toolchain.lock.json` 與其 README。
- Phase 1／Phase 2 由明確模式選取，不以 CSV 檔名猜測。Phase 2 遵循 dataset manifest、group-aware split 與標註規則。
- 保持 Baseline A 的原始 prompt、options 順序及四欄 state serialization；完成 baseline 後才進行 A/B/C comparison。
- development 用於選擇候選與 policy；held-out 驗證須綁定 frozen candidate／policy provenance，不用 held-out 結果反向調參。

## 開發與驗證指令

以下命令均從儲存庫根目錄執行。需要能 target `net8.0` 的 .NET SDK 與可執行該程式的 runtime；工具驗證另需 Node.js，Phase 2 acceptance 另需 Bash、`jq` 與 `sha256sum`。

### 建置與測試

```bash
dotnet restore LayaDotNetPoc.sln
dotnet build LayaDotNetPoc.sln --no-restore
dotnet test tests/Laya.Core.Tests/Laya.Core.Tests.csproj --no-restore --filter "Category=PureLogic"
```

- `PureLogic` 不需要大型 ONNX 權重，但目前部分測試仍讀取 English `laya_config.json` 與 tokenizer 檔案；不要把這個分類描述為完全無資產依賴。
- 其他分類為 `EnglishTokenizer`、`EnglishModel`、`MultilingualTokenizer`、`MultilingualModel`，可替換上方 filter 按影響範圍執行。部分測試未標記分類，因此分類測試不能取代完整套件。
- English 測試可透過 `LAYA_MODEL_ROOT` 指向完整 bundle；multilingual 測試可透過 `LAYA_MULTILINGUAL_MODEL_ROOT` 指向實際 versioned bundle。
- 資產齊備後執行完整測試：

```bash
dotnet test LayaDotNetPoc.sln --no-restore
```

修改 `tools/` 時，依變更範圍執行相應工具測試與 Shell 語法檢查：

```bash
node --test tools/*.test.mjs
python3 -m unittest discover -s tools/laya-reference -p 'test_*.py'
for script in tools/*.sh tools/laya-reference/run.sh; do
    bash -n "$script" || break
done
```

Python reference／export 的實際推論另需鎖定的 CPU toolchain 與模型資產，準備方式見 `tools/laya-reference/README.md`。

### 執行與驗收

```bash
dotnet run --project src/Laya.Console -- --model-root models/laya
dotnet run --project src/Laya.Console -- --profile multilingual --smoke --smoke-count 1
./tools/run-phase2-acceptance.sh --until readiness
node tools/validate-phase2-evidence.mjs . --require-held-out
./tools/run-phase2-acceptance.sh
```

- readiness 驗證工程可用性；不等於 Phase 2 品質、deployment 或 Gate 3 通過。
- full scope 會檢查必要的 held-out 與 supporting evidence；缺失時應維持 blocked。不得把它當作自動產生所有品質／benchmark／deployment 證據的命令。
- 缺少必要資產或 fixture 時如實回報阻擋原因。Skipped、零執行案例、舊報告與未執行階段都不能當成本次 parity 通過。
- 模型測試與驗收可能寫入 `reports/`；執行後檢查差異，分辨本次有效證據與無關生成內容。

### 效能與部署

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya
docker build --tag laya-dotnet-poc:phase2 .
```

- 效能變更按需量測 cold／warm、Run-only／end-to-end 與 concurrency；managed allocation 與 process working set 分別記錄。
- Docker final image 保持 .NET runtime，權重透過唯讀 `/models` 掛載，報告寫入獨立 `/reports`；不把 weights、Python 或 Node.js 放入 final image。
- 詳細 benchmark 與部署驗證參數見 `README.md` 和 `tools/validate-phase2-deployment.sh`。

## 資料、證據與交付

- Phase 2 run 使用唯一 run-id 保存至 `reports/phase2-runs/<run-id>/`，保留未 rounding 的 raw results、manifest、artifact index、metrics 與來源身分。
- 根目錄 `misclassified-transactions.csv` 維持既定八欄去識別格式；不要將完整敏感交易原文寫入錯誤報告。
- 模型、fixture、dataset、policy 或 artifact 異動時，同步檢查相關 hash 與 provenance，透過既有工具重建必要證據。
- 量測數值必須來自實測；缺少資料標記為未完成／blocked，區分證據不足與模型品質失敗。POC reference thresholds 不宣稱為 production SLA。
- 遵循 `.gitignore`：不納入模型權重、`bin/`、`obj/`、`BenchmarkDotNet.Artifacts/` 或本機工具環境；不要為了加入本機規格而擅自 force-add 被忽略目錄。
- 開始與結束時檢查 `git status`／diff，保留既有使用者變更；僅在明確要求時 commit、push 或建立 PR。
- 依變更範圍驗證：純文件變更核對路徑、命令與 `git diff --check`；程式變更執行建置及相關測試；推論契約變更補做受影響的模型／parity 驗證。
- 交付摘要說明變更檔案、實際執行的驗證及結果，以及任何未執行項目與阻擋原因，不以預期結果宣稱完成。
