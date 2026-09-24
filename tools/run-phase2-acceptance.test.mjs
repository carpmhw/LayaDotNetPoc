import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const acceptanceScript = path.join(repositoryRoot, "tools", "run-phase2-acceptance.sh");
const acceptanceDirectory = path.join(repositoryRoot, "reports", "phase2-acceptance");

/** 讀取工作樹內所有 acceptance record，供 orchestration contract 檢查。 */
function readAcceptanceRecords() {
  return fs.readdirSync(acceptanceDirectory, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => path.join(acceptanceDirectory, entry.name, "acceptance.json"))
    .filter((filePath) => fs.existsSync(filePath))
    .map((filePath) => JSON.parse(fs.readFileSync(filePath, "utf8")));
}

/** 驗證每個 stage 的對外狀態與 exit code 保持明確映射。 */
function assertStageExitMapping(records) {
  for (const record of records) {
    for (const stage of record.stages ?? []) {
      if (stage.status === "complete" || stage.status === "reused") {
        assert.equal(stage.exitCode, 0, `${record.runId}/${stage.name}`);
      } else if (stage.status === "blocked") {
        assert.equal(stage.exitCode, 2, `${record.runId}/${stage.name}`);
      } else if (stage.status === "failed") {
        assert.notEqual(stage.exitCode, 0, `${record.runId}/${stage.name}`);
      }
      if (stage.artifactPaths !== undefined) {
        assert.ok(Array.isArray(stage.artifactPaths), `${record.runId}/${stage.name} artifactPaths`);
      }
    }
  }
}

/** 驗證未知 acceptance scope 會以參數錯誤退出，不執行相依 stages。 */
test("rejects an unknown acceptance scope", () => {
  const result = spawnSync(acceptanceScript, ["--until", "unsupported"], {
    cwd: repositoryRoot,
    encoding: "utf8"
  });
  assert.equal(result.status, 3);
  assert.match(result.stderr, /unsupported acceptance scope/);
});

// 驗證 readiness 成功與 full blocked 可以同時存在，不能互相填補。
test("keeps readiness separate from incomplete full acceptance", () => {
  const records = readAcceptanceRecords();
  assertStageExitMapping(records);
  assert.ok(records.some((record) => record.scope === "readiness" && record.status === "complete"));
  assert.ok(records.some((record) => record.scope === "full" && record.status === "blocked" && record.exitCode === 2));
  assert.ok(records.some((record) => record.stages.some((stage) => Array.isArray(stage.artifactPaths))));
});

// 驗證 blocked/failed stage 後不會繼續產生 complete 的相依 stage。
test("stops dependent stages after blocked or failed stage", () => {
  const records = readAcceptanceRecords();
  for (const record of records) {
    const stages = record.stages ?? [];
    const terminalIndex = stages.findIndex((stage) => stage.status === "blocked" || stage.status === "failed");
    if (terminalIndex < 0) continue;
    assert.ok(
      stages.slice(terminalIndex + 1).every((stage) => stage.status !== "complete" && stage.status !== "reused"),
      `${record.runId} continued after terminal stage`
    );
  }
});

// 驗證 acceptance reuse 會涵蓋 source、fixture、bundle 與 final evidence 依賴。
test("fingerprints all readiness inputs and does not reuse final inventory", () => {
  const script = fs.readFileSync(acceptanceScript, "utf8");
  for (const dependency of [
    "test-data/multilingual-parity-fixtures.json",
    "test-data/multilingual-tokenizer-fixtures.json",
    "test-data/category-label-guideline.md",
    "LayaDotNetPoc.sln",
    "src/**/*.cs",
    "tests/**/*.cs",
    "models/laya-multilingual/current-bundle.json"
  ]) {
    assert.match(script, new RegExp(dependency.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")), dependency);
  }
  assert.match(script, /full_final_inventory[\s\S]*return 1/);
});

// 驗證 acceptance top-level 狀態不會把 evidence failed 誤報成 blocked。
test("preserves failed and blocked exit status at acceptance level", () => {
  const script = fs.readFileSync(acceptanceScript, "utf8");
  assert.match(script, /full_final_inventory[\s\S]*case[\s\S]*1\)/);
  assert.match(script, /full_final_inventory[\s\S]*case[\s\S]*2\)/);
});
