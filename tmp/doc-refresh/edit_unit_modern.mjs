import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile} from 'file:///C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const dir='D:/DroneOps-Intelligence-/tmp/doc-refresh';
const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(`${dir}/unit-modern-source.xlsx`));
for(const [name,range] of [['Functions','A10:H20'],['Statistics','A11:I30']]){const png=await wb.render({sheetName:name,range,scale:1.2});await fs.writeFile(`${dir}/unit-${name}-before.png`,new Uint8Array(await png.arrayBuffer()));}
const cases=[
 ['UT-AF-01','FN-11','InConfirmationWindow','B','Check T-5 minus one tick, T-5, start minus one tick and start.','True only in [T-5 minutes, start), using backend time.'],
 ['UT-AF-02','FN-12','ReadAsync / ConfirmAsync','A','Change plan hash or connection after confirmation; expire presence or passing preflight.','Readiness false for mismatched or stale evidence; scope remains owner/UAV/UID.'],
 ['UT-AF-03','FN-13','ConfirmAsync / PlanAsync','N','Confirm with an approved opted-in mission and all fresh scoped prerequisites.','dispatchEnabled=false; no aircraft command or readiness-driven InProgress transition.']
];
const f=wb.worksheets.getItem('Functions');
for(let i=0;i<3;i++){let row=21+i;f.getRange(`A${row}:H${row}`).copyFrom(f.getRange('A20:H20'),'all');f.getRange(`A${row}:H${row}`).values=[[11+i,'FR-AF-01–04','MissionAutomationService',cases[i][2],cases[i][1],'FlightPreparation','Automatic flight readiness; owner ThongLQ','Isolated fixture; controlled clock; Untested']];f.getRange(`A${row}:H${row}`).format.rowHeight=58;}
const st=wb.worksheets.getItem('Statistics');
st.getRange('A25:I33').copyFrom(st.getRange('A22:I30'),'all');st.getRange('A22:I24').clear({applyTo:'contents'});
for(let i=0;i<3;i++){let r=22+i;st.getRange(`A${r}:I${r}`).copyFrom(st.getRange('A21:I21'),'all');st.getRange(`A${r}:I${r}`).values=[[11+i,cases[i][1],0,0,1,cases[i][3]==='N'?1:0,cases[i][3]==='A'?1:0,cases[i][3]==='B'?1:0,1]];}
st.getRange('C26:I26').formulas=[['=SUM(C12:C24)','=SUM(D12:D24)','=SUM(E12:E24)','=SUM(F12:F24)','=SUM(G12:G24)','=SUM(H12:H24)','=SUM(I12:I24)']];
st.getRange('B7').values=[['13 planned unit cases; all Untested. Includes three flight-preparation cases; aircraft dispatch disabled. Example sheet excluded.']];
for(const [r,formula] of [[28,'=IFERROR((C26+D26)*100/I26,0)'],[29,'=IFERROR(C26*100/I26,0)'],[30,'=F26*100/I26'],[31,'=G26*100/I26'],[32,'=H26*100/I26']])st.getRange(`E${r}`).formulas=[[formula]];
st.getRange('C28:D32').clear({applyTo:'contents'});st.getRange('F28:F32').values=[['%'],['%'],['%'],['%'],['%']];
st.getRange('E28:E32').setNumberFormat('0.0');
st.getRange('A12:I24').format.fill='#FFFFFF';st.getRange('A12:I24').format.font={name:'Arial',size:11,color:'#000000'};
st.getRange('A11:I26').format.borders={preset:'all',style:'thin',color:'#D9D9D9'};st.getRange('C12:I26').setNumberFormat('0');
st.getRange('A26:I26').format.fill='#1F4E78';st.getRange('A26:I26').format.font={name:'Arial',size:11,color:'#FFFFFF',bold:true};
st.getRange('C12:I26').format.horizontalAlignment='center';st.getRange('A12:I26').format.rowHeight=23;
const sh=wb.worksheets.add('FlightPreparation');
sh.getRange('A1:G1').merge();sh.getRange('A1').values=[['Automatic flight preparation unit test design']];
sh.getRange('A2:G2').merge();sh.getRange('A2').values=[['Owner ThongLQ (MF2); NghiaHQ supports preflight. Controlled service fixtures; no execution/pass or hardware claim.']];
sh.getRange('A4:G4').values=[['Case','Function','Method','Type','Input / boundary','Expected result','Status']];
sh.getRange('A5:G7').values=cases.map(c=>[...c,'Untested']);
sh.getRange('A9:G9').merge();sh.getRange('A9').values=[['Types: N normal, A abnormal, B boundary. New tests do not enable upload, ARM, TAKEOFF or AUTO/MISSION_START.']];
sh.getRange('A1:G9').format.font={name:'Arial',size:11};sh.getRange('A4:G7').format.borders={preset:'all',style:'thin',color:'#D9D9D9'};
sh.getRange('A4:G4').format.fill='#1F4E78';sh.getRange('A4:G4').format.font={name:'Arial',size:11,bold:true,color:'#FFFFFF'};
sh.getRange('A1:G1').format.font={name:'Arial',size:18,bold:true};sh.getRange('A1:G9').format.wrapText=true;
for(const [col,width] of [['A',15],['B',13],['C',28],['D',8],['E',55],['F',55],['G',14]])sh.getRange(`${col}:${col}`).format.columnWidth=width;
sh.getRange('A5:G7').format.rowHeight=85;sh.getRange('A2:G2').format.rowHeight=40;sh.getRange('A4:G4').format.rowHeight=30;sh.getRange('A9:G9').format.rowHeight=32;
sh.getRange('A4:G7').format.verticalAlignment='center';
wb.recalculate();
console.log((await wb.inspect({kind:'table',range:'Statistics!B22:I26',include:'values,formulas',tableMaxRows:5,tableMaxCols:8,maxChars:1500})).ndjson);
for(const [name,range,label] of [['FlightPreparation','A1:G9','unit-cases-after'],['Statistics','A11:I32','unit-stats-after']]){const png=await wb.render({sheetName:name,range,scale:1.3});await fs.writeFile(`${dir}/${label}.png`,new Uint8Array(await png.arrayBuffer()));}
await (await SpreadsheetFile.exportXlsx(wb)).save('D:/DroneOps-Intelligence-/docs/reports/Report5_Unit Test.xlsx');
console.log('Saved modern unit workbook; 13 Untested, 0 Passed.');
