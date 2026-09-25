import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** 判斷本機產生的 evidence 與預存 repository guidance 是否應排除於 source identity。 */
function isExcludedIdentityPath(relativePath) {
  const normalizedPath = relativePath.replaceAll('\\', '/');
  return normalizedPath === 'AGENTS.md' || normalizedPath.startsWith('reports/phase3a/');
}

/** 擷取與 .NET Probe 相同的 commit／dirty identity，排除 run evidence output。 */
export function captureSourceIdentity(repositoryRoot) {
  const root = path.resolve(repositoryRoot);
  const git = (...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8' }).trim();
  const commit = git('rev-parse', 'HEAD');
  const status = git('status', '--porcelain=v1', '--untracked-files=all')
    .split('\n')
    .filter((line) => line.length < 4 || !isExcludedIdentityPath(line.slice(3)))
    .join('\n');

  if (status.length === 0) {
    return { commit, dirty: false, dirtyIdentity: null };
  }

  const hash = createHash('sha256');
  hash.update(status, 'utf8');
  hash.update(git('diff', '--binary', 'HEAD', '--', '.', ':(exclude)reports/phase3a', ':(exclude)AGENTS.md'), 'utf8');
  const untracked = git('ls-files', '--others', '--exclude-standard', '-z');
  for (const relativePath of untracked.split('\0').filter(Boolean)) {
    if (isExcludedIdentityPath(relativePath)) {
      continue;
    }

    hash.update(relativePath, 'utf8');
    hash.update(readFileSync(path.resolve(root, relativePath)));
  }

  return { commit, dirty: true, dirtyIdentity: hash.digest('hex') };
}

/** CLI entry point: 輸出供 Docker host wrapper 傳給 probe container 的 JSON identity。 */
function main() {
  const repositoryRoot = process.argv[2] ?? process.cwd();
  process.stdout.write(`${JSON.stringify(captureSourceIdentity(repositoryRoot))}\n`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  main();
}
