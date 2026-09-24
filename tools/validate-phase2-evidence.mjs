#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

const root = path.resolve(process.argv[2] ?? process.cwd());
const requireHeldOut = process.argv.includes("--require-held-out");
const supportingReports = [
  "model-validation.md",
  "multilingual-parity-report.md",
  "dataset-summary.md",
  "multilingual-baseline-evaluation.md",
  "model-comparison.md",
  "confidence-analysis.md",
  "noul-analysis.md",
  "prompt-comparison.md",
  "misclassification-analysis.md",
  "phase2-benchmark-results.md",
  "performance-comparison.md",
  "deployment-validation.md"
];
const errors = [];
const blocked = [];

/** 讀取 UTF-8 文字檔，缺少時記錄完整性錯誤。 */
function readText(relativePath) {
  const absolutePath = path.join(root, relativePath);
  if (!fs.existsSync(absolutePath)) {
    errors.push(`missing file: ${relativePath}`);
    return null;
  }

  return fs.readFileSync(absolutePath, "utf8");
}

/** 讀取 JSON 檔案並將格式錯誤記錄為 failed evidence。 */
function readJson(relativePath) {
  const content = readText(relativePath);
  if (content === null) {
    return null;
  }

  try {
    return JSON.parse(content);
  } catch (error) {
    errors.push(`invalid JSON ${relativePath}: ${error.message}`);
    return null;
  }
}

/** 計算檔案 SHA-256 供 manifest provenance 比對。 */
function sha256(relativePath) {
  const absolutePath = path.join(root, relativePath);
  return crypto.createHash("sha256").update(fs.readFileSync(absolutePath)).digest("hex");
}

/** 計算已解析絕對路徑的 SHA-256，供 run artifact index 驗證。 */
function sha256Absolute(absolutePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(absolutePath)).digest("hex");
}

/** 判斷值是否為可供 provenance 使用的 SHA-256 hex 字串。 */
function isSha256(value) {
  return typeof value === "string" && /^[0-9a-f]{64}$/iu.test(value);
}

/** 依照 .NET Phase 2 的固定欄位順序重算 policy SHA-256。 */
function candidatePolicyHash(policy) {
  if (policy === null || typeof policy !== "object" || Array.isArray(policy) ||
      typeof policy.name !== "string" || !policy.name || typeof policy.autoEnabled !== "boolean") {
    return null;
  }
  const thresholds = [
    policy.autoProbabilityThreshold,
    policy.autoMarginThreshold,
    policy.suggestProbabilityThreshold,
    policy.suggestMarginThreshold
  ];
  if (thresholds.some((value) => typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 1)) {
    return null;
  }
  const canonical = [
    policy.name,
    policy.autoEnabled ? "true" : "false",
    ...thresholds.map((value) => String(value))
  ].join("|");
  return crypto.createHash("sha256").update(canonical, "utf8").digest("hex");
}

/** 將 selected IDs 排序成可比較的 canonical 集合。 */
function sortedIds(values) {
  return [...values].sort((left, right) => left.localeCompare(right));
}

/** 從 JSON object 讀取第一個存在的欄位，兼容 raw result 的現有命名。 */
function getProperty(object, ...names) {
  if (object === null || typeof object !== "object" || Array.isArray(object)) return undefined;
  for (const name of names) {
    if (Object.prototype.hasOwnProperty.call(object, name)) return object[name];
  }
  return undefined;
}

/** 比較可重算的浮點 metric，拒絕非有限值與明顯誤差。 */
function equalMetric(actual, expected, field, runName) {
  if (typeof actual !== "number" || !Number.isFinite(actual) ||
      typeof expected !== "number" || !Number.isFinite(expected) ||
      Math.abs(actual - expected) > 1e-12) {
    errors.push(`run ${runName} metrics ${field} is not recomputable from raw results`);
  }
}

