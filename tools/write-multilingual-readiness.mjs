#!/usr/bin/env node

import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

/** 計算 repository-relative artifact 的 SHA-256。 */
function hashFile(root, relativePath) {
  const absolutePath = path.resolve(root, relativePath);
  return crypto.createHash("sha256").update(fs.readFileSync(absolutePath)).digest("hex");
}

/** 讀取必要 JSON，格式或檔案錯誤會回傳 null 供 readiness blocked。 */
function readJson(root, relativePath) {
  try {
    return JSON.parse(fs.readFileSync(path.join(root, relativePath), "utf8"));
  } catch {
    return null;
  }
}

/** 收集 category coverage 報告並要求每一類都有 complete status。 */
function readCoverage(stageDirectory) {
  if (!fs.existsSync(stageDirectory)) return [];
  return fs.readdirSync(stageDirectory)
    .filter((name) => name.endsWith(".json"))
    .sort()
    .map((name) => readJson(stageDirectory, name))
    .filter((report) => report !== null);
}

/** 將存在的 readiness artifact 轉成可追溯的 path/hash 記錄。 */
function describeArtifacts(root, relativePaths) {
  return relativePaths.map((relativePath) => {
    const absolutePath = path.resolve(root, relativePath);
    return fs.existsSync(absolutePath)
      ? { path: relativePath, sha256: hashFile(root, relativePath) }
      : { path: relativePath, status: "missing" };
  });
}

/** 收集已保存的 development A run，並保留 profile、split、分母與 artifact hashes。 */
function readDevelopmentRuns(root) {
  const directory = path.join(root, "reports", "phase2-runs");
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => {
      const relativeDirectory = path.join("reports", "phase2-runs", entry.name);
      const manifestPath = path.join(relativeDirectory, "manifest.json");
      const resultsPath = path.join(relativeDirectory, "results.json");
      const indexPath = path.join(relativeDirectory, "artifact-index.json");
      const manifest = readJson(root, manifestPath);
      const results = readJson(root, resultsPath);
      if (manifest?.split !== "development" || manifest?.promptVariant !== "A") return null;
      return {
        runId: manifest.runId,
        profile: manifest.profile,
        split: manifest.split,
        promptVariant: manifest.promptVariant,
        selectedIdsCount: Array.isArray(manifest.selectedIds) ? manifest.selectedIds.length : 0,
        inputCount: results?.inputCount ?? null,
        complete: results?.complete === true,
        artifacts: describeArtifacts(root, [manifestPath, resultsPath, indexPath])
      };
    })
    .filter((run) => run !== null)
    .sort((left, right) => left.runId.localeCompare(right.runId));
}

/** 建立不依賴自身 hash 的 machine-readable readiness snapshot。 */
function buildReport(root, coverage) {
  const reference = readJson(root, "test-data/multilingual-reference-manifest.json");
  const python = readJson(root, "reports/multilingual-export-validation.json");
  const dotnet = readJson(root, "reports/multilingual-dotnet-parity.json");
  const pointer = readJson(root, "models/laya-multilingual/current-bundle.json");
  const stages = {
    reference: reference?.referenceStatus === "complete",
    bundle: reference?.runtimeBundle?.status === "verified" && pointer?.status === "verified",
    pythonExport: python?.status === "complete" && python?.coverage?.fixtureCount === 20 &&
      python?.coverage?.executedCount === 20 && (python?.failedFixtures?.length ?? 0) === 0,
    dotnetParity: dotnet?.status === "complete" && dotnet?.expectedCount === 20 &&
      dotnet?.executedCount === 20 && dotnet?.passedCount === 20 && dotnet?.skippedCount === 0,
    testCategories: coverage.length > 0 && coverage.every((report) => report.status === "complete")
  };
  const status = Object.values(stages).every(Boolean) ? "ready-for-phase2" : "blocked";
  const artifacts = {};
  for (const relativePath of [
    "test-data/multilingual-reference-manifest.json",
    "reports/multilingual-export-validation.json",
    "reports/multilingual-dotnet-parity.json",
      "models/laya-multilingual/current-bundle.json"
  ]) {
    if (fs.existsSync(path.join(root, relativePath))) artifacts[relativePath] = hashFile(root, relativePath);
  }
  const evidence = {
    reference: describeArtifacts(root, [
      "test-data/multilingual-reference-manifest.json",
      "reports/multilingual-reference-validation.md",
      "reports/multilingual-reference-probe.json"
    ]),
    bundle: describeArtifacts(root, [
      "models/laya-multilingual/current-bundle.json",
      pointer?.root
        ? path.join(pointer.root, "laya-bundle-manifest.json")
        : "models/laya-multilingual/current-bundle.json"
    ]),
    pythonExport: describeArtifacts(root, [
      "reports/multilingual-export-validation.json",
      "reports/multilingual-export-validation.md",
      "test-data/multilingual-parity-fixtures.json"
    ]),
    dotnetParity: describeArtifacts(root, [
      "reports/multilingual-dotnet-parity.json",
      "test-data/multilingual-tokenizer-fixtures.json"
    ]),
    testCategories: coverage.map((report) => ({
      category: report.category,
      status: report.status,
      expectedCount: report.expectedCount,
      executedCount: report.executedCount,
      passedCount: report.passedCount,
      skippedCount: report.skippedCount
    }))
  };
  return {
    schemaVersion: 1,
    status,
    scope: "engineering-readiness",
    stages,
    testCategories: coverage,
    artifacts,
    evidence,
    developmentA: {
      status: "handoff-only",
      runs: readDevelopmentRuns(root),
      note: "Development A artifacts are preserved for the original Phase 2 change; this report does not select policy or evaluate held-out data."
    },
    phase2TaskMapping: [
      {
        tasks: ["1.3", "2.5", "3.1", "3.2", "3.3", "3.4", "4.1", "4.2", "4.3", "4.4", "4.5"],
        status: "readiness-evidence",
        evidence: ["reference", "bundle", "pythonExport", "dotnetParity", "testCategories"]
      },
      {
        tasks: ["7.1", "7.2"],
        status: "development-A-handoff",
        evidence: ["developmentA"],
        note: "The original change owns quality interpretation and final baseline reports."
      },
      {
        tasks: ["7.3", "8.6", "9.1", "9.3", "10.1", "10.2", "10.3", "10.4", "11.x", "12.x", "13.6"],
        status: "owned-by-phase2",
        evidence: [],
        note: "Quality, policy, held-out, benchmark, deployment, and Gate 3 evidence remain outside this readiness handoff."
      }
    ],
    blockedReason: status === "blocked"
      ? "One or more fixed reference, bundle, parity, or required test category checks are incomplete."
      : null
  };
}

