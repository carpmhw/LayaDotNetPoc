# Confidence Analysis

Status: **development A/B/C and frozen Prompt C held-out evidence complete; AUTO disabled for integration**

The implementation provides deterministic confidence and margin buckets, ten equal-width ECE bins, the 9x8 probability/margin grid, and Conservative/Balanced/Aggressive scenario selection. Noul is not used as an implicit policy override.

## Development A Calibration

| Profile | Mean confidence | Mean margin | ECE | P(confidence >= .95) count / accuracy | P(confidence >= .99) count / accuracy |
|---|---:|---:|---:|---:|---:|
| multilingual | 0.8384656124 | 0.7362980052 | 0.1684693338 | 89 / 0.9101123596 | 75 / 0.9066666667 |
| English | 0.9964720038 | 0.9929441334 | 0.6293982982 | 162 / 0.3641975309 | 157 / 0.3566878981 |

## Development A Policy Grid

The full 72-cell grids are stored in each run `metrics.json`. Targets are POC references, not production thresholds.

| Profile | Conservative (98%) | Balanced (95%) | Aggressive |
|---|---|---|---|
| multilingual | disabled; no qualifying cell | disabled; no qualifying cell | `P>=.60`, `margin>=.20`, coverage `0.8242424242`, accuracy `0.7941176471`, wrong AUTO `28` |
| English | disabled; no qualifying cell | disabled; no qualifying cell | `P>=.75`, `margin>=.50`, coverage `1.0000000000`, accuracy `0.3696969697`, wrong AUTO `104` |

The exploratory SUGGEST lower bound `P>=.60` and `margin>=.10` is preserved in `reports/phase2-policy-analysis.json`; it is not an approval signal. The development C Balanced candidate was frozen by owner decision and evaluated without held-out retuning. The held-out fixed policy classified `32` AUTO, `15` SUGGEST and `8` REVIEW; AUTO accuracy was `0.9375` with `2` wrong AUTO, so no production AUTO recommendation is made.

## Frozen Prompt C Held-Out Calibration

Run: `20260924T103842588Z-654915347f184bd8bdf41773971d1dac`; candidate: `reports/phase2-frozen-candidate.json`.

| Metric | Held-out value |
|---|---:|
| Input / successful predictions | `55 / 55` |
| Mean confidence / margin | `0.8660803909 / 0.7968943296` |
| ECE | `0.1480106256` |
| Confidence >= .95 count / accuracy | `32 / 0.9375` |
| Confidence >= .99 count / accuracy | `23 / 0.9565217391` |
| Fixed Balanced AUTO coverage / accuracy / wrong | `0.5818181818 / 0.9375 / 2` |

The high-confidence errors remain material even though the stricter `P>=.99` subset has higher measured accuracy. The held-out split contains only mixed-language rows, so these calibration values do not establish English- or Chinese-only generalization.