/** 解析 RFC 4180 CSV，保留 quoted comma、quoted newline 與 escaped quote。 */
function parseCsv(content) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;
  for (let index = 0; index < content.length; index += 1) {
    const character = content[index];
    if (character === '"') {
      if (quoted && content[index + 1] === '"') {
        field += '"';
        index += 1;
      } else {
        quoted = !quoted;
      }
    } else if (character === "," && !quoted) {
      row.push(field);
      field = "";
    } else if ((character === "\n" || character === "\r") && !quoted) {
      if (character === "\r" && content[index + 1] === "\n") index += 1;
      row.push(field);
      field = "";
      if (row.length > 1 || row[0] !== "") rows.push(row);
      row = [];
    } else {
      field += character;
    }
  }
  if (quoted) throw new Error("unterminated quoted CSV field");
  if (field.length > 0 || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

/** 驗證 metrics.json 的分母、正確數、accuracy 與 coverage 可由 raw results 重算。 */
function validateMetrics(runName, results, metrics) {
  const metricsPayload = getProperty(metrics, "metrics", "Metrics");
  if (metricsPayload === undefined || metricsPayload === null || typeof metricsPayload !== "object") {
    errors.push(`run ${runName} is missing metrics payload`);
    return;
  }

  const predictions = getProperty(results, "predictions", "Predictions");
  const failures = getProperty(results, "failures", "Failures");
  const inputCount = getProperty(results, "inputCount", "InputCount");
  if (!Array.isArray(predictions) || !Array.isArray(failures) ||
      !Number.isInteger(inputCount)) {
    errors.push(`run ${runName} raw results are not suitable for metric recomputation`);
    return;
  }

  const expectedCorrectCount = predictions.filter((prediction) =>
    getProperty(prediction, "expectedLabel", "ExpectedLabel") ===
    getProperty(prediction, "predictedLabel", "PredictedLabel")).length;
  const expectedSuccessCount = predictions.length;
  const expectedFailureCount = failures.length;
  const expectedFullAccuracy = inputCount === 0 ? 0 : expectedCorrectCount / inputCount;
  const expectedCoverage = inputCount === 0 ? 0 : expectedSuccessCount / inputCount;
  const actualCounts = {
    inputCount: getProperty(metricsPayload, "inputCount", "InputCount"),
    successCount: getProperty(metricsPayload, "successCount", "SuccessCount"),
    failureCount: getProperty(metricsPayload, "failureCount", "FailureCount"),
    correctCount: getProperty(metricsPayload, "correctCount", "CorrectCount")
  };

  if (actualCounts.inputCount !== inputCount) {
    errors.push(`run ${runName} metrics inputCount does not match raw results`);
  }
  if (actualCounts.successCount !== expectedSuccessCount) {
    errors.push(`run ${runName} metrics successCount does not match raw results`);
  }
  if (actualCounts.failureCount !== expectedFailureCount) {
    errors.push(`run ${runName} metrics failureCount does not match raw results`);
  }
  if (actualCounts.correctCount !== expectedCorrectCount) {
    errors.push(`run ${runName} metrics correctCount does not match raw results`);
  }

  const successAccuracy = getProperty(metricsPayload, "successAccuracy", "SuccessAccuracy");
  if (successAccuracy !== null && successAccuracy !== undefined) {
    equalMetric(successAccuracy, expectedSuccessCount === 0 ? 0 : expectedCorrectCount / expectedSuccessCount,
      "successAccuracy", runName);
  }
  equalMetric(getProperty(metricsPayload, "fullInputAccuracy", "FullInputAccuracy"),
    expectedFullAccuracy, "fullInputAccuracy", runName);
  equalMetric(getProperty(metricsPayload, "coverage", "Coverage"), expectedCoverage, "coverage", runName);
}

/** 驗證 supporting Markdown、去識別錯誤 CSV 與固定 gate 報告存在。 */
function validateInventory() {
  for (const report of supportingReports) {
    readText(path.join("reports", report));
  }
  readText(path.join("reports", "phase2-gate3-report.md"));
  const csv = readText("misclassified-transactions.csv");
  const expectedHeader = ["id", "description_redacted", "expected", "predicted", "confidence", "second_choice", "margin", "language"];
  if (csv !== null) {
    try {
      const records = parseCsv(csv);
      if (!records[0] || JSON.stringify(records[0]) !== JSON.stringify(expectedHeader)) {
        errors.push("misclassified-transactions.csv has an unexpected header");
      }
    } catch (error) {
      errors.push(`misclassified-transactions.csv is invalid CSV: ${error.message}`);
    }
  }
}

/** 驗證 dataset hash、唯一 ID、group 完整性與 development/held-out 分割。 */
function validateDataset() {
  const manifest = readJson(path.join("test-data", "transactions-phase2-manifest.json"));
  const csv = readText(path.join("test-data", "transactions-phase2.csv"));
  const guideline = readText(path.join("test-data", "category-label-guideline.md"));
  if (!manifest || csv === null || guideline === null) {
    return null;
  }

  let records;
  try {
    records = parseCsv(csv);
  } catch (error) {
    errors.push(`transactions-phase2.csv is invalid CSV: ${error.message}`);
    return null;
  }
  const ids = records.slice(1).map((record) => record[0]);
  const uniqueIds = new Set(ids);
  if (ids.length !== manifest.dataset.rowCount || uniqueIds.size !== ids.length) {
    errors.push("dataset rowCount or unique ID set does not match manifest");
  }
  if (sha256(path.join("test-data", "transactions-phase2.csv")) !== manifest.dataset.sha256) {
    errors.push("transactions-phase2.csv SHA-256 does not match manifest");
  }
  if (sha256(path.join("test-data", "category-label-guideline.md")) !== manifest.guideline.sha256) {
    errors.push("category-label-guideline.md SHA-256 does not match manifest");
  }

  const groupIds = [];
  let developmentCount = 0;
  let heldOutCount = 0;
  for (const [groupName, group] of Object.entries(manifest.groups)) {
    if (!Array.isArray(group.ids) || group.ids.length !== 5) {
      errors.push(`group ${groupName} does not contain five IDs`);
      continue;
    }
    groupIds.push(...group.ids);
    if (group.split === "development") developmentCount += group.ids.length;
    else if (group.split === "held-out") heldOutCount += group.ids.length;
    else errors.push(`group ${groupName} has an invalid split`);
  }
  if (new Set(groupIds).size !== groupIds.length ||
      new Set(groupIds).size !== uniqueIds.size ||
      groupIds.some((id) => !uniqueIds.has(id))) {
    errors.push("manifest group IDs do not exactly match dataset IDs");
  }
  if (developmentCount !== manifest.split.developmentCount ||
      heldOutCount !== manifest.split.heldOutCount) {
    errors.push("manifest split counts do not match group split");
  }
  return {
    manifestSha256: sha256(path.join("test-data", "transactions-phase2-manifest.json")),
    datasetSha256: manifest.dataset.sha256,
    guidelineSha256: manifest.guideline.sha256,
    splitIds: {
      development: Object.values(manifest.groups)
        .filter((group) => group.split === "development")
        .flatMap((group) => group.ids),
      "held-out": Object.values(manifest.groups)
        .filter((group) => group.split === "held-out")
        .flatMap((group) => group.ids)
    }
  };
}

/** 驗證 reference manifest 狀態，並將目前缺少模型資產標成 blocked。 */
function validateReferenceStatus() {
  const manifest = readJson(path.join("test-data", "multilingual-reference-manifest.json"));
  if (!manifest) return;
  if (manifest.status === "blocked") {
    blocked.push(manifest.reason);
  } else if (manifest.status !== "complete" && manifest.status !== "ready-for-phase2") {
    errors.push("multilingual reference manifest has no complete status");
  }
}

/** 驗證每個 phase2 run 的 manifest 與結果檔案使用相同 run-id。 */
function validateFrozenCandidate(runName, runManifest, dataset) {
  const candidatePath = runManifest.frozenCandidatePath;
  if (typeof candidatePath !== "string" || !candidatePath || path.isAbsolute(candidatePath) || candidatePath.includes("..")) {
    errors.push(`run ${runName} has no safe frozen candidate artifact path`);
    return false;
  }
  const absoluteCandidatePath = path.resolve(root, candidatePath);
  if (!absoluteCandidatePath.startsWith(`${root}${path.sep}`) || !fs.existsSync(absoluteCandidatePath)) {
    errors.push(`run ${runName} frozen candidate artifact is missing or outside repository root`);
    return false;
  }
  if (!isSha256(runManifest.frozenCandidateManifestSha256)) {
    errors.push(`run ${runName} frozenCandidateManifestSha256 is not a SHA-256`);
    return false;
  }
  if (sha256Absolute(absoluteCandidatePath) !== runManifest.frozenCandidateManifestSha256.toLowerCase()) {
    errors.push(`run ${runName} frozen candidate artifact hash does not match manifest`);
    return false;
  }
  let candidate;
  try {
    candidate = JSON.parse(fs.readFileSync(absoluteCandidatePath, "utf8"));
  } catch (error) {
    errors.push(`run ${runName} frozen candidate is invalid JSON: ${error.message}`);
    return false;
  }
  if (candidate.kind !== "laya.phase2.frozen-candidate" || candidate.schemaVersion !== 1) {
    errors.push(`run ${runName} frozen candidate has an invalid schema`);
    return false;
  }
  const expectedPolicyHash = candidatePolicyHash(candidate.policy);
  if (!isSha256(runManifest.policyHash) || !isSha256(candidate.policyHash) ||
      candidate.policyHash.toLowerCase() !== runManifest.policyHash.toLowerCase() ||
      expectedPolicyHash === null || expectedPolicyHash !== candidate.policyHash.toLowerCase()) {
    errors.push(`run ${runName} frozen candidate policy hash does not match manifest`);
    return false;
  }
  if (candidate.model?.profile !== runManifest.profile ||
      candidate.model?.checkpointRevision !== runManifest.modelRevision ||
      candidate.model?.bundleManifestSha256 !== runManifest.bundleManifestSha256) {
    errors.push(`run ${runName} frozen candidate model identity does not match manifest`);
    return false;
  }
  if (candidate.request?.promptVariant !== runManifest.promptVariant ||
      !isSha256(candidate.request?.promptHash) ||
      candidate.request.promptHash.toLowerCase() !== String(runManifest.promptHash).toLowerCase() ||
      !isSha256(candidate.request?.optionOrderHash) ||
      candidate.request.optionOrderHash.toLowerCase() !== String(runManifest.optionOrderHash).toLowerCase() ||
      candidate.request?.serializationVersion !== runManifest.serializationVersion ||
      !isSha256(candidate.request?.serializationHash) ||
      candidate.request.serializationHash.toLowerCase() !== String(runManifest.serializationHash).toLowerCase()) {
    errors.push(`run ${runName} frozen candidate request identity does not match manifest`);
    return false;
  }
  if (candidate.dataset?.sourceSplit !== "development" || candidate.dataset?.targetSplit !== "held-out" ||
      candidate.dataset?.manifestSha256 !== dataset.manifestSha256 ||
      candidate.dataset?.datasetSha256 !== dataset.datasetSha256 ||
      candidate.dataset?.guidelineSha256 !== dataset.guidelineSha256 ||
      candidate.dataset?.developmentSelectedIdsHash !== crypto.createHash("sha256").update(
        JSON.stringify(dataset.splitIds.development), "utf8").digest("hex") ||
      candidate.dataset?.heldOutSelectedIdsHash !== runManifest.selectedIdsHash) {
    errors.push(`run ${runName} frozen candidate dataset identity does not match manifest`);
    return false;
  }
  if (candidate.reference?.manifestSha256 !== runManifest.referenceManifestSha256) {
    errors.push(`run ${runName} frozen candidate reference identity does not match manifest`);
    return false;
  }
  for (const [field, relativePath] of [
    ["manifestSha256", candidate.developmentRun?.manifestPath],
    ["resultsSha256", candidate.developmentRun?.resultsPath]
  ]) {
    const candidateArtifactPath = relativePath && path.resolve(root, relativePath);
    if (!candidateArtifactPath || !candidateArtifactPath.startsWith(`${root}${path.sep}`) ||
        !fs.existsSync(candidateArtifactPath) ||
        !isSha256(candidate.developmentRun?.[field]) ||
        sha256Absolute(candidateArtifactPath) !== candidate.developmentRun[field].toLowerCase()) {
      errors.push(`run ${runName} frozen candidate development artifact ${field} is not auditable`);
      return false;
    }
  }
  return true;
}

/** 驗證每個 phase2 run 的 manifest、結果、fixture 與 candidate provenance。 */
function validateRuns(dataset) {
  const directory = path.join(root, "reports", "phase2-runs");
  if (!fs.existsSync(directory)) {
    blocked.push("reports/phase2-runs does not exist; no model run artifacts are available");
    return;
  }

  const runs = fs.readdirSync(directory, { withFileTypes: true }).filter((entry) => entry.isDirectory());
  if (runs.length === 0) {
    blocked.push("reports/phase2-runs is empty; no model run artifacts are available");
    return;
  }
  let hasCompleteHeldOutRun = false;
  for (const run of runs) {
    const runRelative = path.join("reports", "phase2-runs", run.name);
    const manifest = readJson(path.join(runRelative, "manifest.json"));
    const results = readJson(path.join(runRelative, "results.json"));
    const metrics = readJson(path.join(runRelative, "metrics.json"));
    const index = readJson(path.join(runRelative, "artifact-index.json"));
    if (!manifest || !results || !metrics || !index) continue;
    if (manifest.schemaVersion !== 2 || Object.keys(manifest).some((key) => /^[A-Z]/u.test(key))) {
      blocked.push(`run ${run.name} is legacy or not canonical schemaVersion=2`);
      continue;
    }

    if (results.schemaVersion !== 2) {
      errors.push(`run ${run.name} results are not schemaVersion=2`);
    }

    const manifestRunId = manifest.runId;
    const resultManifest = results.manifest;
    if (manifestRunId !== run.name || resultManifest?.runId !== manifestRunId) {
      errors.push(`run directory ${run.name} has mismatched run identity`);
    }
    const identityFields = [
      "runId", "profile", "modelRevision", "datasetHash", "guidelineHash", "datasetManifestHash",
      "split", "selectedIdsHash", "promptVariant", "promptHash", "optionOrderHash",
      "serializationVersion", "serializationHash", "bundleManifestSha256", "referenceManifestSha256",
      "policyHash", "frozenCandidateManifestSha256", "selectedIds", "fixtureHash", "sourceCommit",
      "sourceDirty", "sourceDigest", "environmentFingerprint", "threadCount", "createdUtc", "command"
    ];
    const requiredFields = [
      "runId", "profile", "modelRevision", "datasetHash", "guidelineHash", "datasetManifestHash",
      "split", "selectedIdsHash", "selectedIds", "promptVariant", "promptHash", "optionOrderHash",
      "serializationVersion", "serializationHash", "fixtureHash", "sourceCommit", "sourceDirty",
      "sourceDigest", "environmentFingerprint", "threadCount", "createdUtc", "command"
    ];
    for (const field of requiredFields) {
      if (manifest[field] === undefined || manifest[field] === null || manifest[field] === "unavailable") {
        blocked.push(`run ${run.name} is missing required v2 provenance field ${field}`);
      }
    }
    for (const field of identityFields) {
      const resultValue = resultManifest?.[field];
      const manifestValue = manifest[field];
      if (JSON.stringify(resultValue) !== JSON.stringify(manifestValue)) {
        errors.push(`run ${run.name} results manifest differs at ${field}`);
      }
    }

    const fixturePath = path.join("test-data", "multilingual-parity-fixtures.json");
    const fixtureContent = readText(fixturePath);
    if (fixtureContent !== null && manifest.fixtureHash !== sha256(fixturePath)) {
      errors.push(`run ${run.name} fixtureHash does not match multilingual parity fixture`);
    }
    if (dataset?.splitIds?.[manifest.split] &&
        JSON.stringify(sortedIds(manifest.selectedIds ?? [])) !== JSON.stringify(sortedIds(dataset.splitIds[manifest.split]))) {
      errors.push(`run ${run.name} selected IDs do not exactly match ${manifest.split} dataset split`);
    }

    const predictions = Array.isArray(results.predictions) ? results.predictions : [];
    const failures = Array.isArray(results.failures) ? results.failures : [];
    if (!Number.isInteger(results.inputCount) || predictions.length + failures.length !== results.inputCount) {
      errors.push(`run ${run.name} inputCount does not equal prediction plus failure count`);
    }
    const resultIds = [...predictions, ...failures].map((item) => getProperty(item, "id", "Id"));
    if (new Set(resultIds).size !== resultIds.length) {
      errors.push(`run ${run.name} contains duplicate result IDs`);
    }
    if (!Array.isArray(manifest.selectedIds) ||
        JSON.stringify([...new Set(resultIds)].sort()) !== JSON.stringify([...manifest.selectedIds].sort())) {
      errors.push(`run ${run.name} result IDs do not exactly cover selected IDs`);
    }
    if (typeof manifest.selectedIdsHash !== "string" ||
        crypto.createHash("sha256").update(JSON.stringify(manifest.selectedIds), "utf8").digest("hex") !==
          manifest.selectedIdsHash.toLowerCase()) {
      errors.push(`run ${run.name} selectedIdsHash does not match selectedIds`);
    }
    if (results.complete !== (failures.length === 0)) {
      errors.push(`run ${run.name} complete flag does not match failure count`);
    }
    if (results.complete !== true) {
      blocked.push(`run ${run.name} is incomplete`);
    }
    if (results.complete === true && manifest.split === "held-out") {
      hasCompleteHeldOutRun = dataset !== null && validateFrozenCandidate(run.name, manifest, dataset);
    }
    validateMetrics(run.name, results, metrics);

    if (index.schemaVersion !== 1 || index.runId !== manifestRunId || !index.artifacts) {
      errors.push(`run ${run.name} has an invalid artifact index`);
      continue;
    }
    for (const artifactName of ["manifest.json", "results.json", "metrics.json", "summary.md"]) {
      const artifact = index.artifacts[artifactName];
      const absoluteArtifact = path.join(root, runRelative, artifactName);
      if (!artifact || artifact.path !== artifactName || !fs.existsSync(absoluteArtifact)) {
        errors.push(`run ${run.name} artifact index is missing ${artifactName}`);
        continue;
      }
      const stats = fs.statSync(absoluteArtifact);
      if (artifact.sizeBytes !== stats.size || artifact.sha256 !== sha256Absolute(absoluteArtifact)) {
        errors.push(`run ${run.name} artifact hash mismatch for ${artifactName}`);
      }
    }
  }
  if (requireHeldOut && !hasCompleteHeldOutRun) {
    blocked.push("no complete held-out run with frozen-candidate and policy provenance is available");
  }
}

/** 執行完整性檢查並以 0/1/2 區分 complete、failed、blocked。 */
function main() {
  validateInventory();
  const dataset = validateDataset();
  validateReferenceStatus();
  validateRuns(dataset);
  const status = errors.length > 0 ? "failed" : blocked.length > 0 ? "blocked" : "complete";
  const output = { status, root, errors, blocked };
  process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
  process.exitCode = status === "complete" ? 0 : status === "blocked" ? 2 : 1;
}

main();
