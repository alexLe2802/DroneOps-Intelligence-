import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/maps/mission-waypoints.ts", import.meta.url), "utf8");
const compiled = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { appendLandingWaypoint } = await import(`data:text/javascript;base64,${Buffer.from(compiled).toString("base64")}`);

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
