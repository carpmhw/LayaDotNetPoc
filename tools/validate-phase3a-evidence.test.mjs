import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import {
  isVerifiedTerminalFailureEvidence,
  validateArtifactIndex,
  validateCampaignPolicy,
  validateGateDecision,
  validatePhase3aEvidence,
  validateReproductionRunIds
} from './validate-phase3a-evidence.mjs';

/** 建立每項測試使用的隔離 evidence root。 */
function createEvidenceRoot() {
  return mkdtempSync(path.join(os.tmpdir(), 'phase3a-evidence-'));
}

/** 計算測試 artifact SHA-256。 */
function sha256(contents) {
  return createHash('sha256').update(contents).digest('hex');
}

/** 驗證缺少 required artifacts 會產生 blocked，而非假造通過。 */
test('validatePhase3aEvidence blocks when required evidence is missing', () => {
  const root = createEvidenceRoot();
  try {
    const result = validatePhase3aEvidence(root);
    assert.equal(result.evidenceStatus, 'blocked');
    assert.ok(result.errors.length > 0);
    assert.ok(result.errors.some(error => error.includes('phase3a-memory-gate.json')));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

/** 驗證 aggregate artifact index 會偵測內容 hash tampering。 */
test('validateArtifactIndex rejects a mismatched SHA-256', () => {
  const root = createEvidenceRoot();
  try {
    writeFileSync(path.join(root, 'summary.md'), 'original');
    const errors = validateArtifactIndex(root, {
      reports: [{ path: 'summary.md', sha256: sha256('different'), sizeBytes: 8 }]
    });
    assert.ok(errors.some(error => error.includes('hash')));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

/** 驗證 formal campaign 的 policy file hash 與 target limit 必須一致。 */
test('validateCampaignPolicy rejects missing or mismatched frozen policy', () => {
  const root = createEvidenceRoot();
  try {
    writeFileSync(path.join(root, 'memory-policy.json'), JSON.stringify({ schemaVersion: 1, status: 'frozen', targetMemoryLimitBytes: 3000000000 }));
    const errors = validateCampaignPolicy(root, {
      mode: 'formal',
      policy: { path: 'memory-policy.json', sha256: sha256('changed'), targetMemoryLimitBytes: 3000000000 }
    });
    assert.ok(errors.some(error => error.includes('hash does not match')));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

/** 驗證 reproduction groups 至少由兩個 distinct run IDs 組成。 */
test('validateReproductionRunIds rejects a missing independent replicate', () => {
  const errors = validateReproductionRunIds([
    'campaign-repro-baseline-r1',
    'campaign-repro-run-only-r1', 'campaign-repro-run-only-r2'
  ]);
  assert.ok(errors.some(error => error.includes('repro-baseline')));
  assert.deepEqual(validateReproductionRunIds([
    'campaign-repro-baseline-r1', 'campaign-repro-baseline-r2'
  ]), []);
});

/** 驗證 Gate status 與 integration recommendation 的 mapping 不可互相矛盾。 */
test('validateGateDecision rejects recommendation mismatch', () => {
  const errors = validateGateDecision({
    evidenceStatus: 'complete',
    status: 'PASS',
    classification: 'No Leak Evidence',
    recommendation: 'DO NOT PROCEED'
  });
  assert.ok(errors.some(error => error.includes('recommendation')));
  assert.deepEqual(validateGateDecision({
    evidenceStatus: 'blocked',
    status: null,
    classification: null,
    recommendation: 'DO NOT PROCEED'
  }), []);
});

/** 驗證 OOM terminal-failure 可由外部 inspect／cgroup evidence 證明。 */
test('isVerifiedTerminalFailureEvidence accepts externally verified OOM without final probe manifest', () => {
  const root = createEvidenceRoot();
  try {
    mkdirSync(path.join(root, 'docker'), { recursive: true });
    writeFileSync(path.join(root, 'docker', 'inspect.json'), JSON.stringify({ State: { OOMKilled: true, ExitCode: 137 } }));
    writeFileSync(path.join(root, 'docker', 'cgroup.csv'), 'memory.current,memory.peak\n1,2\n');
    const verified = isVerifiedTerminalFailureEvidence(root, {
      status: 'terminal-failure',
      terminalOutcome: 'oom',
      oomKilled: true,
      dockerExitCode: 137,
      inspectPath: 'docker/inspect.json',
      cgroupSamplesPath: 'docker/cgroup.csv',
      runManifestPath: 'missing-run-manifest.json'
    });
    assert.equal(verified.valid, true);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
