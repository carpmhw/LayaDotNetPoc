# Multilingual Baseline Evaluation

Status: **development A complete; frozen Prompt C Balanced held-out complete**

The Phase 2 baseline contract is unchanged from Phase 1: the original category question, the original uncertainty question, the fixed 11 options, and the four-field state serialization. This report only interprets the reproducible development run; it is not a held-out or production-quality conclusion.

## Run Identity

- Run: `20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f`
- Manifest: `reports/phase2-runs/20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f/manifest.json`
- Profile: `multilingual`; prompt A; split `development`
- Dataset hash: `3d25581e9014c1de9709346f1bf5cd49a5d88614e085a19a9015eee578b7f4e9`
- Dataset manifest hash: `0503882813a57430e28d9acea25eeb08053c7ab881c5e74ab3d2a70b2ba82ccd`
- Model revision: `052592a15d198d9ad47da779604259b10b47b7aa`
- Inputs / success / failure: `165 / 165 / 0`

## Development Metrics

| Metric | Value |
|---|---:|
| Full-input accuracy | 0.6787878788 |
| Macro F1 | 0.6516331064 |
| Top-1 / Top-2 / Top-3 | 0.6787878788 / 0.7939393939 / 0.8363636364 |
| Mean confidence / margin | 0.8384656124 / 0.7362980052 |
| ECE | 0.1684693338 |
| End-to-end P95 | 132.6843 ms |

Language full-input accuracy was `en=0.6969696969`, `zh=0.6818181818`, and `mixed=0.5454545455`. The largest predicted-share delta was `bank_fee=+0.1636363636`; predicted `transfer` share was `0.1333333333` versus expected `0.0909090909`.

The complete per-category metrics, confusion matrix, calibration buckets, Noul buckets, per-request sequence length, truncation flag, and raw unrounded predictions remain in the run `metrics.json` and `results.json`. This A run remains the baseline and was not used to select the frozen C policy.

## Frozen Prompt C Held-Out

Held-out was executed only after `reports/phase2-frozen-candidate.json` was validated. The fixed policy is Balanced (`P>=.95`, `margin>=.80` for AUTO; `P>=.60`, `margin>=.10` for SUGGEST). No held-out result was used to change the candidate.

- Run: `20260924T103842588Z-654915347f184bd8bdf41773971d1dac`
- Candidate: `reports/phase2-frozen-candidate.json` (`80be1980b0cd20d3ae6073c482534a9a0fc27844117ca04276b15d662b2bbff8`)
- Input / success / failure: `55 / 55 / 0`
- Accuracy / Macro F1: `0.7272727273 / 0.7221628045`
- Top-1 / Top-2 / Top-3: `0.7272727273 / 0.8363636364 / 0.8727272727`
- ECE: `0.1480106256`; mean confidence / margin: `0.8660803909 / 0.7968943296`
- Fixed-policy decisions: `Auto=32`, `Suggest=15`, `Review=8`; AUTO accuracy `0.9375`, wrong AUTO `2`, AUTO coverage `0.5818181818`
- End-to-end latency mean / P95 / P99: `139.281 / 176.305 / 195.033 ms`
- Sequence length min / mean / max: `76 / 78.964 / 84`; truncation `0`
- Language limitation: all 55 held-out rows are `mixed` under the fixed group-aware split.

## Full-Data Descriptive View

Combining the frozen Prompt C development predictions with the held-out predictions gives a descriptive 220-row view only; it was computed after freezing and was not used for selection. Accuracy is `0.6909090909`, Macro F1 `0.6696799740`, Top-1/Top-2/Top-3 `0.6909090909 / 0.8000000000 / 0.8545454545`, ECE `0.1293222674`, and transfer share collapse `0.0590909091`. Language accuracy is `en=0.6969696970`, `zh=0.6818181818`, `mixed=0.6969696970`.
