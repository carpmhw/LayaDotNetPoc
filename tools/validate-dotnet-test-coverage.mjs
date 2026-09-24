#!/usr/bin/env node

import fs from "node:fs";

/** 解析命令列參數並要求 category、list 與 trx 三份輸入。 */
function parseArguments(argumentsList) {
  const values = new Map();
  for (let index = 0; index < argumentsList.length; index += 1) {
    const argument = argumentsList[index];
    if (!argument.startsWith("--") || index === argumentsList.length - 1) {
      throw new Error(`invalid argument: ${argument}`);
    }

    values.set(argument, argumentsList[index + 1]);
    index += 1;
  }

  for (const required of ["--category", "--list", "--trx"]) {
    if (!values.has(required)) {
      throw new Error(`missing required argument: ${required}`);
    }
  }

  return values;
}

/** 從 dotnet test list-tests 輸出取得唯一 test names，零匹配視為未完成。 */
function readExpectedTests(listPath) {
  const lines = fs.readFileSync(listPath, "utf8").split(/\r?\n/u);
  return [...new Set(lines.map((line) => line.trim()).filter((line) => line.startsWith("Laya.Core.Tests.")))];
}

/** 解析 TRX UnitTestResult 的 outcome，兼容欄位順序及 XML namespace。 */
function readResults(trxPath) {
  const content = fs.readFileSync(trxPath, "utf8");
  const results = [];
  for (const match of content.matchAll(/<[^>]*UnitTestResult\b[^>]*>/gu)) {
    const tag = match[0];
    const outcome = tag.match(/\boutcome="([^"]+)"/u)?.[1] ?? "Unknown";
    results.push(outcome);
  }

  return results;
}

/** 將 TRX 結果轉為 readiness 所需的 expected/executed/pass/skip 計數。 */
function summarize(expectedTests, outcomes, category) {
  const skippedOutcomes = new Set(["Skipped", "NotExecuted", "NotRunnable"]);
  const skippedCount = outcomes.filter((outcome) => skippedOutcomes.has(outcome)).length;
  const passedCount = outcomes.filter((outcome) => outcome === "Passed").length;
  const failedCount = outcomes.length - passedCount - skippedCount;
  const report = {
    schemaVersion: 1,
    category,
    expectedCount: expectedTests.length,
    executedCount: outcomes.length,
    passedCount,
    failedCount,
    skippedCount,
    expectedTests,
    status: expectedTests.length > 0 &&
      outcomes.length === expectedTests.length &&
      passedCount === expectedTests.length &&
      skippedCount === 0 &&
      failedCount === 0 ? "complete" : "failed"
  };
  return report;
}

/** 輸出 machine-readable coverage 並以非零狀態阻擋零匹配或 skip。 */
function main() {
  try {
    const values = parseArguments(process.argv.slice(2));
    const report = summarize(
      readExpectedTests(values.get("--list")),
      readResults(values.get("--trx")),
      values.get("--category"));
    process.stdout.write(`${JSON.stringify(report, null, 2)}\n`);
    process.exitCode = report.status === "complete" ? 0 : 1;
  } catch (error) {
    process.stderr.write(`coverage validation failed: ${error.message}\n`);
    process.exitCode = 1;
  }
}

main();
