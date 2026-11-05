import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const source = await readFile(new URL('../src/lib/portal/arming-state.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { armingDisplay } = await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);
const packet = { missionId: 'selected', armed: false, connectivityStatus: 'Online', isStale: false };

test('live disarm reminds the operator and the next armed heartbeat removes the reminder', () => {
  assert.equal(armingDisplay(packet, 'selected', true, 1).remindToArm, true);
  const armed = armingDisplay({ ...packet, armed: true }, 'selected', true, 1);
  assert.equal(armed.label, 'ARMED');
  assert.equal(armed.remindToArm, false);
  assert.equal(armingDisplay(packet, 'selected', true, 1).label, 'DISARMED');
});
test('missing state is unknown rather than disarmed', () => {
  for (const sample of [null, { ...packet, armed: null }, { ...packet, armed: undefined }]) {
    assert.equal(armingDisplay(sample, 'selected', true, 1).label, 'ARM STATE UNKNOWN');
    assert.equal(armingDisplay(sample, 'selected', true, 1).remindToArm, false);
  }
});
test('expired data, a lost stream or packets from another selected mission never urge arming', () => {
  for (const result of [armingDisplay(packet, 'selected', true, 16), armingDisplay(packet, 'selected', true, null),
    armingDisplay(packet, 'selected', false, 1), armingDisplay({ ...packet, isStale: true }, 'selected', true, 1),
    armingDisplay({ ...packet, connectivityStatus: 'Offline' }, 'selected', true, 1), armingDisplay(packet, 'other', true, 1)]) {
    assert.equal(result.state, 'unknown');
    assert.equal(result.remindToArm, false);
  }
  assert.equal(armingDisplay(packet, 'selected', true, 16).label, 'LAST KNOWN: DISARMED');
});
