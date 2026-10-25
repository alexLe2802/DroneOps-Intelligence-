import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import ts from 'typescript';
const source=await readFile(new URL('../src/lib/portal/map-link-state.ts',import.meta.url),'utf8');
const js=ts.transpileModule(source,{compilerOptions:{module:ts.ModuleKind.ESNext,target:ts.ScriptTarget.ES2022}}).outputText;
const {mapLinkState}=await import('data:text/javascript;base64,'+Buffer.from(js).toString('base64'));
test('map follows online -> stale -> offline -> reconnect while preserving last coordinates',()=>{
 const sample={latitude:10.84,longitude:106.8,connectivityStatus:'Online',isStale:false};
 assert.equal(mapLinkState(sample,true),'online');
 assert.equal(mapLinkState({...sample,connectivityStatus:'Stale',isStale:true},true),'stale');
 assert.equal(mapLinkState({...sample,connectivityStatus:'Offline',isStale:true},true),'offline');
 assert.equal(mapLinkState(sample,true),'online');
 assert.equal(sample.latitude,10.84);
});
test('open web stream does not imply drone online; lost stream does not imply a live position',()=>{
 assert.equal(mapLinkState({connectivityStatus:'Offline',isStale:true},true),'offline');
 assert.equal(mapLinkState({connectivityStatus:'Online',isStale:false},false),'stale');
 assert.equal(mapLinkState({connectivityStatus:'Online',isStale:true},true),'stale');
 assert.equal(mapLinkState(null,true),'offline');
});
