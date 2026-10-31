import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext}}).outputText).toString('base64')}`;
const auth=compile(await readFile(new URL('../src/lib/auth/client.ts',import.meta.url),'utf8'));
const client=await import(compile((await readFile(new URL('../src/lib/portal/drone-connections.ts',import.meta.url),'utf8')).replace('"@/lib/auth/client"',JSON.stringify(auth))));
test('pilot pairing creates a mission-scoped session, reads status and revokes it without integration credentials',async()=>{
 const calls=[];const mock=test.mock.method(globalThis,'fetch',async(url,opts)=>{calls.push([url,opts.method??'GET',opts.body&&JSON.parse(opts.body)]);return opts.method==='DELETE'?new Response(null,{status:204}):Response.json({connection:{id:'session'},pairingCode:'ABCDEF0123456789'});});
 try{await client.createDroneConnection('uav','mission');await client.getDroneConnections();await client.revokeDroneConnection('session');assert.deepEqual(calls,[['/api/drone-connections','POST',{uavId:'uav',missionId:'mission'}],['/api/drone-connections','GET',undefined],['/api/drone-connections/session','DELETE',undefined]]);}finally{mock.mock.restore();}
});
test('browser proxy exposes pilot sessions but not unauthenticated gateway credential exchanges',async()=>{
 const source=await readFile(new URL('../src/app/api/[...path]/route.ts',import.meta.url),'utf8');const regex=source.match(/const allowed = (\/.*\/);/)[1];const allowed=Function(`return ${regex}`)();
 assert.equal(allowed.test('drone-connections'),true);assert.equal(allowed.test('drone-connections/aircraft'),true);assert.equal(allowed.test('drone-connections/11111111-1111-1111-1111-111111111111'),true);assert.equal(allowed.test('drone-gateway/pair'),false);assert.equal(allowed.test('drone-connections/not-a-uuid'),false);
});
test('connect-drone generates one complete Jetson bootstrap command and keeps the laptop radio profile',async()=>{
 const source=await readFile(new URL('../src/app/portal/drone-connection/drone-connection-client.tsx',import.meta.url),'utf8');
 assert.match(source,/JETSON ONBOARD/);assert.match(source,/bootstrap-jetson\.sh/);assert.match(source,/--server/);assert.match(source,/--package-url/);assert.match(source,/--otp/);assert.match(source,/COPY JETSON COMMAND/);
 assert.match(source,/choose the Pixhawk port by number/);
 assert.match(source,/DroneOps\.API\.csproj/);assert.match(source,/repository root/);assert.match(source,/--urls http:\/\/0\.0\.0\.0:5159/);
 assert.match(source,/--gcs-host/);assert.match(source,/continuously shows gateway and GPS logs/);assert.doesNotMatch(source,/QGroundControl · UDP 14550/);assert.doesNotMatch(source,/After deployment has a domain/);
 assert.match(source,/12-hour session expires/);assert.match(source,/Network recovery alone does not require pairing again/);
 assert.match(source,/RADIO TELEMETRY/);assert.match(source,/aria-pressed=\{profile===/);
 assert.match(source,/--device COM5 --baud 57600/);assert.match(source,/Keep this process running/);
 assert.match(source,/\.venv\/bin\/python gateway\.py/);assert.match(source,/Linux or macOS terminal/);assert.match(source,/\/dev\/cu\./);
 assert.doesNotMatch(source,/YOUR-BACKEND/);
 assert.match(source,/Gateway online/);assert.doesNotMatch(source,/Last Jetson contact/);
 assert.doesNotMatch(source,/new WebSocket\s*\(/);assert.doesNotMatch(source,/COM3/);
});
