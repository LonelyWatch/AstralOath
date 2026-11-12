import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

const changeLog = 'docs/development/change-log.md';
const implementationRoots = /^(Assets\/Game|Server|Shared|Config|Tools|Packages|ProjectSettings|\.github\/workflows)\//;
const rootConfigurations = new Set(['global.json', '.gitattributes', '.gitignore', '.vsconfig']);
const modulePath = /^(Assets\/Game\/Modules\/[^/]+)\//;

// Both lists use repository-relative Git paths. currentPaths is the target
// snapshot (index or HEAD), so deleted documentation never counts as an update.
export function validateChangedPaths(changedPaths, currentPaths) {
  const changed = new Set(changedPaths);
  const current = new Set(currentPaths);
  const implementation = [...changed].filter(name => (implementationRoots.test(name) || rootConfigurations.has(name)) &&
    !/\.(md|meta)$/i.test(name) && !name.startsWith('Tools/Tests/'));
  if (implementation.length === 0) return [];

  const errors = [];
  const updated = name => changed.has(name) && current.has(name);
  if (!updated(changeLog)) {
    errors.push(`代码、配置或资源发生变更，请在同批改动中更新 ${changeLog}，记录改动、验证结果和未完成项。`);
  }
  const currentModules = new Set([...current].map(name => name.match(modulePath)?.[1]).filter(Boolean));
  const changedModules = new Set(implementation.map(name => name.match(modulePath)?.[1]).filter(Boolean));
  for (const module of [...changedModules].sort()) {
    // A fully removed module is documented in the change log, without recreating it.
    if (!currentModules.has(module)) continue;
    const readme = `${module}/README.md`;
    if (!updated(readme)) errors.push(`业务模块 ${module} 发生变更，请在同批改动中更新 ${readme}。`);
  }
  return errors;
}

function git(root, args) {
  return execFileSync('git', args, {
    cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'],
    timeout: 30000, maxBuffer: 64 * 1024 * 1024
  });
}

function commit(root, reference) {
  try {
    return git(root, ['rev-parse', '--verify', '--end-of-options', `${reference}^{commit}`]).trim();
  } catch {
    throw new Error(`无法解析基准提交 ${JSON.stringify(reference)}；请提供本地已存在的 Git 提交，并确保已获取所需历史。初次推送的全零 before 不是有效基准，不能跳过检查。`);
  }
}

function ciBase(root, environment) {
  const required = name => {
    const value = environment[name]?.trim();
    if (!value) throw new Error(`CI 文档检查必须提供环境变量 ${name}，不能跳过比较。`);
    return value;
  };
  const event = required('DOCUMENTATION_EVENT');
  if (event === 'pull_request') return commit(root, required('DOCUMENTATION_PR_BASE'));
  if (event === 'workflow_dispatch') {
    console.log('CI 手动运行：检查最近提交（HEAD^..HEAD），初始提交缺少父提交时明确失败。');
    return commit(root, 'HEAD^');
  }
  if (event === 'push') {
    const before = required('DOCUMENTATION_PUSH_BASE');
    if (!/^0+$/.test(before)) return commit(root, before);
    const defaultBranch = required('DOCUMENTATION_DEFAULT_BRANCH');
    // Initial repository pushes have no prior change boundary to compare.
    commit(root, 'HEAD^');
    const remote = commit(root, `refs/remotes/origin/${defaultBranch}`);
    const head = commit(root, 'HEAD');
    try {
      // A new branch can contain several commits. HEAD^ would only inspect
      // the last one; the shared ancestor includes the entire branch change.
      return git(root, ['merge-base', head, remote]).trim();
    } catch {
      throw new Error(`无法解析 HEAD 与远端默认分支 ${JSON.stringify(defaultBranch)} 的共同祖先；请获取完整历史后重试，不能退回仅检查最近提交。`);
    }
  }
  throw new Error(`CI 文档检查不支持事件 ${JSON.stringify(event)}；请使用 push、pull_request 或 workflow_dispatch。`);
}

function main(args) {
  const staged = args.length === 1 && args[0] === '--staged';
  const ciMode = args.length === 1 && args[0] === '--ci';
  const baseMode = args.length === 2 && args[0] === '--base' && args[1].trim() && !args[1].startsWith('-');
  if (!staged && !baseMode && !ciMode) {
    throw new Error('用法：node Tools/check-docs.mjs --staged 或 node Tools/check-docs.mjs --base <gitref>（与 HEAD 比较），或 node Tools/check-docs.mjs --ci（从 CI 环境选择范围）。');
  }
  const root = git(process.cwd(), ['rev-parse', '--show-toplevel']).replace(/\r?\n$/, '');
  const splitPaths = output => output.split('\0').filter(Boolean);
  // Disabling rename detection exposes both old and new paths, including when
  // code moves between modules or out of the implementation roots entirely.
  const diffArgs = ['diff', '--name-only', '-z', '--no-renames', '--no-ext-diff', '--no-textconv'];
  const comparison = staged ? ['--cached'] : [ciMode ? ciBase(root, process.env) : commit(root, args[1]), commit(root, 'HEAD')];
  const changedPaths = splitPaths(git(root, [...diffArgs, ...comparison, '--']));
  const currentPaths = splitPaths(git(root, staged ? ['ls-files', '--cached', '-z'] : ['ls-tree', '-r', '--name-only', '-z', 'HEAD']));
  const errors = validateChangedPaths(changedPaths, currentPaths);
  for (const error of errors) console.error(error);
  console.log(`文档同步检查：${changedPaths.length} 个变更路径，${errors.length} 个错误。仅检查路径同步，不保证文档语义正确。`);
  return errors.length ? 1 : 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    process.exitCode = main(process.argv.slice(2));
  } catch (error) {
    console.error(`文档同步检查失败：${error.stderr?.toString().trim() || error.message}`);
    process.exitCode = 1;
  }
}
