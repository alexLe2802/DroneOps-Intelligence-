import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';
const require = createRequire(import.meta.url);
const compile = source => 'data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source, {compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText.replace('"react/jsx-runtime"', JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href))).toString('base64');
const time = compile(await readFile(new URL('../src/lib/portal/vietnam-time.ts',import.meta.url),'utf8'));
const source = (await readFile(new URL('../src/app/portal/missions/waypoint-navigation-fields.tsx',import.meta.url),'utf8')).replace('from "@/lib/portal/vietnam-time"', `from ${JSON.stringify(time)}`);
const {default:Fields,FirmwareDefaultsNotice:Notice} = await import(compile(source));
const point = {sequenceOrder:2,latitude:10.7,longitude:106.7,altitude:20,actionType:'FlyThrough'};
const render = value => renderToStaticMarkup(React.createElement(Fields,{point:value,maximumSpeed:3,onChange:()=>{}}));
test('optional waypoint values are not fabricated before FC connection',()=>{
 const html=render(point);
 assert.match(html,/Speed override .*optional/); assert.match(html,/Read from FC/);
 assert.match(html,/Pass radius .*unavailable/); assert.match(html,/disabled=""/);
 assert.doesNotMatch(html,/required=""/);
});
test('Hover duration is conditionally required and visible',()=>{
 const html=render({...point,actionType:'Hover'});
 assert.match(html,/required for Hover/); assert.match(html,/required=""/); assert.match(html,/<details open=""/);
});
test('candidate landing site needs ground and clearance notes rather than a normal coordinate alone',()=>{
 const html=render({...point,navigation:{emergencyLandingCandidate:true}});
 assert.match(html,/Landing ground altitude/); assert.match(html,/clearance notes/);
 assert.equal((html.match(/required=""/g)||[]).length,2);
});
test('takeoff does not offer command parameters that the compiler would ignore',()=>{
 const html=render({...point,actionType:'TakeOff'});
 assert.doesNotMatch(html,/Speed override/); assert.doesNotMatch(html,/Hold time/); assert.doesNotMatch(html,/Acceptance radius/);
});
test('FC cache states its source and never substitutes a made-up Home',()=>{
 const missing=renderToStaticMarkup(React.createElement(Notice,{}));
 assert.match(missing,/No FC parameter snapshot/); assert.match(missing,/Home is read from the bound FC/);
 const known=renderToStaticMarkup(React.createElement(Notice,{snapshot:{firmwareVersion:'4.6.3',observedAtUtc:'2026-11-04T17:00:00Z',values:{WPNAV_SPEED:300,WPNAV_RADIUS:200},source:'FC MAVLink'}}));
 assert.match(known,/cruise 3 m\/s/); assert.match(known,/acceptance radius 2 m/); assert.match(known,/GMT\+7/);
});