/** 寫入 readiness JSON 與 Markdown，明確不宣稱 Phase 2 品質或 Gate 3。 */
function main() {
  const root = path.resolve(process.argv[2] ?? process.cwd());
  const stageDirectory = path.resolve(process.argv[3] ?? path.join(root, "reports", "phase2-acceptance", "test-coverage"));
  const outputPath = path.join(root, "reports", "multilingual-readiness.json");
  const markdownPath = path.join(root, "reports", "multilingual-readiness.md");
  const report = buildReport(root, readCoverage(stageDirectory));
  fs.writeFileSync(outputPath, `${JSON.stringify(report, null, 2)}\n`);
  const stageLines = Object.entries(report.stages)
    .map(([name, complete]) => `- ${name}: ${complete ? "complete" : "blocked"}`)
    .join("\n");
  const evidenceLines = Object.entries(report.evidence)
    .map(([name, artifacts]) => {
      if (name === "testCategories") {
        return `- ${name}: ${artifacts.map((artifact) => `${artifact.category} expected=${artifact.expectedCount} executed=${artifact.executedCount} passed=${artifact.passedCount} skipped=${artifact.skippedCount}`).join(", ")}`;
      }
      return `- ${name}: ${artifacts.map((artifact) => `${artifact.path} (${artifact.sha256 ?? artifact.status})`).join(", ")}`;
    })
    .join("\n");
  const developmentLines = report.developmentA.runs.length === 0
    ? "- No development A run artifacts were present."
    : report.developmentA.runs
      .map((run) => `- ${run.profile}: ${run.runId}; split=${run.split}; selected=${run.selectedIdsCount}; input=${run.inputCount}; complete=${run.complete}`)
      .join("\n");
  const taskLines = report.phase2TaskMapping
    .map((mapping) => `- ${mapping.tasks.join(", ")}: ${mapping.status}`)
    .join("\n");
  fs.writeFileSync(markdownPath,
    `# Multilingual Readiness\n\n` +
    `Status: **${report.status}**\n\n` +
    `This report covers engineering readiness only. It does not certify Phase 2 quality, held-out selection, benchmark, deployment, or Gate 3.\n\n` +
    `## Stages\n\n${stageLines}\n\n` +
    `## Evidence\n\n${evidenceLines}\n\n` +
    `## Development A Handoff\n\n${developmentLines}\n\n` +
    `## Original Phase 2 Task Mapping\n\n${taskLines}\n`);
  process.stdout.write(`${JSON.stringify({ status: report.status, outputPath, markdownPath }, null, 2)}\n`);
  process.exitCode = report.status === "ready-for-phase2" ? 0 : 2;
}

main();
