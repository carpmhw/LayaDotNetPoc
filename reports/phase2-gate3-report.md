# Phase 2 Gate 3 Report

Status: **PARTIAL / suggestion evidence only**  
Integration recommendation: **SUGGESTION-ONLY**

This `PARTIAL` decision does not authorize production changes or AUTO. The owner-selected frozen candidate has a complete held-out result, but high-confidence errors prevent reliable automation. Suggestion-only research evidence is available; full acceptance completed in `reports/phase2-acceptance/20260924T104441Z-phase2-acceptance/` with exit code `0`.

## Model / Checkpoint / Runtime

- Target checkpoint: `convaiinnovations/laya-multilingual`
- Observed revision: `052592a15d198d9ad47da779604259b10b47b7aa`
- Versioned ONNX graph, external-data shards, tokenizer and config: **verified**
- Bundle manifest SHA256: `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`
- Python/.NET parity: **20/20 passed**, zero skipped
- Runtime: ONNX Runtime CPU; host `.NET 10.0.12` rolled forward from the `.NET 8` target

## Evidence Matrix

| 面向 | Status | Evidence / limitation |
|---|---|---|
| Model acquisition and parity | complete | Versioned bundle, reference identity and Python/.NET parity artifacts |
| Dataset | complete | 220 rows, 165 development, 55 held-out, fixed group split and manifest hashes |
| Quality | complete with split limitation | Six reproducible A/B/C development runs plus frozen C held-out; held-out is all mixed-language groups |
| Calibration and automation | complete / AUTO disabled for integration | Balanced candidate is frozen and evaluated; held-out AUTO accuracy is `0.9375` with 2 wrong AUTO |
| Performance and concurrency | complete with memory risk | Practical, 9-scenario, cold-start and concurrency 1/2/4 artifacts; all concurrency checks have zero errors/integrity failures |
| Deployment and memory | deployment complete / investigation required | Native and Docker repeated-request checks passed; warm-matrix WorkingSet growth exceeded the diagnostic boundary |
| Full acceptance | complete | `20260924T104441Z-phase2-acceptance`, exit `0`, evidence inventory and held-out validation passed |

## Development Evidence

- Latest multilingual A run: `reports/phase2-runs/20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f/`
- Latest English A run: `reports/phase2-runs/20260924T055011652Z-72d14048b85c4d2396dc899832d41343/`
- Multilingual prompt B is best for development accuracy/Macro F1: `0.6848484848` / `0.6587883300`.
- Multilingual prompt C is the only variant with non-empty POC Conservative/Balanced cells:
  - Conservative: accuracy `0.9846153846`, coverage `0.3939393939`, wrong AUTO `1`.
  - Balanced: accuracy `0.9506172839`, coverage `0.4909090909`, wrong AUTO `4`.
- Owner-selected frozen candidate: multilingual Prompt C Balanced; candidate `reports/phase2-frozen-candidate.json`, policy hash `8cacd8c9242562eed66a0dcab16b06d95fe9e9feda69227881867b8027e16e0b`.
- Held-out run: `reports/phase2-runs/20260924T103842588Z-654915347f184bd8bdf41773971d1dac/`.

## Required Report Fields

- Dataset total: 220; development/held-out: `165/55`; languages: `en=66`, `zh=88`, `mixed=66`.
- Latest multilingual A quality: accuracy `0.6787878788`, Macro F1 `0.6516331064`, Top-1/Top-2/Top-3 `0.6787878788 / 0.7939393939 / 0.8363636364`.
- Latest English A quality: accuracy `0.3696969697`, Macro F1 `0.3458157262`.
- Frozen Prompt C held-out quality: accuracy `0.7272727273`, Macro F1 `0.7221628045`, Top-1/Top-2/Top-3 `0.7272727273 / 0.8363636364 / 0.8727272727`.
- Held-out ECE: `0.1480106256`; Balanced policy decisions: `Auto=32`, `Suggest=15`, `Review=8`; AUTO coverage/accuracy/wrong `0.5818181818 / 0.9375 / 2`.
- Held-out end-to-end mean/P95/P99: `139.281 / 176.305 / 195.033 ms`; sequence min/mean/max `76 / 78.964 / 84`; truncation `0`.
- Practical P95: multilingual `143.858 ms`; English `364.422 ms`.
- Concurrency 1/2/4: all profiles completed `50/50` at every level with zero errors and zero integrity failures.
- Warm-matrix peak WorkingSet: multilingual `1,809,342,464` bytes; English `2,447,970,304` bytes.
- Memory growth exceeded the fixed diagnostic boundary for both profiles; this is a risk requiring investigation, not proof of a leak.
- Held-out evidence is complete, but generalization is limited because all 55 held-out rows are mixed-language groups.

## Bias and Known Issues

The dataset guideline covers merchant ambiguity, convenience-store use, credit-card payment versus transfer, language, abbreviations, and `other`. Development and held-out results show bank-fee and transfer share bias signals. The held-out split contains only mixed-language groups, and synthetic data plus the fixed group split limit external validity.

## Decision

Gate 3 is `PARTIAL` with `SUGGESTION-ONLY` integration. The evidence chain is complete enough for research suggestions, but the fixed Balanced policy has 2 wrong AUTO predictions on held-out and therefore cannot support production AUTO. No production threshold, SLA, automatic categorization, BankReportImporter change, UI change, or database change is authorized. Memory growth remains a deployment investigation risk and does not independently change this decision.
