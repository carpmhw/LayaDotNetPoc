# Misclassification Analysis

Status: **frozen multilingual Prompt C held-out triage complete; semantic label review pending**

Source run: `20260924T103842588Z-654915347f184bd8bdf41773971d1dac`; dataset hash `3d25581e9014c1de9709346f1bf5cd49a5d88614e085a19a9015eee578b7f4e9`; CSV: `misclassified-transactions.csv`. The CSV contains 15 errors and only the required eight columns with irreversible description surrogates.

## Structural Triage

- Error counts by language: `mixed=15`; the held-out split contains no English-only or Chinese-only rows. This is a split limitation, not proof of a causal language defect.
- The largest error pair is `other -> bank_fee` (`3` rows).
- False-positive `bank_fee` predictions total `6` rows above its 6 correctly predicted rows; this is a model-share bias signal.
- False-positive `transfer` predictions total `7` rows above its 4 correctly predicted rows, including `utilities -> transfer` (`2`) and `other -> transfer` (`2`); this is the transfer-bias signal.
- The `Unknown Merchant` / `Account adjustment` groups require `other`-label and insufficient-context review; no label was changed.
- Merchant-format and abbreviation candidates such as domain-style descriptions are retained for manual review only; the redacted CSV cannot prove that formatting caused an error.

## Nine-Taxonomy Review

| Taxonomy | Development triage |
|---|---|
| Merchant ambiguity | Review the unknown-merchant groups; no automatic relabeling |
| Category definition ambiguity | Review Apple/device, investment/bank-fee, and entertainment/transfer boundaries |
| Language issue | Held-out contains only `mixed=55`; no causal claim |
| Abbreviation issue | Review domain and abbreviated merchant forms; no causal claim |
| Model bias | Bank-fee and transfer false-positive concentrations above |
| Transfer bias | 9 false-positive transfer predictions; no Noul override |
| Insufficient context | Unknown/adjustment rows remain human-review candidates |
| Incorrect label | 0 corrections proposed from model output |
| Other | No additional category assigned without evidence |

This is a frozen-candidate held-out triage, not a production conclusion. Any confirmed label correction must create a new dataset version and rerun both profiles; no label correction was proposed from this run.
