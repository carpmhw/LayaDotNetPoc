# Phase 3A Forced-GC Control v3

Run `phase3a-formal-20260925-v3-forced-gc-full-pipeline` 為獨立 formal diagnostic control：5 次 warmup、5,000 measured、multilingual short／one question／concurrency 1／arena ON，forced-GC checkpoints 在 requests 100／500／1,000／5,000。全數 5,000 requests 完成，0 errors／integrity-counter failures。forced-GC run 不作為 natural baseline；強制 GC 前後 samples 不納入 natural OLS。

| Request checkpoint | Managed heap 前→後 (bytes) | WorkingSet 差 | PrivateMemory 差 | VmRSS 差 | GC index 前→後 |
|---:|---:|---:|---:|---:|---:|
| 100 | 3,823,184 → 731,104 | +532,480 | -167,936 | +532,480 | 1 → 3 |
| 500 | 2,741,464 → 703,344 | -425,984 | -528,384 | -425,984 | 4 → 6 |
| 1,000 | 5,278,640 → 698,176 | -12,288 | -4,096 | -12,288 | 7 → 9 |
| 5,000 | 3,207,456 → 689,952 | -4,194,304 | -4,194,304 | -4,194,304 | 21 → 23 |

Paired observations顯示明確 GC 後 managed heap 下降；process／RSS 差值較小且依 checkpoint 改變。這只描述 forced-GC control，不證明 natural-run plateau、leak 修復或 production workaround。

**Baseline managed counter 的限制：** v3 natural baseline 在 requests 1,900／1,950／2,200／2,250／2,300 五個 checkpoints 的 `GC.GetTotalMemory(false)` 回報負值，保存為 unavailable；因此 **1,000–2,500** managed-heap slope segment incomplete。Baseline **2,500–5,000** managed segment有完整 endpoints／samples，不能標成被上述五點阻擋。natural process／RSS late segments各自維持獨立分析，見 `baseline-full-pipeline.md`。
