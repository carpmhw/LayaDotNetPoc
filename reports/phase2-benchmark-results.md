# Phase 2 Benchmark Results

Status: **complete for the requested benchmark matrix; memory stability remains a diagnostic risk**.

All measurements were run in independent Release processes on the same host. The process ran with `DOTNET_ROLL_FORWARD=Major` because the host exposes .NET 10 while the projects target .NET 8. The runtime still uses ONNX Runtime CPU and one long-lived session/tokenizer per process.

## Environment and Artifacts

- OS: Linux Mint 22.3, x64, 10 logical processors.
- Runtime: `.NET 10.0.12` selected by roll-forward from the net8.0 target.
- ONNX Runtime: `Microsoft.ML.OnnxRuntime 1.30.0`, CPU provider.
- Percentiles: linear interpolation on sorted samples at `(n-1)*p`.
- Nine-scenario command: `dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile <profile> --collect --output <artifact>`.
- Multilingual warm artifact: `reports/phase2-benchmark-runs/multilingual-warm-collection.json`, run `20260924T045507682Z-87208680052c4ee281db922d1c9d29eb`.
- English warm artifact: `reports/phase2-benchmark-runs/english-warm-collection.json`, run `20260924T052253654Z-ca685ac671154f43b3bf5b6ce254f271`.
- Cold artifacts: `multilingual-cold-start.json` run `20260924T052525432Z-ecca60036c6f4d0aa1a413667e7036c7`; `english-cold-start.json` run `20260924T052531713Z-0e53b2f108ba4f16914640ae92f3b396`.

Each warm profile completed 9 scenarios with 5 warmups and 100 measured samples. Raw latency arrays and memory samples are retained in the artifacts.

## Cold Start

| Profile | Load ms | First end-to-end ms | First Run-only ms | WorkingSet before | After load | After first run | Peak |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| multilingual | 2403.994 | 94.840 | 67.919 | 44,142,592 | 928,473,088 | 934,703,104 | 934,703,104 |
| english | 1579.948 | 213.056 | 186.822 | 35,823,616 | 1,612,808,192 | 1,621,123,072 | 1,621,123,072 |

## Warm Run-Only

| Questions | State | Tokens | Sequence | Truncated | English Mean/P50/P95/P99 ms | Multilingual Mean/P50/P95/P99 ms |
| ---: | --- | ---: | ---: | --- | --- | --- |
| 1 | short | 28 | 62 | no | 174.105 / 168.877 / 216.170 / 276.801 | 61.895 / 58.408 / 87.230 / 101.913 |
| 1 | medium | 130 / 129 | 164 / 163 | no | 403.215 / 403.523 / 455.713 / 487.922 | 128.564 / 124.185 / 160.005 / 168.870 |
| 1 | long | 914 / 913 | 512 / 947 | yes / no | 1446.927 / 1414.029 / 1637.229 / 1786.278 | 929.381 / 933.000 / 1005.044 / 1087.642 |
| 2 | short | 28 | 62 | no | 307.032 / 301.056 / 356.784 / 382.656 | 102.283 / 97.295 / 139.259 / 154.020 |
| 2 | medium | 130 / 129 | 164 / 163 | no | 777.619 / 772.779 / 852.254 / 880.565 | 255.363 / 248.956 / 304.270 / 317.400 |
| 2 | long | 914 / 913 | 512 / 947 | yes / no | 2854.128 / 2825.983 / 3014.413 / 3547.309 | 1905.117 / 1891.986 / 2076.548 / 2199.960 |
| 5 | short | 28 | 62 | no | 713.283 / 707.769 / 776.868 / 810.596 | 227.948 / 217.880 / 311.645 / 332.581 |
| 5 | medium | 130 / 129 | 164 / 163 | no | 1930.313 / 1925.128 / 2060.794 / 2168.396 | 591.880 / 591.092 / 655.125 / 702.677 |
| 5 | long | 914 / 913 | 512 / 947 | yes / no | 7077.444 / 7065.287 / 7311.592 / 7890.187 | 4773.556 / 4751.285 / 4970.574 / 5669.758 |

## Warm End-to-End

End-to-end includes request encoding, tensor creation, ONNX Run, calibration and result mapping. It excludes model loading and report I/O.

