import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const source = await readFile(new URL('../src/lib/portal/weather-display.ts', import.meta.url), 'utf8');
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { groupWeatherWaypoints, weatherWarnings, formatWeatherTime } = await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);
const point = (sequenceOrder, overrides = {}) => ({sequenceOrder, latitude:10.8, longitude:106.8, forecastTime:'2026-10-23T04:00:00Z', precipitationMillimeters:0.3, warnings:[`WP${sequenceOrder}: rain warning`], ...overrides});
test('nearby identical forecasts share one representative and repeated warnings disappear', () => {
  const waypoints = [point(1), point(2,{latitude:10.8001})];
  assert.deepEqual(groupWeatherWaypoints(waypoints).map(g=>g.sequences), [[1,2]]);
  assert.deepEqual(weatherWarnings({waypoints,warnings:['WP1: rain warning','WP2: rain warning','Cached forecast expired.']}), ['WP1: rain warning','Cached forecast expired.']);
});
test('different conditions, forecast time, warnings and distant points stay separate', () => {
  for (const overrides of [{precipitationMillimeters:0.4},{forecastTime:'2026-10-23T05:00:00Z'},{latitude:10.9},{warnings:['WP2: thunderstorm']}]) {
    assert.equal(groupWeatherWaypoints([point(1),point(2,overrides)]).length,2);
  }
});
test('distance is checked against representative to avoid a chain covering a large area', () => {
  assert.deepEqual(groupWeatherWaypoints([point(1),point(2,{latitude:10.807}),point(3,{latitude:10.814})]).map(g=>g.sequences),[[1,2],[3]]);
});
test('timestamps include Vietnam date, seconds and timezone; missing dates are explicit', () => {
  const value=formatWeatherTime('2026-10-23T18:30:15Z');
  assert.match(value,/24\/10\/2026/); assert.match(value,/01:30:15/); assert.match(value,/UTC\+07:00/);
  assert.equal(formatWeatherTime('2026-10-23T18:30:15'),value);
  assert.equal(formatWeatherTime(null),'Unavailable');
  assert.equal(formatWeatherTime('invalid'),'Unavailable');
});
test('route samples sharing waypoint numbers have distinct stable row identities and accurate labels', async () => {
  const { weatherPointLabel } = await import(`data:text/javascript;base64,${Buffer.from(js).toString('base64')}`);
  const waypoints = [3, 5, 7].flatMap(sequence => [
    point(sequence),
    point(sequence, {isRouteSample:true, sampleOrder:sequence * 2, segmentFrom:sequence, segmentTo:sequence + 1}),
    point(sequence, {isRouteSample:true, sampleOrder:sequence * 2 + 1, segmentFrom:sequence, segmentTo:sequence + 1}),
  ]);
  const groups = groupWeatherWaypoints(waypoints);
  assert.equal(groups.length, 7);
  assert.equal(new Set(groups.map(group => group.key)).size, groups.length);
  assert.deepEqual(new Set(groupWeatherWaypoints([...waypoints].reverse()).map(group => group.key)), new Set(groups.map(group => group.key)));
  assert.equal(groups.filter(group => group.point.isRouteSample).length, 6);
  assert.equal(weatherPointLabel(waypoints[1]), 'Route sample 6 · WP3–WP4');
  assert.equal(weatherPointLabel(waypoints[0]), 'WP3');
});
test('different forecasts for the same waypoint stay visible with unique keys', () => {
  const groups = groupWeatherWaypoints([point(3), point(3, {forecastTime:'2026-10-23T05:00:00Z'})]);
  assert.equal(groups.length, 2);
  assert.equal(new Set(groups.map(group => group.key)).size, 2);
});
test('route samples do not overwrite real waypoint warning representatives', () => {
  const waypoints = [point(1), point(3), point(3, {isRouteSample:true, sampleOrder:1, segmentFrom:3, segmentTo:4})];
  assert.deepEqual(weatherWarnings({waypoints,warnings:['WP1: rain warning','WP3: rain warning','Route sample 1 on segment 3-4: rain warning']}), ['WP1: rain warning','Route sample 1 on segment 3-4: rain warning']);
});
