# Phase 3A Shape 記憶體矩陣 v3

**Stage blocked：六個 short／medium／long × 一題、short × 1／2／5 題 cases complete；short→long→short retention case 只完成 2,600／3,000 requests。** 各獨立 case 使用 1,000 measured requests；下表 process／managed peaks 來自 measurement samples，managed peak 只代表可用值中的最大 observation。

| Run ID | Shape／questions | Sequence length | 完成／錯誤 | P95 (ms) | Peak WorkingSet／VmRSS (bytes) | Peak PrivateMemory (bytes) | Peak managed heap (bytes)／available samples |
|---|---|---:|---|---:|---:|---:|---:|
| `phase3a-formal-20260925-v3-shape-short-q1` | short／1 | 67 | 1000／0 | 108.023 | 966,311,936／966,311,936 | 2,056,638,464 | 8,064,912／19 of 22 |
| `phase3a-formal-20260925-v3-shape-medium-q1` | medium／1 | 133 | 1000／0 | 180.613 | 964,325,376／964,325,376 | 2,051,141,632 | 8,927,992／22 of 22 |
| `phase3a-formal-20260925-v3-shape-long-q1` | long／1 | 445 | 1000／0 | 497.588 | 963,452,928／963,452,928 | 2,035,539,968 | 8,514,008／21 of 22 |
| `phase3a-formal-20260925-v3-shape-question-short-q1` | short／1 | 67 | 1000／0 | 112.759 | 966,238,208／966,238,208 | 2,061,475,840 | 8,032,352／19 of 22 |
| `phase3a-formal-20260925-v3-shape-question-short-q2` | short／2 | 67 | 1000／0 | 176.280 | 964,100,096／964,100,096 | 2,056,093,696 | 8,534,624／18 of 22 |
| `phase3a-formal-20260925-v3-shape-question-short-q5` | short／5 | 67 | 1000／0 | 350.470 | 969,412,608／969,412,608 | 2,037,645,312 | 8,850,776／20 of 22 |

## Retention schedule 與 gate disposition

預定一個 session 中 short → long → short 各 1,000 requests 的 run `phase3a-formal-20260925-v3-shape-retention-short-long-short`，planned 3,000、attempted／completed 2,600、errors／integrity counter 0。Stage command 超過 30-minute tool timeout；child process 已停止，manifest 無 indexed raw artifacts，僅本機可見 partial CSV。`MemoryCampaignResumeValidator` 只重用完整且 artifact hash 驗證通過的 runs，因此此 run 不會被當作 complete 或原 run ID resume。

shape／question matrix cases不等於短→長→短 retention 測試；partial 2,600 也不支持最後 short 狀態的 retention 結論。Machine-readable interruption disposition 見 `phase3a-shape-interruption-v3.json`；保留 stage `in-progress` 與 overall Gate blocker，不推算未完成的 400 requests。
