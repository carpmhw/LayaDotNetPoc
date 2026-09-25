# Phase 3A Full-Pipeline 記憶體基線

Campaign ID：`phase3a-formal-20260925-v2`
狀態：**已取得 raw run；仍須完成 policy／Gate 驗證**

來源 run：`phase3a-formal-20260925-v2-baseline-full-pipeline`

## 分段 OLS slope（bytes／100 requests）

| Metric | Request 區間 | Slope | 端點差值 | R² | 樣本數 | 狀態 |
|---|---:|---:|---:|---:|---:|---|
| working_set_bytes | 0-500 | 2721053.520439293 | 15392768 | 0.7311616524187448 | 12 | complete |
| working_set_bytes | 500-1000 | 138742.6909090909 | 925696 | 0.5295456071184126 | 11 | complete |
| working_set_bytes | 1000-2500 | 518213.36774193554 | 5455872 | 0.8105714092727705 | 31 | complete |
| working_set_bytes | 2500-5000 | 3201.1815384615384 | 118784 | 0.3282966018431519 | 51 | complete |
| private_memory_bytes | 0-500 | 7736916.656497864 | 29753344 | 0.32966195140842003 | 12 | complete |
| private_memory_bytes | 500-1000 | 119230.83636363635 | 839680 | 0.45081537893311097 | 11 | complete |
| private_memory_bytes | 1000-2500 | 500683.14838709694 | 5210112 | 0.796493932660514 | 31 | complete |
| private_memory_bytes | 2500-5000 | -29394.82352941176 | -757760 | 0.490397775081876 | 51 | complete |
| vmrss_bytes | 0-500 | 2726701.4521049415 | 15413248 | 0.7319431602307177 | 12 | complete |
| vmrss_bytes | 500-1000 | 138742.6909090909 | 925696 | 0.5295456071184126 | 11 | complete |
| vmrss_bytes | 1000-2500 | 518173.7290322582 | 5455872 | 0.8106858738999869 | 31 | complete |
| vmrss_bytes | 2500-5000 | 3201.1815384615384 | 118784 | 0.3282966018431519 | 51 | complete |
| managed_heap_bytes | 0-500 | 525985.600976205 | 5234288 | 0.17783814195346237 | 12 | complete |
| managed_heap_bytes | 500-1000 | -621607.4181818181 | -3866528 | 0.14383947131432018 | 11 | complete |
| managed_heap_bytes | 1000-2500 | 35478.690322580645 | 5134072 | 0.004462005008044945 | 31 | complete |
| managed_heap_bytes | 2500-5000 | BLOCKED | BLOCKED | BLOCKED | 51 | A required memory metric is unavailable in the segment. |
