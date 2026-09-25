# Phase 3A 起始環境紀錄

此檔案記錄開始實作與正式量測前的工作目錄／工具環境，不是 Phase 3A memory experiment evidence。

## Source identity

- Captured UTC: `2026-09-24T12:56:29Z`
- Branch: `dev`
- HEAD: `98f4840c2b7746b94825fe904f9228ed63c43392`
- Repository state before Phase 3A edits: tracked files clean；存在預先未追蹤的 `AGENTS.md`，本 change 不讀寫或納入該檔。
- Repository shallow: `false`

## Verified multilingual bundle identity

- Profile: `multilingual`
- Pointer: `models/laya-multilingual/current-bundle.json`，status `verified`
- Versioned root: `models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728`
- Checkpoint revision: `052592a15d198d9ad47da779604259b10b47b7aa`
- Bundle manifest SHA-256: `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553`
- Tokenizer SHA-256: `609d8f4c067cd3950f88594c5a802616cea245823836ef5848ee4fc40aab5b6f`
- ONNX external data SHA-256: `6ef993ee707fe1d6a75529f4a1f8347ff89966ff955d332ea3ebe4e5171a9ac1`
- Source of verified identity: `test-data/multilingual-reference-manifest.json` and current bundle pointer; model files are local ignored assets.

## Host environment

- OS: Linux Mint 22.3, Ubuntu 24.04 x64 RID
- Kernel: `7.0.0-34-generic`
- CPU: AMD Ryzen 7 255 w/ Radeon 780M Graphics；10 online logical CPUs
- Host memory: 16,301,592 kB; host swap: 2,097,148 kB
- .NET SDK: `10.0.112`
- Installed .NET runtime: `10.0.12` (no .NET 8 runtime installed at capture time)
- Docker Engine: `29.8.1`
- Cgroup filesystem: cgroup v2 is mounted; `/proc/self/cgroup` reports `/user.slice/user-1000.slice/session-c2.scope`, while `/sys/fs/cgroup/memory.max` is unavailable at the visible mount root. Per-process cgroup memory limit remains unverified and must not be inferred from host RAM.

## Initial command status

- `dotnet restore LayaDotNetPoc.sln`: passed; all projects up to date.
- `dotnet build LayaDotNetPoc.sln --no-restore`: passed; 0 warnings, 0 errors.
- `dotnet test LayaDotNetPoc.sln --no-restore`: test host did not start because `Microsoft.NETCore.App 8.0.0` is absent; this is an environment/runtime failure, not a test assertion failure.
- `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-build --no-restore`: passed on installed .NET runtime 10.0.12; 227 passed, 0 failed, 0 skipped. This result is explicitly a .NET 10 major-roll-forward baseline, not a native .NET 8 runtime result.
- No model-dependent memory experiment is implied by restore/build or the PureLogic-independent full test run; model availability and executed test categories remain separately recorded for formal Phase 3A.

## Core diagnostic seam checkpoint

- Captured UTC: `2026-09-24T13:24:34Z`
- Added two net8.0 host projects to the solution; `dotnet build LayaDotNetPoc.sln --no-restore`: passed, 0 warnings, 0 errors.
- `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore`: passed on .NET runtime 10.0.12; 235 passed, 0 failed, 0 skipped. The full suite includes PureLogic, English model/tokenizer, multilingual model/tokenizer and parity coverage available in this checkout.
- This is a post-change regression checkpoint on .NET 10 major roll-forward; native .NET 8 runtime verification remains unavailable in the captured environment.

## Phase 3A tooling and pilot checkpoint

