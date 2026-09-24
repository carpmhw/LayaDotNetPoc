# Multilingual Reference Validation

Status: **reference, ONNX export, Python ORT, and .NET parity verified**  
Validation date: `2026-09-23`  
Scope: fixed-source inspection, native Python reference validation, published ONNX/Python ORT evidence, and .NET parity handoff. Phase 2 quality results are not claimed.

## Implementation Baseline

- Repository commit: `fe4c9d958b4a1366562d26b40574036c81d951d0`
- Branch: `dev`
- Worktree: dirty before this change; `51` porcelain entries were present and were preserved.
- `git diff --check`: passed.
- `dotnet build LayaDotNetPoc.sln --no-restore`: passed with `0` warnings and `0` errors.
- `dotnet test LayaDotNetPoc.sln --no-restore`: could not start because this host has only `Microsoft.NETCore.App 10.0.12`, while the projects target `net8.0`.
- `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore`: passed, `88/88`, `0` skipped. This is an environment-only verification and no roll-forward setting was added to the repository.

### Baseline hashes

| Asset | SHA-256 |
|---|---|
| `test-data/reference-manifest.json` | `57fc45c806a91dc2c57c9c82916ecb968d25cf49cb4df3742a1143f62bf6d61d` |
| `test-data/parity-fixtures.json` | `ca0239c7f41c58c1a93e9842094182223ce0bbf4fdbd26ea3efb82686f36fc30` |
| `test-data/transactions-phase2.csv` | `3d25581e9014c1de9709346f1bf5cd49a5d88614e085a19a9015eee578b7f4e9` |
| `test-data/transactions-phase2-manifest.json` | `0503882813a57430e28d9acea25eeb08053c7ab881c5e74ab3d2a70b2ba82ccd` |
| `test-data/multilingual-reference-manifest.json` | `d39800699775d137aca94df97109aa039bd02252a186e39d280182efec13bcfb` |

## Fixed Checkpoint Evidence

The target is `convaiinnovations/laya-multilingual` at revision `052592a15d198d9ad47da779604259b10b47b7aa`.

The fixed Hugging Face tree reports these source checkpoint files:

| Path | Size | Reported object ID |
|---|---:|---|
| `model.safetensors` | `643835514` | `b99c8bea239c53f6f6bce734557dc6c403fa6b3e` |
| `encoder/config.json` | `1938` | `0de0e2d30638873790cf962def52e2acf4db3eef` |
| `rl_agent_config.json` | `472` | `00e35f88bb731bb9126a914666ab1cdac8a204c8` |
| `tokenizer/tokenizer.json` | `34363188` | `56d765858ed38eaabd35cd90759cbb1f89b0baa4` |
| `tokenizer/tokenizer_config.json` | `502` | `eea1ed61121530d74c1722dc2aab6ec917a75909` |

The LFS content IDs are `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204` for the weights and `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f` for the tokenizer.

The checkpoint declares:

- `encoder`: `jhu-clsp/mmBERT-base`
- `head_layers`: `2`
- `max_len`: `1024`
- `head_max_len`: `256`
- `temperature`: `[1.0, 1.0, 1.0]`
- `temperature_by_options`: `{}`
- `amp_dtype`: `bf16`
- encoder config `model_type`: `modernbert`, `transformers_version`: `5.0.0`, vocabulary size `256000`
- special tokens `<bos>`, `<eos>`, `<mask>`, `<pad>`, `<unk>`

The source checkpoint has no ONNX graph or external-data shards. Its `rl_agent_config.json` is evidence of the Laya decision-head configuration, not evidence that the .NET ONNX contract has already been exported or validated.

## Official Source Candidate

The model card links the official source repository `https://github.com/NandhaKishorM/laya`. The PyPI `laya 0.3.7` release is published from source commit `010bacef009c855ccba814b51f7c8e1d38ab5e3f`.

- Wheel: `laya-0.3.7-py3-none-any.whl`, SHA-256 `370beee36a6962f0daed3f086146b7dc6cc825fee0a1941ec05ea7207989ea8a`.
- Source distribution: `laya-0.3.7.tar.gz`, SHA-256 `cca6e999f18f8a5c5efb51d7f6870aebbfa95477855634d4ba933063c38d0c3f`.
- Source tree blobs inspected at the fixed commit:
  - `laya/agent.py`: `70163bf98bf72b0ed1bd44823cb3a764fae28b88`
  - `laya/common.py`: `950c41df36bc231fa37d012b7f802be0c4eb4a55`
  - `pyproject.toml`: `f519fc39274e53a0d7182e3c18100e0e2a7c418f`

