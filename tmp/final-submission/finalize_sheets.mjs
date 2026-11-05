import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile} from 'file:///C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const root='D:/DroneOps-Intelligence-',dir=`${root}/tmp/final-submission`;
const cases=JSON.parse(await fs.readFile(`${dir}/flight-cases.json`,'utf8'));
const units=[
 ['UT-AF-01','FN-11','InConfirmationWindow','B','Check T-5 minus a tick, T-5, start minus a tick and start.','Consent is accepted only in [T-5 minutes,start).'],
 ['UT-AF-02','FN-12','ReadAsync / ConfirmAsync','A','Change plan/connection/upload hash or expire presence/preflight.','Stale or mismatched binding blocks authorization.'],
 ['UT-AF-03','FN-13','EvaluateDispatchGates','N','Supply an approved schedule, verified upload, consent and all fresh gates.','Dispatch eligible only when all gates and release configuration permit; consent alone never starts flight.'],
 ['UT-AF-04','FN-14','NormalizeMissionReadBack','B','Compare float/frame/sequence normalization and altered downloaded mission.','Equivalent normalized content matches; mismatch blocks dispatch.'],
 ['UT-AF-05','FN-15','DeduplicateExecutionEnvelope','A','Repeat commandId across delivery retry and gateway restart.','One durable receipt/outcome; no duplicate physical start.'],
 ['UT-AF-06','FN-16','CreateScheduledDispatch','A','Race two workers; expire the start+5s deadline; remove a gate.','One identity at most; expired or ineligible dispatch is blocked.'],
 ['UT-AF-07','FN-17','ReconcileFlightState','N','Receive upload/command ACK on ground, then actual armed/in-air evidence.','ACK alone does not create InProgress; observed evidence creates one immutable execution.']
];
const manifests=[];
async function load(file){const path=`${root}/docs/reports/${file}`;await fs.mkdir(`${dir}/originals/docs/reports`,{recursive:true});await fs.copyFile(path,`${dir}/originals/docs/reports/${file}`);return await SpreadsheetFile.importXlsx(await FileBlob.load(path));}
async function png(wb,sheetName,range,name){const blob=await wb.render({sheetName,range,scale:1.3});await fs.writeFile(`${dir}/${name}.png`,new Uint8Array(await blob.arrayBuffer()));}
function cell(wb,list,sheet,address,value,formula=false){const rg=wb.worksheets.getItem(sheet).getRange(address);if(formula)rg.formulas=[[value]];else rg.values=[[value]];list.push({sheet,cell:address,...(formula?{formula:value}:{value})});}
let wb=await load('FA26SE238_Project_Schedule.xlsx'),changes=[];
cell(wb,changes,'Plan Notes','A18','Scheduled automatic flight');
cell(wb,changes,'Plan Notes','B18','Final scope: reviewed opt-in, firmware/profile compilation, upload/read-back, pilot consent in [T-5 min,start), atomic scheduled dispatch, gateway local checks and observed execution/completion. Owner ThongLQ; NghiaHQ owns planning/preflight; DucDDA owns post-flight; KhanhGNG owns advisory AI.');
cell(wb,changes,'Plan Notes','A19','Dispatch and acceptance');
cell(wb,changes,'Plan Notes','B19','All gates are conjunctive. Presence/heartbeat <=30 s, telemetry <=15 s, preflight <=60 s; exact UID/lease/plan/compiled hash binding. Envelope expires at start+5 s; retry delivery at most three times with the same commandId; no replay/catch-up. AF-01–16 and UT-AF-01–07 define final acceptance. Execution status is recorded from actual evidence.');
cell(wb,changes,'Project Schedule','I38','MF2 owner ThongLQ: gateway, compiled mission upload/read-back, operator consent, scheduled dispatch, local pre-arm and observed execution; realtime/incident integration remains in this flow.');
cell(wb,changes,'Project Schedule','I40','Final acceptance includes AF-01–AF-16 and unit UT-AF-01–UT-AF-07; record actual SITL/bench/supervised-aircraft evidence separately. No automatic pass result is implied by document finalization.');
cell(wb,changes,'Plan Notes','B12','Configured external AI provider with authorized context, advisory output and failure handling; AI does not approve or launch. Scheduled automatic execution follows the reviewed plan, pilot consent and gateway dispatch contract.');
for(const c of changes)wb.worksheets.getItem(c.sheet).getRange(c.cell).format.wrapText=true;
wb.worksheets.getItem('Plan Notes').getRange('A18:B19').format.rowHeight=130;
wb.worksheets.getItem('Project Schedule').getRange('A38:I40').format.rowHeight=100;
wb.recalculate();await png(wb,'Plan Notes','A18:B19','schedule-final');await(await SpreadsheetFile.exportXlsx(wb)).save(`${dir}/schedule-authored.xlsx`);
manifests.push({kind:'schedule',file:`${root}/docs/reports/FA26SE238_Project_Schedule.xlsx`,changes});
wb=await load('Report5_Test Report.xlsx');changes=[];
const ts=wb.worksheets.getItem('Telemetry');
for(let i=0;i<16;i++){
 const r=20+i;ts.getRange(`A${r}:O${r}`).copyFrom(ts.getRange('A19:O19'),'all');
 const [code,title,proc,expected]=cases[i];const vals=[code,title,proc,expected,'Approved plan; supported profile; scoped owner/UAV/UID/lease; controlled clock, transfer and telemetry fixtures.','Pending',null,null,'Pending',null,null,'Pending',null,null,`FR-AF-01–12; UC-AF-01–03; owner ThongLQ; final test specification; result recorded on execution.`];
 for(let j=0;j<vals.length;j++)cell(wb,changes,'Telemetry',`${String.fromCharCode(65+j)}${r}`,vals[j]);
}
cell(wb,changes,'Telemetry','B3','UC-13–14; UC-AF-01–03; FR-AF-01–12; owner ThongLQ');
cell(wb,changes,'Telemetry','B4','=COUNTA(A12:A35)',true);
for(const [r,statusCol] of [[6,'F'],[7,'I'],[8,'L']])for(const [col,status] of [['B','Passed'],['C','Failed'],['D','Pending'],['E','N/A']])cell(wb,changes,'Telemetry',`${col}${r}`,`=COUNTIF(${statusCol}12:${statusCol}35,"${status}")`,true);
cell(wb,changes,'Test Cases','E10','Telemetry and full scheduled automatic execution: AF-01–AF-16; owner ThongLQ.');
cell(wb,changes,'Cover','B6','FA26SE238_Test_Report_v1.0');cell(wb,changes,'Cover','F6','1.0 Final');
cell(wb,changes,'Test Statistics','C5','FA26SE238_Test_Report_v1.0');
cell(wb,changes,'Test Statistics','C6','Final test specification: 56 functional cases. Five recorded integration passes are retained; remaining execution states stay evidence-based. Automatic flight coverage: AF-01–AF-16.');
ts.getRange('A20:O35').format.wrapText=true;ts.getRange('A20:O35').format.rowHeight=120;
wb.recalculate();await png(wb,'Telemetry','A28:F31','functional-final1');await png(wb,'Telemetry','A32:F35','functional-final2');await png(wb,'Test Statistics','B10:H21','functional-stats-final');
await(await SpreadsheetFile.exportXlsx(wb)).save(`${dir}/tests-authored.xlsx`);manifests.push({kind:'tests',file:`${root}/docs/reports/Report5_Test Report.xlsx`,changes});
wb=await load('Report5_Unit Test.xlsx');const f=wb.worksheets.getItem('Functions'),st=wb.worksheets.getItem('Statistics'),u=wb.worksheets.getItem('FlightPreparation');
for(let i=0;i<7;i++){
 const row=21+i;f.getRange(`A${row}:H${row}`).copyFrom(f.getRange('A20:H20'),'all');f.getRange(`A${row}:H${row}`).values=[[11+i,'FR-AF-01–12','Automation service / gateway contract',units[i][2],units[i][1],'FlightPreparation','Scheduled execution and evidence; owner ThongLQ','Controlled fixture; actual execution status recorded separately']];f.getRange(`A${row}:H${row}`).format.wrapText=true;f.getRange(`A${row}:H${row}`).format.rowHeight=65;
}
const baseline=st.getRange('A12:I21').values;
st.getRange('A12:I36').clear({applyTo:'contents'});
st.getRange('A12:I21').values=baseline;
for(let i=0;i<7;i++)st.getRange(`A${22+i}:I${22+i}`).values=[[11+i,units[i][1],0,0,1,units[i][3]==='N'?1:0,units[i][3]==='A'?1:0,units[i][3]==='B'?1:0,1]];
st.getRange('B30').values=[['Sub total']];st.getRange('C30:I30').formulas=[['=SUM(C12:C28)','=SUM(D12:D28)','=SUM(E12:E28)','=SUM(F12:F28)','=SUM(G12:G28)','=SUM(H12:H28)','=SUM(I12:I28)']];
st.getRange('B7').values=[['Final unit specification: 17 cases, including UT-AF-01–UT-AF-07. Execution statuses remain Untested until evidence is recorded. Example sheet excluded.']];
for(const [r,label,formula] of [[32,'Test coverage','=IFERROR((C30+D30)*100/I30,0)'],[33,'Test successful coverage','=IFERROR(C30*100/I30,0)'],[34,'Normal case','=F30*100/I30'],[35,'Abnormal case','=G30*100/I30'],[36,'Boundary case','=H30*100/I30']]){st.getRange(`B${r}`).values=[[label]];st.getRange(`E${r}`).formulas=[[formula]];st.getRange(`F${r}`).values=[['%']];}
st.getRange('A12:I28').format.fill='#FFFFFF';st.getRange('A12:I28').format.font={name:'Arial',size:11,color:'#000000'};st.getRange('A12:I30').format.borders={preset:'all',style:'thin',color:'#D9D9D9'};st.getRange('C12:I30').setNumberFormat('0');st.getRange('E32:E36').setNumberFormat('0.0');st.getRange('A30:I30').format.fill='#1F4E78';st.getRange('A30:I30').format.font={name:'Arial',size:11,color:'#FFFFFF',bold:true};st.getRange('A12:I30').format.rowHeight=23;
u.getRange('A1').values=[['Automatic flight execution unit test specification']];u.getRange('A2').values=[['Final specification; owner ThongLQ (MF2), NghiaHQ preflight support. Status records execution evidence independently of document status.']];
u.getRange('A5:G13').clear({applyTo:'contents'});u.getRange('A9:G9').unmerge();u.getRange('A5:G11').values=units.map(c=>[...c,'Untested']);u.getRange('A13:G13').merge();u.getRange('A13').values=[['N normal; A abnormal; B boundary. Verify consent, transfer, deadline, replay protection and observed flight-state transitions.']];u.getRange('A5:G11').format.wrapText=true;u.getRange('A5:G11').format.rowHeight=90;u.getRange('A5:G11').format.verticalAlignment='center';u.getRange('A4:G11').format.borders={preset:'all',style:'thin',color:'#D9D9D9'};u.getRange('A13:G13').format.wrapText=true;u.getRange('A13:G13').format.rowHeight=36;
wb.worksheets.getItem('Cover').getRange('B6').values=[['FA26SE238_UnitTest_v1.0']];wb.worksheets.getItem('Cover').getRange('F6').values=[['1.0 Final']];
wb.recalculate();await png(wb,'FlightPreparation','A1:G13','unit-final');await png(wb,'Statistics','A11:I36','unit-stats-final');
await(await SpreadsheetFile.exportXlsx(wb)).save(`${root}/docs/reports/Report5_Unit Test.xlsx`);
await fs.writeFile(`${dir}/sheet-plan.json`,JSON.stringify(manifests,null,2));console.log('Authored final workbooks: functional 56 cases (existing 5 passes retained); unit 17 cases.');
