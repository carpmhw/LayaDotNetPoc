# Phase 3A Docker 記憶體限制矩陣

狀態：**BLOCKED — 缺少外部 Docker／cgroup 證據**

必要限制（十進位 bytes）：2,000,000,000、2,500,000,000、3,000,000,000、4,000,000,000。
報告須保存 inspect、OOMKilled、exit code、cgroup usage、process RSS 與 run manifest hashes。
不以 native process 量測推測 Docker 結果。
