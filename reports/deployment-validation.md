# Deployment Validation

Status: **multilingual Docker matrix complete; memory-growth investigation retained**

The Dockerfile, smoke mode, and validation script were run with the verified versioned multilingual bundle mounted read-only. These are runtime/deployment observations only; they do not certify Phase 2 quality or Gate 3.

## English Runtime Path

The same container image was run with the verified `models/laya` bundle on Linux using a read-only model mount. Each memory configuration completed 100 requests with zero failures:

| Limit | Completed | Failures | Peak WorkingSet bytes |
|---|---:|---:|---:|
| 2 GiB | 100 | 0 | 1,658,118,144 |
| 3 GiB | 100 | 0 | 1,656,942,592 |
| 4 GiB | 100 | 0 | 1,658,359,808 |

The raw command was `tools/validate-phase2-deployment.sh --model-root models/laya --profile english --output-root /tmp/laya-phase2-deployment-english`. The container smoke output retained a WorkingSet series for every request. These are English regression/runtime results only; they are not multilingual quality or deployment evidence.

## Multilingual Runtime Path

Command:

```bash
tools/validate-phase2-deployment.sh \
  --model-root models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728 \
  --profile multilingual \
  --output-root reports/deployment-runs
```

| Limit | Requests | Successes | Failures | Peak WorkingSet bytes | Peak GiB | First/last 20 median delta bytes |
|---|---:|---:|---:|---:|---:|---:|
| 2 GiB | 100 | 100 | 0 | 2,078,351,360 | 1.9356 | 6,660,096 |
| 3 GiB | 100 | 100 | 0 | 2,260,873,216 | 2.1056 | 6,672,384 |
| 4 GiB | 100 | 100 | 0 | 2,260,811,776 | 2.1055 | 6,668,288 |

Raw artifacts are `reports/deployment-runs/deployment-2g.json`, `deployment-3g.json`, and `deployment-4g.json`. The process WorkingSet rises across the 100-request series; this report preserves the observed 20-sample median deltas and does not claim absence of a leak. The lowest tested limit completed the smoke workload, while the bundle remains approximately 1.29 GB external data plus runtime overhead.

The same verified model was also run directly on Linux without Docker for 100 requests. Artifact: `reports/deployment-runs/native-linux-multilingual-100.json`; result was `100/100` success, peak WorkingSet `947,793,920` bytes (`0.8827 GiB`), and first/last-20 median delta `10,960,896` bytes. This native run also shows growth, so the result is retained as an observation rather than a no-leak claim.

Passing deployment smoke does not provide the missing held-out policy, benchmark/concurrency, or Gate 3 owner acceptance evidence.
