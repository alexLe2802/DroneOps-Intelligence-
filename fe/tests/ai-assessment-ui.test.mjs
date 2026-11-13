import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const source = await readFile(new URL("../src/app/portal/ai-assessment/ai-assessment-client.tsx", import.meta.url), "utf8");

test("assessment history can select and render a previous run", () => {
  assert.match(source, /setActiveAssessmentId\(item\.id\)/);
  assert.match(source, /aria-pressed=/);
  assert.match(source, /items\?\.find\(item => item\.id === activeAssessmentId\)/);
});

test("historical evidence links to authorized flight and incident routes", () => {
  assert.match(source, /\/portal\/flight-history\/\$\{encodeURIComponent\(flight\.missionId\)\}/);
  assert.match(source, /\/portal\/incidents\/\$\{encodeURIComponent\(incidentId\)\}/);
  assert.match(source, /RECORDED HISTORICAL EVIDENCE/);
  assert.match(source, /Incident \{String\(index \+ 1\)\.padStart\(2, "0"\)\}/);
  assert.doesNotMatch(source, />\{incidentId\}<\/Link>/);
});

test("no-match and generated advice states do not imply approval or zero risk", () => {
  assert.match(source, /No historical evidence/);
  assert.match(source, /does not mean zero risk/i);
  assert.match(source, /NO APPROVAL AUTHORITY/);
  assert.doesNotMatch(source, /confidence/i);
});

test("provider retry preserves already loaded assessment history", () => {
  assert.match(source, /RETRY ASSESSMENT/);
  const generateCatch = source.slice(source.indexOf("} catch (reason) {", source.indexOf("async function generate")), source.indexOf("} finally", source.indexOf("async function generate")));
  assert.doesNotMatch(generateCatch, /setItems\(\[\]\)/);
  assert.match(source, /Previously loaded evidence remains available below/);
});
