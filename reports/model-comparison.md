# Model Comparison

Status: **development A comparison, frozen Prompt C held-out and CPU benchmark comparison complete; Gate 3 is suggestion-only**

The comparison uses the same dataset hash, split, prompt A, option order, serialization, environment fingerprint, thread count, and 165 selected IDs. Both runs completed all 165 inputs, so the common successful subset is also 165. The comparison is development-only.

| Profile | Accuracy | Macro F1 | Top-1 / Top-2 / Top-3 | ECE | Predicted transfer share | End-to-end P95 |
|---|---:|---:|---:|---:|---:|---:|
| multilingual | 0.6787878788 | 0.6516331064 | 0.6787878788 / 0.7939393939 / 0.8363636364 | 0.1684693338 | 0.1333333333 | 132.6843 ms |
| English | 0.3696969697 | 0.3458157262 | 0.3696969697 / 0.7333333333 / 0.7818181818 | 0.6293982982 | 0.6969696970 | 389.8244 ms |

| Profile | Warm matrix peak WorkingSet | Practical end-to-end P95 / P99 | Concurrency 4 throughput | Concurrency 4 end-to-end P95 |
|---|---:|---:|---:|---:|
| multilingual | 1,809,342,464 bytes | 143.858 / 153.611 ms | 17.003 req/s | 283.362 ms |
| English | 2,447,970,304 bytes | 364.422 / 378.744 ms | 6.192 req/s | 735.447 ms |

## Provenance

- Multilingual run: `reports/phase2-runs/20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f/`
- English run: `reports/phase2-runs/20260924T055011652Z-72d14048b85c4d2396dc899832d41343/`
- Dataset, guideline, dataset-manifest, selected-ID, prompt, option-order, serialization, environment, and thread identities match exactly.
- Common successful IDs: `165/165`; no subset correction was required.

The multilingual result has a verified bundle manifest; the English Phase 2 run intentionally records no Phase 2 bundle manifest because it uses the existing Phase 1 profile. AUTO candidate analysis is recorded separately in `reports/phase2-policy-analysis.json`. The warm matrix exceeded the fixed memory-growth diagnostic boundary for both profiles; see `reports/performance-comparison.md`. The held-out result and Gate 3 decision are documented below.

## Frozen Held-Out Comparison

The owner-selected multilingual Prompt C Balanced candidate was evaluated on the fixed 55-row held-out split. No English held-out run was performed because the frozen candidate is multilingual Prompt C; English remains the development comparison baseline.

| Evidence | Value |
|---|---:|
| Held-out run | `20260924T103842588Z-654915347f184bd8bdf41773971d1dac` |
| Input / success / failure | `55 / 55 / 0` |
| Accuracy / Macro F1 | `0.7272727273 / 0.7221628045` |
| Top-1 / Top-2 / Top-3 | `0.7272727273 / 0.8363636364 / 0.8727272727` |
| ECE | `0.1480106256` |
| Balanced AUTO coverage / accuracy / wrong | `0.5818181818 / 0.9375 / 2` |
| End-to-end mean / P95 / P99 | `139.281 / 176.305 / 195.033 ms` |

The held-out rows are all `mixed` language groups. The two fixed-policy AUTO errors mean the candidate supports suggestion-only evidence, not a production AUTO recommendation. Combining development C with held-out C is descriptive only: 220-row accuracy `0.6909090909`, Macro F1 `0.6696799740`, Top-1/Top-2/Top-3 `0.6909090909 / 0.8000000000 / 0.8545454545`, ECE `0.1293222674`.
