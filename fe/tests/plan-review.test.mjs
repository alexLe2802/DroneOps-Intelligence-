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
const helper = compile(await readFile(new URL('../src/lib/portal/plan-review.ts', import.meta.url), 'utf8'));
const { planFieldChanges, waypointReviewRows } = await import(helper);
const point = (sequenceOrder, overrides = {}) => ({ sequenceOrder, latitude:10.8, longitude:106.8, altitude:30, actionType:'FlyThrough', ...overrides });
const plan = { name:'Survey', description:null, uavId:'uav', startTime:'2026-10-24T01:00:00Z', endTime:'2026-10-24T02:00:00Z', altitudeReference:'Relative', waypoints:[point(1)] };
test('compares timestamps by instant and flags actual field edits', () => {
  assert.deepEqual(planFieldChanges(plan, {...plan,startTime:'2026-10-24T08:00:00+07:00'}), []);
  assert.deepEqual(planFieldChanges(plan, {...plan,name:'Changed'}), ['name']);
});
test('matches waypoint sequence and identifies added, removed and action-only edits', () => {
  const rows=waypointReviewRows([point(2),point(1),point(4)], [point(1),point(2,{actionType:'Hover'}),point(3)]);
  assert.deepEqual(rows.map(row => [row.sequence,row.status]), [[1,'Unchanged'],[2,'Changed'],[3,'Added'],[4,'Removed']]);
});
let component = await readFile(new URL('../src/app/portal/missions/mission-plan-comparison.tsx', import.meta.url), 'utf8');
component = component.replace('from "react"', `from ${JSON.stringify(pathToFileURL(require.resolve('react')).href)}`)
  .replace('import { getGeofences } from "@/lib/portal/client";', 'const getGeofences = async () => [];')
  .replace('import { formatWeatherTime } from "@/lib/portal/weather-display";', 'const formatWeatherTime = value => value || "Unavailable";')
  .replace('from "@/lib/portal/plan-review"', `from ${JSON.stringify(helper)}`)
  .replace('import MissionMap from "./mission-map";', 'const MissionMap = () => null;')
  .replace('import styles from "./mission-evidence.module.css";', 'const styles = new Proxy({}, {get: (_, key) => key});');
let js = ts.transpileModule(component, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } }).outputText;
js = js.replace('"react/jsx-runtime"', JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href));
const { default: Comparison } = await import('data:text/javascript;base64,'+Buffer.from(js).toString('base64'));
test('initial submission shows one submitted plan without Current or saved route toggle', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {isInitial:true,proposed:plan}));
  assert.match(html,/Kế hoạch gửi duyệt/);
  assert.doesNotMatch(html,/>Current<|>Proposed<|>Saved plan<|Plans are not available/);
});
test('update shows both columns and marks changed fields and waypoints', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {current:plan,proposed:{...plan,name:'New',waypoints:[point(1,{altitude:50})]}}));
  assert.match(html,/>Current</); assert.match(html,/>Proposed</); assert.match(html,/changedRow/); assert.match(html,/Changed/);
});
test('unchanged update remains a comparison and explains no changes', () => {
  const html=renderToStaticMarkup(React.createElement(Comparison, {current:plan,proposed:plan}));
  assert.match(html,/>Current</); assert.match(html,/Không có thay đổi/);
});
