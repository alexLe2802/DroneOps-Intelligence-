import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const formSource = await readFile(new URL("../src/app/portal/missions/new/mission-form.tsx", import.meta.url), "utf8");
const suggestionSource = await readFile(new URL("../src/app/portal/missions/new/mission-suggestion-panel.tsx", import.meta.url), "utf8");
const editSource = await readFile(new URL("../src/app/portal/missions/[id]/edit/mission-edit-client.tsx", import.meta.url), "utf8");
const analysisSource = await readFile(new URL("../src/app/portal/missions/new/draft-analysis-panel.tsx", import.meta.url), "utf8");
const assessmentSource = await readFile(new URL("../src/app/portal/ai-assessment/ai-assessment-client.tsx", import.meta.url), "utf8");

test("mission suggestion belongs to pilot mission authoring and not risk assessment", () => {
  assert.ok(formSource.indexOf("<DraftAnalysisPanel") > formSource.indexOf("<MissionMap"));
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
  assert.match(formSource, /key=\{JSON.stringify\(analysisDraft\)\}/);
  assert.doesNotMatch(analysisSource, /setWaypoints|setUavId|setName/);
});

test("editing still supports prior suggestions without treating generated waypoints as route evidence", () => {
  assert.match(editSource, /<MissionSuggestionPanel/);
  assert.match(suggestionSource, /You choose the UAV, schedule and route/);
  assert.doesNotMatch(suggestionSource, /selected\.waypoints/);
});
