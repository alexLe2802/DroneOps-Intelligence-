import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";
const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ESNext}}).outputText).toString("base64")}`;
const { archiveHref, analyticsQuery } = await import(compile(await readFile(new URL("../src/lib/portal/analytics-scope.ts",import.meta.url),"utf8")));
test("analytics drill-down retains date/UAV/mission while excluding unrelated history-only filters",()=>{
  const query = new URLSearchParams({fromDate:"2026-11-01",toDate:"2026-11-14",uavId:"uav",missionId:"mission",search:"inspection",status:"Completed",page:"2",returnTo:"https://untrusted.test"});
  const href=archiveHref(query);
  assert.equal(new URL(href,"http://fixture").searchParams.get("search"),"inspection");
  assert.equal(new URL(href,"http://fixture").searchParams.get("page"),"2");
  assert.equal(new URL(href,"http://fixture").searchParams.has("returnTo"),false);
  const source=new URLSearchParams(analyticsQuery(query));
  assert.deepEqual([...source.keys()],["fromDate","toDate","uavId","missionId"]);
  assert.equal(source.get("missionId"),"mission");
});
