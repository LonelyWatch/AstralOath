import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync, spawnSync } from 'node:child_process';
import { validateChangedPaths } from '../check-docs.mjs';

const log = 'docs/development/change-log.md';
const inventory = 'Assets/Game/Modules/Inventory';
const party = 'Assets/Game/Modules/Party';
const script = fileURLToPath(new URL('../check-docs.mjs', import.meta.url));

function git(root, ...args) {
  return execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

function write(root, name, text = 'changed\n') {
  const filename = path.join(root, name);
  fs.mkdirSync(path.dirname(filename), { recursive: true });
  fs.writeFileSync(filename, text);
}

function repository(t, initial = { 'Server/Program.cs': 'initial\n', [log]: 'initial\n' }) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'astraloath docs '));
  t.after(() => {
    assert.ok(path.resolve(root).startsWith(path.resolve(os.tmpdir()) + path.sep));
    fs.rmSync(root, { recursive: true, force: true });
  });
  git(root, 'init', '--quiet');
  git(root, 'config', 'user.name', 'Documentation Gate Tests');
  git(root, 'config', 'user.email', 'documentation-tests@example.invalid');
  git(root, 'config', 'commit.gpgsign', 'false');
  git(root, 'config', 'core.autocrlf', 'false');
  git(root, 'config', 'core.hooksPath', path.join(root, 'disabled-hooks'));
  for (const [name, text] of Object.entries(initial)) write(root, name, text);
  commit(root);
  return root;
}

function commit(root) {
  git(root, 'add', '--all');
  git(root, 'commit', '--quiet', '-m', '测试：准备文档检查输入');
}

function cli(root, ...args) {
  const result = spawnSync(process.execPath, [script, ...args], { cwd: root, encoding: 'utf8' });
  assert.ifError(result.error);
  return { code: result.status, output: result.stdout + result.stderr };
}

test('source, config and asset changes require the handoff log in the same change', () => {
  for (const source of ['Assets/Game/Core/Service.cs', 'Assets/Game/Art/Badge.png', 'Server/Program.cs',
    'Shared/Rules.cs', 'Config/Table.json', 'Tools/Build.ps1', 'Packages/manifest.json',
    'ProjectSettings/ProjectSettings.asset', '.github/workflows/check.yml',
    'global.json', '.gitattributes', '.gitignore', '.vsconfig']) {
    assert.ok(validateChangedPaths([source], [source, log]).some(error => error.includes(log)), source);
  }
});

test('a changed handoff log satisfies a non-module implementation change', () => {
  assert.deepEqual(validateChangedPaths(['Server/Program.cs', log], ['Server/Program.cs', log]), []);
});

test('business changes require their own module README even when the log changed', () => {
  const source = `${inventory}/Inventory.cs`;
  assert.ok(validateChangedPaths([source, log], [source, log, `${inventory}/README.md`])
    .some(error => error.includes(`${inventory}/README.md`)));
  assert.deepEqual(validateChangedPaths([source, log, `${inventory}/README.md`], [source, log, `${inventory}/README.md`]), []);
});

test('empty, documentation, meta and Tools/Tests-only changes need no extra documentation', () => {
  for (const changes of [[], ['docs/README.md'], [`${inventory}/README.md`],
    ['Assets/Game/Core/Service.cs.meta'], ['Tools/Tests/check-docs.test.mjs']]) {
    assert.deepEqual(validateChangedPaths(changes, changes), []);
  }
});

test('renaming business code across surviving modules requires both module READMEs', () => {
  const changes = [`${inventory}/Old Name.cs`, `${party}/New Name.cs`, log, `${party}/README.md`];
  const current = [`${inventory}/Other.cs`, `${inventory}/README.md`, `${party}/New Name.cs`, `${party}/README.md`, log];
  assert.ok(validateChangedPaths(changes, current).some(error => error.includes(`${inventory}/README.md`)));
  assert.deepEqual(validateChangedPaths([...changes, `${inventory}/README.md`], current), []);
});

