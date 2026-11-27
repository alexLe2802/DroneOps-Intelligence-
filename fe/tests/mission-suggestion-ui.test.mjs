import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const limitsSource = await readFile(new URL("../src/app/portal/missions/new/draft-analysis-limits.ts", import.meta.url), "utf8");
const formSource = await readFile(new URL("../src/app/portal/missions/new/mission-form.tsx", import.meta.url), "utf8");
const suggestionSource = await readFile(new URL("../src/app/portal/missions/new/mission-suggestion-panel.tsx", import.meta.url), "utf8");
const editSource = await readFile(new URL("../src/app/portal/missions/[id]/edit/mission-edit-client.tsx", import.meta.url), "utf8");
const analysisSource = await readFile(new URL("../src/app/portal/missions/new/draft-analysis-panel.tsx", import.meta.url), "utf8");
const assessmentSource = await readFile(new URL("../src/app/portal/ai-assessment/ai-assessment-client.tsx", import.meta.url), "utf8");

test("mission suggestion belongs to pilot mission authoring and not risk assessment", () => {
  assert.ok(formSource.indexOf("<DraftAnalysisPanel") > formSource.indexOf("<AutomaticFlightFields"));
  assert.match(formSource, /name\.trim\(\)\.length < 2/);
  assert.match(formSource, /routeError\(waypoints\)/);
  assert.match(analysisSource, /disabled=\{disabled \|\| !draft \|\| busy\}/);
  assert.doesNotMatch(assessmentSource, /MissionSuggestionPanel/);
  assert.match(suggestionSource, /generateMissionSuggestion/);
  assert.match(suggestionSource, /getMyMissionSuggestions/);
  assert.match(suggestionSource, /AI drafts details only/);
});

test("new mission does not display a previous proposal until the pilot generates or selects one", () => {
  assert.match(suggestionSource, /selectedId \? items\?\.find/);
  assert.match(suggestionSource, /setSelectedId\(current => history\.some\(item => item\.id === current\) \? current : ""\)/);
  assert.doesNotMatch(suggestionSource, /\?\? items\?\.\[0\]/);
  assert.match(suggestionSource, /setSelectedId\(created\.id\)/);
});

test("draft analysis applies individual speed and schedule fields only", () => {
  assert.match(formSource, /applyCruise=\{setAverageSpeed\} applyCeiling=\{setMaxSpeed\} applyEnd=\{setEndTime\}/);
  assert.match(formSource, /key=\{analysisContextKey\}/);
  assert.doesNotMatch(formSource, /key=\{JSON.stringify\(analysisDraft\)\}/);
  const contextKey = formSource.match(/const analysisContextKey = JSON\.stringify\(\{([\s\S]*?)\}\);/)?.[1] ?? "";
  assert.ok(contextKey.length > 0);
  assert.doesNotMatch(contextKey, /cruise|ceiling|endTime|limits|averageSpeed|maxSpeed|automaticFlightLimits/);
  assert.match(formSource, /applyAutomaticLimit=/);
  assert.match(formSource, /applyAllAutomaticLimits=/);
  assert.match(formSource, /\.\.\.current, \[field\]: value/);
  assert.doesNotMatch(analysisSource, /setWaypoints|setUavId|setName/);
});

test("draft analysis panel is limited to automatic flight", () => {
  assert.match(formSource, /\{automaticFlightRequested && <>\s*<div className="mission-altitude-card">[\s\S]*?<DraftAnalysisPanel[\s\S]*?<\/>\}/);
  assert.match(formSource, /missing\.length \|\| !automaticFlightRequested \? null/);
  assert.match(analysisSource, /draft\.automatic !== true/);
});

test("both AI panels exist on the create page: suggestion on top, analysis next to automatic limits", () => {
  const form = formSource.indexOf("<form ref={formRef}");
  assert.ok(formSource.indexOf("<MissionSuggestionPanel") > 0 && formSource.indexOf("<MissionSuggestionPanel") < form);
  const limits = formSource.indexOf("<AutomaticFlightFields");
  const analysis = formSource.indexOf("<DraftAnalysisPanel");
  assert.ok(limits > form && analysis > limits && analysis < formSource.indexOf("<MissionMap"));
});

test("draft analysis shows apply buttons per field and per automatic limit", async () => {
  for (const key of ["takeoffSpeedMetersPerSecond", "climbSpeedMetersPerSecond", "descentSpeedMetersPerSecond", "landingSpeedMetersPerSecond", "maxAltitudeMeters", "maxWindMetersPerSecond", "maxGustMetersPerSecond"])
    assert.match(limitsSource, new RegExp(key));
  assert.match(analysisSource, /recommendedLimits/);
  assert.match(analysisSource, /Apply all limits/);
  assert.match(analysisSource, /aria-label=\{`Apply \$\{label\} limit`\}/);
  assert.match(analysisSource, /!matchesNumericValue\(draft\.cruise, result\.cruise\)/);
  assert.match(analysisSource, /!matchesNumericValue\(draft\.ceiling, result\.ceiling\)/);
  const { differingLimitKeys, matchesNumericValue } = await import("data:text/javascript;base64," + Buffer.from(ts.transpileModule(limitsSource, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText).toString("base64"));
  const recommended = { takeoffSpeedMetersPerSecond: 4.9, climbSpeedMetersPerSecond: 4.9, descentSpeedMetersPerSecond: 4.4, landingSpeedMetersPerSecond: 0.9, maxAltitudeMeters: 25, maxWindMetersPerSecond: 3, maxGustMetersPerSecond: 5, policyVersion: "multicopter-envelope-v2" };
  assert.equal(differingLimitKeys({ ...recommended, takeoffSpeedMetersPerSecond: 6.5 }, recommended).join(), "takeoffSpeedMetersPerSecond");
  assert.deepEqual(differingLimitKeys(recommended, recommended), []);
  const edited = { ...recommended, maxAltitudeMeters: 20, maxGustMetersPerSecond: 4 };
  assert.equal(differingLimitKeys(edited, recommended).join(), "maxAltitudeMeters,maxGustMetersPerSecond");
  assert.equal(matchesNumericValue(11.2, 11.2), true);
  assert.equal(matchesNumericValue(null, 11.2), false);
  assert.equal(matchesNumericValue(11.1, 11.2), false);
});

test("editing still supports prior suggestions without treating generated waypoints as route evidence", () => {
  assert.match(editSource, /<MissionSuggestionPanel/);
  assert.match(suggestionSource, /You choose the UAV, schedule and route/);
  assert.doesNotMatch(suggestionSource, /selected\.waypoints/);
});
