# Phase 3A v3 後續決策

## 決策

本次 v3 不採用 buffer reuse、pooling、threading 或 multi-session optimization。生命週期 source audit 與受影響測試未找到已確認未釋放的 owner；arena ON／OFF 本身沒有顯示可歸因的明顯 trade-off；現有資料無法隔離 native/process retention 來源。在 integrity 與 reproduction 證據不足時變更 ownership 或 concurrency，會改變尚未釐清的被測系統。

## 證據與限制

- v3 baseline 與 component full-pipeline 均完成 5,000 requests、0 errors／integrity-counter failures。Baseline WorkingSet／PrivateMemory／VmRSS late process segments具備端點；managed heap 的 **1,000–2,500** 區段因 requests 1,900、1,950、2,200、2,250、2,300 五個 `GC.GetTotalMemory(false)` 負值而以 unavailable 保存，該 managed slope segment blocked。2,500–5,000 區段有完整樣本，不應錯標為缺值。
- v3 arena ON／OFF 各完成 5,000 requests；P50／P95／P99 及 process memory peak／end 差異見 `phase3a-ort-arena-comparison.md`。配對 run 不證明可重現的 allocator trade-off，三次 replicate 的 slope 仍有超標差異。
- v3 lifecycle owner audit 未發現已確認未釋放的 input、output、RunOptions、session 或 tokenizer owner。Session-recreate dispose 後有 process residual；10／100 cycles 結尾相近，但沒有歸因來源，也未建立 bounded allocator plateau。
- v3 concurrency 1／2／4 的各 1,000 requests 已完成。固定 integrity request set 未由現有 Probe plan 執行；summary 中 `integrityFailures=0` 不構成 integrity parity 結果。
- 四組 reproduction 均執行第三個 replicate；每組至少一項 late-slope pairwise 差異超過凍結 25% tolerance。不平均、不挑選有利 replicate。
- short→long→short shape-retention run 在 2,600／3,000 requests 中斷，不能依 complete-only resume contract 重用作為已完成證據。

須待 source-matched integrity checks、完整 shape retention、有效 managed heap segment 與通過凍結界線的可重現 v3 evidence 到齊，再考慮第二輪 optimization／profiling。現有結論維持 blocked；不分類 leak、plateau 或 Memory Gate pass。
