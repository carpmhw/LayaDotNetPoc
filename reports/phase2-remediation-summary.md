# Phase 2 Remediation Summary

日期：2026-09-24

## 執行摘要

本次工作完成 multilingual ONNX 的工程 readiness 與 Phase 2 evidence chain remediation，確認官方 Python reference、ONNX bundle、Python/.NET parity、English regression、dataset split 與 acceptance pipeline 可以被重現及追溯。

目前結論是：

- Engineering readiness：`ready-for-phase2`
- Readiness acceptance：complete，exit `0`
- Full acceptance：complete，exit `0`；最新 artifact `20260924T104441Z-phase2-acceptance`
- Held-out evidence：complete，exit `0`，55/55 successful，validator 已通過 candidate/policy provenance
- Gate 3：`PARTIAL / SUGGESTION-ONLY`

Owner 已選定 multilingual Prompt C Balanced policy。Held-out 固定門檻為 AUTO `P>=.95`、`margin>=.80`，結果 AUTO accuracy `0.9375`、wrong AUTO `2`，因此只支持 suggestion-only，不支持 production AUTO。這不是 multilingual model quality failure，也不代表 production approval。

## 已完成功能

### 1. Reference 與 Provenance

- 固定官方 checkpoint：`convaiinnovations/laya-multilingual@052592a15d198d9ad47da779604259b10b47b7aa`。
- 固定 `laya==0.3.7`、Python/runtime package identity、source snapshot、source file size/hash 與 toolchain lock。
- Python reference scripts 透過 isolated site 執行，並驗證 runtime package location、version 與 wheel archive hash。
- Snapshot acquisition、probe、fixture generation、export、comparison 都保存 source、runtime、bundle、fixture 與 reference manifest provenance。
- Custom source root 也必須經過同一份 source lock 驗證，不接受只依賴目錄名稱或未鎖定來源。

### 2. ONNX Bundle 與 Runtime

- 建立 versioned multilingual ONNX bundle，包含 graph、external data、tokenizer/config 與 bundle manifest v2。
- `models/laya-multilingual/current-bundle.json` 只指向已完成 Python/.NET parity 的 versioned bundle。
- Runtime 預設拒絕 `candidate-staged` model；只有明確的 parity/test opt-in 才可載入 staged candidate。
- Python export 與 .NET parity 綁定相同 fixture hash，避免使用不同 fixture 或不同 bundle 產生 false-green 結果。
- 發佈流程要求 Python 與 .NET 報告引用同一 candidate manifest、fixture 與 bundle identity，並以 atomic publish 更新 current pointer。

### 3. Phase 2 Dataset 與 Evidence

- Dataset 由 manifest 驅動，固定 220 筆資料、165 筆 development、55 筆 held-out，並驗證 group membership、完整 ID 集合、dataset hash 與 guideline hash。
- Phase 2 run 使用 schemaVersion 2 canonical camelCase manifest，保存 selected IDs、prompt/options/serialization、policy、model/reference/fixture hash、source commit/dirty/digest、runtime environment 與 command。
- 每個 run 保存 manifest、raw results、metrics、summary 與 artifact index；metrics 可由未 rounding raw results 重算。
- Held-out evaluation 必須在 inference 前提供 frozen candidate；candidate audit 會驗證 model/reference/dataset/split、policy hash、prompt/request identity、development artifact hash 與 candidate path。
- Evidence validator 會拒絕 fixture hash 不符、results/index 被竄改、legacy/incomplete run、policy tamper 與 request identity tamper。

### 4. Acceptance Pipeline

- Acceptance 分成 preflight、readiness 與 full scope，不把 readiness 當成完整 Phase 2。
- Stage dependency fingerprint 包含 source、fixtures、model、reports、tests 與 acceptance inputs。
- `full_final_inventory` 不允許從既有 stage reuse，避免沿用過期的 full evidence 結果。
- `complete`、`failed`、`blocked` 對外 exit code 固定為 `0`、`1`、`2`；相依 stage 在 blocked/failed 後停止。
- Reuse 只有在 dependency fingerprint 與 artifact validation 同時一致時才允許。

## 主要報告與 Artifact

