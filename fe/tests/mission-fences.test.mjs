import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import ts from 'typescript';

const require = createRequire(import.meta.url);
const source=await readFile(new URL('../src/lib/maps/mission-fences.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.ESNext}}).outputText;
const {withMissionFences,missionFencesError,fenceDistance}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));

const basemapSource = await readFile(new URL('../src/lib/maps/basemap.ts', import.meta.url), 'utf8');
const basemapCode = ts.transpileModule(basemapSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { addBasemap, MAX_MAP_ZOOM, MAX_NATIVE_TILE_ZOOM } = await import('data:text/javascript;base64,' + Buffer.from(basemapCode).toString('base64'));

const fenceFieldsSource = await readFile(new URL('../src/app/portal/missions/mission-fence-fields.tsx', import.meta.url), 'utf8');
const fenceFieldsCode = ts.transpileModule(
  fenceFieldsSource
    .replace('@/lib/maps/mission-fences', 'data:text/javascript;base64,' + Buffer.from(code).toString('base64'))
    .replace('import styles from "./planned-home-fields.module.css";', 'const styles = new Proxy({}, { get: (_, key) => key });'),
  { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX } },
).outputText
  .replace('"react"', JSON.stringify(pathToFileURL(require.resolve('react')).href))
  .replace('"react/jsx-runtime"', JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href));
const { default: MissionFenceFields } = await import('data:text/javascript;base64,' + Buffer.from(fenceFieldsCode).toString('base64'));

const formSource = await readFile(new URL('../src/app/portal/missions/new/mission-form.tsx', import.meta.url), 'utf8');
const editSource = await readFile(new URL('../src/app/portal/missions/[id]/edit/mission-edit-client.tsx', import.meta.url), 'utf8');
const mapSource = await readFile(new URL('../src/app/portal/missions/mission-map.tsx', import.meta.url), 'utf8');
const locationSource = await readFile(new URL('../src/app/portal/missions/mission-location.tsx', import.meta.url), 'utf8');
const fenceCssSource = await readFile(new URL('../src/app/portal/missions/planned-home-fields.module.css', import.meta.url), 'utf8');
const suggestionSource = await readFile(new URL('../src/app/portal/missions/new/mission-suggestion-panel.tsx', import.meta.url), 'utf8');

