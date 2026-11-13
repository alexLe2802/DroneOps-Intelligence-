import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import React from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import ts from 'typescript';

const require = createRequire(import.meta.url);
const compile = source => 'data:text/javascript;base64,' + Buffer.from(ts.transpileModule(source.replace('import RecoveryEnergyProfileFields from "./recovery-energy-profile";', 'const RecoveryEnergyProfileFields = () => null;').replace('import styles from "./mission-authoring.module.css";', 'const styles = new Proxy({}, {get: (_, key) => key});').replace('from "lucide-react"', `from ${JSON.stringify(pathToFileURL(require.resolve('lucide-react')).href)}`), {
  compilerOptions: {module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX},
}).outputText.replace('"react/jsx-runtime"',JSON.stringify(pathToFileURL(require.resolve('react/jsx-runtime')).href))).toString('base64');
const {automaticFlightError} = await import(compile(await readFile(new URL('../src/lib/portal/automatic-flight-limits.ts',import.meta.url),'utf8')));
const {default:Mode} = await import(compile(await readFile(new URL('../src/app/portal/missions/flight-mode-fields.tsx',import.meta.url),'utf8')));
const {defaultAutomaticLimits} = await import(compile(await readFile(new URL('../src/app/portal/missions/automatic-flight-fields.tsx',import.meta.url),'utf8')));
const points = [
  {sequenceOrder:1,latitude:10.8,longitude:106.8,altitude:20,actionType:'TakeOff'},
  {sequenceOrder:2,latitude:10.801,longitude:106.801,altitude:20,actionType:'Hover',navigation:{holdTimeSeconds:5}},
  {sequenceOrder:3,latitude:10.8,longitude:106.8,altitude:20,actionType:'Land'},
];
const validate = (limits=defaultAutomaticLimits(), route=points, cruise=3, maximum=3,frame='Relative') => automaticFlightError(frame,cruise,maximum,limits,route);

<<<<<<< HEAD
=======
test('v2 higher defaults validate while v1 retains original speed caps',()=>{
 const value=defaultAutomaticLimits();
 assert.equal(value.policyVersion,'multicopter-envelope-v2');
 assert.equal(value.takeoffSpeedMetersPerSecond,6.5);assert.equal(value.climbSpeedMetersPerSecond,6.5);
 assert.equal(value.descentSpeedMetersPerSecond,5.8);assert.equal(value.landingSpeedMetersPerSecond,1);
 assert.equal(validate(value,points,11.2,15),null);
 assert.match(validate({...value,landingSpeedMetersPerSecond:1.6},points,11.2,15),/landing/i);
 const legacy={...value,policyVersion:'multicopter-envelope-v1',takeoffSpeedMetersPerSecond:1,climbSpeedMetersPerSecond:1,descentSpeedMetersPerSecond:.7,landingSpeedMetersPerSecond:.7};
 assert.equal(validate(legacy),null);assert.match(validate(legacy,points,11.2,15),/maximum 5/);
});

>>>>>>> cec74278727e75d9815570c2eabb47decac1b62e
test('new mission requires a visible deliberate mode selection',()=>{
 const html=renderToStaticMarkup(React.createElement(Mode,{value:null,onChange:()=>{}}));
 assert.match(html,/Manual operation/); assert.match(html,/Automatic mission/);
 assert.equal((html.match(/type="radio"/g)||[]).length,2);
 assert.equal((html.match(/required=""/g)||[]).length,2);
 assert.doesNotMatch(html,/checked=""/);
});
test('automatic mode keeps the compact selection without removed instructional copy',()=>{
 const html=renderToStaticMarkup(React.createElement(Mode,{value:'automatic',onChange:()=>{}}));
 assert.equal((html.match(/checked=""/g)||[]).length,1);
 assert.doesNotMatch(html,/You fly|Set bounded|Plan →|Launch remains unavailable/);
});
test('default and 10kmh profiles accept valid routes without invented landing ground',()=>{
 assert.equal(validate(),null);
 assert.equal(validate(defaultAutomaticLimits(),points,2.77,2.77),null);
});
test('switching a 50m manual route to automatic requires a deliberate height correction',()=>{
 const route=points.map(p=>({...p,altitude:50}));
 assert.match(validate(defaultAutomaticLimits(),route),/Waypoint 1.*30 m/);
 assert.equal(route[0].altitude,50);
});
test('cross-field rates and wind limits fail before submitting',()=>{
<<<<<<< HEAD
 assert.match(validate({...defaultAutomaticLimits(),takeoffSpeedMetersPerSecond:1.5}),/climb/);
 assert.match(validate({...defaultAutomaticLimits(),descentSpeedMetersPerSecond:0.6}),/landing/);
 assert.match(validate({...defaultAutomaticLimits(),maxGustMetersPerSecond:2}),/gust/);
 assert.match(validate({...defaultAutomaticLimits(),maxAltitudeMeters:NaN}),/restore/);
 assert.match(validate(defaultAutomaticLimits(),points,3,6),/maximum 5/);
=======
 assert.match(validate({...defaultAutomaticLimits(),takeoffSpeedMetersPerSecond:8}),/climb/);
 assert.match(validate({...defaultAutomaticLimits(),descentSpeedMetersPerSecond:0.6}),/landing/);
 assert.match(validate({...defaultAutomaticLimits(),maxGustMetersPerSecond:2}),/gust/);
 assert.match(validate({...defaultAutomaticLimits(),maxAltitudeMeters:NaN}),/restore/);
 assert.match(validate(defaultAutomaticLimits(),points,3,16),/maximum 15/);
>>>>>>> cec74278727e75d9815570c2eabb47decac1b62e
 assert.match(validate(defaultAutomaticLimits(),points,3,3,'AGL'),/relative/);
});
test('Hover cannot be submitted with omitted or zero dwell',()=>{
 for(const hold of [null,0])assert.match(validate(defaultAutomaticLimits(),points.map(p=>p.actionType==='Hover'?{...p,navigation:{holdTimeSeconds:hold}}:p)),/Hover requires/);
});
test('per-waypoint overrides stay within approved command semantics and bounds',()=>{
 assert.match(validate(defaultAutomaticLimits(),points.map(p=>p.actionType==='Hover'?{...p,navigation:{holdTimeSeconds:5,speedMetersPerSecond:4}}:p)),/speed override/);
 assert.match(validate(defaultAutomaticLimits(),points.map(p=>p.actionType==='TakeOff'?{...p,navigation:{speedMetersPerSecond:1}}:p)),/TakeOff uses/);
 assert.match(validate(defaultAutomaticLimits(),points.map(p=>p.actionType==='Hover'?{...p,navigation:{holdTimeSeconds:5,passRadiusMeters:2}}:p)),/unsupported/);
});
test('landing candidate requires measured ground and descriptive clearance',()=>{
 const route=points.map(p=>p.actionType==='Land'?{...p,navigation:{emergencyLandingCandidate:true}}:p);
 assert.match(validate(defaultAutomaticLimits(),route),/ground altitude and clearance/);
 route[2].navigation={emergencyLandingCandidate:true,landingGroundAltitudeMeters:0,landingSiteNotes:'Open level site reviewed for approach.'};
 assert.equal(validate(defaultAutomaticLimits(),route),null);
});
