import { createHash } from 'node:crypto';
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const requiredReports = [
  'phase3a-memory-samples.csv',
  'baseline-full-pipeline.csv',
  'baseline-full-pipeline.md',
  'phase3a-memory-components.md',
  'phase3a-lifecycle-audit.md',
  'phase3a-session-lifecycle.md',
  'phase3a-ort-arena-comparison.md',
  'phase3a-shape-memory-matrix.md',
  'phase3a-memory-concurrency.md',
  'phase3a-docker-memory.md',
  'phase3a-reproducibility.md',
  'phase3a-memory-gate-report.md',
  'phase3a-memory-gate.json',
  'phase3a-artifact-index.json'
];
const requiredStages = ['baseline', 'components', 'lifecycle', 'arena', 'shape', 'concurrency', 'reproduction'];
const validClassifications = new Set([
  'No Leak Evidence',
  'Allocator Plateau',
  'Native Retention Suspected',
  'Leak Suspected',
  'Leak Confirmed'
]);

/** 以標準小寫十六進位格式計算檔案 bytes 的 SHA-256。 */
function hashFile(filePath) {
  return createHash('sha256').update(readFileSync(filePath)).digest('hex');
}

/** 解析相對 artifact path，拒絕絕對路徑與 parent traversal。 */
function resolveContainedPath(root, relativePath) {
  if (typeof relativePath !== 'string' || relativePath.length === 0 || path.isAbsolute(relativePath)) {
    return null;
  }
  const resolvedRoot = path.resolve(root);
  const resolvedPath = path.resolve(resolvedRoot, relativePath);
  const relative = path.relative(resolvedRoot, resolvedPath);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..') ? resolvedPath : null;
}

/** 解析外部 evidence path，並確認路徑仍位於 evidence root 之下。 */
function resolveEvidencePath(root, evidencePath) {
  if (typeof evidencePath !== 'string' || evidencePath.length === 0) return null;
  const resolvedRoot = path.resolve(root);
  const resolvedPath = path.isAbsolute(evidencePath)
    ? path.resolve(evidencePath)
    : path.resolve(resolvedRoot, evidencePath);
  const relative = path.relative(resolvedRoot, resolvedPath);
  return relative === '' || (!relative.startsWith(`..${path.sep}`) && relative !== '..') ? resolvedPath : null;
}

/** 驗證 artifact index 的安全路徑、位元組長度與 SHA-256。 */
export function validateArtifactIndex(root, index) {
  const errors = [];
  if (!index || !Array.isArray(index.reports)) {
    return ['Artifact index must contain a reports array.'];
  }

  for (const artifact of index.reports) {
    const artifactPath = resolveContainedPath(root, artifact?.path);
    if (!artifactPath) {
      errors.push(`Artifact path is unsafe or invalid: ${artifact?.path}`);
      continue;
    }
    if (!existsSync(artifactPath) || !statSync(artifactPath).isFile()) {
      errors.push(`Artifact is missing: ${artifact.path}`);
      continue;
    }
    if (!/^[a-f0-9]{64}$/.test(artifact.sha256 ?? '') || hashFile(artifactPath) !== artifact.sha256) {
      errors.push(`Artifact hash does not match: ${artifact.path}`);
    }
    if (!Number.isInteger(artifact.sizeBytes) || statSync(artifactPath).size !== artifact.sizeBytes) {
      errors.push(`Artifact size does not match: ${artifact.path}`);
    }
  }
  return errors;
}

/** 驗證 formal campaign 的 frozen policy 路徑、target limit 與檔案 hash。 */
export function validateCampaignPolicy(root, campaign) {
  if (campaign?.mode !== 'formal') {
    return [];
  }
  const policy = campaign.policy;
  if (!policy || typeof policy.path !== 'string') {
    return ['Formal campaign has no frozen memory policy path.'];
  }
  const policyPath = path.isAbsolute(policy.path)
    ? policy.path
    : resolveContainedPath(root, policy.path);
  if (!policyPath || !existsSync(policyPath)) {
    return ['Formal campaign policy file is missing or outside the evidence root.'];
  }

  let document;
  try {
    document = JSON.parse(readFileSync(policyPath, 'utf8'));
  } catch (error) {
    return [`Formal campaign policy JSON is invalid: ${error.message}`];
  }
  const errors = [];
  if (document.schemaVersion !== 1 || document.status !== 'frozen' || document.targetMemoryLimitBytes !== 3000000000) {
    errors.push('Formal policy must be schema 1, frozen and targeted at 3,000,000,000 bytes.');
  }
  if (!/^[a-f0-9]{64}$/.test(policy.sha256 ?? '') || hashFile(policyPath) !== policy.sha256) {
    errors.push('Formal policy file hash does not match the campaign manifest.');
  }
  if (policy.targetMemoryLimitBytes !== 3000000000) {
    errors.push('Campaign policy reference changed the fixed decimal 3 GB target.');
  }
  return errors;
}

