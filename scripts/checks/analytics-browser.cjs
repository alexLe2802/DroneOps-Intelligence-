// Local browser regression with synthetic data only. Requires playwright-core and Chrome.
const http = require('node:http');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright-core');
const root = process.cwd();
const uiPort = 3188, apiPort = 5188;
const origin = `http://127.0.0.1:${uiPort}`;
const id = '00000000-0000-0000-0000-000000000001';
const uav = '00000000-0000-0000-0000-000000000002';
const user = '00000000-0000-0000-0000-000000000003';
const timestamp = '2026-11-14T01:00:00Z';
const token = `fixture.${Buffer.from(JSON.stringify({exp:Math.floor(Date.now()/1000)+3600})).toString('base64url')}.fixture`;
const profile = {id:user,fullName:'Fixture Operator',email:'fixture@example.test',role:'uav_operator',createdAt:timestamp,profileVersion:1};
const mission = {id,name:'Fixture inspection',createdBy:user,createdAt:timestamp,status:'Completed',description:'Synthetic route',uavId:uav,uavCode:'FIX-01',startTime:timestamp,endTime:'2026-11-14T01:10:00Z',waypoints:[],validation:null};
const aircraft = {id:uav,name:'Fixture UAV',code:'FIX-01',status:'Active'};
const report = {reportId:id,missionId:id,missionName:mission.name,status:'Completed',generationStatus:'Available',revision:1,isCurrent:true,uavName:aircraft.name,uavCode:aircraft.code,pilotName:profile.fullName,startTime:timestamp,endTime:mission.endTime,totalFlightDurationMinutes:10,totalDistanceMeters:100,totalTelemetryRecords:2,totalIncidents:0,summary:'Synthetic report',createdAt:timestamp,generatedAt:timestamp,executionRoute:{planHash:'fixture-immutable-plan',startedAt:timestamp,altitudeReference:'MSL',waypoints:[{sequenceOrder:1,latitude:12,longitude:108,altitude:null},{sequenceOrder:2,latitude:12.001,longitude:108.001,altitude:40}]}};
const history = {missionId:id,missionName:mission.name,status:'Completed',uavId:uav,uavCode:'FIX-01',uavName:aircraft.name,pilotName:profile.fullName,startTime:timestamp,endTime:mission.endTime,durationMinutes:10,totalIncidents:0,hasReport:true,reportStatus:'Available',reportRevision:1,telemetryCoverageComplete:true};
const requests = [];
const paged = items => ({items,totalItems:items.length,totalCount:items.length,totalPages:1,pageNumber:1,pageSize:8});
const api = http.createServer((req,res)=>{
  const url = new URL(req.url,`http://127.0.0.1:${apiPort}`);
  requests.push({method:req.method,path:url.pathname,query:url.searchParams});
  let data;
  if(url.pathname === '/api/users/profile') data=profile;
  else if(url.pathname === '/api/auth/login') data={accessToken:token};
  else if(url.pathname === '/api/missions/my') data=[mission];
  else if(url.pathname === `/api/missions/${id}`) data=mission;
  else if(url.pathname === '/api/geofences') data=[];
  else if(url.pathname === '/api/notifications') data={unreadCount:0,items:[]};
  else if(url.pathname === '/api/uavs/myUAV') data=paged([aircraft]);
  else if(url.pathname === '/api/flight-history/analytics') data={totalFlights:1,completedFlights:1,abortedOrFailedFlights:0,totalFlightHours:.17,totalDistanceKilometers:.1,totalIncidentsRecorded:0,successRatePercentage:100,completionRateStatus:'Available',reportsAvailable:1,reportsExcluded:0,reportCoveragePercentage:100,fromDate:url.searchParams.get('fromDate'),toDate:url.searchParams.get('toDate'),uavId:url.searchParams.get('uavId'),missionId:url.searchParams.get('missionId'),completionTrend:[{date:'2026-11-14',totalFlights:1,completedFlights:1,failedOrAbortedFlights:0}],incidentTypeBreakdown:{},incidentSeverityBreakdown:{},excludedReportReasons:{},contract:{windowBasis:'Mission.StartTime',windowTimeZone:'UTC',communicationLossThresholdSeconds:30,units:{totalFlightHours:'hours',totalDistanceKilometers:'kilometers'},sourceLinks:{missions:'/api/flight-history'}},missingCoverage:{validDistanceSegments:1,excludedDistanceSegments:0,excludedGapSeconds:0,missingGpsSamples:0,invalidTelemetrySamples:0,missionsWithoutValidGpsCoverage:0,missionsWithoutAvailableReport:0}};
  else if(url.pathname === '/api/flight-history') data=paged([history]);
  else if(url.pathname === `/api/flight-history/${id}/report`) data=report;
  else if(url.pathname === `/api/flight-history/${id}/report/revisions`) data=[report];
  else if(url.pathname === `/api/flight-history/${id}/telemetry`) data=[{latitude:12,longitude:108,recordedAt:timestamp},{latitude:12.0009,longitude:108.0011,recordedAt:'2026-11-14T01:00:05Z'}];
  else if(url.pathname === `/api/flight-history/${id}/incidents`) data=[];
  else if(url.pathname === '/api/users/pilot-profile') data={profileVersion:1,phoneNumber:'0901234567',dateOfBirth:'2000-01-01',experienceYears:2,pilotLicenseNo:'FIX-LICENSE'};
  else if(url.pathname === '/api/users/pilot-update-requests') data=paged([]);
  else {res.writeHead(404,{'content-type':'application/json'});res.end(JSON.stringify({message:'Fixture endpoint not configured'}));return;}
  res.writeHead(200,{'content-type':'application/json'});res.end(JSON.stringify(data));
});
let ui, browser;
const evidence=[];
async function check(name,fn) {await fn();evidence.push(name);console.log(`PASS: ${name}`);}
async function main(){
  await new Promise(resolve=>api.listen(apiPort,'127.0.0.1',resolve));
  ui=spawn(process.execPath,['node_modules/next/dist/bin/next','dev','--webpack','--hostname','127.0.0.1','--port',String(uiPort)],{cwd:`${root}/fe`,env:{...process.env,API_BASE_URL:`http://127.0.0.1:${apiPort}`,NEXT_TELEMETRY_DISABLED:'1'},stdio:['ignore','ignore','ignore']});
  for(let i=0;i<90;i++){try{if((await fetch(`${origin}/login`)).ok)break;}catch{}await new Promise(r=>setTimeout(r,500));if(i===89)throw Error('Fixture UI failed to start');}
  browser=await chromium.launch({executablePath:process.env.CHROME_EXECUTABLE || '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',headless:true});
  const context=await browser.newContext({viewport:{width:1440,height:900}});
  await context.route('**/*',route=>new URL(route.request().url()).hostname === '127.0.0.1' ? route.continue() : route.abort());
  const page=await context.newPage();page.setDefaultTimeout(20000);
  await context.addCookies([{name:'droneops_access',value:token,url:origin,httpOnly:true}]);
  await check('archive sends common date/UAV/mission scope to history and analytics',async()=>{
    await page.goto(`${origin}/portal/flight-history`);await page.getByRole('heading',{name:'Operational analytics'}).waitFor();
    await page.getByLabel('From date (UTC)',{exact:true}).fill('2026-11-01');await page.getByLabel('To date (UTC)',{exact:true}).fill('2026-11-14');
    await page.locator('#archive-uav').selectOption(uav);await page.getByLabel('Exact mission ID').fill(id);await page.getByRole('button',{name:'Apply filters'}).click();
    await page.waitForURL('**/portal/flight-history?**');await page.getByRole('heading',{name:'Operational analytics'}).waitFor();
    for(const path of ['/api/flight-history','/api/flight-history/analytics']) assert.ok(requests.some(r=>r.path===path&&r.query.get('uavId')===uav&&r.query.get('missionId')===id&&r.query.get('fromDate')==='2026-11-01T00:00:00.000Z'));
  });
  await check('report shows immutable planned overlay, recorded overlay and scoped back link',async()=>{
    await page.getByRole('link',{name:'VIEW REPORT'}).click();await page.getByRole('heading',{name:'Planned & recorded flight path'}).waitFor();
    await page.getByText('fixture-immutable-plan',{exact:true}).waitFor();
    await page.waitForFunction(()=>document.querySelectorAll('.leaflet-overlay-pane path').length>=2);
    assert.ok(await page.locator('.leaflet-overlay-pane path[stroke-dasharray]').count());
    const back=await page.getByRole('link',{name:'BACK TO FLIGHT HISTORY'}).getAttribute('href');
    assert.equal(new URL(back,origin).searchParams.get('uavId'),uav);assert.equal(new URL(back,origin).searchParams.get('missionId'),id);
    await page.getByRole('link',{name:'BACK TO FLIGHT HISTORY'}).click();await page.getByRole('heading',{name:'Operational analytics'}).waitFor();
  });
  await check('source drill-down keeps selected scope',async()=>{
    await page.getByRole('link',{name:'Inspect source flights, telemetry and incidents'}).click();
    await page.getByRole('heading',{name:'Analytics source flights'}).waitFor();
    assert.equal(new URL(page.url()).searchParams.get('uavId'),uav);
  });
  assert.equal(requests.filter(r=>r.method!=='GET'&&r.path!=='/api/auth/login').length,0,'view-only fixture must not mutate missions or flight controls');
  console.log(JSON.stringify({browser:browser.version(),viewportWidths:[1440],checks:evidence.length,syntheticData:true}));
}
main().catch(error=>{console.error(error.message);process.exitCode=1;}).finally(async()=>{await browser?.close();ui?.kill('SIGTERM');await new Promise(resolve=>api.close(resolve));});