const circle={id:'circle',shape:'Circle',center:{latitude:10,longitude:106},radiusMeters:100};
test('fences persist once on first waypoint and preserve Home/navigation',()=>{
 const points=[{navigation:{plannedHome:{latitude:10,longitude:106},yawDegrees:90}},{navigation:{missionFences:[circle],holdTimeSeconds:5}}];
 const result=withMissionFences(points,[circle]);
 assert.deepEqual(result[0].navigation.missionFences,[circle]);assert.deepEqual(result[0].navigation.plannedHome,points[0].navigation.plannedHome);
 assert.equal(result[1].navigation.missionFences,undefined);assert.equal(result[1].navigation.holdTimeSeconds,5);
 assert.equal(points[0].navigation.missionFences,undefined);
 assert.equal(withMissionFences(result,[])[0].navigation.missionFences,undefined);
});
test('circle creation measures metres; incomplete shapes cannot be submitted',()=>{
 assert.ok(fenceDistance({latitude:0,longitude:0},{latitude:0.001,longitude:0})>111);
 assert.equal(missionFencesError([circle]),null);
 assert.ok(missionFencesError([{...circle,radiusMeters:0}]));
 assert.ok(missionFencesError([{...circle,radiusMeters:NaN}]));
 assert.ok(missionFencesError([circle,circle]));
 assert.ok(missionFencesError([{id:'p',shape:'Polygon',vertices:[{latitude:10,longitude:106}]}]));
});
test('basemap supports deep zoom beyond 1:20 while scaling native z19 Esri tiles',()=>{
 assert.equal(MAX_NATIVE_TILE_ZOOM, 19);
 assert.ok(MAX_MAP_ZOOM >= 24);
 const createdLayers = [];
 let appliedMaxZoom = null;
 const fakeL = {
  tileLayer: (url, options) => {
   const layer = { url, options, on() { return this; }, addTo() { return this; } };
   createdLayers.push(layer);
   return layer;
  },
  Control: class { constructor() {} addTo() { return this; } },
  DomUtil: { create: () => ({ setAttribute() {}, style: {} }) },
  DomEvent: { disableClickPropagation() {} },
 };
 const fakeMap = {
  setMaxZoom(z) { appliedMaxZoom = z; },
  getContainer: () => ({ dataset: {}, classList: { toggle() {} } }),
  hasLayer: () => false,
  removeLayer() {},
 };
 addBasemap(fakeL, fakeMap, () => {}, () => {});
 assert.equal(appliedMaxZoom, MAX_MAP_ZOOM);
 assert.equal(createdLayers.length, 2);
 for (const layer of createdLayers) {
  assert.equal(layer.options.maxNativeZoom, 19);
  assert.equal(layer.options.maxZoom, MAX_MAP_ZOOM);
 }
});
test('interactive mission map preserves user zoom on waypoint edits and cancels auto-locate on zoom',()=>{
 assert.match(mapSource, /maxZoom:\s*MAX_MAP_ZOOM/);
 assert.match(mapSource, /const shouldFit = interactiveRef\.current\s*\?\s*isInitialMapRender/);
 assert.match(mapSource, /if \(shouldFit && waypoints\.length > 0\)/);
 assert.match(locationSource, /movestart zoomstart click/);
});
test('geofence zone controls sit directly above MissionMap in create and edit mission flows for manual and automatic modes',()=>{
 for (const src of [formSource, editSource]) {
  const routeBuilderIdx = src.indexOf('<section className="route-builder">');
  const fenceIdx = src.indexOf('<MissionFenceFields');
  const mapIdx = src.indexOf('<MissionMap');
  assert.ok(routeBuilderIdx > 0 && fenceIdx > routeBuilderIdx && mapIdx > fenceIdx);
  assert.equal(src.indexOf('<MissionFenceFields'), src.lastIndexOf('<MissionFenceFields'));
 }
 const html = renderToStaticMarkup(React.createElement(MissionFenceFields, {
  fences: [circle],
  onChange: () => {},
  drawing: 'Circle',
  onDraw: () => {},
  vertexCount: 1,
  onFinish: () => {},
 }));
 assert.match(html, /aria-pressed="true"[^>]*>CIRCULAR FENCE/);
 assert.match(html, /for="mission-fence-radius-circle"/);
 assert.match(html, /id="mission-fence-radius-circle"/);
 assert.match(html, /Click the map below/);
 assert.match(html, /class="fenceItem"/);
 assert.match(html, /class="deleteButton"[^>]*>DELETE/);

 const invalidHtml = renderToStaticMarkup(React.createElement(MissionFenceFields, {
  fences: [{ ...circle, radiusMeters: 0 }],
  onChange: () => {},
  drawing: null,
  onDraw: () => {},
  vertexCount: 0,
  onFinish: () => {},
 }));
 assert.match(invalidHtml, /aria-invalid="true"/);
 assert.match(invalidHtml, /role="img" aria-label="Error"/);
 assert.match(invalidHtml, /1–100,000 m/);
});
test('mission form aligns fence delete controls, avoids option-text leaks in errors, and keeps submit enabled for validation feedback',()=>{
 assert.match(fenceCssSource, /\.radiusInputWrap\s*\{[^}]*height:\s*38px/);
 assert.match(fenceCssSource, /\.deleteButton\s*\{[^}]*height:\s*38px/);
 assert.match(fenceCssSource, /color-scheme:\s*dark/);
 assert.match(formSource, /export function describeInvalidMissionField/);
 assert.match(formSource, /data-field-label="Assigned UAV"/);
 assert.match(formSource, /Flight mode: Choose Manual operation or Automatic mission before saving\./);
 assert.doesNotMatch(formSource, /disabled=\{saving \|\| activeAircraft\.length === 0 \|\| flightMode === null\}/);
 assert.match(suggestionSource, /promptTouched/);
});
