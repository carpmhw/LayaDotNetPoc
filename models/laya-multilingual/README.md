# Laya Multilingual Bundle

Status: **engineering readiness complete / Phase 2 quality and Gate 3 pending**

This directory contains the verified `convaiinnovations/laya-multilingual` source snapshot, current atomic pointer, and versioned ONNX bundle. The ignored `source/` directory contains the fixed checkpoint used by the offline native probe. Runtime code never downloads or substitutes the English bundle.

## Observed Upstream Checkpoint

- Repository: `convaiinnovations/laya-multilingual`
- Observed commit: `052592a15d198d9ad47da779604259b10b47b7aa`
- Source: <https://huggingface.co/convaiinnovations/laya-multilingual/tree/052592a15d198d9ad47da779604259b10b47b7aa>
- Available upstream format: Transformers `model.safetensors` with `rl_agent_config.json`
- Observed `max_len`: `1024`
- Observed `head_max_len`: `256`
- Observed calibration: `temperature=[1.0,1.0,1.0]`, `temperature_by_options={}`
- Observed special token strings: `<bos>`, `<eos>`, `<mask>`, `<pad>`, `<unk>`

The upstream artifact has no published `laya.onnx` or external-data shard set. It does include the upstream `rl_agent_config.json`, encoder config, tokenizer, and decision-head checkpoint weights. The linked official Python source and the PyPI `laya` package are validated as the native reference. The local CPU Dynamo export passes all 20 Python ORT fixtures, .NET parity, publication, and runtime smoke; Phase 2 quality remains a separate gate.

## Official Reference Candidate

- Source repository: <https://github.com/NandhaKishorM/laya>
- Candidate source commit: `010bacef009c855ccba814b51f7c8e1d38ab5e3f`
- Candidate package: `laya==0.3.7`
- Wheel SHA-256: `370beee36a6962f0daed3f086146b7dc6cc825fee0a1941ec05ea7207989ea8a`
- Native validation report: `reports/multilingual-reference-validation.md`
- Python export report: `reports/multilingual-export-validation.md`
- .NET parity report: `reports/multilingual-dotnet-parity.json`
- Current verified pointer: `current-bundle.json`
- Current verified bundle: `versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728/`

The candidate source was validated against the fixed checkpoint: all 170 model state keys loaded, and the native Choice/Noul probe passed. The probe is recorded in `reports/multilingual-reference-validation.md` and `reports/multilingual-reference-probe.json`.

## Required Verified Layout

The current verified version contains the actual files named by its `laya-bundle-manifest.json`, including `laya.onnx.data`, profile, checkpoint revision, file size, SHA-256, tokenizer identity, calibration identity, external shard identity, and ONNX schema. Python ORT and .NET layered parity both pass all 20 fixtures. The pointer is updated atomically; previous versions remain available for rollback. Large weights remain ignored by `.gitignore`.

## Reproduction Contract

The export is produced from a fixed checkpoint revision and version-pinned toolchain. The command records opset 18, Dynamo export, MHA fast-path disabling during export, head configuration, calibration, tokenizer source, maximum lengths, and reference package revision. `tools/publish-laya-multilingual.sh` requires both parity reports to reference the same candidate before atomic publication; Phase 2 readiness remains a separate gate.
