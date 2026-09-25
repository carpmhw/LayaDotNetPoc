# Phase 3A 記憶體元件拆解 v3

Campaign `phase3a-formal-20260925-v3`；每個 row 是各自程序內的單一 scenario，不把不同 setup 的 allocations 當成可加總的元件成本。Process peaks 只取 measurement samples；managed heap peak 是該段內可用 observations 的最大值；`TotalAllocatedBytes` 是 request 0 到最後 request 的 process-wide 累積配置差，不是 retained memory。

| Run／Scenario | Requests | P50／P95／P99 (ms) | Measurement peak WorkingSet／VmRSS (bytes) | Peak PrivateMemory (bytes) | Peak observed managed heap (bytes) | Managed heap unavailable samples | TotalAllocatedBytes delta (bytes) |
|---|---:|---|---:|---:|---:|---:|---:|
| `phase3a-formal-20260925-v3-component-tokenizer-en`／tokenizer-only | 10,000 | 0.0068／0.0161／0.0669 | 408,739,840／408,739,840 | 524,087,296 | 8,858,872 | 0／202 | 24,875,976 |
| `phase3a-formal-20260925-v3-component-tokenizer-zh`／tokenizer-only | 10,000 | 0.0059／0.0140／0.0694 | 408,428,544／408,428,544 | 524,009,472 | 8,864,144 | 0／202 | 24,952,888 |
| `phase3a-formal-20260925-v3-component-tokenizer-mixed`／tokenizer-only | 10,000 | 0.0075／0.0178／0.0683 | 412,282,880／412,278,784 | 528,142,336 | 8,857,544 | 0／202 | 24,970,688 |
| `phase3a-formal-20260925-v3-component-sequence-only`／sequence-only | 10,000 | 0.0922／0.1733／0.3091 | 418,369,536／418,369,536 | 532,180,992 | 8,945,752 | 3／202 | 200,481,912 |
| `phase3a-formal-20260925-v3-component-tensor-only`／tensor-only | 10,000 | 0.0044／0.0119／0.0399 | 416,382,976／416,382,976 | 527,601,664 | 8,863,120 | 0／202 | 36,094,352 |
| `phase3a-formal-20260925-v3-component-calibration-only`／calibration-only | 10,000 | 0.0033／0.0096／0.0347 | 408,928,256／408,928,256 | 524,046,336 | 8,890,280 | 0／202 | 35,365,816 |
| `phase3a-formal-20260925-v3-component-run-only`／run-only | 5,000 | 77.0547／109.2736／127.4144 | 962,260,992／962,260,992 | 2,050,940,928 | 8,298,800 | 0／102 | 12,366,560 |
| `phase3a-formal-20260925-v3-component-full-pipeline`／full-pipeline | 5,000 | 77.7246／110.8112／131.6414 | 966,787,072／966,787,072 | 2,056,716,288 | 8,713,336 | 9／102 | 128,610,264 |

## 固定 2,500–5,000 segment slopes（bytes／100 requests）

| Scenario | WorkingSet | PrivateMemory | VmRSS | Managed heap |
|---|---:|---:|---:|---|
| tokenizer-en | 60,381 | 51,124 | 60,616 | 114,794 |
| tokenizer-zh | 50,503 | 41,805 | 50,822 | 115,398 |
| tokenizer-mixed | 117,546 | 105,740 | 116,696 | 116,157 |
| sequence-only | 78,597 | 54,815 | 78,655 | BLOCKED：48／51 可用，含 unavailable sample |
| tensor-only | 262,552 | 252,465 | 262,832 | -91,030 |
| calibration-only | 35,759 | 31,345 | 35,759 | -72,208 |
| run-only | 9,329 | 3,947 | 9,455 | 245,326 |
| full-pipeline | 2,630 | -33,294 | 2,630 | BLOCKED：48／51 可用，含 unavailable sample |

run-only 的 5,000 次 `TotalAllocatedBytes` 增量為 12,366,560 bytes，full-pipeline 為 128,610,264 bytes；該差異屬 cumulative allocation，不代表仍存活／洩漏 bytes。Full-pipeline 的 9 個 unavailable managed samples 包含 late segment 4,800／4,850／4,900；sequence-only late segment亦有 3 個 unavailable 點。依「segment 中任何 required value unavailable 即不計 slope」規則，這兩個 managed late slopes 明列 BLOCKED，未用剩餘資料代算。所有其餘 slope 與 raw CSV 中 51 個 late observations 一致；模型載入／native reserved address space 的峰值不能直接解讀為 retained working set 或 leak。

具體 run 身分、每段原始 samples、latency 與 artifact hashes 見 `runs/phase3a-formal-20260925-v3-component-*/`。