The relevant native paths are:

- `laya.agent.Agent.__init__`: loads local/Hugging Face files, `rl_agent_config.json`, tokenizer, encoder config, safetensors, and verifies `encoder.`, `type_emb.`, `scorer.`, and `act_head.` weights strictly.
- `laya.agent.Agent.system_one`: validates typed questions, builds sequences, calls the model once, applies temperature buckets, and returns answers/probabilities.
- `laya.common.serialize_state`: JSON serializes non-string state with `ensure_ascii=False`.
- `laya.common.render_options`: renders Choice, Score, and Noul options; Noul uses `false` and `true` markers.
- `laya.common.build_sequence`: builds `[CLS]`, question, option `[MASK]` markers, state, and `[SEP]` sequence with `max_len`/`head_max_len` truncation.
- `laya.common.collate_items`: pads `input_ids`, `attention_mask`, `marker_pos`, `marker_mask`, and `qtype`.
- `laya.common.DecisionModel.forward`: runs the encoder and decision head and returns `logits` plus `act_logits`.
- `laya.common.temp_bucket` and `laya.common.clamp_temperature`: choose option-count buckets and apply the runtime temperature bounds.

These paths are sufficient to define the candidate reference contract and were executed against the fixed checkpoint by the probe below.

## Native Reference Result

Acquisition completed using the fixed revision and wrote `models/laya-multilingual/source/source-manifest.json`. Offline re-verification completed without a network request. The source manifest records these actual SHA-256 values:

- `model.safetensors`: `9d628fd971b700382ac6f65920a86f149777b2e748e0c955fb3b19695aa8f204`
- `encoder/config.json`: `83f6916d13ef0f556ac461f28308dc2bffa7ebeadee8ec9e2db5812020ea5bb4`
- `rl_agent_config.json`: `25061739243b617ad88d1219ba6f8a9c86c5881ca28df024fa2d9b3b2fcc30c6`
- `tokenizer/tokenizer.json`: `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`
- `tokenizer/tokenizer_config.json`: `424b69444bf7b5809dc2cd2e36d0bd71b8055124dd24274d6db3c655d38205e7`

`tools/laya-reference/probe.py` completed with:

- Python `3.12.3`, `laya 0.3.7`, `torch 2.14.0+cpu`, `transformers 5.0.0`, `numpy 2.5.3`, CPU FP32, one thread.
- State-key coverage `170/170`, with zero missing and zero unexpected keys.
- Five independent synthetic cases and six questions covering English, Chinese, mixed-language Choice, Noul, mixed Choice/Noul inference, and 11 options.
- Native reference versus instrumented trace: all finite, selected answers matched, maximum probability absolute error `0.00004968702268374689`, below the default `1e-4` tolerance.
- Configuration observed at runtime: `max_len=1024`, `head_max_len=256`, temperature `[1.0, 1.0, 1.0]`, empty option buckets.

Machine-readable evidence: `reports/multilingual-reference-probe.json`. This proves the official Python reference and checkpoint load path; the published Python ORT and .NET parity edges are recorded separately in the export and parity reports.

## Engineering Handoff

- The host has no `python3-venv`; the validated toolchain therefore uses a target directory under `/tmp/opencode/laya-reference-cpu-site`, outside the repository.
- The upstream checkpoint has no published ONNX graph, but the local fixed exporter produced the verified version under `models/laya-multilingual/versions/`.
- Python ORT comparison, .NET tokenizer/encoding parity, bundle manifest v2, pointer publication, and .NET-only runtime smoke are complete; their hashes are listed in `test-data/multilingual-reference-manifest.json`.
- Phase 2 policy selection, held-out evaluation, benchmark, deployment, and Gate 3 remain owned by the original Phase 2 change.

The next step for the original Phase 2 change is quality evaluation from the preserved development A artifacts. The engineering readiness report does not select a policy or reuse development output as held-out evidence.
