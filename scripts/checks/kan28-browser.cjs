// Local browser regression with synthetic data only. Requires playwright-core and Chrome.
const http = require('node:http');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright-core');
const root = process.cwd();
const uiPort = 3190, apiPort = 5190;
const origin = `http://127.0.0.1:${uiPort}`;
const id = '00000000-0000-0000-0000-000000000001';
const uav = '00000000-0000-0000-0000-000000000002';
const user = '00000000-0000-0000-0000-000000000003';
const timestamp = '2026-11-14T01:00:00Z';
const token = `fixture.${Buffer.from(JSON.stringify({exp:Math.floor(Date.now()/1000)+3600})).toString('base64url')}.fixture`;
const profile = {id:user,fullName:'Fixture Operator',email:'fixture@example.test',role:'operation_manager',createdAt:timestamp,profileVersion:1};
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
  else if(url.pathname === '/api/users') data=paged([{...profile,role:"uav_operator"}]);
  else if(url.pathname.endsWith('/credentials')) data={phoneNumber:null,dateOfBirth:null,experienceYears:0,pilotLicenseNo:'',licenseIssuedDate:null,licenseExpiredDate:null,licenseImageUrls:[],documentsAvailable:true};
  else if(url.pathname === '/api/missions') data={...paged([mission]),totalCount:1};
  else if(url.pathname.endsWith('/history')) data=[{id:'review',status:'Approved',isInitial:true,createdAt:timestamp,actorId:user,reviewedBy:'Fixture reviewer',reviewedAt:timestamp,reason:'Fixture decision reason',proposedPlan:{uavId:uav}}];
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
  await check('manager drills into scoped allocations and decision history without mutations',async()=>{
    await page.goto(`${origin}/portal/operators`);
    await page.getByRole('heading',{name:'Mission allocations & review history'}).waitFor();
    await page.getByRole('link',{name:mission.name,exact:true}).waitFor();
    assert.ok(requests.some(r=>r.path==='/api/missions'&&r.query.get('createdBy')===user&&r.query.get('pageSize')==='10'));
    await page.getByRole('button',{name:'Review history',exact:true}).click();
    await page.getByText('Reason: Fixture decision reason',{exact:true}).waitFor();
    await page.setViewportSize({width:390,height:844});
    const region=page.getByRole('region',{name:'Operator mission allocations'});
    await region.focus();assert.equal(await region.getAttribute('tabindex'),'0');
    assert.equal(await page.locator('body').evaluate(el=>el.scrollWidth>window.innerWidth),false);
  });
  assert.equal(requests.filter(r=>r.method!=='GET'&&r.path!=='/api/auth/login').length,0,'view-only fixture must not mutate missions or flight controls');
  console.log(JSON.stringify({browser:browser.version(),viewportWidths:[1440,390],checks:evidence.length,syntheticData:true}));
}
main().catch(error=>{console.error(error.message);process.exitCode=1;}).finally(async()=>{await browser?.close();ui?.kill('SIGTERM');await new Promise(resolve=>api.close(resolve));});
