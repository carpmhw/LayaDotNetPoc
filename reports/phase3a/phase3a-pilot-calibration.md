# Phase 3A Pilot Calibration

## 狀態與身分

此報告記錄工具／環境 pilot 與正式 policy 輸入；**不是**正式 baseline、完整 memory matrix 或 Memory Gate 證據。Pilot 結果不得作為 PASS／PARTIAL／FAIL 結論。

- Pilot campaign：`phase3a-pilot-20260925`，stage status `complete`，4/4 runs complete。
- Noise repeats：`phase3a-pilot-noise-a-20260925`、`phase3a-pilot-noise-b-20260925`，各 1,000 measured full-pipeline requests，0 errors、0 integrity failures。
- Container cgroup smoke：`phase3a-pilot-docker-load-20260925`，load-only、0 requests；非 request matrix replicate。
- Source：commit `98f4840c2b7746b94825fe904f9228ed63c43392`，dirty identity `b62688199f34956240dd1aaccd3fc55a3805be0ff5561497c8c2113f4107ed18`。
- Workload SHA-256：`9c781dd4f312dc90b5f74443721650a7f2a5a87d2745bbdb6d353fb900a5b56a`。
- Bundle revision：`052592a15d198d9ad47da779604259b10b47b7aa`；manifest SHA-256 `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`；tokenizer SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。
- Machine-readable pilot artifact index SHA-256：`56534c4c65cc488faa6b7d5cb9e45ec86d8ad718c9d3756bab1f09dfe36583bf` (`phase3a-pilot-calibration.json`)。

## 環境與可用性

| 執行環境 | Runtime／ORT | 記憶體限制 | 採樣可用性 |
| --- | --- | --- | --- |
| Native pilot | .NET 10.0.12／ORT 1.30.0.0，Linux Mint 22.3，kernel 7.0.0-34，Ryzen 7 255，10 logical cores | RAM 16,692,830,208 bytes；swap 2,147,479,552 bytes；可見 cgroup mount 無法取得 native host limit | `/proc` 指定欄位皆有資料；manifest 將 host cgroup limit／swap 記為 null |
| Docker load-only smoke | .NET 8.0.31／ORT 1.30.0.0，Docker 29.8.1，Debian 12 | 要求 4,000,000,000 bytes；effective cgroup memory 3,999,997,952 bytes（page-rounded）；swap limit 0 | container `memory.max`／`memory.swap.max` 可讀；0 requests，不作為 throughput／soak 結果 |

Native runtime 與 Docker runtime 不混作同組 replicate。實際 Docker image 為 .NET runtime，不含 Python／Node.js；verified bundle 以唯讀 `/models` 掛載，run evidence 寫至獨立 `/reports`。

## Pilot 量測摘要

- 固定 tokenizer fixture zh／en／mixed 各 1,000 requests，皆完整且無 errors／integrity failures；P50 約 0.0065–0.0082 ms，P95 約 0.0179–0.0243 ms。
- 初始 campaign full-pipeline：200 requests、warmup 5、concurrency 1、arena ON、short state／1 question；P50 78.231 ms、P95 109.524 ms，0 errors／integrity failures。
- 兩次 1,000-request native repeat：

| 指標 | Repeat A | Repeat B | 相對差異概況 |
| --- | ---: | ---: | ---: |
| P50 latency | 77.308 ms | 79.128 ms | 約 2.3% |
| P95 latency | 112.746 ms | 115.229 ms | 約 2.2% |
| request 1,000 VmRSS | 968,347,648 bytes | 969,224,192 bytes | 876,544 bytes（約 0.1%） |
| request 1,000 PrivateMemory | 2,027,913,216 bytes | 2,028,773,376 bytes | 860,160 bytes（約 0.04%） |
| request 500→1,000 VmRSS endpoint delta | 4,444,160 bytes | 4,108,288 bytes | endpoint rate 888,832／821,658 bytes per 100 requests |
| request 1,000 TotalAllocatedBytes | 33,937,448 bytes | 33,950,840 bytes | process-wide cumulative allocation；非 retained heap |

PrivateMemory 曾有約 34.7 MB 的單一 checkpoint spread，但 repeat end 值接近；pilot 不足以將該變化歸因 allocator、native retention 或 leak。GC index 到 request 1,000 為 4；沒有 forced GC。200-request 與 1,000-request pilot 都不覆蓋正式 2,500–5,000 late segment。

每個 1,000-request run 約寫出 9.97 KB samples、25.96–26.01 KB numeric latency、37 bytes errors CSV 與約 0.62 KB summary。Process-wide TotalAllocatedBytes 包含 inference、Probe buffers、collector 與 writer；沒有 telemetry-disabled control，因此**無法從本 pilot 單獨分離**各項 instrumentation allocation。

## Policy freeze rationale

`memory-policy.json` 已凍結，SHA-256：`191da02a3ce54b74feff9942095604e2ad7f99fe1a8e7fa37849303380f4dfc5`。Policy 的 pilot artifact hash 指向上方 index SHA，target 固定為十進位 3,000,000,000 bytes。

- 以 3 GB target 保留明確 600,000,000-byte（20%）deployment headroom；這是此 POC 選用的部署預算，不是 production SLA。
- Native pilot 較大的 500–1,000 VmRSS endpoint rate 約 888,832 bytes／100 requests；PrivateMemory／RSS 最大 late slope budget 設為 2,000,000 bytes／100 requests，約為此短 pilot endpoint rate 的 2.25 倍。
- 另設 64,000,000-byte late growth cap，約為 600 MB reserve 的 10.7%；在 2 MB／100 requests 的 slope 上，2,500-request late segment 對應 50 MB，留下 14 MB growth margin。PrivateMemory 和 RSS 必須同時符合 slope 與 growth cap。
- Managed heap 使用 1,000,000 bytes／100 requests slope、32,000,000 bytes growth cap；TotalAllocatedBytes 作為 allocation rate 觀察值，不能代替 retained heap。
- Replicate peak／end 相對差異上限為 5%，略高於 pilot 已見的記憶體 checkpoint spread；slope 差異上限為 25%，若兩次不一致須執行第三次。R² 下限 0.95 僅代表嚴格的線性擬合品質，不能單獨宣告 leak，仍須同時超過正向 slope／growth budgets。

此 policy 的 late-segment 門檻尚未由正式 2,500–5,000 runs 驗證；正式 campaign 須依 policy 完整執行。若 policy 要改值，需新 hash／campaign 並重做必要證據。完整 thresholds、minimum matrix、pilot IDs 與限制見 `memory-policy.json`；pilot raw files／manifest hashes 見 `phase3a-pilot-calibration.json`。
