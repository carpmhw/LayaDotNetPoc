# Benchmark Results

## Status

`PASS`: the formal Release collection completed all nine scenarios with 5 warmup observations and 100 measured observations per scenario. Run-only and end-to-end values are reported separately. The collector also emitted the 900 raw Run-only and 900 raw end-to-end observations as JSON arrays.

## Environment

- OS: Linux Mint 22.3 (Zena).
- CPU: AMD Ryzen 7 255 with Radeon 780M, 10 logical and 10 physical cores.
- .NET SDK/runtime: SDK 8.0.425, runtime 8.0.31.
- ONNX Runtime: `Microsoft.ML.OnnxRuntime 1.30.0`, CPU Execution Provider, `ORT_ENABLE_ALL`.
- BenchmarkDotNet package: `0.14.0`.
- Model: `receptron/laya-onnx` revision `68f27dfe5a27a54fb2b1fefc432f43f972e90868`.
- Bundle size: 1,689,065,531 bytes.
- Formal command: `dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --collect`.
- Warmup/sample count: 5/100 per scenario.
- Percentiles: linear interpolation on sorted samples at `(n-1)*p`.
- No transaction descriptions or other transaction source text are included in benchmark output.

## Cold Start

One new Release process and one short synthetic request.

| Measure | Value |
| --- | ---: |
| Model load | 1,875.278 ms |
| First end-to-end | 353.554 ms |
| First Run-only | 332.830 ms |
| WorkingSet before | 29,057,024 bytes |
| WorkingSet after load | 1,610,649,600 bytes |
| WorkingSet after first Run | 1,620,398,080 bytes |
| Peak WorkingSet | 1,620,398,080 bytes |
| Managed memory before | 106,456 bytes |
| Managed memory after load | 225,488 bytes |
| Managed memory after first Run | 252,856 bytes |

## Scenario Metadata

Marker width is 11 for every scenario. Long state reaches the model maximum and is truncated.

| Questions | State | State tokens | Sequence length | Truncated |
| ---: | --- | ---: | ---: | --- |
| 1 | short | 28 | 62 | no |
| 1 | medium | 130 | 164 | no |
| 1 | long | 914 | 512 | yes |
| 2 | short | 28 | 62 | no |
| 2 | medium | 130 | 164 | no |
| 2 | long | 914 | 512 | yes |
| 5 | short | 28 | 62 | no |
| 5 | medium | 130 | 164 | no |
| 5 | long | 914 | 512 | yes |

## Run-Only Latency

All values are milliseconds from 100 measured samples per scenario.

| Questions | State | Mean | P50 | P95 | P99 | Min | Max |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | short | 200.899 | 196.594 | 247.867 | 279.470 | 156.428 | 295.856 |
| 1 | medium | 450.285 | 450.683 | 501.012 | 525.616 | 376.950 | 528.566 |
| 1 | long | 1,597.617 | 1,590.776 | 1,697.301 | 1,731.528 | 1,475.804 | 1,768.944 |
| 2 | short | 348.011 | 348.317 | 399.465 | 422.033 | 293.943 | 428.036 |
| 2 | medium | 875.381 | 871.831 | 946.493 | 998.134 | 753.686 | 1,009.467 |
| 2 | long | 3,169.028 | 3,150.929 | 3,308.384 | 3,329.981 | 3,017.690 | 3,331.404 |
| 5 | short | 800.793 | 792.572 | 874.493 | 902.233 | 707.980 | 943.072 |
| 5 | medium | 2,190.083 | 2,151.851 | 2,416.535 | 2,786.003 | 2,031.347 | 2,940.377 |
| 5 | long | 7,831.129 | 7,837.333 | 7,991.739 | 8,096.771 | 7,582.454 | 8,154.674 |

## End-To-End Latency

End-to-end includes sequence building, tensor creation, ONNX Run, calibration, and result mapping; it excludes model load.

| Questions | State | Mean | P50 | P95 | P99 | Min | Max |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | short | 201.300 | 196.929 | 248.151 | 279.926 | 156.741 | 296.166 |
| 1 | medium | 450.790 | 451.211 | 501.546 | 526.000 | 377.333 | 528.933 |
| 1 | long | 1,599.759 | 1,593.246 | 1,700.753 | 1,734.317 | 1,477.270 | 1,773.972 |
| 2 | short | 348.422 | 348.761 | 399.899 | 422.378 | 294.291 | 428.370 |
| 2 | medium | 876.112 | 872.566 | 947.126 | 998.754 | 754.330 | 1,010.538 |
| 2 | long | 3,173.015 | 3,154.499 | 3,311.323 | 3,333.806 | 3,020.842 | 3,342.032 |
| 5 | short | 801.718 | 793.336 | 876.312 | 902.833 | 708.717 | 943.823 |
| 5 | medium | 2,191.902 | 2,153.818 | 2,418.690 | 2,787.820 | 2,032.627 | 2,941.633 |
| 5 | long | 7,841.215 | 7,848.320 | 8,004.217 | 8,104.605 | 7,592.827 | 8,167.320 |

## Memory

The warm collection process reported the following counters after the nine-scenario run. These are separate from the cold-start process above.

| Measure | Value |
| --- | ---: |
| WorkingSet before warm collection | 1,603,727,360 bytes |
| WorkingSet after warm collection | 2,440,798,208 bytes |
| Peak WorkingSet | 2,440,802,304 bytes |
| Managed memory before warm collection | 233,304 bytes |
| Managed memory after warm collection | 485,056 bytes |

## Reproduction

```bash
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --metadata
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --cold-start
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya --collect
dotnet run -c Release --project benchmarks/Laya.Benchmarks -- --model-root models/laya
```
