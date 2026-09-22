# Laya .NET POC

## Purpose

此專案驗證 Laya English ONNX bundle 是否能在不依賴 Python Runtime 的情況下，透過 .NET 8、ONNX Runtime CPU 與本機 Hugging Face tokenizer 完成 Choice／Noul 決策推論。

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
models/laya/                 # local, ignored model bundle
test-data/                   # fixtures and transactions
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

交易 CSV 評估使用 11 類 `food`、`transport`、`shopping`、`utilities`、`transfer`、`salary`、`bank_fee`、`investment`、`medical`、`entertainment`、`other`：

```bash
dotnet run --project src/Laya.Console -- --model-root models/laya --evaluate-csv test-data/transactions.csv
```

## Test

```bash
dotnet test LayaDotNetPoc.sln --no-restore
```

不需模型的單元測試可獨立執行；完整驗收命令會將缺少模型或 reference fixture 標示為 `PARTIAL`，不能把 skipped 測試算作 parity 通過。

模型準備完成後可執行完整驗收（包含 build、45 項 tests、CSV evaluation、cold-start 與正式 warm collection）：

```bash
./tools/run-acceptance.sh
```

缺少模型或 parity fixture 時此命令輸出 `PARTIAL` 並以非零狀態結束。

## Benchmark

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya
```

BenchmarkDotNet warm matrix 預設使用 5 次暖機與 100 次觀測，涵蓋 1／2／5 questions × short／medium／long state；`--metadata` 輸出實際 token 長度與截斷狀態，`--cold-start` 在新程序中分開量測 load／首次 Run／memory，`--collect` 輸出九組 Run-only／end-to-end 的 mean、P50、P95、P99：

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --metadata
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --cold-start
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --collect
```

Benchmark 結果輸出為本機實測資料，不使用範例數值；`MemoryDiagnoser` 的 managed allocation 與 Process.WorkingSet64／peak working set 分開記錄。

## Known Limitations

- 第一階段只鎖定 English ONNX bundle；中文／混合文字測試不代表 multilingual checkpoint 表現。
- Score、GPU、量化、Docker、模型自動下載、訓練與 LLM fallback 不在本階段範圍。
- Confidence threshold 是 POC 評估預設值，不是 production policy。
- 缺少模型或 reference revision 時，Gate 1／Gate 2 只能標示未執行或 PARTIAL。

## Reference Implementation

Reference revision、bundle hash、tokenizer／prompt／calibration 證據與 fixture 產生步驟記錄在 `test-data/reference-manifest.json`。實作不得自行猜測 Laya prompt、Noul encoding 或 calibration formula。

## Future BankReportImporter Integration

只有 English parity、multilingual evaluation、CPU／RAM 與實際 validation dataset 門檻都通過後，才建立 `ITransactionClassificationService` 將 category、probability 與 decision mode 接入 BankReportImporter。BankReportImporter 不應直接引用 ONNX Runtime。
