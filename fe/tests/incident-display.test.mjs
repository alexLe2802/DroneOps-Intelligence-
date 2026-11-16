import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/portal/incident-display.ts", import.meta.url), "utf8");
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { incidentDescription, incidentDescriptionFindings } = await import(`data:text/javascript;base64,${Buffer.from(js).toString("base64")}`);

test("missing forecasts show one representative instead of repeating every waypoint", () => {
  const missing = "forecast unavailable (provider error, missing data or outside forecast horizon).";
  const value = `Mandatory checks passed. ${[1, 2, 3, 4, 5].map(n => `WP${n}: ${missing}`).join(" ")}`;
  assert.equal(incidentDescription(value), `Mandatory checks passed. WP1: ${missing}`);
});

test("identical weather findings collapse but different numbers and thresholds remain", () => {
  const value = "WP6: cloud cover 95 % reaches threshold 90 %. WP7: cloud cover 91 % reaches threshold 90 %. WP8: cloud cover 95 % reaches threshold 90 %. WP8: gust 7.9 m/s exceeds threshold 7 m/s. WP9: gust 8.2 m/s exceeds threshold 7 m/s.";
  const result = incidentDescription(value);
  assert.ok(!result.includes("WP8: cloud cover"));
  assert.ok(result.includes("WP7: cloud cover 91"));
  assert.ok(result.includes("gust 7.9"));
  assert.ok(result.includes("gust 8.2"));
});

test("non-weather errors, decimal commas and source text are preserved", () => {
  const value = "Preflight blocked. WP1: altitude exceeds ceiling. WP2: altitude exceeds ceiling. WP3: rain 0,2 mm/hour. WP4: rain 0,3 mm/hour.";
  assert.equal(incidentDescription(value), value);
  assert.equal(incidentDescriptionFindings(value).length, 5);
});

test("empty descriptions and advisory boilerplate do not create duplicate content", () => {
  assert.deepEqual(incidentDescriptionFindings(null), []);
  assert.equal(incidentDescription("Weather warnings are advisory."), "");
  assert.equal(incidentDescription("Communication lost for 30 seconds."), "Communication lost for 30 seconds.");
});
