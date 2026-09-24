# Dataset Summary

Status: **complete for dataset construction; model evaluation not run**

## Version and Provenance

- Dataset: `test-data/transactions-phase2.csv`
- Version: `phase2-v1`
- Source: synthetic, manually reviewed against `test-data/category-label-guideline.md`
- Rows: 220
- Dataset SHA-256: `3d25581e9014c1de9709346f1bf5cd49a5d88614e085a19a9015eee578b7f4e9`
- Guideline SHA-256: `6304c96ae622fe8e6db9c314168b356edc1a073ada60bc9269f5d0eea6a0e0bb`
- Seed: `20260923`
- Validation: `TransactionCsvReader` Phase 2 mode and `Phase2DatasetManifestTests`

## Distribution

- Categories: all 11 fixed categories have 20 rows each; minimum support is 20.
- Languages: `en=66` (30%), `zh=88` (40%), `mixed=66` (30%).
- Groups: 44 fixed five-row merchant/template groups; no group crosses a split.
- Development: 165 rows (75%), 33 groups.
- Held-out: 55 rows (25%), 11 groups.

The 75/25 split is an explicit small-data exception to the approximately 70/30 target. It preserves every category in both sets and is fixed in the manifest rather than optimized after seeing model results.

## Limitations

- The dataset is synthetic and does not establish production representativeness.
- Repeated templates are intentionally group-split, but the number of distinct merchant patterns is still small.
- No model accuracy, calibration, AUTO coverage, or generalization claim is made from this dataset.
