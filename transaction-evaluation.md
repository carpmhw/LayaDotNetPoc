# Transaction Evaluation

## Run

- Source: synthetic, de-identified `test-data/transactions.csv`; 35 rows, 18 `en`, 17 `zh`.
- Model: `receptron/laya-onnx` English bundle, revision `68f27dfe5a27a54fb2b1fefc432f43f972e90868`.
- Questions: one 11-option category Choice and one reference-defined `needs_review` Noul per request.
- Successful inferences: 35; failed inferences: 0; evaluation complete: yes.
- Run-only latency: min 326.977 ms, mean 406.168 ms, max 572.829 ms.

## Overall

- Correct: 13/35.
- Overall accuracy: 0.3714.
- Category confidence: min 0.4266, mean 0.9823, max 1.0000.
- Top1-Top2 margin: min 0.0444, mean 0.9702, max 1.0000.
- Policy modes: AUTO 34, SUGGEST 0, REVIEW 1.
- Policy: AUTO requires `P >= 0.85` and `margin >= 0.20`; SUGGEST is `0.60 <= P < 0.85`; all other cases are REVIEW. Noul `P(true)` is reported independently.

## Language Groups

| Language | Samples | Correct | Accuracy |
| --- | ---: | ---: | ---: |
| en | 18 | 9 | 0.5000 |
| zh | 17 | 4 | 0.2353 |

## Per Category

Class accuracy is correct predictions divided by actual support. `N/A` means the metric denominator is zero.

| Category | Support | Predicted | Accuracy | Precision | Recall | F1 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| food | 4 | 0 | 0.0000 | N/A | 0.0000 | N/A |
| transport | 3 | 0 | 0.0000 | N/A | 0.0000 | N/A |
| shopping | 4 | 2 | 0.0000 | 0.0000 | 0.0000 | 0.0000 |
| utilities | 4 | 2 | 0.2500 | 0.5000 | 0.2500 | 0.3333 |
| transfer | 3 | 22 | 1.0000 | 0.1364 | 1.0000 | 0.2400 |
| salary | 3 | 2 | 0.6667 | 1.0000 | 0.6667 | 0.8000 |
| bank_fee | 3 | 3 | 1.0000 | 1.0000 | 1.0000 | 1.0000 |
| investment | 3 | 3 | 1.0000 | 1.0000 | 1.0000 | 1.0000 |
| medical | 2 | 0 | 0.0000 | N/A | 0.0000 | N/A |
| entertainment | 3 | 1 | 0.3333 | 1.0000 | 0.3333 | 0.5000 |
| other | 3 | 0 | 0.0000 | N/A | 0.0000 | N/A |

## Confusion Matrix

Rows are expected labels; columns are predicted labels.

| Expected \ Predicted | food | transport | shopping | utilities | transfer | salary | bank_fee | investment | medical | entertainment | other |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| food | 0 | 0 | 1 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| transport | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| shopping | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 0 | 0 | 0 |
| utilities | 0 | 0 | 0 | 1 | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| transfer | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 0 | 0 | 0 |
| salary | 0 | 0 | 0 | 0 | 1 | 2 | 0 | 0 | 0 | 0 | 0 |
| bank_fee | 0 | 0 | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 0 |
| investment | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 |
| medical | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 0 | 0 |
| entertainment | 0 | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 1 | 0 |
| other | 0 | 0 | 1 | 1 | 1 | 0 | 0 | 0 | 0 | 0 | 0 |

## Confidence Coverage

Accuracy is calculated only among rows with `P(top1) >= threshold`.

| Threshold | Included | Coverage | Accuracy |
| ---: | ---: | ---: | ---: |
| 0.60 | 34 | 0.9714 | 0.3824 |
| 0.70 | 34 | 0.9714 | 0.3824 |
| 0.80 | 34 | 0.9714 | 0.3824 |
| 0.85 | 34 | 0.9714 | 0.3824 |
| 0.90 | 34 | 0.9714 | 0.3824 |
| 0.95 | 34 | 0.9714 | 0.3824 |

## Limitations

- This is a small synthetic dataset, so the measured accuracy and automation coverage are not production thresholds.
- Chinese and mixed-text rows were evaluated with an English bundle; these results do not demonstrate multilingual checkpoint quality.
- The high confidence with low accuracy is an observed model behavior and is explicitly retained in the report rather than converted into a production policy.
