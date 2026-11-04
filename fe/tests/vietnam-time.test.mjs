import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const source = await readFile(new URL('../src/lib/portal/vietnam-time.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const time = await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);

test('Vietnam schedules and displays do not depend on the browser/server timezone', () => {
  const original = process.env.TZ;
  try {
    for (const zone of ['UTC', 'America/Los_Angeles', 'Asia/Tokyo', 'Asia/Ho_Chi_Minh']) {
      process.env.TZ = zone;
      assert.equal(time.toVietnamDateTimeInput('2026-11-04T18:30:00Z'), '2026-11-05T01:30');
      assert.equal(time.vietnamInputToUtcIso('2026-11-05T01:30'), '2026-11-04T18:30:00.000Z');
      assert.equal(time.apiInstant('2026-11-04T18:30:00').toISOString(), '2026-11-04T18:30:00.000Z');
      assert.match(time.formatVietnamDateTime('2026-11-04T18:30:00Z'), /05\/11\/2026/);
      assert.match(time.formatVietnamDateTime('2026-11-04T18:30:00Z'), /01:30:00/);
      assert.match(time.formatVietnamDateTime('2026-11-04T18:30:00Z'), /GMT\+7/);
      assert.equal(time.formatVietnamDate('1999-11-05'), '05/11/1999');
    }
  } finally { if (original === undefined) delete process.env.TZ; else process.env.TZ = original; }
});
test('Vietnam input rejects impossible dates and timestamps that already carry a zone', () => {
  assert.throws(() => time.vietnamInputToUtcIso('2026-02-30T08:00'));
  assert.throws(() => time.vietnamInputToUtcIso('2026-11-05T01:30Z'));
  assert.equal(time.toVietnamDateTimeInput('invalid'), '');
});
