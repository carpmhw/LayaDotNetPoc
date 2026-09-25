# Phase 3A CPU Arena 比較 v3

Campaign `phase3a-formal-20260925-v3`；兩個 run 均為同一 multilingual bundle／workload、.NET 10.0.12、ORT 1.30.0.0、5 次 warmup、5,000 measured requests、concurrency 1；唯一設定差異為 `EnableCpuMemArena`。兩份 manifest 的 source identity、policy hash 與 workload hash 相同。

| Run ID | Arena | 完成／錯誤 | P50／P95／P99 (ms) | 峰值 WorkingSet／VmRSS (bytes) | 峰值 PrivateMemory (bytes) | Post-dispose WorkingSet／VmRSS (bytes) | Post-dispose PrivateMemory (bytes) |
|---|---|---|---|---:|---:|---:|---:|
| `phase3a-formal-20260925-v3-arena-on` | ON | 5000／0 | 77.852／111.114／133.084 | 968,331,264／968,331,264 | 2,062,286,848 | 483,868,672／483,868,672 | 677,298,176 |
| `phase3a-formal-20260925-v3-arena-off` | OFF | 5000／0 | 79.023／111.260／131.227 | 968,216,576／968,208,384 | 2,062,262,272 | 475,361,280／475,361,280 | 668,884,992 |

## 自然量測 2,500–5,000 requests

| Metric | Arena ON slope (bytes／100 requests) | ON endpoint delta (bytes) | Arena OFF slope (bytes／100 requests) | OFF endpoint delta (bytes) |
|---|---:|---:|---:|---:|
| WorkingSet | 3,215.27 | 114,688 | 2,768.97 | 102,400 |
| PrivateMemory | -29,535.68 | -757,760 | -59,338.25 | -819,200 |
| VmRSS | 3,215.27 | 114,688 | 2,768.97 | 102,400 |

OFF 相較 ON 的 P50 約高 1.50%、P95 高 0.13%、P99 低 1.40%；post-dispose WorkingSet 約低 1.76%、PrivateMemory 約低 1.24%。同 workload checksum 均為 `4955.674224215673`，run counters 為 0 errors／0 integrity failures。這個 counter 不是獨立 integrity-fixture 比對結果。

本 pair 沒有顯示有意義且可歸因的 arena trade-off。小的 process end 差異不等於釋放差異；自然 late slopes 為小的正／負變化，且 reproduction stage 的 arena ON／OFF 三次 repeats 各自仍有超出凍結 25% tolerance 的 slope 分歧。故不據此調整預設、宣稱 allocator plateau 或改變第二輪優化方向。各 run 的 raw samples、summary、manifest hashes 位於 `runs/<run-id>/`。
