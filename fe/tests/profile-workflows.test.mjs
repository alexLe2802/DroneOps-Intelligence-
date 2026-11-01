import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';
const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString('base64')}`;
const { api } = await import(compile(await readFile(new URL('../src/lib/auth/client.ts', import.meta.url), 'utf8')));
const server = new URL('../node_modules/next/server.js', import.meta.url).href;
let source = await readFile(new URL('../src/app/api/[...path]/route.ts', import.meta.url), 'utf8');
source = source.replace(/^import .*pending-activation.*;$/m, 'const activationCookie="activation"; const clearActivation=()=>{}; const getActivation=()=>null; const createActivation=()=>"fixture";')
  .replace('"next/server"', JSON.stringify(server))
  .replace(/^import .*auth\/server.*;$/m, 'const accessCookie="droneops_access"; const backendUrl=()=>"http://backend.test"; const sessionFromToken=async()=>null;');
const { POST } = await import(compile(source));
const { NextRequest } = await import(server);
function request(action, origin = 'http://localhost:3000') {
  return new NextRequest(`http://localhost:3000/api/users/change-email/${action}`, {method:'POST', headers: { host:'localhost:3000', origin, cookie:'droneops_access=fixture', 'content-type':'application/json'}, body: JSON.stringify({newEmail:'new@example.test', otp:'ABCDE'})});
}
test('email proxy preserves errors and clears cookie only after successful confirmation', async () => {
  for (const [action,status] of [['request',200],['request',429],['confirm',400],['confirm',200]]) {
    const mock = test.mock.method(globalThis,'fetch',async (url, options) => {
      assert.equal(url,`http://backend.test/api/users/change-email/${action}`);
      assert.equal(options.headers.get('authorization'),'Bearer fixture');
      return Response.json({message:'fixture result'}, {status});
    });
    try {
      const result = await POST(request(action),{params:Promise.resolve({path:['users','change-email',action]})});
      assert.equal(result.status,status);
      assert.equal(result.headers.get('cache-control'),'no-store');
      assert.equal(result.headers.get('set-cookie')?.includes('Max-Age=0') ?? false, action === 'confirm' && status === 200);
    } finally { mock.mock.restore(); }
  }
});
test('email proxy rejects foreign origins before contacting backend',async()=>{
  const mock=test.mock.method(globalThis,'fetch',async()=>{throw Error('must not forward');});
  try { assert.equal((await POST(request('confirm','https://other.test'),{params:Promise.resolve({path:['users','change-email','confirm']})})).status,403); assert.equal(mock.mock.calls.length,0); }
  finally {mock.mock.restore();}
});
test('client preserves duplicate, cooldown, expired code and delivery failures',async()=>{
  for(const [status,message] of [[409,'Email already registered'],[429,'Wait 60 seconds'],[400,'Verification code expired'],[503,'Email delivery failed']]) {
    const mock=test.mock.method(globalThis,'fetch',async(url, options)=>{
      assert.equal(url,'/api/users/change-email/request'); assert.equal(options.credentials,'same-origin'); assert.equal(options.cache,'no-store');
      assert.deepEqual(JSON.parse(options.body),{newEmail:'new@example.test',currentPassword:'fixture'});
      return Response.json({message},{status});
    });
    try {await assert.rejects(api('/users/change-email/request',{method:'POST',body:JSON.stringify({newEmail:'new@example.test',currentPassword:'fixture'})}),error=>error.status===status && error.message===message);}
    finally {mock.mock.restore();}
  }
});
const documentUrl=compile(await readFile(new URL('../src/lib/license-document.ts',import.meta.url),'utf8'));
const {validateLicenseChange}=await import(compile((await readFile(new URL('../src/lib/license-change.ts',import.meta.url),'utf8')).replace('"./license-document"',JSON.stringify(documentUrl))));
test('license validation rejects empty number, nonfuture expiry, invalid issue date and oversized images',()=>{
 const now=new Date(2026,9,31,12); const image={name:'scan.png',type:'image/png',size:5*1024*1024};
 const validate=(number='LIC-02',issued='2026-01-01',expires='2027-01-01',files=[image])=>validateLicenseChange(number,issued,expires,files,now);
 assert.equal(validate(),null);
 assert.match(validate('   '),/number is required/);
 assert.match(validate('LIC-02','2026-01-01','2026-10-31'),/future/);
 assert.match(validate('LIC-02','invalid'),/valid issue date/);
 assert.match(validate('LIC-02','2026-11-01'),/not in the future/);
 assert.match(validate('LIC-02','2026-01-01','2027-01-01',[{...image,size:image.size+1}]),/5 MB/);
 assert.match(validate('LIC-02','2026-01-01','2027-01-01',[]),/Attach one/);
 assert.match(validate('LIC-02','2026-01-01','2027-01-01',[image,image]),/Attach one/);
 assert.match(validate('LIC-02','2026-01-01','2027-01-01',[{...image,type:'application/octet-stream'}]),/Attach one/);
});
test('license client sends a single multipart transaction and preserves upload and pending conflicts',async()=>{
 for(const status of [200,409,503]) {
  const body=new FormData(); body.set('data',JSON.stringify({pilotLicenseNo:'LIC-02',licenseIssuedDate:'2026-01-01',licenseExpiredDate:'2027-01-01'})); body.append('files',new Blob(['synthetic']), 'license.png');
  const mock=test.mock.method(globalThis,'fetch',async(url,options)=>{
   assert.equal(url,'/api/users/pilot-update-requests');assert.equal(options.body,body);assert.equal(options.headers.has('content-type'),false);
   return Response.json({message:status===409?'Pending request already exists':'Upload result'},{status});
  });
  try {const result=api('/users/pilot-update-requests',{method:'POST',body});if(status===200) await result;else await assert.rejects(result,error=>error.status===status);assert.equal(mock.mock.calls.length,1);}
  finally{mock.mock.restore();}
 }
});
