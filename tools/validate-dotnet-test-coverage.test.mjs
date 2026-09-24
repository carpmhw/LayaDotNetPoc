import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const validatorPath = path.join(repositoryRoot, "tools", "validate-dotnet-test-coverage.mjs");

/** 建立 list/TRX fixture 並執行 coverage validator。 */
function runCoverage(expectedTests, outcomes) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "laya-coverage-"));
  try {
    const listPath = path.join(directory, "list.log");
    const trxPath = path.join(directory, "results.trx");
    fs.writeFileSync(listPath, `${expectedTests.join("\n")}\n`);
    fs.writeFileSync(
      trxPath,
      `<TestRun>${outcomes.map((outcome) => `<UnitTestResult outcome="${outcome}" />`).join("")}</TestRun>`
    );
    const result = spawnSync(process.execPath, [
      validatorPath,
      "--category", "Synthetic",
      "--list", listPath,
      "--trx", trxPath
    ], { encoding: "utf8" });
    return { exitCode: result.status, report: JSON.parse(result.stdout) };
  } finally {
    fs.rmSync(directory, { recursive: true, force: true });
  }
}

// 驗證完整 expected/executed/pass 集合可以通過。
test("accepts complete category coverage", () => {
  const result = runCoverage(
    ["Laya.Core.Tests.Synthetic.First", "Laya.Core.Tests.Synthetic.Second"],
    ["Passed", "Passed"]
  );
  assert.equal(result.exitCode, 0);
  assert.equal(result.report.status, "complete");
});

// 驗證零匹配即使沒有 process-level test failure 仍不能通過。
test("rejects zero expected tests", () => {
  const result = runCoverage([], []);
  assert.equal(result.exitCode, 1);
  assert.equal(result.report.status, "failed");
  assert.equal(result.report.expectedCount, 0);
});

// 驗證 skipped outcome 不能被視為通過。
test("rejects skipped tests", () => {
  const result = runCoverage(["Laya.Core.Tests.Synthetic.First"], ["Skipped"]);
  assert.equal(result.exitCode, 1);
  assert.equal(result.report.status, "failed");
  assert.equal(result.report.skippedCount, 1);
});
