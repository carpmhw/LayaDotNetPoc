import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { captureSourceIdentity } from './phase3a-source-identity.mjs';

/** 在 isolated git repository 驗證 source identity 排除 output 並追蹤 untracked contents。 */
test('captureSourceIdentity excludes reports and hashes untracked source content', () => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'phase3a-identity-'));
  try {
    execFileSync('git', ['init', '--quiet'], { cwd: root });
    execFileSync('git', ['-c', 'user.name=Phase3A Test', '-c', 'user.email=phase3a@example.invalid', 'commit', '--allow-empty', '-m', 'initial'], { cwd: root });

    mkdirSync(path.join(root, 'reports', 'phase3a'), { recursive: true });
    writeFileSync(path.join(root, 'reports', 'phase3a', 'run.json'), '{"status":"in-progress"}');
    const cleanIdentity = captureSourceIdentity(root);
    assert.equal(cleanIdentity.dirty, false);
    assert.equal(cleanIdentity.dirtyIdentity, null);

    writeFileSync(path.join(root, 'new-source.cs'), 'class First {}');
    const firstDirtyIdentity = captureSourceIdentity(root);
    writeFileSync(path.join(root, 'new-source.cs'), 'class Second {}');
    const secondDirtyIdentity = captureSourceIdentity(root);

    assert.equal(firstDirtyIdentity.commit, cleanIdentity.commit);
    assert.equal(firstDirtyIdentity.dirty, true);
    assert.match(firstDirtyIdentity.dirtyIdentity, /^[a-f0-9]{64}$/);
    assert.notEqual(firstDirtyIdentity.dirtyIdentity, secondDirtyIdentity.dirtyIdentity);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

/** 驗證既有 AGENTS.md 不會被讀取或納入 source dirty identity。 */
test('captureSourceIdentity excludes the pre-existing repository guidance file', () => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'phase3a-identity-agents-'));
  try {
    execFileSync('git', ['init', '--quiet'], { cwd: root });
    execFileSync('git', ['-c', 'user.name=Phase3A Test', '-c', 'user.email=phase3a@example.invalid', 'commit', '--allow-empty', '-m', 'initial'], { cwd: root });
    writeFileSync(path.join(root, 'AGENTS.md'), 'first local guidance contents');

    const firstIdentity = captureSourceIdentity(root);
    writeFileSync(path.join(root, 'AGENTS.md'), 'changed local guidance contents');
    const secondIdentity = captureSourceIdentity(root);

    assert.deepEqual(firstIdentity, secondIdentity);
    assert.equal(firstIdentity.dirty, false);
    assert.equal(firstIdentity.dirtyIdentity, null);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

/** 驗證 CLI 輸出是可供 host wrapper 傳遞的 machine-readable JSON。 */
test('source identity CLI prints JSON with commit and dirty fields', () => {
  const root = mkdtempSync(path.join(os.tmpdir(), 'phase3a-identity-cli-'));
  try {
    execFileSync('git', ['init', '--quiet'], { cwd: root });
    execFileSync('git', ['-c', 'user.name=Phase3A Test', '-c', 'user.email=phase3a@example.invalid', 'commit', '--allow-empty', '-m', 'initial'], { cwd: root });
    const script = new URL('./phase3a-source-identity.mjs', import.meta.url);
    const output = execFileSync('node', [script.pathname, root], { encoding: 'utf8' });
    const identity = JSON.parse(output);
    assert.equal(identity.dirty, false);
    assert.equal(identity.commit, execFileSync('git', ['rev-parse', 'HEAD'], { cwd: root, encoding: 'utf8' }).trim());
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
