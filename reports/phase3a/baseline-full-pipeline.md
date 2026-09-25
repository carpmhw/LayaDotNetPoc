# Phase 3A Full-Pipeline 記憶體基線

Campaign ID：`phase3a-formal-20260925-v3`
狀態：**已取得 raw run；仍須完成 policy／Gate 驗證**

來源 run：`phase3a-formal-20260925-v3-baseline-full-pipeline`

## 分段 OLS slope（bytes／100 requests）

| Metric | Request 區間 | Slope | 端點差值 | R² | 樣本數 | 狀態 |
|---|---:|---:|---:|---:|---:|---|
| working_set_bytes | 0-500 | 3021281.073825503 | 17903616 | 0.8139946847325443 | 12 | complete |
| working_set_bytes | 500-1000 | 305189.2363636364 | 2568192 | 0.4237017174528659 | 11 | complete |
| working_set_bytes | 1000-2500 | 21873.96129032258 | 282624 | 0.7310508114250456 | 31 | complete |
| working_set_bytes | 2500-5000 | 52491.815384615344 | 1040384 | 0.6613748744166591 | 51 | complete |
| private_memory_bytes | 0-500 | 8548619.402074436 | 49086464 | 0.43997317260592606 | 12 | complete |
| private_memory_bytes | 500-1000 | -1547021.9636363636 | 19218432 | 0.03614225809260263 | 11 | complete |
| private_memory_bytes | 1000-2500 | -739361.032258064 | -33484800 | 0.13644764437502177 | 31 | complete |
| private_memory_bytes | 2500-5000 | 19986.255927601807 | 188416 | 0.10949318999720947 | 51 | complete |
| vmrss_bytes | 0-500 | 3022992.946918853 | 17895424 | 0.813954664776014 | 12 | complete |
| vmrss_bytes | 500-1000 | 309210.7636363636 | 2592768 | 0.42522179774571356 | 11 | complete |
| vmrss_bytes | 1000-2500 | 21873.96129032258 | 282624 | 0.7310508114250456 | 31 | complete |
| vmrss_bytes | 2500-5000 | 52491.815384615344 | 1040384 | 0.6613748744166591 | 51 | complete |
| managed_heap_bytes | 0-500 | 524435.1921903599 | 5236352 | 0.17660550602076952 | 12 | complete |
| managed_heap_bytes | 500-1000 | -622144.8727272728 | -3873384 | 0.1439916376410565 | 11 | complete |
| managed_heap_bytes | 1000-2500 | BLOCKED | BLOCKED | BLOCKED | 31 | A required memory metric is unavailable in the segment. |
| managed_heap_bytes | 2500-5000 | 8827.853031674214 | 1510992 | 0.0007672320410970768 | 51 | complete |
