# Noul Analysis

Status: **development and frozen Prompt C held-out evidence complete; production use not recommended**

`LayaNoulAnalyzer` uses the five fixed, non-overlapping buckets `[0,.2)`, `[.2,.4)`, `[.4,.6)`, `[.6,.8)`, and `[.8,1]`. Empty groups remain `N/A`; Noul is an independent diagnostic and does not override category policy.

## Development A Buckets

| Profile | Bucket | Count | Classification accuracy | Correct mean P(true) | Wrong mean P(true) |
|---|---|---:|---:|---:|---:|
| multilingual | `[0,.2)` | 144 | 0.7222222222 | 0.0495230270 | 0.0681480753 |
| multilingual | `[.2,.4)` | 12 | 0.3333333333 | 0.2983443974 | 0.2591286707 |
| multilingual | `[.4,.6)` | 3 | 0.6666666667 | 0.4699300015 | 0.5570812065 |
| multilingual | `[.6,.8)` | 6 | 0.3333333333 | 0.6745693852 | 0.7834021004 |
| multilingual | `[.8,1]` | 0 | N/A | N/A | N/A |
| English | `[0,.2)` | 122 | 0.4180327869 | 0.1434992937 | 0.1522724148 |
| English | `[.2,.4)` | 38 | 0.2631578947 | 0.2602711974 | 0.2487897918 |
| English | `[.4,.6)` | 0 | N/A | N/A | N/A |
| English | `[.6,.8)` | 2 | 0 | N/A | 0.7893437951 |
| English | `[.8,1]` | 3 | 0 | N/A | 0.8192986160 |

The bucket distributions do not provide stable evidence for using Noul as a production policy override. Full raw values are in the development and held-out run `metrics.json` artifacts.

## Frozen Prompt C Held-Out Buckets

Run: `20260924T103842588Z-654915347f184bd8bdf41773971d1dac`; all 55 rows are mixed-language groups.

| Bucket | Count | Classification accuracy | Correct mean P(true) | Wrong mean P(true) |
|---|---:|---:|---:|---:|
| `[0,.2)` | 51 | 0.7254901961 | 0.0579240201 | 0.0572728468 |
| `[.2,.4)` | 2 | 0.5000000000 | 0.2451679750 | 0.2019954413 |
| `[.4,.6)` | 1 | 1.0000000000 | 0.4551204407 | N/A |
| `[.6,.8)` | 0 | N/A | N/A | N/A |
| `[.8,1]` | 1 | 1.0000000000 | 0.8911357479 | N/A |

The held-out Noul values do not demonstrate stable separation of correct and wrong predictions. Noul remains an independent diagnostic and does not override the frozen category policy.
