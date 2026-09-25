# Phase 3A 記憶體並行矩陣 v3

Campaign `phase3a-formal-20260925-v3`；三個獨立程序均使用 multilingual／short／一題 Choice／arena ON／.NET 10.0.12／ORT 1.30.0.0，warmup 5、每組 1,000 measured requests。source、policy 與 workload identity 相同。

| Run ID | Concurrency | 完成／錯誤 | P50／P95／P99 (ms) | 峰值 WorkingSet／VmRSS (bytes) | 峰值 PrivateMemory (bytes) | Post-dispose WorkingSet／VmRSS (bytes) | Post-dispose PrivateMemory (bytes) | Checksum |
|---|---:|---|---|---:|---:|---:|---:|---:|
| `phase3a-formal-20260925-v3-concurrency-1` | 1 | 1000／0 | 133.313／176.557／196.848 | 964,997,120／964,997,120 | 2,050,920,448 | 479,571,968／479,571,968 | 623,861,760 | 1123.6709091286364 |
| `phase3a-formal-20260925-v3-concurrency-2` | 2 | 1000／0 | 135.612／174.897／193.058 | 965,185,536／965,185,536 | 2,050,883,584 | 479,776,768／479,776,768 | 623,767,552 | 1123.6709091286364 |
| `phase3a-formal-20260925-v3-concurrency-4` | 4 | 1000／0 | 134.170／173.502／203.468 | 968,511,488／968,511,488 | 2,057,744,384 | 483,098,624／483,098,624 | 627,138,560 | 1123.6709091286364 |

三個 run 的 checksum 一致，沒有操作 errors；這些數據是獨立程序的 concurrency／process-memory observations，不是每組 5,000 requests 的延伸結果。

## Integrity 狀態

**Integrity requirement blocked；不可將 summary counter 的零值當成通過。** `phase3a-concurrency-integrity-v3.json` 記錄了固定 requests `integrity-001` 至 `integrity-004`，但 Probe／campaign 並未執行這組 fixture，也未將 shared-engine 回應逐項和 serial reference 比對。`integrityChecksExecuted=false`；各 run 的 `integrityFailures=0` 僅表示未觸發 Probe counter，不構成 parity evidence。未執行 concurrency 5,000-request 加長測試。
