// Local synthetic API/UI checks. Never connects to production.
const http = require('node:http');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright-core');
const origin='http://127.0.0.1:3191', apiPort=5191;
const id='00000000-0000-0000-0000-000000000061', owner='00000000-0000-0000-0000-000000000065';
const requestId='00000000-0000-0000-0000-000000000066', now='2026-11-14T09:00:00Z';
const profile={id:owner,role:'operation_manager',fullName:'Fixture Manager',email:'fixture@example.test',createdAt:now};
const current={code:'FIX-61',name:'Approved fixture',batteryCapacityMah:5000};
let version=5, pending=true, staleOnce=true; const history=[], decisions=[];
const aircraft=()=>({id,...current,pilotId:owner,pilotName:'Fixture Operator',status:'Active',registryVersion:version,hasApprovedInformation:true,hasPendingProposal:pending,createdAt:now});
const proposed=()=>({...current,name:'Updated fixture',batteryCapacityMah:7000});
const state=()=>({registryVersion:version,status:'Active',hasApprovedInformation:true,current:{...current},pending:pending?{id:requestId,kind:'Update',status:'Pending',submittedBy:owner,submittedAt:now,proposed:proposed()}:null,history,legacyHistoryUnavailable:false});
const paged=items=>({items,totalItems:items.length,totalPages:1,pageNumber:1,pageSize:20});
const server=http.createServer(async(req,res)=>{
 const url=new URL(req.url,origin); let data, status=200;
 if(url.pathname==='/api/users/profile') data=profile;
 else if(url.pathname==='/api/notifications') data={items:[],unreadCount:0};
 else if(url.pathname===`/api/uavs/${id}/reviews`) data=state();
 else if(url.pathname==='/api/admin/uavs'||url.pathname==='/api/uavs/myUAV') data=paged([aircraft()]);
 else if(url.pathname===`/api/admin/uavs/${id}`) data=aircraft();
 else if(req.method==='PUT'&&url.pathname.startsWith(`/api/admin/uavs/${id}/`)){
  let body='';for await(const chunk of req)body+=chunk;const decision=JSON.parse(body);decisions.push(decision);
  assert.equal(decision.requestId,requestId);assert.ok(decision.reason.trim());
  if(staleOnce){staleOnce=false;version++;status=409;data={message:'Fixture stale record. Reload before deciding.'};}
  else{assert.equal(decision.expectedVersion,version); const approved=url.pathname.endsWith('/approve');
   history.push({id:`decision-${version}`,kind:'Update',status:approved?'Approved':'Rejected',submittedBy:owner,submittedAt:now,reviewedBy:'Fixture reviewer',reviewedAt:now,reason:decision.reason,currentAtSubmission:{...current},proposed:proposed()});
   if(approved)Object.assign(current,proposed());pending=false;version++;data=aircraft();}
 }
 else {status=404;data={message:'Fixture endpoint unavailable'};}
 res.writeHead(status,{'content-type':'application/json'});res.end(JSON.stringify(data));
});
let ui,browser;
(async()=>{
 await new Promise(resolve=>server.listen(apiPort,'127.0.0.1',resolve));
 ui=spawn(process.execPath,['node_modules/next/dist/bin/next','dev','--webpack','--hostname','127.0.0.1','--port','3191'],{cwd:`${process.cwd()}/fe`,env:{...process.env,API_BASE_URL:`http://127.0.0.1:${apiPort}`,NEXT_TELEMETRY_DISABLED:'1'},stdio:'ignore'});
 for(let i=0;i<90;i++){try{if((await fetch(`${origin}/login`)).ok)break;}catch{}await new Promise(r=>setTimeout(r,500));if(i===89)throw Error('UI did not start');}
 browser=await chromium.launch({executablePath:'/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});
 await context.route('**/*',r=>new URL(r.request().url()).hostname==='127.0.0.1'?r.continue():r.abort());
 const token=`fixture.${Buffer.from(JSON.stringify({exp:Math.floor(Date.now()/1000)+3600})).toString('base64url')}.fixture`;
 await context.addCookies([{name:'droneops_access',value:token,url:origin,httpOnly:true}]);
 const page=await context.newPage();page.setDefaultTimeout(20000);
 async function open(){await page.goto(`${origin}/portal/uav-management`);await page.getByRole('button').filter({hasText:'FIX-61'}).click();await page.getByRole('heading',{name:'UAV information review'}).waitFor();await page.getByRole('table').filter({hasText:'Current information compared with proposal'}).waitFor();}
 await open();
 const capacity=page.getByRole('row').filter({hasText:'Capacity (mAh)'});assert.match(await capacity.innerText(),/5000[\s\S]*7000/);
 await page.getByRole('button',{name:'Review rejection',exact:true}).click();
 assert.equal(await page.getByRole('button',{name:'Confirm reject',exact:true}).isDisabled(),true);
 await page.getByLabel('Decision reason').fill('Battery evidence incomplete');await page.getByRole('button',{name:'Confirm reject',exact:true}).click();
 await page.getByRole('alert').filter({hasText:'Fixture stale'}).waitFor();assert.equal(decisions.length,1);
 assert.equal(await page.getByRole('button',{name:'Review approval',exact:true}).isDisabled(),true);
 await page.getByRole('button',{name:'Reload review',exact:true}).click();await page.getByRole('button',{name:'Review rejection',exact:true}).click();
 await page.getByLabel('Decision reason').fill('Retain approved capacity');await page.getByRole('button',{name:'Confirm reject',exact:true}).click();
 await page.getByText('No pending information proposal.',{exact:true}).waitFor();assert.equal(current.batteryCapacityMah,5000);assert.equal(decisions.length,2);
 console.log('PASS: comparison, required reason, stale conflict and reject preserves approved data');
 pending=true;version++;await open();await page.getByRole('button',{name:'Review approval',exact:true}).click();
 await page.getByLabel('Decision reason').fill('Capacity verified');await page.getByRole('button',{name:'Confirm approve',exact:true}).click();
 await page.getByText('No pending information proposal.',{exact:true}).waitFor();assert.equal(current.batteryCapacityMah,7000);
 await page.getByText('Reviewed: Fixture reviewer',{exact:false}).first().waitFor();
 console.log('PASS: approve submits exact proposal/version and renders saved audit');
 pending=true;version++;await open();await page.setViewportSize({width:390,height:844});
 const region=page.getByRole('region',{name:'Current and proposed UAV information'});await region.focus();assert.equal(await region.getAttribute('tabindex'),'0');
 assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth>innerWidth),false);
 console.log('PASS: comparison remains keyboard-scrollable at 390px without page overflow');
 profile.role='uav_operator';await page.goto(`${origin}/portal/uav/${id}`);
 await page.getByRole('heading',{name:'UAV information review'}).waitFor();await page.getByText('Pending Update',{exact:false}).waitFor();
 assert.equal(await page.getByRole('button',{name:'Review approval',exact:true}).count(),0);
 console.log('PASS: Operator sees proposal/history without Manager actions');
})().catch(e=>{console.error(e);process.exitCode=1;}).finally(async()=>{await browser?.close();ui?.kill('SIGTERM');await new Promise(resolve=>server.close(resolve));});
