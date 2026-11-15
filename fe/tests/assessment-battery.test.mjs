import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";
import ts from "typescript";

const source = await readFile(new URL("../src/lib/portal/assessment-battery.ts", import.meta.url), "utf8");
const js = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2022 } }).outputText;
const { assessmentBatteryState: state } = await import(`data:text/javascript;base64,${Buffer.from(js).toString("base64")}`);
const now = Date.parse("2026-11-14T08:00:00Z");
const date = seconds => new Date(now + seconds * 1000).toISOString();
const sample = () => ({ missionId: "mission", uavId: "uav", sourceId: "fc", isValid: true, isStale: false,
  snapshotKind: "Latest", connectivityStatus: "Online", sourceTimestamp: date(0), receivedAt: date(0),
  batteryObservedAt: date(0), batteryPercent: 70, batteryVoltageVolts: 22, staleAfterSeconds: 10 });
const assessment = () => ({ missionRef: "mission", isOutdated: false, createdAt: date(-60),
  result: { battery: { requiredBatteryPercent: 45 } } });

test("connected battery comparison preserves real zero and requirements above 100%", () => {
  assert.equal(state(sample(), "mission", "uav", assessment(), now).margin, 25);
  assert.equal(state({ ...sample(), batteryPercent: 0 }, "mission", "uav", assessment(), now).margin, -45);
  const report = assessment(); report.result.battery.requiredBatteryPercent = 120;
  assert.equal(state({ ...sample(), batteryPercent: 100 }, "mission", "uav", report, now).margin, -20);
});

test("identity, validity, source and battery timestamps gate live charge", () => {
  for (const overrides of [{missionId:"other"}, {uavId:"other"}, {sourceId:null}, {isValid:false}, {isStale:true},
    {snapshotKind:"Stale"}, {connectivityStatus:"Offline"}, {sourceTimestamp:date(-16)}, {receivedAt:date(-16)},
    {batteryObservedAt:date(-16)}, {sourceTimestamp:date(6)}, {receivedAt:date(6)}, {batteryObservedAt:date(6)},
    {sourceTimestamp:null}, {batteryPercent:null}, {batteryPercent:101}, {batteryPercent:-1}]) {
    const result = state({ ...sample(), ...overrides }, "mission", "uav", assessment(), now);
    assert.equal(result.percent, null, JSON.stringify(overrides));
    assert.equal(result.margin, null);
  }
  assert.equal(state(sample(), "mission", null, assessment(), now).margin, null);
  assert.equal(state(sample(), "mission", "uav", assessment(), now + 11000).percent, null);
});

test("no telemetry still displays the plan estimate without claiming sufficiency", () => {
  const result = state(null, "mission", "uav", assessment(), now);
  assert.equal(result.required, 45);
  assert.equal(result.percent, null);
  assert.equal(result.margin, null);
});

test("outdated, old, future and foreign assessments cannot be compared with live charge", () => {
  for (const changes of [{isOutdated:true}, {missionRef:"other"}, {createdAt:date(-601)}, {createdAt:date(60)}]) {
    const result = state(sample(), "mission", "uav", { ...assessment(), ...changes }, now);
    assert.equal(result.required, null);
    assert.equal(result.margin, null);
  }
  assert.equal(state({ ...sample(), batteryVoltageVolts:0.003 }, "mission", "uav", assessment(), now).margin, null);
});
