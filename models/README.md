# Laya Model Bundle

## Expected Bundle

第一階段使用 `receptron/laya-onnx` English bundle，對應 `convaiinnovations/laya` checkpoint，固定 revision：
`68f27dfe5a27a54fb2b1fefc432f43f972e90868`。完整檔案 hash、大小與 reference package 記錄於
`test-data/reference-manifest.json`；不要把模型權重提交到 Git。

```text
models/laya/
├── laya.onnx
├── laya.onnx.data
├── laya_config.json
└── tokenizer/
    ├── tokenizer.json
    └── tokenizer_config.json
```

## Preparation

1. 從固定的 upstream revision 取得 bundle。
2. 將上述檔案放到 `models/laya`。
3. 以 SHA-256 記錄每個檔案、bundle revision、reference package／commit 與模型大小於 `test-data/reference-manifest.json`。
4. 執行 model metadata smoke test，再執行 parity。

Runtime 只讀取本機檔案，不會自動下載或修改模型資產。缺檔時應回報明確路徑並標示模型相關驗收為未執行。

## Download URLs

以下 URL 的 revision 必須保持一致：

```text
https://huggingface.co/receptron/laya-onnx/resolve/68f27dfe5a27a54fb2b1fefc432f43f972e90868/laya.onnx
https://huggingface.co/receptron/laya-onnx/resolve/68f27dfe5a27a54fb2b1fefc432f43f972e90868/laya.onnx.data
https://huggingface.co/receptron/laya-onnx/resolve/68f27dfe5a27a54fb2b1fefc432f43f972e90868/laya_config.json
https://huggingface.co/receptron/laya-onnx/resolve/68f27dfe5a27a54fb2b1fefc432f43f972e90868/tokenizer/tokenizer.json
https://huggingface.co/receptron/laya-onnx/resolve/68f27dfe5a27a54fb2b1fefc432f43f972e90868/tokenizer/tokenizer_config.json
```

## Verified ONNX Schema

本機 .NET 8 smoke test 已驗證以下 export schema。`-1` 代表動態維度：

| Direction | Name | Dtype | Shape |
| --- | --- | --- | --- |
| Input | `input_ids` | `int64` | `[-1, -1]` |
| Input | `attention_mask` | `int64` | `[-1, -1]` |
| Input | `marker_pos` | `int64` | `[-1, -1]` |
| Input | `marker_mask` | `bool` | `[-1, -1]` |
| Input | `qtype` | `int64` | `[-1]` |
| Output | `logits` | `float32` | `[-1, -1]` |
| Output | `act_probs` | `float32` | `[-1, 2]` |
