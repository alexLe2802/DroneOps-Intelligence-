import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { renderToStaticMarkup } from 'react-dom/server';
import React from 'react';
import ts from 'typescript';
const require = createRequire(import.meta.url);
const compile = source => 'data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText).toString('base64');
const reviewHelper = compile(await readFile(new URL("../src/lib/portal/mission-review.ts", import.meta.url), "utf8"));
const helper = compile(await readFile(new URL('../src/lib/portal/plan-review.ts', import.meta.url), 'utf8'));
const { planFieldChanges, waypointReviewRows } = await import(helper);
const point = (sequenceOrder, overrides = {}) => ({ sequenceOrder, latitude:10.8, longitude:106.8, altitude:30, actionType:'FlyThrough', ...overrides });
const plan = { name:'Survey', description:null, uavId:'uav', averageSpeedMetersPerSecond:10, maxSpeedMetersPerSecond:15, startTime:'2026-10-24T01:00:00Z', endTime:'2026-10-24T02:00:00Z', altitudeReference:'Relative', waypoints:[point(1)] };
test('compares timestamps by instant and flags actual field edits', () => {
  assert.deepEqual(planFieldChanges(plan, {...plan,startTime:'2026-10-24T08:00:00+07:00'}), []);
  assert.deepEqual(planFieldChanges(plan, {...plan,name:'Changed'}), ['name']);
  assert.deepEqual(planFieldChanges(plan, {...plan,maxSpeedMetersPerSecond:18}), ['maxSpeedMetersPerSecond']);
});
test('matches waypoint sequence and identifies added, removed and action-only edits', () => {
  const rows=waypointReviewRows([point(2),point(1),point(4)], [point(1),point(2,{actionType:'Hover'}),point(3)]);
  assert.deepEqual(rows.map(row => [row.sequence,row.status]), [[1,'Unchanged'],[2,'Changed'],[3,'Added'],[4,'Removed']]);
});
let evidenceSource = await readFile(new URL('../src/app/portal/missions/mission-validation-evidence.tsx', import.meta.url), 'utf8');
const vietnamTime = compile(await readFile(new URL('../src/lib/portal/vietnam-time.ts', import.meta.url), 'utf8'));
evidenceSource = evidenceSource.replace('from "@/lib/portal/vietnam-time"', `from ${JSON.stringify(vietnamTime)}`);
evidenceSource = evidenceSource.replace('from "@/lib/portal/mission-review"', `from ${JSON.stringify(reviewHelper)}`);
evidenceSource = evidenceSource.replace('import styles from "./mission-evidence.module.css";', 'const styles = { validationEvidence: "validationEvidence" };');
// Resolve the JSX runtime in data URL modules against this test package.
const evidenceJs = ts.transpileModule(evidenceSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText.replace('"react/jsx-runtime"', JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href));
const evidenceModule = 'data:text/javascript;base64,' + Buffer.from(evidenceJs).toString('base64');
const { default: Evidence } = await import(evidenceModule);
const envelopeHelper = compile(await readFile(new URL("../src/lib/portal/automatic-flight-limits.ts", import.meta.url), "utf8"));
let component = await readFile(new URL('../src/app/portal/missions/mission-plan-comparison.tsx', import.meta.url), 'utf8');
component = component.replace('from "@/lib/portal/automatic-flight-limits"', `from ${JSON.stringify(envelopeHelper)}`).replace('from "react"', `from ${JSON.stringify(pathToFileURL(require.resolve('react')).href)}`)
  .replace('import { getGeofences } from "@/lib/portal/client";', 'const getGeofences = async () => [];')
  .replace('import { formatWeatherTime } from "@/lib/portal/weather-display";', 'const formatWeatherTime = value => value || "Unavailable";')
  .replace('from "@/lib/portal/plan-review"', `from ${JSON.stringify(helper)}`)
  .replace('from \"@/lib/portal/mission-review\"', `from ${JSON.stringify(reviewHelper)}`)
  .replace('from \"./mission-validation-evidence\"', `from ${JSON.stringify(evidenceModule)}`)
  .replace('import MissionMap from "./mission-map";', 'const MissionMap = () => null;')
  .replace('import styles from "./mission-evidence.module.css";', 'const styles = new Proxy({}, {get: (_, key) => key});');
