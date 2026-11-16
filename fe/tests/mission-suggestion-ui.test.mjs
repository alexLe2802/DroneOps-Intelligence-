import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const formSource = await readFile(new URL("../src/app/portal/missions/new/mission-form.tsx", import.meta.url), "utf8");
const suggestionSource = await readFile(new URL("../src/app/portal/missions/new/mission-suggestion-panel.tsx", import.meta.url), "utf8");
const assessmentSource = await readFile(new URL("../src/app/portal/ai-assessment/ai-assessment-client.tsx", import.meta.url), "utf8");

test("mission suggestion belongs to pilot mission authoring and not risk assessment", () => {
  assert.match(formSource, /<MissionSuggestionPanel disabled=\{saving\} onApply=\{applySuggestion\}/);
  assert.doesNotMatch(assessmentSource, /MissionSuggestionPanel/);
  assert.match(suggestionSource, /generateMissionSuggestion/);
  assert.match(suggestionSource, /getMyMissionSuggestions/);
  assert.match(suggestionSource, /assistant suggests mission details only/);
});

test("new mission does not display a previous proposal until the pilot generates or selects one", () => {
  assert.match(suggestionSource, /selectedId \? items\?\.find/);
  assert.match(suggestionSource, /setSelectedId\(current => history\.some\(item => item\.id === current\) \? current : ""\)/);
  assert.doesNotMatch(suggestionSource, /\?\? items\?\.\[0\]/);
  assert.match(suggestionSource, /setSelectedId\(created\.id\)/);
});

test("applying a suggestion fills mission details but keeps UAV, schedule and route manual", () => {
  assert.match(formSource, /setName\(suggestion\.missionName\.trim\(\)\)/);
  assert.match(formSource, /setDescription\(suggestion\.missionPurpose\.trim\(\)\)/);
  assert.match(formSource, /setAverageSpeed/);
  assert.match(formSource, /setMaxSpeed/);
  assert.doesNotMatch(formSource.slice(formSource.indexOf("function applySuggestion"), formSource.indexOf("async function submit")), /setWaypoints/);
  assert.match(formSource, /Your UAV, schedule and waypoints will be kept/);
  assert.match(suggestionSource, /APPLY MISSION DETAILS/);
  assert.match(suggestionSource, /Waypoint coordinates are never applied/);
});

test("mission suggestion UI does not treat AI output as route evidence", () => {
  assert.match(suggestionSource, /pilot still selects the UAV, schedule and every waypoint/);
  assert.doesNotMatch(suggestionSource, /selected\.waypoints/);
  assert.doesNotMatch(formSource.slice(formSource.indexOf("function applySuggestion"), formSource.indexOf("async function submit")), /latitude|longitude|actionType/);
});
