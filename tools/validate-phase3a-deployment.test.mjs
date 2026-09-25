import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { chmodSync, existsSync, mkdirSync, mkdtempSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

/** 建立可控 docker CLI stub，用來驗證 host wrapper 的 byte limits 與外部 outcome 保存。 */
function createFakeDocker(stubRoot) {
  const stateRoot = path.join(stubRoot, 'docker-state');
  const logsPath = path.join(stubRoot, 'docker-calls.ndjson');
  const executable = path.join(stubRoot, 'bin', 'docker');
  const implementation = path.join(stubRoot, 'fake-docker.mjs');
  mkdirSync(path.dirname(executable), { recursive: true });
  writeFileSync(implementation, `
import fs from 'node:fs';
import path from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
const args = process.argv.slice(2);
const stateRoot = process.env.FAKE_DOCKER_STATE;
const logPath = process.env.FAKE_DOCKER_LOG;
const image = process.env.FAKE_DOCKER_IMAGE;
fs.mkdirSync(stateRoot, { recursive: true });
fs.appendFileSync(logPath, JSON.stringify(args) + "\\n");
function statePath(name) { return path.join(stateRoot, name + ".json"); }
function readState(name) { return JSON.parse(fs.readFileSync(statePath(name), "utf8")); }
function writeState(name, state) { fs.writeFileSync(statePath(name), JSON.stringify(state)); }
const command = args[0];
if (command === "info") process.exit(0);
if (command === "version") { process.stdout.write("29.8.1\\n"); process.exit(0); }
if (command === "build" || command === "stats") {
  if (command === "stats") process.stdout.write(JSON.stringify({ Container: "fake", MemUsage: "128MiB / 2GB", MemPerc: "6.25%" }) + "\\n");
  process.exit(0);
}
if (command === "image" && args[1] === "inspect") process.exit(0);
if (command === "container" && args[1] === "inspect") process.exit(fs.existsSync(statePath(args[2])) ? 0 : 1);
if (command === "inspect") {
  const name = args[args.length - 1];
  const state = readState(name);
  const format = args[args.indexOf("--format") + 1];
  if (format.includes(".State.Running")) process.stdout.write(String(state.running) + "\\n");
  else if (format.includes(".State.Pid")) process.stdout.write(String(state.pid) + "\\n");
  else if (format.includes(".HostConfig.MemorySwap")) process.stdout.write(String(state.memorySwap) + "\\n");
  else if (format.includes(".HostConfig.Memory")) process.stdout.write(String(state.memory) + "\\n");
  else if (format.includes(".State.OOMKilled")) process.stdout.write(String(state.oomKilled) + "\\n");
  else if (format.includes(".State.ExitCode")) process.stdout.write(String(state.exitCode) + "\\n");
  else process.stdout.write(JSON.stringify(state) + "\\n");
  process.exit(0);
}
if (command === "exec") {
  process.exit(0);
}
if (command === "rm") {
  fs.rmSync(statePath(args[1]), { force: true });
  process.exit(0);
}
if (command === "run") {
  const name = args[args.indexOf("--name") + 1];
  const memory = Number(args[args.indexOf("--memory") + 1]);
  const memorySwap = Number(args[args.indexOf("--memory-swap") + 1]);
  const imageIndex = args.indexOf(image);
  const appArgs = args.slice(imageIndex + 1);
  const runId = appArgs[appArgs.indexOf("--run-id") + 1];
  const requestsIndex = appArgs.indexOf("--requests");
  const requests = requestsIndex < 0 ? 0 : Number(appArgs[requestsIndex + 1]);
  const warmupIndex = appArgs.indexOf("--warmup");
  const warmup = warmupIndex < 0 ? 0 : Number(appArgs[warmupIndex + 1]);
  const isOom = process.env.FAKE_DOCKER_OOM_LIMIT === String(memory);
  const isCrash = !isOom && process.env.FAKE_DOCKER_CRASH_LIMIT === String(memory);
  const reportMount = args.find(value => value.includes("target=/reports"));
  const mountSource = reportMount.split(",").find(value => value.startsWith("source=")).slice("source=".length);
  const runDirectory = path.join(mountSource, "runs", runId);
  fs.mkdirSync(runDirectory, { recursive: true });
  fs.writeFileSync(path.join(runDirectory, "manifest.json"), JSON.stringify({
    schemaVersion: 1,
    runId,
    status: isOom ? "incomplete" : "complete",
    environment: {
      memoryLimitBytes: Math.floor(memory / 4096) * 4096,
      memorySwapLimitBytes: 0
    },
    actualCounts: {
      warmupRequests: warmup,
      attemptedRequests: isOom ? 10 : requests,
      completedRequests: isOom ? 10 : requests,
      errors: 0,
      integrityFailures: 0
    }
  }));
  const omitRaw = process.env.FAKE_DOCKER_MISSING_RAW === "true" && memory === 2000000000 && requests === 100;
  if (!omitRaw) {
    fs.writeFileSync(path.join(runDirectory, "phase3a-memory-samples.csv"),
      "sample_index,run_id,scenario,request_count,working_set_bytes,private_memory_bytes\\n0," + runId + ",full-pipeline,0,1024,2048\\n");
    fs.writeFileSync(path.join(runDirectory, "phase3a-memory-latency.csv"),
      "request_index,end_to_end_latency_ms,inference_latency_ms,integrity_failure\\n");
  }
  const state = {
    running: true,
    pid: process.pid,
    memory: process.env.FAKE_DOCKER_MISMATCH_LIMIT === String(memory) ? memory - 4096 : memory,
    memorySwap,
    oomKilled: false,
    exitCode: 0
  };
  writeState(name, state);
  await delay(1100);
  state.running = false;
  state.oomKilled = isOom;
  state.exitCode = isOom ? 137 : isCrash ? 42 : 0;
  writeState(name, state);
  process.exit(state.exitCode);
}
process.exit(1);
`);
  writeFileSync(executable, `#!/bin/sh\nexec "${process.execPath}" "${implementation}" "$@"\n`);
  chmodSync(executable, 0o755);
  return { executable, logsPath, stateRoot };
}

/** 建立 fake verified model mount 與格式正確的 policy header。 */
function createInputs(root) {
  const modelRoot = path.join(root, 'versioned-model');
  mkdirSync(path.join(modelRoot, 'tokenizer'), { recursive: true });
  for (const file of [
    'laya-bundle-manifest.json',
    'laya.onnx',
    'laya.onnx.data',
    'laya_config.json',
    'tokenizer/tokenizer.json',
    'tokenizer/tokenizer_config.json'
  ]) {
    writeFileSync(path.join(modelRoot, file), file.endsWith('.json') ? '{}' : 'synthetic');
  }
  writeFileSync(path.join(modelRoot, 'laya-bundle-manifest.json'), JSON.stringify({
    schemaVersion: 2,
    status: 'verified',
    profile: 'multilingual',
    checkpoint: { revision: '052592a15d198d9ad47da779604259b10b47b7aa' }
  }));
  const policy = path.join(root, 'memory-policy.json');
  writeFileSync(policy, JSON.stringify({ schemaVersion: 1, status: 'frozen', targetMemoryLimitBytes: 3000000000 }));
  return { modelRoot, policy };
}

/** 執行部署 script 並傳入隔離 docker fake 與測試 evidence root。 */
function runDeploymentScript(
  stub,
  inputs,
  outputRoot,
  campaignId,
  oomLimit = '',
  crashLimit = '',
  missingRaw = false,
  mismatchLimit = ''
) {
  const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const scriptPath = path.join(repositoryRoot, 'tools', 'validate-phase3a-deployment.sh');
  const result = spawnSync('bash', [
    scriptPath,
    '--image', process.env.FAKE_DOCKER_IMAGE,
    '--model-root', inputs.modelRoot,
    '--policy', inputs.policy,
    '--output-root', outputRoot,
    '--campaign-id', campaignId
  ], {
    encoding: 'utf8',
    env: {
      ...process.env,
      PATH: `${path.dirname(stub.executable)}:${process.env.PATH}`,
      FAKE_DOCKER_STATE: stub.stateRoot,
      FAKE_DOCKER_LOG: stub.logsPath,
      FAKE_DOCKER_OOM_LIMIT: oomLimit,
      FAKE_DOCKER_CRASH_LIMIT: crashLimit,
      FAKE_DOCKER_MISSING_RAW: String(missingRaw),
      FAKE_DOCKER_MISMATCH_LIMIT: mismatchLimit
    }
  });
  return result;
}

/** 驗證 Docker wrapper 使用精確 bytes limits 並保留可驗證 terminal OOM evidence。 */
test('deployment wrapper captures exact byte limits and low-limit OOM without hiding target pass', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-script-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  const campaignId = 'docker-smoke-20260924';
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const result = runDeploymentScript(stub, inputs, outputRoot, campaignId, '2000000000', '2500000000');
    const resultRoot = path.join(outputRoot, 'docker', campaignId);
    const summary = JSON.parse(readFileSync(path.join(resultRoot, 'phase3a-docker-memory.json'), 'utf8'));
    assert.equal(result.status, 0, `${result.stderr || result.stdout}\n${JSON.stringify(summary)}`);
    assert.equal(summary.memoryLimitsVerified, true);
    assert.equal(summary.targetStable, true);
    assert.equal(summary.targetRunCount, 2);
    assert.equal(summary.targetOomCount, 0);

    const outcomeFiles = readdirSync(resultRoot)
      .flatMap(name => {
        const directory = path.join(resultRoot, name);
        return existsSync(path.join(directory, 'deployment-result.json'))
          ? [path.join(directory, 'deployment-result.json')]
          : [];
      });
    const outcomes = outcomeFiles.map(pathname => JSON.parse(readFileSync(pathname, 'utf8')));
    const lowLimitOom = outcomes.find(item => item.limitBytes === 2000000000 && item.oomKilled);
    assert.ok(lowLimitOom);
    assert.equal(lowLimitOom.actualMemoryLimitBytes, 2000000000);
    assert.equal(lowLimitOom.actualMemorySwapLimitBytes, 2000000000);
    assert.equal(lowLimitOom.effectiveCgroupMemoryLimitBytes, Math.floor(2000000000 / lowLimitOom.pageSizeBytes) * lowLimitOom.pageSizeBytes);
    assert.equal(lowLimitOom.effectiveCgroupSwapLimitBytes, 0);
    assert.ok(existsSync(lowLimitOom.inspectPath));
    assert.ok(existsSync(lowLimitOom.cgroupSamplesPath));
    assert.ok(existsSync(lowLimitOom.stderrPath));
    const lowLimitCrash = outcomes.find(item => item.limitBytes === 2500000000 && item.terminalOutcome === 'crash');
    assert.ok(lowLimitCrash);
    assert.equal(lowLimitCrash.status, 'terminal-failure');
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

/** 驗證缺少 bundle shard 時在啟動 docker 前以 blocked 結束。 */
test('deployment wrapper blocks when verified model mount is incomplete', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-model-blocked-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  rmSync(path.join(inputs.modelRoot, 'laya.onnx.data'));
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const result = runDeploymentScript(stub, inputs, outputRoot, 'docker-model-blocked-20260924');
    assert.equal(result.status, 2);
    assert.match(result.stderr, /verified bundle.*缺少/i);
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

/** 驗證 process exit success 但 raw CSV 缺失時仍須 blocked。 */
test('deployment wrapper blocks successful runs with incomplete raw CSV evidence', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-raw-blocked-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const result = runDeploymentScript(stub, inputs, outputRoot, 'docker-raw-blocked-20260924', '', '', true);
    assert.equal(result.status, 1);
    const summaryPath = path.join(outputRoot, 'docker', 'docker-raw-blocked-20260924', 'phase3a-docker-memory.json');
    const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
    assert.equal(summary.blockedRunCount, 1);
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

/** 驗證 3 GB target 重複 OOM 不會被 4 GB 成功結果覆蓋。 */
test('deployment wrapper preserves repeated target OOM despite higher-limit success', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-target-oom-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const result = runDeploymentScript(stub, inputs, outputRoot, 'docker-target-oom-20260924', '3000000000');
    assert.equal(result.status, 1);
    const summaryPath = path.join(outputRoot, 'docker', 'docker-target-oom-20260924', 'phase3a-docker-memory.json');
    const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
    assert.equal(summary.targetRunCount, 2);
    assert.equal(summary.targetOomCount, 2);
    assert.equal(summary.targetRepeatedOom, true);
    assert.equal(summary.targetStable, false);
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

/** 驗證 inspect limit 與要求的十進位 bytes 不同時 Gate evidence 被 blocked。 */
test('deployment wrapper rejects an effective Docker memory limit mismatch', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-limit-mismatch-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const result = runDeploymentScript(
      stub,
      inputs,
      outputRoot,
      'docker-limit-mismatch-20260924',
      '',
      '',
      false,
      '2000000000');
    assert.equal(result.status, 1);
    const summaryPath = path.join(outputRoot, 'docker', 'docker-limit-mismatch-20260924', 'phase3a-docker-memory.json');
    const resultRoot = path.dirname(summaryPath);
    const diagnostics = {
      status: result.status,
      stdout: result.stdout,
      stderr: result.stderr,
      resultEntries: existsSync(resultRoot) ? readdirSync(resultRoot) : [],
      dockerCalls: existsSync(stub.logsPath) ? readFileSync(stub.logsPath, 'utf8') : ''
    };
    assert.ok(existsSync(summaryPath), `${JSON.stringify(diagnostics)}\nsummary artifact missing`);
    const summary = JSON.parse(readFileSync(summaryPath, 'utf8'));
    assert.equal(summary.memoryLimitsVerified, false);
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});

/** 驗證 daemon unavailable 時以 blocked outcome 退出且不宣稱 deployment pass。 */
test('deployment wrapper blocks when Docker daemon is unavailable', () => {
  const temporaryRoot = mkdtempSync(path.join(os.tmpdir(), 'phase3a-docker-blocked-'));
  const outputRoot = path.join(temporaryRoot, 'reports');
  const stub = createFakeDocker(temporaryRoot);
  const inputs = createInputs(temporaryRoot);
  process.env.FAKE_DOCKER_IMAGE = 'laya-memory-probe:test';

  try {
    const dockerUnavailable = path.join(path.dirname(stub.executable), 'docker');
    writeFileSync(dockerUnavailable, '#!/bin/sh\nexit 1\n');
    chmodSync(dockerUnavailable, 0o755);
    const result = runDeploymentScript(stub, inputs, outputRoot, 'docker-blocked-20260924');
    assert.equal(result.status, 2);
    assert.match(result.stderr, /Docker daemon/i);
  } finally {
    delete process.env.FAKE_DOCKER_IMAGE;
    rmSync(temporaryRoot, { recursive: true, force: true });
  }
});
