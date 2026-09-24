# Prompt Comparison

Status: **development A/B/C complete; multilingual Prompt C Balanced frozen; held-out complete**

All runs use the same 165-row development split, dataset/guideline hashes, option order, serialization, environment fingerprint, and thread count. No held-out result was inspected or used. Per-request sequence length and truncation are now persisted in each raw result.

P95 uses linear interpolation on sorted successful end-to-end latency samples at `(n-1)*p`. Token columns are the minimum/mean/maximum `SequenceLength`; `truncated` is the count of successful predictions marked `WasTruncated=true`.

| Profile | Prompt | Run ID | Accuracy | Macro F1 | ECE | Transfer-share collapse | End-to-end mean / P95 ms | Tokens min / mean / max | Truncated |
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
| multilingual | A | `20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f` | 0.6787878788 | 0.6516331064 | 0.1684693338 | 0.0424242424 | 103.963 / 132.684 | 62 / 66.412 / 70 | 0 |
| multilingual | B | `20260924T054833880Z-d7ab4701d5a24bd192360541ec911878` | 0.6848484848 | 0.6587883300 | 0.1541140519 | 0.0424242424 | 109.585 / 145.596 | 66 / 70.412 / 74 | 0 |
| multilingual | C | `20260924T054908845Z-eaeb23f26ffb44658cd3350ba7a6dea4` | 0.6787878788 | 0.6519713394 | 0.1382694879 | 0.0424242424 | 130.903 / 176.759 | 75 / 79.412 / 83 | 0 |
| English | A | `20260924T055011652Z-72d14048b85c4d2396dc899832d41343` | 0.3696969697 | 0.3458157262 | 0.6293982982 | 0.6060606061 | 337.577 / 389.824 | 61 / 68.485 / 77 | 0 |
| English | B | `20260924T055120455Z-d0472f0405434ce98b1c5de00376f0fb` | 0.3818181818 | 0.3648876370 | 0.6082887188 | 0.5878787879 | 358.301 / 416.737 | 65 / 72.485 / 81 | 0 |
| English | C | `20260924T055232902Z-401e6b3df27c4d16ba31268cc7dd601a` | 0.3151515152 | 0.2921829887 | 0.6568472831 | 0.6060606061 | 393.052 / 455.556 | 74 / 81.485 / 90 | 0 |

## Development Policy Candidates

The 72-cell grids remain in each run `metrics.json`. The following summaries use only development data and POC targets; they are not production thresholds.

| Profile / Prompt | Conservative | Balanced | Aggressive |
|---|---|---|---|
| multilingual A | none | none | P `.60`, margin `.20`, coverage `0.8242424242`, accuracy `0.7941176471`, wrong `28` |
| multilingual B | none | none | P `.60`, margin `.20`, coverage `0.8121212121`, accuracy `0.7910447761`, wrong `28` |
| multilingual C | P `.99`, margin `.80`, coverage `0.3939393939`, accuracy `0.9846153846`, wrong `1` | P `.95`, margin `.80`, coverage `0.4909090909`, accuracy `0.9506172839`, wrong `4` | P `.60`, margin `.30`, coverage `0.7454545455`, accuracy `0.8536585366`, wrong `18` |
| English A | none | none | P `.75`, margin `.50`, coverage `1.0000000000`, accuracy `0.3696969697`, wrong `104` |
| English B | none | none | P `.80`, margin `.60`, coverage `0.9939393939`, accuracy `0.3841463415`, wrong `101` |
| English C | none | none | P `.60`, margin `.20`, coverage `0.9696969697`, accuracy `0.3062500000`, wrong `111` |

Prompt B is the best multilingual development result for accuracy and Macro F1. Prompt C has the lowest multilingual ECE and is the only multilingual variant with non-empty 95%/98% POC policy candidates. This trade-off was exploratory evidence until the owner-selected candidate documented below was frozen.

## Frozen Selection And Held-Out

The owner-selected frozen candidate is multilingual Prompt C with the Balanced development policy: AUTO `P>=0.95` and `margin>=0.80`; SUGGEST `P>=0.60` and `margin>=0.10`. The candidate is `reports/phase2-frozen-candidate.json`, bound to development run `20260924T054908845Z-eaeb23f26ffb44658cd3350ba7a6dea4`. Held-out was executed only after this candidate was validated; no held-out result was used to change the prompt or thresholds.

| Profile | Prompt | Split | Run ID | Policy | Accuracy | Macro F1 | Top-1 / Top-2 / Top-3 | ECE | Auto count / accuracy / wrong | End-to-end mean / P95 ms | Tokens min / mean / max | Truncated |
|---|---|---|---|---|---:|---:|---:|---:|---:|---:|---:|---:|
| multilingual | C | held-out | `20260924T103842588Z-654915347f184bd8bdf41773971d1dac` | Balanced | 0.7272727273 | 0.7221628045 | 0.7272727273 / 0.8363636364 / 0.8727272727 | 0.1480106256 | 32 / 0.9375 / 2 | 139.281 / 176.305 | 76 / 78.964 / 84 | 0 |

The held-out split contains only `mixed` language rows (`55/55`), because the group-aware split assigns these groups by design. This is a generalization limitation, not evidence for language-wide quality. Fixed-policy decisions were `Auto=32`, `Suggest=15`, `Review=8`; the two AUTO errors prevent a production AUTO recommendation. The frozen candidate and held-out run are now accepted by `node tools/validate-phase2-evidence.mjs . --require-held-out`.
