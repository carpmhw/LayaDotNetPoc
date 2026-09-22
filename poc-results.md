# Laya .NET POC Results

## Conclusion

The first-stage English CPU POC is `PASS` for build, model loading, inference, calibration, reference parity, transaction evaluation, and measured performance. The recommendation for BankReportImporter integration remains **NO** until multilingual quality, production thresholds, and deployment requirements are defined and passed.

## Gate Summary

| Area | Status | Evidence |
| --- | --- | --- |
| Build | PASS | `dotnet build LayaDotNetPoc.sln --no-restore`; four projects, zero warnings/errors |
| Unit/integration tests | PASS | 46 passed, 0 failed, 0 skipped |
| Gate 1 model inference | PASS | CPU ONNX session, Choice/Noul batch, repeated inference, 35 transaction rows |
| Gate 2 reference parity | PASS | 10 fixtures, exact discrete tensors, selected answers, probability tolerance `1e-4` |
| Transaction evaluation | PASS | 35/35 successful rows; report in `transaction-evaluation.md` |
| Cold performance | PASS | New-process load/first-Run/memory measurement in `benchmark-results.md` |
| Warm performance | PASS | 9 scenarios × 100 samples after 5 warmups; Run-only and end-to-end P50/P95/P99 |
| Gate 3 production readiness | NO | No multilingual checkpoint result, formal business thresholds, or deployment validation |

## Measured Transaction Result

- Dataset: synthetic/de-identified, 35 rows, 18 English and 17 Chinese, including mixed text.
- Overall accuracy: 13/35 = 0.3714.
- English group accuracy: 9/18 = 0.5000.
- Chinese group accuracy: 4/17 = 0.2353.
- Successful model calls: 35; failed calls: 0.
- Run-only latency: min 326.977 ms, mean 406.168 ms, max 572.829 ms.
- Policy modes: AUTO 34, SUGGEST 0, REVIEW 1.
- Full per-category metrics, confusion matrix, confidence coverage, and limitations: `transaction-evaluation.md`.

## Measured Performance Result

- Model load: 1,875.278 ms.
- First end-to-end decision: 353.554 ms.
- First Run-only duration: 332.830 ms.
- Formal warm collection: 5 warmups and 100 samples for each of 9 scenarios.
- Run-only P99 range: 279.470 ms to 8,096.771 ms across the matrix.
- End-to-end P99 range: 279.926 ms to 8,104.605 ms across the matrix.
- Peak process WorkingSet64 in cold run: 1,620,398,080 bytes.
- Peak process WorkingSet64 in warm collection: 2,440,802,304 bytes.
- Detailed scenario statistics: `benchmark-results.md`.

## Known Issues

- The English bundle produces low Chinese accuracy on this small synthetic set; this is not a multilingual model validation.
- Confidence is high despite low overall accuracy, so the provisional AUTO policy must not be promoted to production policy.
- Score questions remain explicitly unsupported.
- Dataset size and synthetic labels are insufficient for production accuracy or coverage decisions.

## Follow-up

1. Evaluate a verified multilingual checkpoint and a larger validation dataset.
2. Define CPU/RAM/P95, per-category accuracy, and automation coverage thresholds with the product owner.
3. Keep BankReportImporter integration disabled until Gate 3 evidence is available.