/** 確認每個 reproducibility group 至少包含兩個不同 run IDs。 */
export function validateReproductionRunIds(runIds) {
  if (!Array.isArray(runIds) || new Set(runIds).size !== runIds.length) {
    return ['Reproduction run IDs must be a unique array.'];
  }

  const groups = new Map();
  for (const runId of runIds) {
    const match = /^(.*-repro-.+)-r([1-9][0-9]*)$/.exec(runId);
    if (!match) {
      continue;
    }
    const runs = groups.get(match[1]) ?? new Set();
    runs.add(Number(match[2]));
    groups.set(match[1], runs);
  }
  const errors = [];
  for (const [group, replicates] of groups) {
    if (replicates.size < 2) {
      errors.push(`Reproduction group '${group}' has fewer than two independent run IDs.`);
    }
  }
  return errors;
}

/** 驗證 machine-readable Gate status 與 Phase 3B recommendation 一致。 */
export function validateGateDecision(gate) {
  const errors = [];
  if (!gate || !['complete', 'blocked'].includes(gate.evidenceStatus)) {
    return ['Gate evidenceStatus must be complete or blocked.'];
  }
  const expectedRecommendation = gate.evidenceStatus === 'blocked'
    ? 'DO NOT PROCEED'
    : gate.status === 'PASS'
      ? 'PROCEED'
      : gate.status === 'PARTIAL'
        ? 'PROCEED WITH CONDITIONS'
        : gate.status === 'FAIL' || gate.status === null
          ? 'DO NOT PROCEED'
          : null;
  if (!expectedRecommendation || gate.recommendation !== expectedRecommendation) {
    errors.push('Gate status/evidenceStatus does not match its Phase 3B recommendation.');
  }
  if (gate.evidenceStatus === 'blocked' && (gate.status !== null || gate.classification !== null)) {
    errors.push('Blocked evidence must not claim a Gate status or memory classification.');
  }
  if (gate.evidenceStatus === 'complete' && gate.classification !== null && !validClassifications.has(gate.classification)) {
    errors.push(`Memory classification is outside the allowed five values: ${gate.classification}`);
  }
  return errors;
}

/** Probe 無法 finalize 時，透過容器外部 artifacts 核實 terminal OOM／crash。 */
export function isVerifiedTerminalFailureEvidence(root, outcome) {
  if (outcome?.status !== 'terminal-failure' || !['oom', 'crash'].includes(outcome.terminalOutcome)) {
    return { valid: false, reason: 'Outcome is not a recognized terminal failure.' };
  }
  const inspectPath = resolveEvidencePath(root, outcome.inspectPath);
  const cgroupPath = resolveEvidencePath(root, outcome.cgroupSamplesPath);
  if (!inspectPath || !existsSync(inspectPath) || !cgroupPath || !existsSync(cgroupPath)) {
    return { valid: false, reason: 'External inspect or cgroup samples are missing.' };
  }
  const cgroupLines = readFileSync(cgroupPath, 'utf8').split(/\r?\n/).filter(Boolean);
  if (cgroupLines.length < 2) {
    return { valid: false, reason: 'External cgroup evidence contains no landed samples.' };
  }
  let inspect;
  try {
    inspect = JSON.parse(readFileSync(inspectPath, 'utf8'));
  } catch (error) {
    return { valid: false, reason: `External container inspect JSON is invalid: ${error.message}` };
  }
  const oomKilled = inspect.State?.OOMKilled === true;
  const exitCode = inspect.State?.ExitCode;
  if (outcome.terminalOutcome === 'oom' && (!oomKilled || !Number.isInteger(exitCode) || exitCode === 0)) {
    return { valid: false, reason: 'Inspect does not confirm the reported OOM/exit outcome.' };
  }
  if (outcome.terminalOutcome === 'crash' && (oomKilled || !Number.isInteger(exitCode) || exitCode === 0)) {
    return { valid: false, reason: 'Inspect does not confirm the reported non-OOM crash.' };
  }
  if (!Number.isInteger(outcome.dockerExitCode) || outcome.dockerExitCode === 0) {
    return { valid: false, reason: 'External docker exit code is missing or indicates success.' };
  }
  if (Number.isInteger(outcome.containerExitCode) && exitCode !== outcome.containerExitCode) {
    return { valid: false, reason: 'Container inspect exit code does not match external run record.' };
  }
  return { valid: true, reason: null };
}

/** 驗證 Phase 3A aggregate reports、campaign／run hashes 與獨立 Gate decision。 */
export function validatePhase3aEvidence(evidenceRoot) {
  const root = path.resolve(evidenceRoot);
  const errors = [];
  for (const filename of requiredReports) {
    if (!existsSync(path.join(root, filename))) {
      errors.push(`Required Phase 3A artifact is missing: ${filename}`);
    }
  }

  let gate = null;
  const gatePath = path.join(root, 'phase3a-memory-gate.json');
  if (existsSync(gatePath)) {
    try {
      gate = JSON.parse(readFileSync(gatePath, 'utf8'));
      errors.push(...validateGateDecision(gate));
    } catch (error) {
      errors.push(`Gate JSON is invalid: ${error.message}`);
    }
  }

  const indexPath = path.join(root, 'phase3a-artifact-index.json');
  if (existsSync(indexPath)) {
    try {
      errors.push(...validateArtifactIndex(root, JSON.parse(readFileSync(indexPath, 'utf8'))));
    } catch (error) {
      errors.push(`Artifact index JSON is invalid: ${error.message}`);
    }
  }

  const campaignId = gate?.campaignId;
  if (typeof campaignId === 'string') {
    validateCampaignTree(root, campaignId, errors);
  }
  const evidenceStatus = errors.length === 0 && gate?.evidenceStatus === 'complete' ? 'complete' : 'blocked';
  return {
    valid: evidenceStatus === 'complete',
    evidenceStatus,
    gateStatus: gate?.status ?? null,
    recommendation: gate?.recommendation ?? 'DO NOT PROCEED',
    errors
  };
}