| Questions | State | English P50/P95/P99 ms | Multilingual P50/P95/P99 ms |
| ---: | --- | --- | --- |
| 1 | short | 169.274 / 216.477 / 277.292 | 58.763 / 90.346 / 102.247 |
| 1 | medium | 403.888 / 456.089 / 488.317 | 124.476 / 160.276 / 169.154 |
| 1 | long | 1415.775 / 1638.611 / 1787.670 | 933.964 / 1006.024 / 1088.732 |
| 2 | short | 301.411 / 357.202 / 383.091 | 97.636 / 139.527 / 154.271 |
| 2 | medium | 773.413 / 853.038 / 881.153 | 249.390 / 304.752 / 317.903 |
| 2 | long | 2829.311 / 3017.054 / 3552.978 | 1894.166 / 2078.358 / 2201.824 |
| 5 | short | 708.476 / 777.569 / 811.149 | 218.279 / 312.170 / 334.307 |
| 5 | medium | 1926.518 / 2062.568 / 2170.739 | 592.207 / 656.030 / 703.534 |
| 5 | long | 7075.033 / 7318.692 / 7896.831 | 4757.698 / 4974.936 / 5683.298 |

## Practical Transaction Scenario

The scenario uses a four-field synthetic state (`description`, `amount`, `currency`, `direction`) with one Choice and one Noul question. Each profile completed 5 warmups and 200 measured requests.

| Profile | Artifact/run | Sequence | End-to-end Mean | P50 | P95 | P99 | Peak WorkingSet |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| multilingual | `multilingual-practical.json` / `20260924T043453350Z-64df3df225de466f9a9778958ffdc26c` | 62 | 98.062 ms | 91.934 | 143.858 | 153.611 | 954,630,144 bytes |
| english | `english-practical.json` / `20260924T043613169Z-45c0e4fa0f7c4115a30a81177cf05c11` | 62 | 304.686 ms | 300.929 | 364.422 | 378.744 | 1,669,890,048 bytes |

## Concurrency

Each row uses one shared engine/session/tokenizer, 5 warmups and 50 measured requests. Integrity compares each concurrent result with its sequential result for the same indexed request.

| Profile | Concurrency | Completed | Errors | Integrity failures | Throughput req/s | End-to-end P50/P95/P99 ms | Peak WorkingSet |
| --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| multilingual | 1 | 50 | 0 | 0 | 9.361 | 98.358 / 148.038 / 159.642 | 950,616,064 |
| multilingual | 2 | 50 | 0 | 0 | 11.134 | 174.531 / 217.558 / 299.688 | 964,132,864 |
| multilingual | 4 | 50 | 0 | 0 | 17.003 | 227.653 / 283.362 / 315.261 | 991,215,616 |
| english | 1 | 50 | 0 | 0 | 3.321 | 296.785 / 347.819 / 387.046 | 1,672,372,224 |
| english | 2 | 50 | 0 | 0 | 4.377 | 444.446 / 513.186 / 630.102 | 1,707,540,480 |
| english | 4 | 50 | 0 | 0 | 6.192 | 660.629 / 735.447 / 818.439 | 1,772,511,232 |

Raw concurrency artifacts are `reports/phase2-benchmark-runs/{multilingual,english}-concurrency-{1,2,4}.json`.

## Memory

The warm matrix process peaks were `1,809,342,464` bytes multilingual and `2,447,970,304` bytes English. The first 20 versus final 20 measured WorkingSet medians were:

| Profile | First 20 median bytes | Final 20 median bytes | Growth bytes | Fixed diagnostic boundary |
| --- | ---: | ---: | ---: | ---: |
| multilingual | 937,084,928 | 1,809,342,464 | 872,257,536 | 134,217,728 bytes |
| english | 1,637,715,968 | 2,447,970,304 | 810,254,336 | 134,217,728 bytes |

Both warm matrix trends exceed the pre-fixed POC diagnostic boundary. This is a memory-stability investigation risk, not evidence of a proven leak or an automatic Gate 3 FAIL. All measured peaks remain below `3,000,000,000` bytes, but the growth requires follow-up before any production conclusion.

## Reproduction

```bash
DOTNET_ROLL_FORWARD=Major dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --metadata --output reports/phase2-benchmark-runs/multilingual-metadata.json
DOTNET_ROLL_FORWARD=Major dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --cold-start --output reports/phase2-benchmark-runs/multilingual-cold-start.json
DOTNET_ROLL_FORWARD=Major dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --collect --output reports/phase2-benchmark-runs/multilingual-warm-collection.json
DOTNET_ROLL_FORWARD=Major dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --practical --output reports/phase2-benchmark-runs/multilingual-practical.json
DOTNET_ROLL_FORWARD=Major dotnet run --no-build -c Release --project benchmarks/Laya.Benchmarks -- --profile multilingual --concurrency 1 --output reports/phase2-benchmark-runs/multilingual-concurrency-1.json
```

Run the same commands with `--profile english` for the English artifacts.
