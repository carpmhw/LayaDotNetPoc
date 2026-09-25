# Phase 3A Pilot Calibration v2

## 狀態與來源

此版本 pilot 使用 managed heap counter 的負值防護，取代原 v1 policy 校準輸入。Pilot、load smoke 與 policy-parse smoke 仍不是 formal baseline／Gate runs。

- Campaign：`phase3a-pilot-20260925-v2`，stage 4/4 complete；source commit `98f4840c2b7746b94825fe904f9228ed63c43392`，dirty identity `d77ea11b5bd8804606db84209fe498b0cee25ac9a1eb503ae4cd1622a509b778`。
- Native pilot：tokenizer zh／en／mixed 各 1,000、full-pipeline 200；另有 full-pipeline repeats A／B 各 1,000。全部 0 errors、0 integrity failures、0 `managed_heap_unavailable_reason` 樣本。
- Docker pilot：.NET 8.0.31／ORT 1.30.0.0、full-pipeline 500 requests；0 errors／integrity failures，所有 managed heap samples 非負且無 unavailable reason。Requested memory 4,000,000,000 bytes，effective cgroup memory 3,999,997,952 bytes，swap limit 0；此結果是 runtime diagnostics pilot，不是 3 GB target matrix。
- Native host：.NET 10.0.12／ORT 1.30.0.0；Linux `/proc` 欄位可用，native host cgroup limit 在可見 mount 下 unavailable。容器 pilot 為獨立 runtime group。
- Bundle revision `052592a15d198d9ad47da779604259b10b47b7aa`；manifest SHA-256 `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`；tokenizer SHA-256 `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`。
- Calibration index：`phase3a-pilot-calibration-v2.json`，SHA-256 `251ee705174657c11b25f97c34a8ce263ac2737892ff96d537b3a2cfa0df9faa`。
- Frozen v2 policy：`memory-policy-v2.json`，SHA-256 `fca022bdbcc450d95eec4c28c4bcd00cc7aba9fec68fb5249c6f341f35628a68`；target `3,000,000,000` decimal bytes。

## Noise 與 tooling observations

| 指標 | Native repeat A | Native repeat B |
| --- | ---: | ---: |
| P50 latency | 76.378 ms | 78.206 ms |
| P95 latency | 113.789 ms | 112.553 ms |
| request 1,000 VmRSS | 968,126,464 bytes | 968,048,640 bytes |
| request 1,000 PrivateMemory | 2,044,592,128 bytes | 2,044,743,680 bytes |
| request 1,000 managed heap | 1,185,344 bytes | 1,263,760 bytes |
| request 1,000 TotalAllocatedBytes | 34,024,064 bytes | 34,144,736 bytes |
| request 500→1,000 VmRSS endpoint rate | 10,649.6 bytes／100 requests | 78,643.2 bytes／100 requests |

End RSS 差異 77,824 bytes，end PrivateMemory 差異 151,552 bytes；P50／P95 latency repeat 差異約 2.4%／1.1%。request 500 PrivateMemory 仍有約 33.7 MB spread，因此 policy 保留 third-replicate trigger；上述 1,000-request pilot rates不代表 formal 2,500–5,000 late-segment slope。

1,000-request run 約輸出 9.8–10.1 KB samples 與 25.9 KB numeric latency。TotalAllocatedBytes 是整個 Probe process 的 cumulative managed allocation，包含 application 與 Probe／collector／writer；未執行 telemetry-disabled control，無法把 overhead 因果拆分。

## Counter validity finding

舊 campaign `phase3a-formal-20260925` 的 5,000 requests 執行完成且 request errors／integrity failures 為 0，但其舊 `managed_heap_bytes` raw CSV 在 request 250–400、3,200–3,300、3,850–3,950 出現 9 筆負值。欄位直接來自 `GC.GetTotalMemory(false)`；依 API 定義它是 managed heap bytes，負數不是有效 heap size。舊 run 與 v1 policy 保留原樣、列為歷史資料品質異常，**不**納入 v2 threshold calibration 或新的 formal campaign。

Probe v2 將負回傳保存為空 `managed_heap_bytes` 加 `managed_heap_unavailable_reason`，絕不截成零；SlopeAnalyzer 對區間內任一 unavailable managed sample 回報 incomplete，不得以該區段聲稱 managed plateau。這項防護沒有聲稱已找到 .NET runtime 或 ORT 回傳負數的底層原因。

## Frozen v2 threshold rationale

- Target memory limit 是 3,000,000,000 bytes；此 POC 明確保留 600,000,000 bytes（20%）作部署 headroom。
- PrivateMemory／RSS 上限設為 2,000,000 bytes per 100 requests，並另設 64,000,000-byte growth cap。以 2 MB／100 的 slope 計算，2,500-request late segment 為 50 MB，低於 reserve 並保留至 64 MB 上限；兩項條件必須同時通過。
- Managed heap 上限為 1,000,000 bytes／100 requests 與 32,000,000-byte growth cap；unavailable／負值樣本使 slope segment blocked。
- Near-linear R² 門檻 0.95 僅是擬合品質要求，不單獨判 leak；replicate peak／end tolerance 為 5%、slope tolerance 為 25%，超界要有第三次獨立 replicate。
- 這些是 tie 到 3 GB 部署預算與 v2 pilot variability 的 POC 判準，不是 formal 5,000-request 實測結論或 production SLA。尚未執行 v2 formal baseline、2,500–5,000 late segment、target Docker soak 或完整 experiment matrix。
