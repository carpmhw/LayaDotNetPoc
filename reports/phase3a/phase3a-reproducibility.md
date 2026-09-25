# Phase 3A 重現性 v3

**狀態：BLOCKED — 第三次 replicate 後 slope 差異仍未解決。** 比較四組各三個獨立 5,000-request runs；凍結界線為 PrivateMemory／RSS peak 與 end pairwise relative difference ≤5%，late-slope difference ≤25%。相對差異沿用 analyzer 定義：`abs(a-b)／max(abs(a),abs(b))`。各組六種 peak／end 差異都在 5% 內；每組至少一項 2,500–5,000 OLS slope 差異超過 25%。

## Raw late slopes（bytes／100 requests）

| Group | PrivateMemory：r1／r2／r3 | VmRSS：r1／r2／r3 | 最大 PrivateMemory slope 差異 | 最大 VmRSS slope 差異 | 判定 |
|---|---|---|---:|---:|---|
| baseline | 197,424／-30,551／-30,863 | 234,707／2,761／3,561 | 115.63% | 98.82% | 不一致 |
| run-only | 2,495／8,664／2,669 | 9,408／9,556／8,084 | 71.21% | 15.41% | 不一致 |
| arena-on | -31,760／-33,157／-33,409 | 3,616／2,381／3,848 | 4.94% | 38.11% | 不一致 |
| arena-off | -33,387／-29,184／-82,192 | 2,356／5,371／3,533 | 64.49% | 56.14% | 不一致 |

## 三次 runs 最大 peak／end pairwise 差異

| Group | Peak PrivateMemory | Peak VmRSS | End PrivateMemory | End VmRSS |
|---|---:|---:|---:|---:|
| baseline | 0.0133% | 0.0127% | 0.0400% | 0.0230% |
| run-only | 0.7856% | 0.0465% | 0.0750% | 0.0965% |
| arena-on | 0.0296% | 0.0386% | 0.0903% | 0.0808% |
| arena-off | 0.0026% | 0.0267% | 0.0080% | 0.0546% |

所有 12 個 raw run 均為 5,000／5,000 complete、0 errors；每個 late segment 有 51 個自然樣本且具 2,500／5,000 endpoints。raw CSV 重算的 OLS slopes、六項三次 pairwise 最大差異與 `phase3a-reproducibility.json` 完全相符。baseline PrivateMemory slope 在 r1 為正、r2／r3 為負；不可藉由平均 slopes、刪除 r1 或選擇有利 replicate 消除分歧。

Machine-readable 結果、每個 run ID 與差異明細見 `phase3a-reproducibility.json`；raw evidence 位於各 run 的 `phase3a-memory-samples.csv`／manifest。此不一致不構成 leak 證據；保留所有 runs，campaign reproducibility 與 Memory Gate 均維持 blocked。
