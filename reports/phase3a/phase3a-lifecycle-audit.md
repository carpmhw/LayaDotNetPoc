# Phase 3A 生命週期稽核 v3

**Source-level owner audit 已完成；leak disposition 未能確認。** 本稽核綁定 campaign `phase3a-formal-20260925-v3`、policy SHA-256 `a72da66f85f95497d8b54bc3fa0e258dfc4bc61518ebff735df383d2928be`（見 machine-readable manifest 為權威 hash）、source commit `98f4840c2b7746b94825fe904f9228ed63c43392` 與 dirty identity `737f971d1cf7de4409c026245c8cbf9f09fe1279e1149559f0c146b34924dfec`。

| 資源 owner | Source path | 正常釋放／失敗路徑 | 相關測試／runs |
|---|---|---|---|
| 五個輸入 OrtValues 與 backing arrays | `src/Laya.Core/Inference/LayaInputTensorOwner.cs` | 每 request 由 `using` 管理；Dispose 清除 Inputs 並反向釋放 OrtValues；部分建構失敗會釋放已建立 tensors，並保留建構／dispose 例外。 | `LayaInputTensorOwnerTests.Create_OwnsFiveInputsAndDisposesThemOnce`、`Create_DisposesPreviouslyCreatedValuesWhenFactoryThrows`、`LayaInferenceRunnerLifecycleTests.Run_DisposesInputValuesWhenNativeRunThrows` |
| RunOptions 與 output OrtValues | `src/Laya.Core/Inference/LayaInferenceRunner.cs` | RunOptions／輸出 collection 由 scope 管理；結果先複製到 managed arrays；Run 與輸出驗證失敗同樣離開 scope 釋放。 | `LayaInferenceRunnerLifecycleTests.Run_DisposesInputValuesWhenNativeRunThrows`；`phase3a-formal-20260925-v3-component-run-only` |
| ONNX session／SessionOptions | `src/Laya.Core/Inference/LayaOnnxSession.cs` | 成功初始化後 session ownership 移交給 wrapper；SessionOptions 在 `finally` dispose；開啟／provider 設定部分失敗釋放尚未移交 session/options。 | `LayaOnnxSessionOptionsTests` arena ON／OFF；session-recreate 10／100 runs |
| Decision engine／tokenizer | `src/Laya.Core/Inference/LayaDecisionEngine.cs`、`src/Laya.Core/Tokenization/LayaTokenizer.cs` | Engine idempotent Dispose 釋放 tokenizer／session；Load／Open 部分建構失敗會釋放尚未移交 ownership 的 native handle。 | `LayaDecisionEngineTests.Dispose_ReleasesInjectedSessionAndTokenizerAfterRepeatedDecisions`；tokenizer tests |
| Probe scenario／run-only workers | `tools/Laya.MemoryProbe/Scenarios/MemoryProbeScenarioExecution.cs` | bounded workers drain 後各自釋放 owner，再釋放 scenario resources；建立 worker／scenario 中途失敗會清理部分資源。 | `MemoryProbeScenarioExecutionTests`、`BoundedProbeCoordinatorTests` |

## `TotalAllocatedBytes` 與 session-recreate 量測

`TotalAllocatedBytes` 是 process-wide cumulative allocation，不是 retained heap。

| Run ID | Request 0／cycle-0 | Final measured | Post-dispose | 增量 | 完成／錯誤 |
|---|---:|---:|---:|---:|---|
| `phase3a-formal-20260925-v3-baseline-full-pipeline` | 2,214,992 | 130,830,416 | 131,403,888 | 128,615,424 bytes／5,000 requests | 5,000／0 |
| `phase3a-formal-20260925-v3-component-full-pipeline` | 2,206,984 | 130,817,248 | 131,390,824 | 128,610,264 bytes／5,000 requests | 5,000／0 |
| `phase3a-formal-20260925-v3-component-run-only` | 2,132,064 | 14,498,624 | 14,672,384 | 12,366,560 bytes／5,000 requests | 5,000／0 |
| `phase3a-formal-20260925-v3-session-recreate-10` | 1,643,456 | — | 4,954,440 | 3,310,984 bytes／10 cycles | 10／0 |
| `phase3a-formal-20260925-v3-session-recreate-100` | 1,651,696 | — | 30,997,272 | 29,345,576 bytes／100 cycles | 100／0 |

Session-recreate 10／100 cycles dispose 後 WorkingSet 為 714,723,328／727,191,552 bytes，PrivateMemory 為 910,888,960／905,306,112 bytes，VmRSS 為 714,702,848／727,191,552 bytes。兩組尾端讀數接近、且皆高於 T0／cycle-0 前建立 session 的讀數；目前無法將殘留歸因於特定 owner，亦未證明 allocator plateau。此數據不支持 Leak Confirmed 或 No Leak Evidence 分類。

Baseline process late segments有端點且變化小；但 managed heap **1,000–2,500** 區段於 request 1,900／1,950／2,200／2,250／2,300 五點回報負值，已保存為 unavailable；**2,500–5,000** managed samples完整。component full-pipeline late segment 另有 4,800／4,850／4,900 三點 unavailable。Source audit、counter anomaly、shape partial 與 reproducibility variance 是不同 evidence 限制，不彼此替代。詳細 machine-readable owner／disposition 見 `phase3a-lifecycle-audit.json`；overall Gate 仍 blocked。
