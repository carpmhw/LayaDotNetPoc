# Phase 3A Docker 記憶體矩陣

Campaign: `phase3a-formal-20260925-v3`

| 十進位 memory limit (bytes) | Case | Requests | Status | OOMKilled | Exit | Completed | Errors |
|---:|---|---:|---|---|---:|---:|---:|
| 2000000000 | full-pipeline | 1000 | complete | false | 0 | 1000 | 0 |
| 2000000000 | full-pipeline | 100 | complete | false | 0 | 100 | 0 |
| 2000000000 | load-only | 0 | complete | false | 0 | 0 | 0 |
| 2500000000 | full-pipeline | 1000 | complete | false | 0 | 1000 | 0 |
| 2500000000 | full-pipeline | 100 | complete | false | 0 | 100 | 0 |
| 2500000000 | load-only | 0 | complete | false | 0 | 0 | 0 |
| 3000000000 | full-pipeline | 1000 | complete | false | 0 | 1000 | 0 |
| 3000000000 | full-pipeline | 100 | complete | false | 0 | 100 | 0 |
| 3000000000 | full-pipeline | 5000 | complete | false | 0 | 5000 | 0 |
| 3000000000 | full-pipeline | 5000 | complete | false | 0 | 5000 | 0 |
| 3000000000 | load-only | 0 | complete | false | 0 | 0 | 0 |
| 4000000000 | full-pipeline | 1000 | complete | false | 0 | 1000 | 0 |
| 4000000000 | full-pipeline | 100 | complete | false | 0 | 100 | 0 |
| 4000000000 | full-pipeline | 5000 | complete | false | 0 | 5000 | 0 |
| 4000000000 | load-only | 0 | complete | false | 0 | 0 | 0 |

3 GB target runs: 2; successful: 2; repeated OOM: false; stable: true; limits verified: true

結果僅供 Phase 3A POC Memory Gate，Docker stats raw 欄位不冒稱 process RSS。

## Effective limits 與外部 peak／latency evidence

Image `laya-memory-probe:phase3a` 使用 .NET 8.0.31／ORT 1.30.0.0；每個容器以十進位 bytes 同值設定 `--memory`／`--memory-swap`，page size 為 4,096 bytes，所有外部 cgroup／process counter samples 均可讀取。cgroup swap limit/current 為 0。`memory.current` 含可能的 file cache；下表分開列 cgroup peak 與 `/proc/<pid>/status` process VmRSS。

| Requested limit (bytes) | Case／requests | Replicate | Effective memory.max (bytes) | cgroup memory.peak (bytes) | process VmRSS peak (bytes) | P95 (ms) | Completed／errors／OOM |
|---:|---|---:|---:|---:|---:|---:|---|
| 2,000,000,000 | load-only／0 | 1 | 1,999,998,976 | 542,961,664 | 108,908,544 | — | 0／0／false |
| 2,000,000,000 | full-pipeline／100 | 1 | 1,999,998,976 | 948,158,464 | 943,550,464 | 122.447 | 100／0／false |
| 2,000,000,000 | full-pipeline／1,000 | 1 | 1,999,998,976 | 948,064,256 | 949,350,400 | 137.526 | 1,000／0／false |
| 2,500,000,000 | load-only／0 | 1 | 2,499,997,696 | 542,253,056 | 525,602,816 | — | 0／0／false |
| 2,500,000,000 | full-pipeline／100 | 1 | 2,499,997,696 | 947,892,224 | 942,882,816 | 125.899 | 100／0／false |
| 2,500,000,000 | full-pipeline／1,000 | 1 | 2,499,997,696 | 948,248,576 | 949,387,264 | 145.192 | 1,000／0／false |
| 3,000,000,000 | load-only／0 | 1 | 2,999,996,416 | 542,326,784 | 595,587,072 | — | 0／0／false |
| 3,000,000,000 | full-pipeline／100 | 1 | 2,999,996,416 | 948,404,224 | 931,205,120 | 177.488 | 100／0／false |
| 3,000,000,000 | full-pipeline／1,000 | 1 | 2,999,996,416 | 948,420,608 | 954,073,088 | 148.008 | 1,000／0／false |
| 3,000,000,000 | full-pipeline／5,000 | 1 | 2,999,996,416 | 948,387,840 | 956,612,608 | 142.536 | 5,000／0／false |
| 3,000,000,000 | full-pipeline／5,000 | 2 | 2,999,996,416 | 948,228,096 | 956,821,504 | 144.974 | 5,000／0／false |
| 4,000,000,000 | load-only／0 | 1 | 3,999,997,952 | 542,638,080 | 153,952,256 | — | 0／0／false |
| 4,000,000,000 | full-pipeline／100 | 1 | 3,999,997,952 | 948,633,600 | 943,624,192 | 153.134 | 100／0／false |
| 4,000,000,000 | full-pipeline／1,000 | 1 | 3,999,997,952 | 948,215,808 | 953,675,776 | 188.324 | 1,000／0／false |
| 4,000,000,000 | full-pipeline／5,000 | 1 | 3,999,997,952 | 948,305,920 | 943,374,336 | 143.587 | 5,000／0／false |

這個 v3 matrix 的全部 15 runs 均為 complete、limit inspection verified、0 errors／integrity failures、無 OOM；3 GB target 的兩次 5,000-request run 都完整成功。此為本機 .NET 8 container evidence；不可與 .NET 10 native baseline 混成同組 latency／slope replicate，也不能單獨放行 Phase 3B，因 shape retention、integrity audit、reproduction consistency 與 managed-heap baseline segment仍有 blockers。
