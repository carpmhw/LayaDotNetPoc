# Multilingual Export Validation

Status: **Python export and ORT comparison complete for published bundle**

## Candidate

- Published root: `models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728`
- Bundle manifest: `models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728/laya-bundle-manifest.json`
- Bundle manifest SHA-256: `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`
- ONNX SHA-256: `7b8aec9fd45882c1591ee83edd84d00b7dc7f0b342a07011ac177e22b93611e2`
- External shard: `laya.onnx.data`, SHA-256 `6ef993ee707fe1d6a75529f4a1f8347ff89966ff955d332ea3ebe4e5171a9ac1`
- Checkpoint: `convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`
- Toolchain lock SHA-256: `f7154044e0dacb4f7eaceee7683c8f690f309703ba8979578df9da3edb027ec2`
- Source manifest SHA-256: `66dd7a86f5e090115616c508fd30067708d6e03d9c8d1fcd87e4a69c277ee621`

## Export Contract

- Exporter: `torch.onnx.export`, Dynamo enabled, opset 18
- CPU computation: FP32
- MHA fast path: disabled only during export because legacy export emitted unsupported `aten::_transformer_encoder_layer_fwd`
- Schema: five inputs (`input_ids`, `attention_mask`, `marker_pos`, `marker_mask`, `qtype`) and two outputs (`logits`, `act_probs`)
- Dynamic coverage: `B={1,2}`, `M={2,3,11}`, sequence lengths include `1024`
- Calibration: upstream `max_len=1024`, `head_max_len=256`, all temperatures `1.0`, empty option buckets

## ORT Evidence

Command:

```bash
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh tools/laya-reference/compare.py \
  --model-root models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728 \
  --fixture-path test-data/multilingual-parity-fixtures.json \
  --output reports/multilingual-export-validation.json
```

- Fixtures executed/passed: `20/20`
- Options: `2`, `3`, and `11`
- Raw logits maximum absolute error: `0.000012874603271484375`
- Raw logits maximum relative error: `0.00003375585137956219`
- Calibrated probability maximum absolute error: `0.000002182206334522263`
- Calibrated probability maximum relative error: `0.00001834033963794136`
- Absolute tolerance: `1e-4`; no relaxation used
- Masked marker padding is excluded only by the fixture `marker_mask`; valid values are finite and checked

This report proves the official Python reference to Python ORT edge and points to the published bundle. The separate .NET report proves .NET layered parity; neither report certifies Phase 2 quality, held-out selection, benchmark, deployment, or Gate 3.
