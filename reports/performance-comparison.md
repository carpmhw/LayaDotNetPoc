# Performance Comparison

Status: **complete for the measured CPU profiles; memory growth is a Deployment Risk requiring investigation**.

Source artifacts:

- English: `reports/phase2-benchmark-runs/english-cold-start.json`, `english-warm-collection.json`, `english-practical.json`, and `english-concurrency-{1,2,4}.json`.
- Multilingual: `reports/phase2-benchmark-runs/multilingual-cold-start.json`, `multilingual-warm-collection.json`, `multilingual-practical.json`, and `multilingual-concurrency-{1,2,4}.json`.
- Same host: Linux Mint 22.3, x64, 10 logical processors, ONNX Runtime CPU, `.NET 10.0.12` roll-forward from net8.0.

## Summary

| Measure | English | Multilingual | Comparison |
| --- | ---: | ---: | --- |
| Cold model load | 1579.948 ms | 2403.994 ms | multilingual 1.52x slower |
| Practical end-to-end P50 | 300.929 ms | 91.934 ms | multilingual 0.31x English |
| Practical end-to-end P95 | 364.422 ms | 143.858 ms | both below the 500 ms POC reference |
| Practical peak WorkingSet | 1,669,890,048 bytes | 954,630,144 bytes | English higher on this host |
| Nine-scenario warm peak WorkingSet | 2,447,970,304 bytes | 1,809,342,464 bytes | English higher on this host |

The practical scenario is the integration-relevant four-field state with one Choice and one Noul question. The nine-scenario matrix includes longer synthetic state and is not interchangeable with the practical POC latency reference.

## Nine-Scenario End-to-End P95

| Questions | State | English ms | Multilingual ms | Multilingual delta |
| ---: | --- | ---: | ---: | ---: |
| 1 | short | 216.477 | 90.346 | -126.131 |
| 1 | medium | 456.089 | 160.276 | -295.813 |
| 1 | long | 1638.611 | 1006.024 | -632.587 |
| 2 | short | 357.202 | 139.527 | -217.675 |
| 2 | medium | 853.038 | 304.752 | -548.286 |
| 2 | long | 3017.054 | 2078.358 | -938.696 |
| 5 | short | 777.569 | 312.170 | -465.399 |
| 5 | medium | 2062.568 | 656.030 | -1406.538 |
| 5 | long | 7318.692 | 4974.936 | -2343.756 |

English long-state scenarios truncate at sequence length 512. The multilingual bundle accepts sequence length 947 for the long synthetic state, so the token/truncation rows must be considered when interpreting the timing difference.

## Concurrency

| Profile | C=1 throughput / P95 | C=2 throughput / P95 | C=4 throughput / P95 | Errors / integrity failures |
| --- | --- | --- | --- | --- |
| English | 3.321 req/s / 347.819 ms | 4.377 req/s / 513.186 ms | 6.192 req/s / 735.447 ms | 0 / 0 at all levels |
| Multilingual | 9.361 req/s / 148.038 ms | 11.134 req/s / 217.558 ms | 17.003 req/s / 283.362 ms | 0 / 0 at all levels |

Concurrency results demonstrate shared-engine result integrity for 50 requests at each level. They do not establish a production SLA.

## Memory Risk

The fixed diagnostic boundary is `max(first-20 median * 10%, 128 MiB)`. The measured warm-matrix growth exceeded that boundary for both profiles:

| Profile | First-20 median | Final-20 median | Growth | Boundary | Peak |
| --- | ---: | ---: | ---: | ---: | ---: |
| English | 1,637,715,968 bytes | 2,447,970,304 bytes | 810,254,336 | 134,217,728 | 2,447,970,304 bytes |
| Multilingual | 937,084,928 bytes | 1,809,342,464 bytes | 872,257,536 | 134,217,728 | 1,809,342,464 bytes |

All observed peaks are below the POC reference of `3,000,000,000` bytes, but the trend is a Deployment Risk and needs investigation. It is not sufficient evidence to claim no leak, and it does not automatically make Gate 3 FAIL.

## Decision Input

- Practical latency is within the 500 ms POC reference for both profiles on this host.
- Multilingual has lower measured practical and matrix latency on this host but higher cold-load time than English.
- Neither profile has a stable memory conclusion from the current warm matrix.
- Formal production thresholds and acceptance criteria remain owner decisions; these POC numbers do not authorize production AUTO or BankReportImporter changes.
