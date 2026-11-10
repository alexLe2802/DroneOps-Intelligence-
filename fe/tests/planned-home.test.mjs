import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';

const source = await readFile(new URL('../src/lib/maps/planned-home.ts', import.meta.url), 'utf8');
const compiled = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext } }).outputText;
const { withPlannedHome, plannedHomeError } = await import('data:text/javascript;base64,' + Buffer.from(compiled).toString('base64'));
const home = { latitude: 10.8231, longitude: 106.6297, altitudeMslMeters: 12 };

test('Home persists once on first waypoint while preserving navigation and original inputs', () => {
  const points = [{ sequenceOrder: 1, navigation: { yawDegrees: 90 } }, { sequenceOrder: 2, navigation: { plannedHome: home, holdTimeSeconds: 5 } }];
  const result = withPlannedHome(points, home);
  assert.deepEqual(result[0].navigation, { yawDegrees: 90, plannedHome: home });
  assert.deepEqual(result[1].navigation, { holdTimeSeconds: 5 });
  assert.equal(points[0].navigation.plannedHome, undefined);
  assert.equal(withPlannedHome(result, null)[0].navigation.plannedHome, undefined);
});

test('optional Home does not invent a location or ground altitude', () => {
  assert.equal(plannedHomeError(null), null);
  assert.equal(plannedHomeError({ ...home, altitudeMslMeters: null }), null);
  assert.equal(withPlannedHome([{ sequenceOrder: 1 }], null)[0].navigation, null);
  for (const value of [{ ...home, latitude: NaN }, { ...home, longitude: 181 }, { ...home, altitudeMslMeters: 10001 }])
    assert.ok(plannedHomeError(value));
});

 test('automatic missions require an explicitly selected Home', () => {
  assert.ok(plannedHomeError(null, true));
  assert.equal(plannedHomeError(home, true), null);
});
