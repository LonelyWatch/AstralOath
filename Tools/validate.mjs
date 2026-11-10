import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { execFileSync } from 'node:child_process';

export function validateAssemblyGraph(definitions) {
  const errors = [];
  const byName = new Map();
  for (const definition of definitions) {
    if (byName.has(definition.name)) errors.push(`Duplicate assembly: ${definition.name}`);
    byName.set(definition.name, definition);
  }
  for (const definition of definitions) {
    for (const reference of definition.references ?? []) {
      if (reference.startsWith('AstralOath.') && !byName.has(reference)) errors.push(`Missing reference: ${definition.name} -> ${reference}`);
      if (definition.path.replaceAll('\\', '/').includes('/Common/') && byName.get(reference)?.path.replaceAll('\\', '/').includes('/Modules/')) {
        errors.push(`Common cannot depend on business modules: ${definition.name} -> ${reference}`);
      }
    }
  }
  const visited = new Set();
  const active = new Set();
  function visit(name, chain) {
    if (active.has(name)) { errors.push(`Assembly cycle: ${[...chain, name].join(' -> ')}`); return; }
    if (visited.has(name)) return;
    active.add(name);
    for (const reference of byName.get(name)?.references ?? []) if (byName.has(reference)) visit(reference, [...chain, name]);
    active.delete(name);
    visited.add(name);
  }
  for (const name of byName.keys()) visit(name, []);
  return errors;
}

function files(directory) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    if (entry.name.startsWith('.') || ['bin', 'obj'].includes(entry.name)) return [];
    const name = path.join(directory, entry.name);
    return entry.isDirectory() ? files(name) : [name];
  });
}

export function validateProject(root, staged = false) {
  const errors = [];
  const definitions = [path.join(root, 'Assets/Game'), path.join(root, 'Shared')].flatMap(files)
    .filter(name => name.endsWith('.asmdef'))
    .map(name => ({ ...JSON.parse(fs.readFileSync(name, 'utf8').replace(/^\uFEFF/, '')), path: path.relative(root, name) }));
  errors.push(...validateAssemblyGraph(definitions));
  const mcp = JSON.parse(fs.readFileSync(path.join(root, 'Packages/com.coplaydev.unity-mcp/Runtime/MCPForUnity.Runtime.asmdef'), 'utf8').replace(/^\uFEFF/, ''));
  if (!mcp.includePlatforms?.includes('Editor') || mcp.includePlatforms.length !== 1) errors.push('MCP helpers must remain Editor-only');
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'Packages/manifest.json'), 'utf8').replace(/^\uFEFF/, ''));
  for (const [name, version] of Object.entries(manifest.dependencies)) {
    if (/^(https?:|git\+|git@)/.test(version) && !/#[0-9a-f]{40}$/.test(version)) errors.push(`Unpinned git dependency: ${name}`);
  }
  for (const name of files(path.join(root, 'Shared'))) {
    if (name.endsWith('.cs') && /^using UnityEngine[.;]/m.test(fs.readFileSync(name, 'utf8'))) errors.push(`Shared source depends on Unity: ${path.relative(root, name)}`);
  }
  if (staged) {
    const names = execFileSync('git', ['diff', '--cached', '--name-only', '-z', '--diff-filter=ACMR'], { cwd: root, encoding: 'utf8' }).split('\0').filter(Boolean);
    for (const name of names) {
      if (/(^|\/)(Library|\.tools|\.artifacts|bin|obj|\.superpowers)\//i.test(name) || /\.(pfx|p12|key)$|\.local\.(json|toml)$|(^|\/)\.env$/i.test(name)) errors.push(`Forbidden staged file: ${name}`);
      const contents = execFileSync('git', ['show', `:${name}`], { cwd: root, maxBuffer: 128 * 1024 * 1024 });
      if (contents.length > 95 * 1024 * 1024) errors.push(`Oversized staged file: ${name}`);
      if (!contents.includes(0) && /^-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----$/m.test(contents.toString('utf8'))) errors.push(`Private key in staged file: ${name}`);
    }
  }
  return { assemblies: definitions.length, errors };
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
  const result = validateProject(root, process.argv.includes('--staged'));
  for (const error of result.errors) console.error(error);
  console.log(`Foundation validation: ${result.assemblies} assemblies, ${result.errors.length} errors`);
  process.exitCode = result.errors.length ? 1 : 0;
}
