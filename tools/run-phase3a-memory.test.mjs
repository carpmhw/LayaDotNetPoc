import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

/** 以明確參數呼叫 Phase 3A orchestrator shell wrapper。 */
function runWrapper(argumentsList) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  return spawnSync('bash', [path.join(root, 'tools', 'run-phase3a-memory.sh'), ...argumentsList], {
    encoding: 'utf8',
    env: process.env
  });
}

/** 驗證 formal stage 缺 frozen policy 時會在 build／run 前 blocked。 */
test('formal stage requires an explicit frozen policy path', () => {
  const result = runWrapper([
    '--campaign-id', 'phase3a-formal-test',
    '--stage', 'baseline',
    '--mode', 'formal'
  ]);

  assert.equal(result.status, 2);
  assert.match(result.stderr, /frozen memory policy/i);
});

/** 驗證 pilot mode 不可使用會執行正式全矩陣的 all stage。 */
test('all stage requires formal mode', () => {
  const result = runWrapper([
    '--campaign-id', 'phase3a-pilot-test',
    '--stage', 'all',
    '--mode', 'pilot'
  ]);

  assert.equal(result.status, 2);
  assert.match(result.stderr, /all.*formal/i);
});

/** 驗證未知 orchestrator option 以 usage exit code 拒絕。 */
test('unknown wrapper options are rejected', () => {
  const result = runWrapper(['--surprise']);

  assert.equal(result.status, 3);
  assert.match(result.stderr, /未知參數/);
});