test('removing an entire module requires only the log, while partial removal requires its README', () => {
  const changes = [`${inventory}/Inventory.cs`, `${inventory}/README.md`, log];
  assert.deepEqual(validateChangedPaths(changes, [log]), []);
  assert.ok(validateChangedPaths([`${inventory}/Inventory.cs`, log], [log, `${inventory}/Other.cs`, `${inventory}/README.md`])
    .some(error => error.includes(`${inventory}/README.md`)));
});

test('deleting required documentation cannot count as updating it', () => {
  assert.ok(validateChangedPaths(['Server/Program.cs', log], ['Server/Program.cs']).some(error => error.includes(log)));
  assert.ok(validateChangedPaths([`${inventory}/Inventory.cs`, `${inventory}/README.md`, log], [`${inventory}/Inventory.cs`, log])
    .some(error => error.includes(`${inventory}/README.md`)));
});

test('staged mode requires staged documentation and preserves spaces in paths', t => {
  const root = repository(t, { 'Server/Space Name.cs': 'initial\n', [log]: 'initial\n' });
  write(root, 'Server/Space Name.cs');
  write(root, log);
  git(root, 'add', '--', 'Server/Space Name.cs');
  const missing = cli(root, '--staged');
  assert.equal(missing.code, 1);
  assert.match(missing.output, /docs\/development\/change-log\.md/);
  git(root, 'add', '--', log);
  assert.equal(cli(root, '--staged').code, 0);
});

test('base mode compares the base commit to HEAD without counting uncommitted documentation', t => {
  const root = repository(t);
  const base = git(root, 'rev-parse', 'HEAD');
  write(root, 'Server/Program.cs');
  commit(root);
  write(root, log);
  git(root, 'add', '--', log);
  assert.equal(cli(root, '--base', base).code, 1);
  commit(root);
  assert.equal(cli(root, '--base', base).code, 0);
  assert.equal(cli(root, '--base', 'HEAD').code, 0);
});

test('staged renames include the original module and removal uses the index snapshot', t => {
  const root = repository(t, { [`${inventory}/Old Name.cs`]: 'initial\n', [`${inventory}/README.md`]: 'initial\n',
    [`${party}/Party.cs`]: 'initial\n', [`${party}/README.md`]: 'initial\n', [log]: 'initial\n' });
  git(root, 'mv', '--', `${inventory}/Old Name.cs`, `${party}/New Name.cs`);
  write(root, `${party}/README.md`);
  write(root, log);
  git(root, 'add', '--all');
  const missing = cli(root, '--staged');
  assert.equal(missing.code, 1);
  assert.ok(missing.output.includes(`${inventory}/README.md`));
  git(root, 'rm', '--', `${inventory}/README.md`);
  // An unstaged file must not make the deleted module exist in the index.
  write(root, `${inventory}/Unstaged.cs`);
  assert.equal(cli(root, '--staged').code, 0);
});

test('unknown, empty and all-zero bases fail explicitly instead of skipping checks', t => {
  const root = repository(t);
  for (const base of ['missing-reference', '', '0'.repeat(40), '--help']) {
    const result = cli(root, '--base', base);
    assert.equal(result.code, 1, base);
    assert.match(result.output, /基准|用法/);
  }
});

test('CLI rejects missing or conflicting modes and accepts an empty staged diff', t => {
  const root = repository(t);
  for (const args of [[], ['--base'], ['--staged', '--base', 'HEAD'], ['--unknown']]) {
    const result = cli(root, ...args);
    assert.equal(result.code, 1);
    assert.match(result.output, /用法/);
  }
  assert.equal(cli(root, '--staged').code, 0);
});

function ci(root, environment) {
  const result = spawnSync(process.execPath, [script, '--ci'], {
    cwd: root, encoding: 'utf8', env: { ...process.env,
      DOCUMENTATION_EVENT: '', DOCUMENTATION_PUSH_BASE: '', DOCUMENTATION_PR_BASE: '', DOCUMENTATION_DEFAULT_BRANCH: '', ...environment }
  });
  assert.ifError(result.error);
  return { code: result.status, output: result.stdout + result.stderr };
}

