import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const validatorPath = path.join(repositoryRoot, "tools", "validate-phase2-evidence.mjs");
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

/** 計算測試 fixture 需要的 SHA-256。 */
function hashText(value) {
  return crypto.createHash("sha256").update(value, "utf8").digest("hex");
}

/** 計算測試 repository 內的 binary artifact SHA-256。 */
function hashFile(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

/** 建立 JSON artifact 並使用固定縮排，方便測試後重新計算 artifact index。 */
function writeJson(filePath, value) {
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`);
}

/** 為 phase2 run 的四個 artifact 產生不含自身的 index。 */
function writeArtifactIndex(runDirectory, runId) {
  const artifacts = {};
  for (const name of ["manifest.json", "results.json", "metrics.json", "summary.md"]) {
    const filePath = path.join(runDirectory, name);
    artifacts[name] = {
      path: name,
      sha256: crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex"),
      sizeBytes: fs.statSync(filePath).size
    };
  }
  writeJson(path.join(runDirectory, "artifact-index.json"), {
    schemaVersion: 1,
    runId,
    artifacts
  });
}

/** 建立最小但完整的 repository fixture，保留真實 dataset/reference manifest。 */
function createRepositoryFixture() {
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "laya-phase2-evidence-"));
  fs.mkdirSync(path.join(temporaryRoot, "reports"), { recursive: true });
  fs.mkdirSync(path.join(temporaryRoot, "test-data"), { recursive: true });
  for (const report of supportingReports) {
    fs.writeFileSync(path.join(temporaryRoot, "reports", report), "# fixture\n");
  }
  fs.writeFileSync(path.join(temporaryRoot, "reports", "phase2-gate3-report.md"), "# fixture\n");
  fs.writeFileSync(
    path.join(temporaryRoot, "misclassified-transactions.csv"),
    "id,description_redacted,expected,predicted,confidence,second_choice,margin,language\n" +
      '"fixture-1","line one\nline two",food,food,0.9,other,0.2,en\n'
  );
  for (const name of [
    "transactions-phase2-manifest.json",
    "transactions-phase2.csv",
    "category-label-guideline.md",
    "multilingual-reference-manifest.json",
    "multilingual-parity-fixtures.json"
  ]) {
    const sourceDirectory = name === "multilingual-reference-manifest.json" ? "test-data" : "test-data";
    fs.copyFileSync(
      path.join(repositoryRoot, sourceDirectory, name),
      path.join(temporaryRoot, "test-data", name)
    );
  }
  return temporaryRoot;
}

/** 建立可通過 validator 的 schemaVersion=2 run 與 metrics。 */
function createValidRun(temporaryRoot) {
  const runId = "fixture-run";
  const runDirectory = path.join(temporaryRoot, "reports", "phase2-runs", runId);
  fs.mkdirSync(runDirectory, { recursive: true });
  const datasetManifest = JSON.parse(fs.readFileSync(
    path.join(temporaryRoot, "test-data", "transactions-phase2-manifest.json"),
    "utf8"
  ));
  const selectedIds = Object.values(datasetManifest.groups)
    .filter((group) => group.split === "development")
    .flatMap((group) => group.ids);
  const datasetManifestHash = hashFile(
    path.join(temporaryRoot, "test-data", "transactions-phase2-manifest.json")
  );
  const datasetHash = datasetManifest.dataset.sha256;
  const guidelineHash = datasetManifest.guideline.sha256;
  const manifest = {
    schemaVersion: 2,
    runId,
    profile: "multilingual",
    modelRevision: "revision",
    datasetHash,
    guidelineHash,
    datasetManifestHash,
    split: "development",
    selectedIdsHash: hashText(JSON.stringify(selectedIds)),
    promptVariant: "A",
    promptHash: "c".repeat(64),
    optionOrderHash: "d".repeat(64),
    serializationVersion: "v1",
    serializationHash: "e".repeat(64),
    policyHash: "a".repeat(64),
    bundleManifestSha256: "a".repeat(64),
    referenceManifestSha256: "b".repeat(64),
    selectedIds,
    fixtureHash: hashFile(path.join(temporaryRoot, "test-data", "multilingual-parity-fixtures.json")),
    sourceCommit: "source-commit",
    sourceDirty: false,
    sourceDigest: "source-digest",
    environmentFingerprint: "environment",
    threadCount: 1,
    createdUtc: "2026-09-23T00:00:00Z",
    command: "fixture"
  };
  const predictions = selectedIds.map((id) => ({ Id: id, ExpectedLabel: "food", PredictedLabel: "food" }));
  const results = {
    schemaVersion: 2,
    inputCount: selectedIds.length,
    complete: true,
    manifest,
    predictions,
    failures: []
  };
  const metrics = {
    metrics: {
      InputCount: selectedIds.length,
      SuccessCount: selectedIds.length,
      FailureCount: 0,
      CorrectCount: selectedIds.length,
      SuccessAccuracy: 1,
      FullInputAccuracy: 1,
      Coverage: 1
    }
  };
  writeJson(path.join(runDirectory, "manifest.json"), manifest);
  writeJson(path.join(runDirectory, "results.json"), results);
  writeJson(path.join(runDirectory, "metrics.json"), metrics);
  fs.writeFileSync(path.join(runDirectory, "summary.md"), "# fixture run\n");
  writeArtifactIndex(runDirectory, runId);
  return runDirectory;
}

/** 建立含 candidate 與 held-out run 的完整 evidence fixture。 */
function createValidHeldOutRun(temporaryRoot) {
  const developmentDirectory = createValidRun(temporaryRoot);
  const developmentManifestPath = path.join(developmentDirectory, "manifest.json");
  const developmentResultsPath = path.join(developmentDirectory, "results.json");
  const developmentManifest = JSON.parse(fs.readFileSync(developmentManifestPath, "utf8"));
  const policy = {
    name: "Balanced",
    autoEnabled: true,
    autoProbabilityThreshold: 0.95,
    autoMarginThreshold: 0.2,
    suggestProbabilityThreshold: 0.6,
    suggestMarginThreshold: 0.1
  };
  developmentManifest.policyHash = hashText([
    policy.name,
    "true",
    policy.autoProbabilityThreshold,
    policy.autoMarginThreshold,
    policy.suggestProbabilityThreshold,
    policy.suggestMarginThreshold
  ].join("|"));
  const developmentResults = JSON.parse(fs.readFileSync(developmentResultsPath, "utf8"));
  developmentResults.manifest = developmentManifest;
  writeJson(developmentManifestPath, developmentManifest);
  writeJson(developmentResultsPath, developmentResults);
  writeArtifactIndex(developmentDirectory, developmentManifest.runId);
  const datasetManifest = JSON.parse(fs.readFileSync(
    path.join(temporaryRoot, "test-data", "transactions-phase2-manifest.json"),
    "utf8"
  ));
  const heldOutSelectedIds = Object.values(datasetManifest.groups)
    .filter((group) => group.split === "held-out")
    .flatMap((group) => group.ids);
  const candidate = {
    schemaVersion: 1,
    kind: "laya.phase2.frozen-candidate",
    frozenAtUtc: "2026-09-24T00:00:00Z",
    developmentRun: {
      runId: developmentManifest.runId,
      manifestPath: "reports/phase2-runs/fixture-run/manifest.json",
      manifestSha256: hashFile(developmentManifestPath),
      resultsPath: "reports/phase2-runs/fixture-run/results.json",
      resultsSha256: hashFile(developmentResultsPath)
    },
    model: {
      profile: developmentManifest.profile,
      checkpointRevision: developmentManifest.modelRevision,
      bundleManifestSha256: developmentManifest.bundleManifestSha256
    },
    reference: {
      manifestPath: "test-data/multilingual-reference-manifest.json",
      manifestSha256: developmentManifest.referenceManifestSha256
    },
    dataset: {
      manifestSha256: developmentManifest.datasetManifestHash,
      datasetSha256: developmentManifest.datasetHash,
      guidelineSha256: developmentManifest.guidelineHash,
      sourceSplit: "development",
      targetSplit: "held-out",
      developmentSelectedIdsHash: developmentManifest.selectedIdsHash,
      heldOutSelectedIdsHash: hashText(JSON.stringify(heldOutSelectedIds))
    },
    request: {
      promptVariant: developmentManifest.promptVariant,
      promptHash: developmentManifest.promptHash,
      optionOrderHash: developmentManifest.optionOrderHash,
      serializationVersion: developmentManifest.serializationVersion,
      serializationHash: developmentManifest.serializationHash
    },
    policy,
    policyHash: developmentManifest.policyHash
  };
  const candidatePath = path.join(temporaryRoot, "frozen-candidate.json");
  writeJson(candidatePath, candidate);

  const runId = "heldout-run";
  const runDirectory = path.join(temporaryRoot, "reports", "phase2-runs", runId);
  fs.mkdirSync(runDirectory, { recursive: true });
  const manifest = {
    ...developmentManifest,
    runId,
    split: "held-out",
    selectedIdsHash: hashText(JSON.stringify(heldOutSelectedIds)),
    selectedIds: heldOutSelectedIds,
    frozenCandidateManifestSha256: hashFile(candidatePath),
    frozenCandidatePath: "frozen-candidate.json",
    createdUtc: "2026-09-24T00:30:00Z",
    command: "fixture held-out"
  };
  const predictions = heldOutSelectedIds.map((id) => ({ Id: id, ExpectedLabel: "food", PredictedLabel: "food" }));
  const results = {
    schemaVersion: 2,
    inputCount: heldOutSelectedIds.length,
    complete: true,
    manifest,
    predictions,
    failures: []
  };
  const metrics = {
    metrics: {
      InputCount: heldOutSelectedIds.length,
      SuccessCount: heldOutSelectedIds.length,
      FailureCount: 0,
      CorrectCount: heldOutSelectedIds.length,
      SuccessAccuracy: 1,
      FullInputAccuracy: 1,
      Coverage: 1
    }
  };
  writeJson(path.join(runDirectory, "manifest.json"), manifest);
  writeJson(path.join(runDirectory, "results.json"), results);
  writeJson(path.join(runDirectory, "metrics.json"), metrics);
  fs.writeFileSync(path.join(runDirectory, "summary.md"), "# held-out fixture\n");
  writeArtifactIndex(runDirectory, runId);
  return { candidatePath, runDirectory };
}

/** 執行 evidence validator 並解析其 machine-readable 輸出。 */
function runValidator(temporaryRoot, argumentsList = []) {
  const result = spawnSync(process.execPath, [validatorPath, temporaryRoot, ...argumentsList], { encoding: "utf8" });
  assert.equal(result.error, undefined, result.error?.message);
  return { exitCode: result.status, output: JSON.parse(result.stdout) };
}

/** 移除測試 fixture，避免驗證案例在主工作樹留下暫存檔。 */
function removeFixture(temporaryRoot) {
  fs.rmSync(temporaryRoot, { recursive: true, force: true });
}

// 驗證 quoted multiline CSV 與完整 v2 metrics 可以通過。
test("accepts recomputable v2 evidence and RFC 4180 CSV", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    createValidRun(temporaryRoot);
    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 0, JSON.stringify(result.output));
    assert.equal(result.output.status, "complete");
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 metrics 被竄改時，即使 artifact index 同步更新也不能通過。
test("rejects metrics that cannot be recomputed", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    const metrics = JSON.parse(fs.readFileSync(path.join(runDirectory, "metrics.json"), "utf8"));
    metrics.metrics.CorrectCount = 0;
    writeJson(path.join(runDirectory, "metrics.json"), metrics);
    writeArtifactIndex(runDirectory, "fixture-run");
    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 1);
    assert.match(result.output.errors.join("\n"), /correctCount does not match/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 raw results 被竄改但 index 未更新時會被 hash audit 拒絕。
test("rejects tampered results artifact", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    const results = JSON.parse(fs.readFileSync(path.join(runDirectory, "results.json"), "utf8"));
    results.predictions[0].PredictedLabel = "other";
    writeJson(path.join(runDirectory, "results.json"), results);
    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 1);
    assert.match(result.output.errors.join("\n"), /artifact hash mismatch for results\.json/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證缺少 metrics artifact 時會回報 failed，而不是借用其他 run。
test("rejects missing metrics artifact", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    fs.unlinkSync(path.join(runDirectory, "metrics.json"));
    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 1);
    assert.match(result.output.errors.join("\n"), /missing file: reports\/phase2-runs\/fixture-run\/metrics\.json/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 legacy run 只會被標記 blocked，不會填補本次 v2 evidence。
test("blocks legacy run instead of accepting it", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    const manifest = JSON.parse(fs.readFileSync(path.join(runDirectory, "manifest.json"), "utf8"));
    manifest.schemaVersion = 1;
    writeJson(path.join(runDirectory, "manifest.json"), manifest);
    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 2);
    assert.match(result.output.blocked.join("\n"), /legacy or not canonical/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 full evidence scope 沒有 held-out frozen run 時保持 blocked。
test("blocks full evidence without frozen held-out run", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    createValidRun(temporaryRoot);
    const result = runValidator(temporaryRoot, ["--require-held-out"]);
    assert.equal(result.exitCode, 2);
    assert.match(result.output.blocked.join("\n"), /no complete held-out run/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 held-out 宣稱的 candidate 必須是可定位且 hash 綁定的 artifact。
test("rejects held-out evidence without an auditable candidate artifact", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    const manifestPath = path.join(runDirectory, "manifest.json");
    const resultsPath = path.join(runDirectory, "results.json");
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    manifest.split = "held-out";
    manifest.policyHash = "a".repeat(64);
    manifest.frozenCandidateManifestSha256 = "b".repeat(64);
    const results = JSON.parse(fs.readFileSync(resultsPath, "utf8"));
    results.manifest = manifest;
    writeJson(manifestPath, manifest);
    writeJson(resultsPath, results);
    writeArtifactIndex(runDirectory, "fixture-run");

    const result = runValidator(temporaryRoot, ["--require-held-out"]);
    assert.equal(result.exitCode, 1);
    assert.match(result.output.errors.join("\n"), /candidate artifact|held-out selected IDs/i);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 run 的 fixtureHash 必須是實際 multilingual parity fixture hash。
test("rejects a phase2 run with an unrelated fixture hash", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const runDirectory = createValidRun(temporaryRoot);
    const manifestPath = path.join(runDirectory, "manifest.json");
    const resultsPath = path.join(runDirectory, "results.json");
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    manifest.fixtureHash = "c".repeat(64);
    const results = JSON.parse(fs.readFileSync(resultsPath, "utf8"));
    results.manifest = manifest;
    writeJson(manifestPath, manifest);
    writeJson(resultsPath, results);
    writeArtifactIndex(runDirectory, "fixture-run");

    const result = runValidator(temporaryRoot);
    assert.equal(result.exitCode, 1);
    assert.match(result.output.errors.join("\n"), /fixtureHash/);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證完整 held-out candidate 的路徑、hash、policy 與 dataset provenance 可以通過。
test("accepts an auditable held-out candidate", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    createValidHeldOutRun(temporaryRoot);
    const result = runValidator(temporaryRoot, ["--require-held-out"]);
    assert.equal(result.exitCode, 0, JSON.stringify(result.output));
    assert.equal(result.output.status, "complete");
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 candidate policy threshold 被竄改時，即使外層 hash 同步也不能通過 evidence audit。
test("rejects held-out candidate with a policy hash mismatch", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const { candidatePath, runDirectory } = createValidHeldOutRun(temporaryRoot);
    const candidate = JSON.parse(fs.readFileSync(candidatePath, "utf8"));
    candidate.policy.autoProbabilityThreshold = 0.01;
    writeJson(candidatePath, candidate);
    const manifestPath = path.join(runDirectory, "manifest.json");
    const resultsPath = path.join(runDirectory, "results.json");
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    manifest.frozenCandidateManifestSha256 = hashFile(candidatePath);
    const results = JSON.parse(fs.readFileSync(resultsPath, "utf8"));
    results.manifest = manifest;
    writeJson(manifestPath, manifest);
    writeJson(resultsPath, results);
    writeArtifactIndex(runDirectory, "heldout-run");

    const result = runValidator(temporaryRoot, ["--require-held-out"]);
    assert.equal(result.exitCode, 1, JSON.stringify(result.output));
    assert.match(result.output.errors.join("\n"), /policy hash/i);
  } finally {
    removeFixture(temporaryRoot);
  }
});

// 驗證 candidate request contract 被竄改時不能與 held-out run 的參數脫鉤。
test("rejects held-out candidate with a request identity mismatch", () => {
  const temporaryRoot = createRepositoryFixture();
  try {
    const { candidatePath, runDirectory } = createValidHeldOutRun(temporaryRoot);
    const candidate = JSON.parse(fs.readFileSync(candidatePath, "utf8"));
    candidate.request.promptHash = "d".repeat(64);
    writeJson(candidatePath, candidate);
    const manifestPath = path.join(runDirectory, "manifest.json");
    const resultsPath = path.join(runDirectory, "results.json");
    const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
    manifest.frozenCandidateManifestSha256 = hashFile(candidatePath);
    const results = JSON.parse(fs.readFileSync(resultsPath, "utf8"));
    results.manifest = manifest;
    writeJson(manifestPath, manifest);
    writeJson(resultsPath, results);
    writeArtifactIndex(runDirectory, "heldout-run");

    const result = runValidator(temporaryRoot, ["--require-held-out"]);
    assert.equal(result.exitCode, 1, JSON.stringify(result.output));
    assert.match(result.output.errors.join("\n"), /request identity/i);
  } finally {
    removeFixture(temporaryRoot);
  }
});
