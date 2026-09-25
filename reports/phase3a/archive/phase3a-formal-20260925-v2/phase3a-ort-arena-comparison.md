# Phase 3A CPU Arena 比較

狀態：**已取得 raw runs；仍須檢閱**

| Run ID | Scenario | 規劃數 | 嘗試數 | 完成數 | 錯誤 | Integrity failures |
|---|---|---:|---:|---:|---:|---:|
| `phase3a-formal-20260925-v2-arena-on` | full-pipeline | 5000 | 5000 | 5000 | 0 | 0 |
| `phase3a-formal-20260925-v2-arena-off` | full-pipeline | 5000 | 5000 | 5000 | 0 | 0 |

## Matched run observations

Both runs use the same v2 source identity `d77ea11b5bd8804606db84209fe498b0cee25ac9a1eb503ae4cd1622a509b778`, verified multilingual bundle/revision, .NET 10.0.12／ORT 1.30.0.0, short state／1 question／concurrency 1, 5 warmups and 5,000 measured requests. All request and integrity error counts are zero. Values below are raw measured outcomes, not production SLA.

| Metric | Arena ON | Arena OFF | OFF minus ON |
| --- | ---: | ---: | ---: |
| P50 latency | 76.744 ms | 76.401 ms | -0.343 ms |
| P95 latency | 111.395 ms | 109.953 ms | -1.442 ms |
| P99 latency | 130.688 ms | 129.648 ms | -1.040 ms |
| Peak WorkingSet / VmRSS | 968,077,312 bytes | 967,176,192 bytes | -901,120 bytes |
| Peak PrivateMemory | 2,061,332,480 bytes | 2,061,623,296 bytes | +290,816 bytes |
| Request-5,000 WorkingSet / VmRSS | 968,040,448 bytes | 967,127,040 bytes | -913,408 bytes |
| Request-5,000 PrivateMemory | 2,027,966,464 bytes | 2,027,290,624 bytes | -675,840 bytes |
| TotalAllocatedBytes delta (measured 0→5,000) | 128,601,200 bytes | 128,615,976 bytes | +14,776 bytes |

## Natural 2,500–5,000 slopes

OLS uses natural `measurement` rows on the request axis; idle／GC／post-dispose rows are excluded. Units are bytes per 100 requests.

| Metric | Arena ON slope／delta／R² | Arena OFF slope／delta／R² |
| --- | --- | --- |
| WorkingSet | 3,422／+118,784／0.3318 | 1,453／+73,728／0.1057 |
| PrivateMemory | -32,412／-823,296／0.4991 | -30,792／-778,240／0.5192 |
| VmRSS | 3,422／+118,784／0.3318 | 1,453／+73,728／0.1057 |
| Managed heap | -10,548／-2,695,464／0.0010 | -10,249／-2,682,176／0.0010 |

Both runs’ requested/effective arena setting and hashes are retained in their run manifests. These two observations show small measured differences, not an established arena memory or latency trade-off; replicate stage remains pending. The baseline managed-heap segment has unavailable samples and is still blocked; arena results do not replace it.
