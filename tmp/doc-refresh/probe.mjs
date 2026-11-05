import fs from 'node:fs/promises';
import {FileBlob,SpreadsheetFile,PresentationFile} from 'file:///C:/Users/PC/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/@oai/artifact-tool/dist/artifact_tool.mjs';
const dir='D:/DroneOps-Intelligence-/tmp/doc-refresh';
try {
 const wb=await SpreadsheetFile.importXlsx(await FileBlob.load('D:/DroneOps-Intelligence-/docs/reports/FA26SE238_Project_Schedule.xlsx'));
 console.log((await wb.inspect({kind:'sheet',include:'id,name',maxChars:1500})).ndjson);
 const png=await wb.render({sheetName:'Responsibility Matrix',range:'A1:F10',scale:1.5});
 await fs.writeFile(`${dir}/matrix-before.png`,new Uint8Array(await png.arrayBuffer()));
 console.log('spreadsheet rendered');
}catch(e){console.log('spreadsheet probe:',e.stack)}
try {
 const p=await PresentationFile.importPptx(await FileBlob.load('D:/DroneOps-Intelligence-/docs/slides/DroneOps_Review2.pptx'));
 console.log((await p.inspect({kind:'slide',include:'id,name',maxChars:1800})).ndjson);
 console.log('slides',p.slides.items.length);
}catch(e){console.log('presentation probe:',e.stack)}
