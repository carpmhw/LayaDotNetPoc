# Laya Multilingual Offline Reference

This directory contains the explicit acquisition and native-reference tools for the fixed
`convaiinnovations/laya-multilingual` checkpoint. The tools never fall back to `main`, another
checkpoint, or the English Node reference.

## Environment

The repository does not vendor Python, model weights, or a virtual environment. The current
validated host setup uses a target-installed CPU toolchain under
`/tmp/opencode/laya-reference-cpu-site` because this host does not provide `python3-venv`.

The versions and direct artifact hashes are recorded in `toolchain.lock.json`. Runtime setup is
an explicit acquisition step and must not be performed by .NET runtime or acceptance preflight.

```bash
python3 -m pip install --no-cache-dir --target /tmp/opencode/laya-reference-cpu-site \
  --index-url https://download.pytorch.org/whl/cpu --no-deps torch==2.14.0+cpu
python3 -m pip install --no-cache-dir --target /tmp/opencode/laya-reference-cpu-site \
  --no-deps laya==0.3.7
python3 -m pip install --no-cache-dir --target /tmp/opencode/laya-reference-cpu-site \
  transformers==5.0.0 safetensors==0.8.0 huggingface_hub==1.32.0 \
  numpy==2.5.3 onnx==1.20.1 onnxruntime==1.30.0 \
  onnxscript==0.7.2 onnx-ir==0.1.16
```

`run.sh` supplies the isolated target path through `PYTHONPATH`.

## Acquisition

The first command is online and is the only command allowed to download the fixed checkpoint:

```bash
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh acquire.py \
  --output models/laya-multilingual/source
```

Offline verification never contacts Hugging Face:

```bash
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh acquire.py \
  --offline --output models/laya-multilingual/source
```

The command writes `source-manifest.json` with actual file sizes and SHA-256 values. Missing
assets are blocked; hash mismatches are failed. Large assets remain ignored by Git.

## Native probe

After acquisition, run the native Python reference probe:

```bash
LAYA_REFERENCE_SITE=/tmp/opencode/laya-reference-cpu-site \
  tools/laya-reference/run.sh probe.py \
  --source-root models/laya-multilingual/source \
  --output reports/multilingual-reference-probe.json
```

The probe uses the official `laya.agent.Agent` and `laya.common` paths, checks source hashes,
loads the checkpoint on CPU, records weight-key coverage, runs independent English/Chinese/mixed
Choice and Noul cases, and compares native answers to instrumented tensors. It writes a blocked or
failed machine-readable report rather than fabricating expected outputs.