| Artifact | Status | 用途 | SHA-256 / Identity |
|---|---|---|---|
| `test-data/multilingual-reference-manifest.json` | complete | source、toolchain、reference 與 bundle provenance | `e340f74fc448a15afbcb4bd9dda8ac4f7ca6d531cb37f3a3c4e03a2837c38500` |
| `reports/multilingual-reference-validation.md` | complete | 官方 source/checkpoint/reference 驗證摘要 | `85cd9fc97388073e44d8555f1768c2fe5a09adb15d6db041bef05c616b45e254` |
| `reports/multilingual-reference-probe.json` | complete | native Choice/Noul probe machine report | `78f00c8ae63923ec6ca26ce9abb9c57a66cb858e94e73c0cc858f294f490f63b` |
| `test-data/multilingual-parity-fixtures.json` | complete | 20 筆官方 Python/ONNX/.NET parity fixture | `fe97f8e47b08c2377af88cffda9610e30615104104784123a9d0c79ddeddc23f` |
| `reports/multilingual-export-validation.json` | complete | Python export/ORT comparison，20/20 passed | `0fafb25a5629f7503497efbbf387b7ad1244fbe7cedae2c9c7f824a6abcb747b` |
| `reports/multilingual-export-validation.md` | complete | Python export 人讀摘要 | `40fdac4a341411fef587892b1c1c008fb83d8a7ca5ac8f1cf9d5748fda2b348d` |
| `models/laya-multilingual/current-bundle.json` | published pointer | current verified versioned bundle pointer | `c619e566325ca2690047309d62a0b9a997c879c7f5091dce628c9877268593f1` |
| `models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728/laya-bundle-manifest.json` | verified | published bundle manifest | `75866db31d4dce160349a5ed986118e79c446527b8b23b1a956946a3744cc553` |
| `reports/multilingual-dotnet-parity.json` | complete | .NET parity，20/20 passed，0 skipped | `8b525b1fdb1bb40a490d09ead981d58efc8a043d4284c521beb1ff8b4eca664e` |
| `reports/multilingual-readiness.md` | ready-for-phase2 | engineering readiness handoff | `2e68543ea995f9af5febe657d855805d882b589ebad92667007cbbeef89d2f8c` |
| `reports/phase2-acceptance/20260924T104441Z-phase2-acceptance/acceptance.json` | full complete | latest machine-readable acceptance record | `be6318e15dc31a14617458b0f633550ad371892c6bdab0cf2e5d3cdabfe01c6b` |
| `reports/phase2-acceptance/20260924T060021Z-phase2-acceptance/acceptance.json` | historical full blocked | pre-candidate acceptance record | `6e9e6ce5faaef22e996dcc9a0916abedd131274b1fc95236cc6b1c9951dc6f3b` |
| `reports/phase2-frozen-candidate.json` | validated | owner-selected multilingual Prompt C Balanced candidate | `80be1980b0cd20d3ae6073c482534a9a0fc27844117ca04276b15d662b2bbff8` |
| `reports/phase2-runs/20260924T103842588Z-654915347f184bd8bdf41773971d1dac/` | complete | fixed-policy held-out run, 55/55 | `80be1980b0cd20d3ae6073c482534a9a0fc27844117ca04276b15d662b2bbff8` candidate |

## Development A Handoff

最新受控 development A runs：

- English：`20260924T055011652Z-72d14048b85c4d2396dc899832d41343`，development，165 selected，165 input，complete。
- Multilingual：`20260924T054805799Z-9c9ecccb8a254918be6ae18ef44a4d7f`，development，165 selected，165 input，complete。

兩個 run 都使用 schemaVersion 2 provenance，且沒有執行 held-out quality 或以 held-out 重新選 policy/prompt。

Frozen candidate：`reports/phase2-frozen-candidate.json`；development Prompt C run `20260924T054908845Z-eaeb23f26ffb44658cd3350ba7a6dea4`，Balanced policy hash `8cacd8c9242562eed66a0dcab16b06d95fe9e9feda69227881867b8027e16e0b`。Held-out run `20260924T103842588Z-654915347f184bd8bdf41773971d1dac` completed 55/55 with accuracy `0.7272727273` and Macro F1 `0.7221628045`.

## 驗證結果

| 驗證 | 結果 |
|---|---:|
| Python reference tests | `14/14` passed |
| Node acceptance/evidence tests | `19/19` passed |
| .NET full test suite | `227/227` passed，0 skipped |
| OpenSpec strict validation | `3/3` passed |
| Python parity fixtures | `20/20` passed |
| .NET parity fixtures | `20/20` passed，0 skipped |
| `node tools/validate-phase2-evidence.mjs .` | complete，exit `0` |
| `node tools/validate-phase2-evidence.mjs . --require-held-out` | complete，exit `0` |
| Full acceptance | complete，exit `0`，run `20260924T104441Z-phase2-acceptance` |

上一份 full acceptance (`20260924T060021Z`) 的 terminal stage 是 `full_final_inventory`，沒有 validation errors，當時 blocked reason 為：

```text
no complete held-out run with frozen-candidate and policy provenance is available
```

## 尚未完成與限制

- Held-out 結果已完成，但所有 55 筆均為 mixed-language group，不能宣稱各語言泛化。
- Development calibration、automation grid、benchmark、concurrency 與 deployment evidence 已完成；memory stability 仍需調查。
- `reports/phase2-gate3-report.md` 現為 `PARTIAL / SUGGESTION-ONLY`；Balanced policy 有 2 筆 held-out wrong AUTO，因此 AUTO 維持停用。
- 不可將 parity、export、smoke、development A 或 readiness 結果解讀為 production approval、正式交易流程整合或資料庫/UI 變更核准。

## 重現命令

工程 readiness：

```bash
./tools/run-phase2-acceptance.sh --until readiness
```

完整 acceptance：

```bash
./tools/run-phase2-acceptance.sh
```

Evidence audit：

```bash
node tools/validate-phase2-evidence.mjs .
node tools/validate-phase2-evidence.mjs . --require-held-out
```

Python reference tests 需使用已鎖定的 isolated site：

```bash
LAYA_REFERENCE_SITE="${LAYA_REFERENCE_SITE:-/tmp/opencode/laya-reference-cpu-site}" \
  tools/laya-reference/run.sh -m unittest discover tools/laya-reference -p 'test_*.py'
```

.NET 測試需使用目前 verified multilingual root，並在只有 .NET 10 runtime 的主機上設定 roll-forward：

```bash
DOTNET_ROLL_FORWARD=Major \
LAYA_MULTILINGUAL_MODEL_ROOT="$PWD/models/laya-multilingual/versions/8f37a12e6df5ef1adebad26bb08a4f552f93119dbc8391e2df3d828317f73728" \
  dotnet test LayaDotNetPoc.sln --no-build --no-restore
```

## 結論

本次工作已完成「可以安全進入 Phase 2」所需的工程基礎、provenance、parity、development/held-out evidence 與 full acceptance；Gate 3 為 `PARTIAL / SUGGESTION-ONLY`，不允許 production AUTO 或正式流程整合。仍須保留 memory risk 與 mixed-only held-out 限制。