test('new-branch CI checks every commit since the default-branch merge base', t => {
  const root = repository(t);
  const base = git(root, 'rev-parse', 'HEAD');
  // The default branch advanced independently. Its log edit must not satisfy
  // documentation for code committed only on the newly pushed branch.
  write(root, log, 'upstream-only documentation\n');
  commit(root);
  git(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
  git(root, 'checkout', '--quiet', '--detach', base);
  write(root, 'Server/Program.cs');
  commit(root);
  write(root, 'docs/unrelated.md');
  commit(root);
  const environment = { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: '0'.repeat(40), DOCUMENTATION_DEFAULT_BRANCH: 'main' };
  const missing = ci(root, environment);
  assert.equal(missing.code, 1);
  assert.ok(missing.output.includes(log), missing.output);
  write(root, log);
  commit(root);
  assert.equal(ci(root, environment).code, 0);
});

test('push CI uses the entire provided before-to-HEAD range', t => {
  const root = repository(t);
  const before = git(root, 'rev-parse', 'HEAD');
  write(root, 'Server/Program.cs');
  commit(root);
  const afterSource = git(root, 'rev-parse', 'HEAD');
  write(root, 'docs/unrelated.md');
  commit(root);
  assert.equal(ci(root, { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: afterSource }).code, 0);
  const missing = ci(root, { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: before });
  assert.equal(missing.code, 1);
  assert.ok(missing.output.includes(log), missing.output);
  write(root, log);
  commit(root);
  assert.equal(ci(root, { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: before }).code, 0);
});

test('pull-request CI uses DOCUMENTATION_PR_BASE rather than the push base or last commit', t => {
  const root = repository(t);
  const base = git(root, 'rev-parse', 'HEAD');
  write(root, 'Server/Program.cs');
  commit(root);
  write(root, 'docs/unrelated.md');
  commit(root);
  const environment = { DOCUMENTATION_EVENT: 'pull_request', DOCUMENTATION_PR_BASE: base, DOCUMENTATION_PUSH_BASE: 'HEAD' };
  const missing = ci(root, environment);
  assert.equal(missing.code, 1);
  assert.ok(missing.output.includes(log), missing.output);
  write(root, log);
  commit(root);
  assert.equal(ci(root, environment).code, 0);
});

test('manual CI checks only the latest commit and rejects a root commit without a parent', t => {
  const root = repository(t);
  const environment = { DOCUMENTATION_EVENT: 'workflow_dispatch' };
  const initial = ci(root, environment);
  assert.equal(initial.code, 1);
  assert.match(initial.output, /HEAD\^/);
  write(root, 'Server/Program.cs');
  commit(root);
  const missing = ci(root, environment);
  assert.equal(missing.code, 1);
  assert.ok(missing.output.includes(log), missing.output);
  write(root, 'docs/unrelated.md');
  commit(root);
  assert.equal(ci(root, environment).code, 0);
});

test('CI rejects missing event data and unresolved baselines without a fallback', t => {
  const root = repository(t);
  write(root, 'docs/unrelated.md');
  commit(root);
  for (const environment of [{}, { DOCUMENTATION_EVENT: 'unknown' },
    { DOCUMENTATION_EVENT: 'push' }, { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: 'missing-reference' },
    { DOCUMENTATION_EVENT: 'pull_request' }, { DOCUMENTATION_EVENT: 'pull_request', DOCUMENTATION_PR_BASE: 'missing-reference' },
    { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: '0'.repeat(40) },
    { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: '0'.repeat(40), DOCUMENTATION_DEFAULT_BRANCH: 'main' }]) {
    const result = ci(root, environment);
    assert.equal(result.code, 1, JSON.stringify(environment));
    assert.match(result.output, /无法解析|必须提供|不支持/);
  }
});

test('new-branch CI rejects an initial root commit even when the default remote points to HEAD', t => {
  const root = repository(t);
  git(root, 'update-ref', 'refs/remotes/origin/main', 'HEAD');
  const result = ci(root, { DOCUMENTATION_EVENT: 'push', DOCUMENTATION_PUSH_BASE: '0'.repeat(40), DOCUMENTATION_DEFAULT_BRANCH: 'main' });
  assert.equal(result.code, 1);
  assert.match(result.output, /HEAD\^|父提交/);
});
