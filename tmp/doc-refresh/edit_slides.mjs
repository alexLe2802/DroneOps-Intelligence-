import fs from 'node:fs/promises';
import {FileBlob,Presentation,PresentationFile} from 'file:///C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const dir='D:/DroneOps-Intelligence-/tmp/doc-refresh';
const src=await PresentationFile.importPptx(await FileBlob.load('D:/DroneOps-Intelligence-/docs/slides/DroneOps_Review2.pptx'));
for(const i of [2,14,15,16]){
 const png=await src.export({slide:src.slides.items[i],format:'png',scale:1});await fs.writeFile(`${dir}/source-slide-${i+1}.png`,new Uint8Array(await png.arrayBuffer()));
}
const p=Presentation.create({slideSize:{width:1280,height:720}});
function text(s,txt,x,y,w,h,size=24,color='#172E49',bold=false){const sh=s.shapes.add({geometry:'textbox',position:{left:x,top:y,width:w,height:h},fill:'none',line:{fill:'none',width:0}});sh.text=txt;sh.text.style={fontFamily:'Corbel',fontSize:size,color,bold};return sh;}
function slide(eyebrow,title,num){const s=p.slides.add();s.background.fill='#F5F7FA';text(s,eyebrow,56,30,1168,35,17,'#276A86',true);text(s,title,56,86,1168,90,36,'#172E49',true);text(s,'DroneOps Intelligence · FA26SE238',56,673,850,24,15,'#64748B');text(s,String(num),1160,673,60,24,15,'#64748B');return s;}
let s=slide('WORKFLOW OWNERSHIP','Current owners keep the existing flow numbers',20);
const rows=[['MF1 / WF-01','NghiaHQ · Ha Quang Nghia','Mission planning and preflight validation'],['MF2 / WF-02','ThongLQ · Le Quoc Thong','Realtime monitoring, incidents and flight preparation'],['MF3 / WF-03','DucDDA · Dang Dao Anh Duc','Post-flight history, reports and analytics'],['MF4 / WF-04','KhanhGNG · Giang Nguyen Gia Khanh','AI context, provider integration and advisory output']];
rows.forEach((r,i)=>{const y=205+i*93;text(s,r[0],56,y,215,35,24,'#276A86',true);text(s,r[1],290,y,920,35,25,'#172E49',true);text(s,r[2],290,y+36,920,42,22);});
text(s,'ThongLQ remains team leader and coordinates frontend, CI and the dossier.',56,603,1160,40,22);
s=slide('AUTOMATIC FLIGHT PREPARATION','Readiness is implemented; aircraft dispatch is disabled',21);
const lines=[['Opt in and approve','AutomaticFlightRequested defaults off; changes require plan review.'],['Confirm in the window','Backend time: T−5 minutes up to start; visible, online authenticated tab.'],['Check fresh evidence','Presence and heartbeat ≤30 s; telemetry ≤15 s; passing preflight ≤1 min.'],['Bind the confirmation','Exact plan hash, mission connection and bound UAV/controller UID.'],['Keep execution separate','dispatchEnabled=false; no upload, ARM, TAKEOFF or AUTO/MISSION_START.']];
lines.forEach((r,i)=>{let y=187+i*79;text(s,r[0],56,y,290,37,24,'#276A86',true);text(s,r[1],370,y,850,65,23);});
text(s,'Next phase: firmware adapters, upload/read-back, command safeguards, SITL and supervised validation.',56,610,1168,50,21);
for(let i=0;i<p.slides.items.length;i++){
 const png=await p.export({slide:p.slides.items[i],format:'png',scale:1.5});await fs.writeFile(`${dir}/new-slide-${i+1}.png`,new Uint8Array(await png.arrayBuffer()));
}
await (await PresentationFile.exportPptx(p)).save(`${dir}/additional-slides.pptx`);
console.log('Two text-only slides authored and rendered; original slide parts will be preserved.');
