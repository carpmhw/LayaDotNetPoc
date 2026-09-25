# Phase 3A v2 Managed Heap Counter Note

Formal baseline run `phase3a-formal-20260925-v2-baseline-full-pipeline` completed the planned 5,000 measured requests, 5 warmup requests, 30／60／300-second idle intervals, forced-GC pair and post-dispose sample. It had 0 request errors and 0 integrity failures. Raw data remain under `runs/phase3a-formal-20260925-v2-baseline-full-pipeline/`.

The new collector preserved three negative `GC.GetTotalMemory(false)` returns as unavailable instead of writing negative or zero bytes:

| Request count | Reported signed value (bytes) | GC index | Raw representation |
| ---: | ---: | ---: | --- |
| 4,800 | -3,299,128 | 15 | blank `managed_heap_bytes`; reason retained |
| 4,850 | -2,009,944 | 15 | blank `managed_heap_bytes`; reason retained |
| 4,900 | -722,856 | 15 | blank `managed_heap_bytes`; reason retained |

Those three missing managed observations make the natural 2,500–5,000 `managed_heap_bytes` slope incomplete; `baseline-full-pipeline.md` and the Gate JSON report it as blocked. WorkingSet, PrivateMemory and VmRSS checkpoints in the same interval are present. The baseline has not been classified as plateau or leak, and the counter’s underlying .NET 10／ORT cause is not established. The earlier v1 run is separately retained with its original negative raw values and is excluded from the v2 campaign.

The current status is therefore **successful 5,000-request execution with managed-heap metric evidence unavailable at three checkpoints**, not a memory Gate pass or model failure. Remaining formal stages and the runtime-specific investigation can proceed without changing this raw evidence.