/** 驗證所選 campaign 的每個 formal stage 與引用的 per-run artifacts。 */
function validateCampaignTree(root, campaignId, errors) {
  const campaignRoot = path.join(root, 'campaigns', campaignId);
  const stageManifests = [];
  for (const stage of requiredStages) {
    const manifestPath = path.join(campaignRoot, stage, 'campaign.json');
    if (!existsSync(manifestPath)) {
      errors.push(`Required campaign stage manifest is missing: ${stage}`);
      continue;
    }
    try {
      const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
      if (manifest.schemaVersion !== 1 || manifest.stage !== stage || manifest.campaignId !== campaignId || manifest.mode !== 'formal') {
        errors.push(`Campaign stage identity/mode mismatch: ${stage}`);
      }
      if (manifest.evidenceStatus !== 'complete') {
        errors.push(`Campaign stage is not complete: ${stage}`);
      }
      errors.push(...validateCampaignPolicy(root, manifest));
      errors.push(...validateArtifactIndex(path.dirname(manifestPath), { reports: manifest.artifacts ?? [] }));
      stageManifests.push(manifest);
      const runIds = manifest.requiredRunIds ?? [];
      if (stage === 'reproduction') {
        errors.push(...validateReproductionRunIds(runIds));
      }
      for (const runId of runIds) {
        validateRunManifest(root, runId, manifest, errors);
      }
    } catch (error) {
      errors.push(`Campaign stage '${stage}' manifest is invalid: ${error.message}`);
    }
  }

  const dockerPath = path.join(root, 'docker', campaignId, 'phase3a-docker-memory.json');
  if (!existsSync(dockerPath)) {
    errors.push(`Docker terminal/limit evidence is missing: docker/${campaignId}/phase3a-docker-memory.json`);
    return;
  }
  try {
    const docker = JSON.parse(readFileSync(dockerPath, 'utf8'));
    if (docker.campaignId !== campaignId || docker.memoryLimitsVerified !== true) {
      errors.push('Docker evidence campaign identity or effective memory limits are invalid.');
    }
    for (const run of docker.runs ?? []) {
      if (run.status === 'terminal-failure') {
        const result = isVerifiedTerminalFailureEvidence(root, run);
        if (!result.valid) errors.push(`Docker terminal failure '${run.runId}' is not verifiable: ${result.reason}`);
      } else if (run.status !== 'complete') {
        errors.push(`Docker run '${run.runId}' is neither complete nor externally verified terminal failure.`);
      }
    }
  } catch (error) {
    errors.push(`Docker evidence JSON is invalid: ${error.message}`);
  }
}

/** 驗證 run completion、campaign identity 與已 flush raw files 的 hashes。 */
function validateRunManifest(root, runId, campaign, errors) {
  const runDirectory = path.join(root, 'runs', runId);
  const manifestPath = path.join(runDirectory, 'manifest.json');
  if (!existsSync(manifestPath)) {
    errors.push(`Referenced run manifest is missing: ${runId}`);
    return;
  }
  try {
    const run = JSON.parse(readFileSync(manifestPath, 'utf8'));
    if (run.status !== 'complete' || run.runId !== runId || run.campaignId !== campaign.campaignId || run.mode !== 'formal') {
      errors.push(`Run completion or identity mismatch: ${runId}`);
    }
    if (run.workloadSha256 !== campaign.workload?.sha256 ||
        run.policySha256 !== (campaign.policy?.sha256 ?? null) ||
        run.model?.bundleManifestSha256 !== campaign.model?.bundleManifestSha256 ||
        run.model?.tokenizerSha256 !== campaign.model?.tokenizerSha256 ||
        run.sourceIdentity?.commit !== campaign.sourceIdentity?.commit ||
        run.sourceIdentity?.dirtyIdentity !== campaign.sourceIdentity?.dirtyIdentity) {
      errors.push(`Run provenance differs from campaign: ${runId}`);
    }
    errors.push(...validateArtifactIndex(runDirectory, { reports: run.artifacts ?? [] }));
  } catch (error) {
    errors.push(`Run manifest '${runId}' is invalid: ${error.message}`);
  }
}

/** CLI 進入點：驗證 reports root，blocked evidence 以 non-zero exit 表示。 */
function main() {
  const evidenceRoot = process.argv[2] ?? 'reports/phase3a';
  const result = validatePhase3aEvidence(evidenceRoot);
  process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
  process.exitCode = result.valid ? 0 : 1;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
