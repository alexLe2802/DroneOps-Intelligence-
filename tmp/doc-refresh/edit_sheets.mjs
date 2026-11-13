import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile} from 'file:///C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const dir='D:/DroneOps-Intelligence-/tmp/doc-refresh';
const plans=JSON.parse(await fs.readFile(`${dir}/sheet-plan.json`,'utf8'));
for(const plan of plans){
 const wb=await SpreadsheetFile.importXlsx(await FileBlob.load(plan.file));
 if(plan.kind==='tests'){
  const png=await wb.render({sheetName:'Telemetry',range:'A15:F19',scale:1.3});
  await fs.writeFile(`${dir}/tests-before.png`,new Uint8Array(await png.arrayBuffer()));
  const sh=wb.worksheets.getItem('Telemetry');
  for(let r=20;r<=27;r++)sh.getRange(`A${r}:O${r}`).copyFrom(sh.getRange('A19:O19'),'all');
  sh.getRange('A20:O27').format.wrapText=true;
  sh.getRange('A20:O27').format.rowHeight=120;
 }
 for(const c of plan.changes){
  const range=wb.worksheets.getItem(c.sheet).getRange(c.cell);
  if(c.formula)range.formulas=[[c.formula]];else range.values=[[c.value]];
 }
 if(plan.kind==='schedule'){
  const sh=wb.worksheets.getItem('Plan Notes');
  sh.getRange('A18:B19').copyFrom(sh.getRange('A11:B12'),'all');
  for(const c of plan.changes.filter(c=>c.sheet==='Plan Notes' && /1[89]$/.test(c.cell)))sh.getRange(c.cell).values=[[c.value]];
  sh.getRange('A18:B19').format.wrapText=true;sh.getRange('A18:B19').format.rowHeight=108;
  wb.worksheets.getItem('Project Schedule').getRange('A38:I40').format.rowHeight=100;
 }
 wb.recalculate();
 console.log(plan.kind,(await wb.inspect({kind:'match',searchTerm:'#REF!|#DIV/0!|#VALUE!|#NAME\\?|#NUM!',options:{useRegex:true,maxResults:15},maxChars:2000})).ndjson);
 for(const [sheet,range,label] of plan.kind==='schedule'?[['Responsibility Matrix','A6:F9','matrix-after'],['Plan Notes','A18:B19','scope-after']]:[['Telemetry','A20:F23','tests-after1'],['Telemetry','A24:F27','tests-after2'],['Test Statistics','B10:H21','stats-after']]){
  const png=await wb.render({sheetName:sheet,range,scale:1.4});await fs.writeFile(`${dir}/${label}.png`,new Uint8Array(await png.arrayBuffer()));
 }
 await fs.copyFile(plan.file,`${dir}/originals/${plan.file.split(/[\\/]/).at(-1)}`);
 await (await SpreadsheetFile.exportXlsx(wb)).save(`${dir}/${plan.kind}-authored.xlsx`);
 console.log(plan.kind,'exported');
}