- Captured UTC: `2026-09-24T18:17:58Z`.
- Source identity: HEAD `98f4840c2b7746b94825fe904f9228ed63c43392`; dirty identity `b62688199f34956240dd1aaccd3fc55a3805be0ff5561497c8c2113f4107ed18`. Identity excludes `reports/phase3a/` evidence and the pre-existing local `AGENTS.md`.
- `dotnet build LayaDotNetPoc.sln --no-restore`: passed, 0 warnings／0 errors. `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore`: 351 passed, 0 failed, 0 skipped on .NET 10.0.12. Test collections are serialized through `tests/Laya.Core.Tests/xunit.runner.json`; an earlier parallel run crashed while multiple native inference tests were active, and the complete serial run passed. Explicit concurrency tests continue to exercise worker concurrency.
- `node --test tools/*.test.mjs`: 37 passed, 0 failed. `node --test tools/validate-phase3a-evidence.test.mjs`: 6 passed. `bash -n` passed for `tools/*.sh` and `tools/laya-reference/run.sh`.
- Docker image `laya-memory-probe:phase3a`: image ID `sha256:b83e449d9ed4640f03b7b4aed4541a81b3284c480e905fde38cb023ee6bf4b66`; final runtime is .NET 8.0.31 and has no Python／Node.js. Build context was 570.97 kB; `.dockerignore` excludes model assets and `AGENTS.md`.
- Read-only verified-bundle Docker load-only smoke: completed with 0 requests; .NET 8.0.31／ORT 1.30.0.0, requested limit 4,000,000,000 bytes, effective cgroup limit 3,999,997,952 bytes, swap limit 0. A separate load-only formal-mode smoke parsed the frozen policy under .NET 8 and wrote only to a temporary `/tmp/opencode` output root; neither smoke is formal Docker matrix evidence.
- Campaign `phase3a-pilot-20260925`: 4/4 runs complete (tokenizer zh／en／mixed × 1,000 and full-pipeline × 200), 0 errors／integrity failures. Two further native full-pipeline pilot repeats completed 1,000 requests each with 0 errors／integrity failures. Native `/proc` counters were available; native host cgroup limits remained null at the visible mount. The Docker load-only smoke confirmed cgroup limit availability inside the container.
- Pilot index: `reports/phase3a/phase3a-pilot-calibration.json`, SHA-256 `56534c4c65cc488faa6b7d5cb9e45ec86d8ad718c9d3756bab1f09dfe36583bf`; analysis: `reports/phase3a/phase3a-pilot-calibration.md`. Frozen policy v1: `reports/phase3a/memory-policy.json`, SHA-256 `191da02a3ce54b74feff9942095604e2ad7f99fe1a8e7fa37849303380f4dfc5`. This original pilot does not imply plateau, leak, or Gate status.

## Managed heap counter validity correction and v2 pilot

- Captured UTC: `2026-09-24T19:15:34Z`.
- The preserved v1 formal run `phase3a-formal-20260925-baseline-full-pipeline` completed 5,000 requests with zero request errors／integrity failures, but its raw `managed_heap_bytes` contained nine negative `GC.GetTotalMemory(false)` values after GC checkpoints. The run／CSV remain unchanged and are excluded from v2 calibration and the new formal campaign.
- Telemetry now stores a negative signed GC byte result as blank `managed_heap_bytes` plus `managed_heap_unavailable_reason`; `MemorySlopeAnalyzer` treats an unavailable point as an incomplete segment instead of a zero or negative slope. The v2 raw samples use the new reason column.
- Regression verification: `dotnet build LayaDotNetPoc.sln --no-restore` passed with 0 warnings／errors; full `DOTNET_ROLL_FORWARD=Major dotnet test LayaDotNetPoc.sln --no-restore` passed 357／357, 0 skipped. `node --test tools/*.test.mjs` passed 37／37; `bash -n` passed all `tools/*.sh` and `tools/laya-reference/run.sh`.
- Source identity for v2: HEAD `98f4840c2b7746b94825fe904f9228ed63c43392`; dirty identity `d77ea11b5bd8804606db84209fe498b0cee25ac9a1eb503ae4cd1622a509b778`.
- Docker image rebuilt: `laya-memory-probe:phase3a`, image ID `sha256:eedd1aa12f88935e24dcbe2efcba1648b93ace68d4f900d2b9829a082745237a`. Final image is .NET 8 runtime only; smoke check confirmed Python／Node absent. V2 .NET 8.0.31／ORT 1.30.0.0 500-request pilot completed with 0 errors and no negative managed-heap counters. Effective cgroup memory was 3,999,997,952 bytes under a requested 4,000,000,000-byte limit; swap limit was 0.
- V2 native pilot campaign `phase3a-pilot-20260925-v2`: 4/4 complete (tokenizer zh／en／mixed × 1,000 and full-pipeline × 200). Two independent native full-pipeline 1,000-request repeats and one .NET 8 Docker 500-request diagnostic also completed with 0 errors／integrity failures; no negative managed heap readings occurred in these v2 pilot runs.
- V2 calibration index: `reports/phase3a/phase3a-pilot-calibration-v2.json`, SHA-256 `251ee705174657c11b25f97c34a8ce263ac2737892ff96d537b3a2cfa0df9faa`. Frozen v2 policy: `reports/phase3a/memory-policy-v2.json`, SHA-256 `fca022bdbcc450d95eec4c28c4bcd00cc7aba9fec68fb5249c6f341f35628a68`. A separate .NET 8 load-only formal-mode smoke validated the policy hash and bundle; it executed 0 requests and is not formal matrix evidence.
- No v2 formal baseline or full experiment matrix has run yet; the v1 5,000-request result is retained for traceability but not used to claim managed-heap stability or Gate status.