let js = ts.transpileModule(component, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
js = js.replace('"react/jsx-runtime"', JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href));
const { default: Comparison } = await import('data:text/javascript;base64,'+Buffer.from(js).toString('base64'));
test('initial submission shows one submitted plan without Current or saved route toggle', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {isInitial:true,proposed:plan}));
  assert.match(html,/>Submitted plan</);
  assert.match(html,/View submitted plan waypoints/);
  assert.doesNotMatch(html,/Nhiệm vụ mới|chưa có kế hoạch trước đó/);
  assert.doesNotMatch(html,/>Current<|>Proposed<|>Saved plan<|Plans are not available/);
});
test('update shows both columns and marks changed fields and waypoints', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {current:plan,proposed:{...plan,name:'New',waypoints:[point(1,{altitude:50})]}}));
  assert.match(html,/>Current</); assert.match(html,/>Proposed</); assert.match(html,/changedRow/); assert.match(html,/Changed/);
});
test('unchanged update remains a comparison and explains no changes', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {current:plan,proposed:plan}));
  assert.match(html,/>Current</); assert.match(html,/No changes from the saved plan\./);
});

test('validation evidence renders textual waypoint/segment locations, exact identities and outdated state', () => {
  const validation = {
    id: 'run-id', missionId: 'mission', updateRequestId: 'proposal-id', isValid: true,
    stale: true, status: 'Pass', message: 'Previous pass', checkedAt: '2026-11-01T01:00:00Z', restrictionRevision: 'restriction-hash',
    findings: [
      { geofenceId: 'zone-a', revision: 2, waypointSequence: 3, reason: 'Inside no-fly zone', rule: 'RestrictedAirspaceIntersection' },
      { geofenceId: 'zone-b', revision: 4, segmentFrom: 3, segmentTo: 4, reason: 'Crosses restriction', rule: 'RestrictedAirspaceIntersection' },
    ],
  };
  const html = renderToStaticMarkup(React.createElement(Evidence, { missionId: 'mission', validation }));
  for (const text of ['Validation: Stale', 'Outdated evidence', 'run-id', 'proposal-id', 'restriction-hash', 'Waypoint 3', 'Segment 3–4', 'zone-a', 'zone-b', 'if the map cannot load', 'runId=run-id']) assert.ok(html.includes(text), text);
});

test('historical evidence warns that an old pass does not establish current clearance', () => {
  const html = renderToStaticMarkup(React.createElement(Evidence, { historical: true, validation: { isValid: true, message: 'Passed' } }));
  assert.match(html, /Historical run; does not establish current clearance/);
});

test('Error evidence cannot render a passed route with a contradictory isValid flag', () => {
  const html = renderToStaticMarkup(React.createElement(Comparison, { current: plan, proposed: plan, validation: { isValid: true, error: true, message: 'Provider failed' } }));
  assert.match(html, /Validation: Error/);
  assert.doesNotMatch(html, /Waypoints and route checks passed/);
});

test('operational envelope changes are highlighted; JSON key order is irrelevant', () => {
 const a = {takeoffSpeedMetersPerSecond:1,maxAltitudeMeters:30};
 assert.deepEqual(planFieldChanges({...plan,automaticFlightLimits:a},{...plan,automaticFlightLimits:{maxAltitudeMeters:30,takeoffSpeedMetersPerSecond:1}}),[]);
 assert.deepEqual(planFieldChanges({...plan,automaticFlightLimits:a},{...plan,automaticFlightLimits:{...a,maxAltitudeMeters:20}}),['automaticFlightLimits']);
 const html=renderToStaticMarkup(React.createElement(Comparison,{isInitial:true,proposed:{...plan,automaticFlightLimits:{...a,climbSpeedMetersPerSecond:1,descentSpeedMetersPerSecond:0.7,landingSpeedMetersPerSecond:0.7,maxWindMetersPerSecond:3,maxGustMetersPerSecond:5,policyVersion:'multicopter-envelope-v1'}}}));
 assert.match(html,/Automatic operating limits/); assert.match(html,/Takeoff 1 m\/s/); assert.match(html,/Ceiling 30 m/);
});

test('nested measurement/survey key order does not fabricate a plan change',()=>{
 const one={...plan,automaticFlightLimits:{recoveryEnergyProfile:{measurementReference:'report',remainingEnergyCurve:[{batteryPercent:0,lowerBoundWh:0},{batteryPercent:100,lowerBoundWh:80}]}}};
 const two={...plan,automaticFlightLimits:{recoveryEnergyProfile:{remainingEnergyCurve:[{lowerBoundWh:0,batteryPercent:0},{lowerBoundWh:80,batteryPercent:100}],measurementReference:'report'}}};
 assert.deepEqual(planFieldChanges(one,two),[]);
 const p=point(1,{navigation:{landingSiteSurvey:{groundClear:true,evidenceReference:'survey'}}});
 const q=point(1,{navigation:{landingSiteSurvey:{evidenceReference:'survey',groundClear:true}}});
 assert.equal(waypointReviewRows([p],[q])[0].changed,false);
 assert.equal(waypointReviewRows([p],[{...q,navigation:{landingSiteSurvey:{evidenceReference:'different survey',groundClear:true}}}])[0].changed,true);
});
