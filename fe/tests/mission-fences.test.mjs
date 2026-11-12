import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import ts from 'typescript';
const source=await readFile(new URL('../src/lib/maps/mission-fences.ts',import.meta.url),'utf8');
const code=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.ESNext}}).outputText;
const {withMissionFences,missionFencesError,fenceDistance}=await import('data:text/javascript;base64,'+Buffer.from(code).toString('base64'));
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
