import test from 'node:test';
import assert from 'node:assert/strict';
import { validateAssemblyGraph } from '../validate.mjs';

const core = { name: 'AstralOath.Client.Core', path: 'Assets/Game/Core/Core.asmdef', references: [] };
const common = { name: 'AstralOath.Client.Common', path: 'Assets/Game/Common/Common.asmdef', references: [core.name] };
const moduleA = { name: 'AstralOath.Client.Inventory', path: 'Assets/Game/Modules/Inventory/Inventory.asmdef', references: [common.name] };

test('a module may depend on Common and Core without a cycle', () => {
  assert.deepEqual(validateAssemblyGraph([core, common, moduleA]), []);
});
test('Common cannot make an implemented business module a dependency', () => {
  const errors = validateAssemblyGraph([core, { ...common, references: [moduleA.name] }, moduleA]);
  assert.ok(errors.some(x => x.includes('Common cannot depend')));
});
test('a cycle between otherwise valid feature modules fails validation', () => {
  const moduleB = { name: 'AstralOath.Client.Shop', path: 'Assets/Game/Modules/Shop/Shop.asmdef', references: [moduleA.name] };
  assert.ok(validateAssemblyGraph([core, common, { ...moduleA, references: [moduleB.name] }, moduleB]).some(x => x.includes('cycle')));
});
test('missing internal references and duplicate assembly names are rejected', () => {
  assert.ok(validateAssemblyGraph([core, { ...moduleA, references: ['AstralOath.Missing'] }]).some(x => x.includes('Missing reference')));
  assert.ok(validateAssemblyGraph([core, { ...core, path: 'Assets/Game/Other.asmdef' }]).some(x => x.includes('Duplicate assembly')));
});

test('AOT startup cannot reference a hot update module but Editor may inspect it', () => {
  const startup = { name: 'AstralOath.Client.Bootstrap', path: 'Assets/Game/Bootstrap/Bootstrap.asmdef', references: [moduleA.name] };
  const editor = { name: 'AstralOath.Editor', path: 'Assets/Game/Editor/Editor.asmdef', includePlatforms: ['Editor'], references: [moduleA.name] };
  assert.ok(validateAssemblyGraph([core, common, moduleA, startup], [moduleA.name]).some(x => x.includes('AOT cannot depend on hot update')));
  assert.deepEqual(validateAssemblyGraph([core, common, moduleA, editor], [moduleA.name]), []);
});

test('pure battle assembly cannot acquire a Unity or network dependency', () => {
  const battle = { name: 'AstralOath.Battle', path: 'Shared/com.astraloath.shared/Runtime/Battle/Battle.asmdef', noEngineReferences: true, references: [] };
  assert.deepEqual(validateAssemblyGraph([battle], [battle.name]), []);
  assert.ok(validateAssemblyGraph([{ ...battle, noEngineReferences: false }], [battle.name]).some(x => x.includes('Battle must remain engine-free')));
  assert.ok(validateAssemblyGraph([core, { ...battle, references: [core.name] }], [battle.name]).some(x => x.includes('Battle cannot depend')));
});
