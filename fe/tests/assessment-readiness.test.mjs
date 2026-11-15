import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/portal/assessment-readiness.ts", import.meta.url), "utf8");
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { assessmentReadiness } = await import(`data:text/javascript;base64,${Buffer.from(js).toString("base64")}`);

const readyPlan = () => ({
  detailAvailable: true,
  uavId: "uav",
  averageSpeedMetersPerSecond: 8,
  maxSpeedMetersPerSecond: 12,
  startTime: "2026-11-15T08:00:00Z",
  endTime: "2026-11-15T09:00:00Z",
  waypoints: [
    { sequenceOrder: 1, latitude: 10.8, longitude: 106.7, altitude: 20, actionType: "TakeOff" },
    { sequenceOrder: 2, latitude: 10.9, longitude: 106.8, altitude: 20, actionType: "Land" },
  ],
});

test("complete saved mission is ready for an assessment request", () => {
  assert.deepEqual(assessmentReadiness(readyPlan()), { ready: true, blockers: [] });
});

test("essential mission gaps block the frontend request", () => {
  const result = assessmentReadiness({
    ...readyPlan(),
    uavId: null,
    averageSpeedMetersPerSecond: null,
    maxSpeedMetersPerSecond: 0,
    startTime: null,
    waypoints: [{ sequenceOrder: 1, latitude: 91, longitude: 106.7, altitude: null, actionType: "TakeOff" }],
  });
  assert.equal(result.ready, false);
  assert.match(result.blockers.join(" "), /Assign a UAV/);
  assert.match(result.blockers.join(" "), /two route waypoints/);
  assert.match(result.blockers.join(" "), /cruise speed/);
  assert.match(result.blockers.join(" "), /maximum speed/);
  assert.match(result.blockers.join(" "), /start and end time/);
});

test("invalid route values and schedule ordering are rejected", () => {
  const invalidPlan = readyPlan();
  invalidPlan.waypoints[1].longitude = 181;
  invalidPlan.waypoints[1].altitude = null;
  invalidPlan.averageSpeedMetersPerSecond = 13;
  invalidPlan.endTime = invalidPlan.startTime;
  const result = assessmentReadiness(invalidPlan);
  assert.equal(result.ready, false);
  assert.match(result.blockers.join(" "), /coordinates/);
  assert.match(result.blockers.join(" "), /altitude/);
  assert.match(result.blockers.join(" "), /at least the cruise speed/);
  assert.match(result.blockers.join(" "), /later than/);
});
