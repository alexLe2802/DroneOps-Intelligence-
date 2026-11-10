import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/maps/mission-waypoints.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { appendLandingWaypoint, normalizeWaypointActions, appendMissionPoint } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

const position = (latitude, longitude) => ({ latitude, longitude, altitude: 50 });

test("the route starts with take-off and ends with landing", () => {
  const first = appendLandingWaypoint([], position(10, 106));
  assert.equal(first[0].actionType, "TakeOff");
  assert.equal(first[0].sequenceOrder, 1);

  const second = appendLandingWaypoint(first, position(11, 107));
  assert.equal(second[0].actionType, "TakeOff");
  assert.equal(second[1].actionType, "Land");
  assert.equal(second[1].sequenceOrder, 2);

  const third = appendLandingWaypoint(second, position(12, 108));
  assert.equal(third[0].actionType, "TakeOff");
  assert.equal(third[1].actionType, "FlyThrough");
  assert.equal(third[2].actionType, "Land");
});

test("adding an endpoint preserves actions before the former endpoint", () => {
  const route = [
    { ...position(10, 106), sequenceOrder: 1, actionType: "TakeOff" },
    { ...position(11, 107), sequenceOrder: 2, actionType: "CapturePhoto" },
  ];
  const next = appendLandingWaypoint(route, position(12, 108));

  assert.equal(next[0].actionType, "TakeOff");
  assert.equal(next[1].actionType, "FlyThrough");
  assert.equal(next[2].actionType, "Land");
});

test("route normalization hard-locks endpoint actions after reorder or removal", () => {
  const route = normalizeWaypointActions([
    { ...position(11, 107), sequenceOrder: 9, actionType: "Land" },
    { ...position(10, 106), sequenceOrder: 4, actionType: "TakeOff" },
    { ...position(12, 108), sequenceOrder: 2, actionType: "TakeOff" },
  ]);

  assert.deepEqual(route.map(point => point.sequenceOrder), [1, 2, 3]);
  assert.deepEqual(route.map(point => point.actionType), ["TakeOff", "FlyThrough", "Land"]);

  const afterRemoval = normalizeWaypointActions(route.slice(1));
  assert.deepEqual(afterRemoval.map(point => point.actionType), ["TakeOff", "Land"]);
});


test("automatic plan adds Home first, then TakeOff, and keeps the newest flight point as Land", () => {
  const home = position(10.8231, 106.6297);
  let plan = appendMissionPoint([], null, home, true);
  assert.deepEqual(plan.plannedHome, { latitude: home.latitude, longitude: home.longitude });
  assert.deepEqual(plan.waypoints, []);

  plan = appendMissionPoint(plan.waypoints, plan.plannedHome, position(10.82311, 106.6297), true);
  assert.deepEqual(plan.waypoints.map(point => point.actionType), ["TakeOff"]);
  plan = appendMissionPoint(plan.waypoints, plan.plannedHome, position(10.8232, 106.6298), true);
  assert.deepEqual(plan.waypoints.map(point => point.actionType), ["TakeOff", "Land"]);
  plan = appendMissionPoint(plan.waypoints, plan.plannedHome, position(10.8233, 106.6299), true);
  assert.deepEqual(plan.waypoints.map(point => point.actionType), ["TakeOff", "FlyThrough", "Land"]);
  // Home remains metadata: the FC receives TakeOff first, never a navigation command to Home.
  assert.deepEqual(plan.waypoints.map(point => point.sequenceOrder), [1, 2, 3]);
  assert.deepEqual(plan.plannedHome, { latitude: home.latitude, longitude: home.longitude });
});

test("a Home selected with the Home editor allows the next click to add TakeOff", () => {
  const home = { latitude: 10.8231, longitude: 106.6297 };
  const plan = appendMissionPoint([], home, position(10.82311, 106.6297), true);
  assert.equal(plan.plannedHome, home);
  assert.equal(plan.waypoints[0].actionType, "TakeOff");
});

test("replacing a cleared automatic Home preserves the existing flight route", () => {
  const route = appendLandingWaypoint([], position(10.82311, 106.6297));
  const plan = appendMissionPoint(route, null, position(10.8231, 106.6297), true);
  assert.equal(plan.waypoints, route);
  assert.deepEqual(plan.plannedHome, { latitude: 10.8231, longitude: 106.6297 });
});

test("manual missions continue adding TakeOff first without creating Home", () => {
  const plan = appendMissionPoint([], null, position(10.8231, 106.6297), false);
  assert.equal(plan.plannedHome, null);
  assert.equal(plan.waypoints[0].actionType, "TakeOff");
});
