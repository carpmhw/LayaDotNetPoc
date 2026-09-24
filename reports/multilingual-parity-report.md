# Multilingual Parity Report

Status: **ready-for-phase2: engineering gates complete; Phase 2 quality pending**

The official Python reference, fixed checkpoint, independent fixtures, Python ORT comparison, .NET tokenizer/encoding/tensor/output parity, versioned publication, atomic pointer, runtime smoke, split selector, frozen-candidate contract, v2 run provenance, and readiness regression gates are complete. Phase 2 quality, held-out results, benchmark, deployment, and Gate 3 remain outside this change.

## Python Reference to ORT

- Candidate manifest: `models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728/laya-bundle-manifest.json`
- Machine report: `reports/multilingual-export-validation.json`
- Fixtures executed: `20/20`
- Coverage: `B={1,2}`, `M={2,3,11}`, `S` includes `1024`; Choice and Noul; padding and truncation
- Raw logits maximum absolute error: `0.000012874603271484375`
- Raw logits maximum relative error: `0.00003375585137956219`
- Calibrated probability maximum absolute error: `0.000002182206334522263`
- Calibrated probability maximum relative error: `0.00001834033963794136`
- Tolerance: absolute `1e-4`; no tolerance relaxation was used

## .NET Reference to Runtime

- Machine report: `reports/multilingual-dotnet-parity.json`
- Fixtures executed/passed: `20/20`
- Layers checked: serialization, sequence/markers, five input tensors/shapes, raw logits, `act_probs`, calibrated probabilities, and answer mapping
- .NET maximum raw absolute error: `0.000012874603271484375`
- .NET maximum probability absolute error: `0.000002182206334522263`
- Runtime smoke: multilingual and English profiles each completed one request successfully

The native source evidence and observed checkpoint revision are recorded in `reports/multilingual-reference-validation.md`, `reports/model-validation.md`, and `test-data/multilingual-reference-manifest.json`. The required 19 tokenizer strings and .NET exact-ID test pass. English parity remains a separate Phase 1 regression using `test-data/parity-fixtures.json`; it is not evidence for multilingual quality.

The report is ready for the Phase 2 workflow, but no multilingual baseline or quality conclusion may consume it as a model-quality pass.
