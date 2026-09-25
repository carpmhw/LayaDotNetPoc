# Phase 3A Session 生命週期 v3

Campaign `phase3a-formal-20260925-v3`；兩個 run 均為同一 verified bundle／workload／.NET 10.0.12／ORT 1.30.0.0，concurrency 1、warmup 0，每 cycle Create → Run once → Dispose，並保存 cycle 前與 Dispose 後 process／GC counters。

| Run ID | Cycles | Mean／P50／P95 cycle latency (ms) | Peak WorkingSet | Peak PrivateMemory | Peak VmRSS | Errors |
|---|---:|---|---:|---:|---:|---:|
| `phase3a-formal-20260925-v3-session-recreate-10` | 10 | 1,629.625／1,611.183／1,723.984 | 715,321,344 bytes | 910,823,424 bytes | 715,292,672 bytes | 0 |
| `phase3a-formal-20260925-v3-session-recreate-100` | 100 | 1,477.244／1,464.834／1,608.445 | 722,997,248 bytes | 916,156,416 bytes | 722,997,248 bytes | 0 |

## Session recreation 與 singleton baseline 對照

| Measurement | Singleton baseline：5,000 full-pipeline requests | Recreate：10 cycles | Recreate：100 cycles |
|---|---:|---:|---:|
| T0／cycle-0 before-create WorkingSet | T0 60,923,904 bytes；request-0 after model setup 946,016,256 | cycle-0 398,356,480 | cycle-0 398,622,720 |
| Post-dispose WorkingSet | 483,860,480 bytes | 714,723,328 bytes | 727,191,552 bytes |
| Post-dispose PrivateMemory | 677,310,464 bytes | 910,888,960 bytes | 905,306,112 bytes |
| Post-dispose VmRSS | 483,860,480 bytes | 714,702,848 bytes | 727,191,552 bytes |
| `TotalAllocatedBytes` delta | 128,615,424 bytes／5,000 requests | 3,310,984 bytes／10 cycles | 29,345,576 bytes／100 cycles |

10／100-cycle post-dispose WorkingSet 差約 1.7%、PrivateMemory 差約 0.6%，但兩者均高於 T0／cycle-0 前建立 session 的讀值。相近尾端值與現存 residual process memory 相符；尚無 source-attributed owner，也未完成 allocator comparison，因此不能認定 bounded allocator plateau。`TotalAllocatedBytes` 是 cumulative allocation，不是 retained bytes；cycle 軸 OLS 僅供描述，未與 5,000-request 固定工作負載 slope 混比。

Singleton 是 5,000 個 end-to-end requests；recreate 是 10／100 次 Create→Run→Dispose，不是等工作量 throughput replicate。每 cycle raw samples、run identity 與 post-dispose counters 保存在各 run 目錄。Lifecycle source audit 未確認未釋放 owner；managed counter anomalies、shape partial 與 reproduction 分歧使整體 Memory Gate 保持 blocked。
