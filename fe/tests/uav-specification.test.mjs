import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString("base64")}`;
const specification = await import(compile(await readFile(new URL("../src/lib/uav-specification.ts", import.meta.url), "utf8")));

const valid = {
  code: "UAV-01", name: "Survey", manufacturer: null, model: null, serialNumber: null,
  emptyWeightKg: 6, maxTakeoffWeightKg: 9, maxPayloadKg: 3,
  batteryType: null, batteryCapacityMah: 5000, batteryVoltage: 22.8, batteryCellCount: 6,
  maxFlightTimeMinutes: 40, maxRangeKm: 12, maxSpeedKmh: 70, maxWindSpeedMps: 10,
  maxOperatingAltitudeMeters: 5000, purchaseDate: null,
};

test("blank optional numbers become null", () => {
  assert.equal(specification.optionalNumber(""), null);
  assert.equal(specification.optionalNumber("  "), null);
  assert.equal(specification.optionalNumber("12.5"), 12.5);
});

test("valid UAV specifications pass client validation", () => {
  assert.equal(specification.uavSpecificationError(valid), null);
});

test("weight relationships are rejected before submission", () => {
  assert.match(specification.uavSpecificationError({ ...valid, maxTakeoffWeightKg: 5 }), /less than empty weight/i);
  assert.match(specification.uavSpecificationError({ ...valid, maxPayloadKg: 4 }), /cannot exceed maximum takeoff weight/i);
});

test("battery integer and cell limits match the backend contract", () => {
  assert.match(specification.uavSpecificationError({ ...valid, batteryCellCount: 101 }), /1 to 100/i);
  assert.match(specification.uavSpecificationError({ ...valid, batteryCapacityMah: 5000.5 }), /whole number/i);
});
