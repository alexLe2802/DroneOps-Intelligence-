import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

function compile(source) {
  const code = ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
  return `data:text/javascript;base64,${Buffer.from(code).toString("base64")}`;
}
const authUrl = compile(await readFile(new URL("../src/lib/auth/client.ts", import.meta.url), "utf8"));
const clientUrl = compile((await readFile(new URL("../src/lib/portal/client.ts", import.meta.url), "utf8")).replace('"@/lib/auth/client"', JSON.stringify(authUrl)));
const client = await import(clientUrl);

// Mock the HTTP boundary only: exercise actual API helper serialization and failures.
test("mission creation preserves UTC schedule and ordered waypoints in a single POST", async () => {
  const input = { name: "Survey", uavId: "uav-id", description: null, startTime: "2026-10-17T01:00:00.000Z", endTime: "2026-10-17T02:00:00.000Z", waypoints: [1, 2].map(i => ({ sequenceOrder: i, latitude: 10 + i, longitude: 106, altitude: 50, actionType: "FlyThrough" })) };
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "/api/missions"); assert.equal(options.method, "POST");
    assert.deepEqual(JSON.parse(options.body), input);
    assert.equal(options.credentials, "same-origin");
    return Response.json({ ...input, id: "mission-id", status: "Draft" });
  });
  try { const saved = await client.createMission(input); assert.equal(saved.status, "Draft"); assert.equal(mock.mock.calls.length, 1); }
  finally { mock.mock.restore(); }
});

test("mission update sends the complete draft to the mission PUT endpoint", async () => {
  const input = { name: "Updated survey", uavId: "uav-id", description: "Revised route", startTime: "2026-10-17T01:00:00.000Z", endTime: "2026-10-17T02:00:00.000Z", waypoints: [1, 2].map(i => ({ sequenceOrder: i, latitude: 10 + i, longitude: 106, altitude: 50, actionType: "FlyThrough" })) };
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "/api/missions/mission-id");
    assert.equal(options.method, "PUT");
    assert.deepEqual(JSON.parse(options.body), input);
    return Response.json({ ...input, id: "mission-id", status: "Draft", waypoints: input.waypoints });
  });
  try {
    const saved = await client.updateMission("mission-id", input);
    assert.equal(saved.name, input.name);
    assert.equal(saved.detailAvailable, true);
    assert.equal(mock.mock.calls.length, 1);
  } finally { mock.mock.restore(); }
});

test("mission lifecycle calls saved-plan validation, start and cancel contracts", async () => {
  const mission = { id: "mission-id", name: "Survey", status: "Draft", description: null, uavId: "uav-id", uavCode: "UAV-01", startTime: null, endTime: null, createdAt: "", waypoints: [{ sequenceOrder: 1, latitude: 10, longitude: 106, altitude: 50, actionType: "TakeOff" }] };
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push([url, options.method, options.body ? JSON.parse(options.body) : null]);
    if (url.endsWith("/validate")) return Response.json({ isValid: true, message: "Mission parameters are valid." });
    return Response.json({ message: "OK" });
  });
  try {
    assert.equal((await client.validateMission(mission)).isValid, true);
    await client.startMission(mission.id);
    await client.cancelMission(mission.id, "Weather changed");
    assert.deepEqual(calls, [
      ["/api/missions/validate", "POST", { missionId: "mission-id" }],
      ["/api/missions/mission-id/start", "POST", null],
      ["/api/missions/mission-id/cancel", "PUT", { reason: "Weather changed" }],
    ]);
  } finally { mock.mock.restore(); }
});

test("geofence list/detail/create/update/delete use the existing backend contracts", async () => {
  const body = { name: "Area", description: null, coordinates: [{ latitude: 10, longitude: 106 }, { latitude: 11, longitude: 106 }, { latitude: 11, longitude: 107 }] };
  const zone = { ...body, id: "zone-id" };
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push([url, options.method ?? "GET"]);
    if (["POST", "PUT"].includes(options.method)) assert.deepEqual(JSON.parse(options.body), body);
    return options.method === "DELETE" ? new Response(null, { status: 204 }) : Response.json(url === "/api/geofences" && !options.method ? [zone] : zone);
  });
  try {
    assert.equal((await client.getGeofences())[0].id, zone.id);
    assert.equal((await client.getGeofence(zone.id)).id, zone.id);
    assert.equal((await client.createGeofence(body)).id, zone.id);
    assert.equal((await client.updateGeofence(zone.id, body)).id, zone.id);
    assert.equal(await client.deleteGeofence(zone.id), undefined);
    assert.deepEqual(calls, [["/api/geofences", "GET"], ["/api/geofences/zone-id", "GET"], ["/api/geofences", "POST"], ["/api/geofences/zone-id", "PUT"], ["/api/geofences/zone-id", "DELETE"]]);
  } finally { mock.mock.restore(); }
});

test("server conflicts and authorization errors remain failures", async () => {
  for (const status of [400, 401, 403, 404, 409, 503]) {
    const mock = test.mock.method(globalThis, "fetch", async () => Response.json({ message: "Backend rejected request" }, { status }));
    try { await assert.rejects(client.createGeofence({ name: "Area", coordinates: [] }), error => error.status === status && error.message === "Backend rejected request"); }
    finally { mock.mock.restore(); }
  }
});


test("review and evidence contracts preserve request identity without mission versions", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options = {}) => {
    calls.push([url, options.method ?? "GET", options.body ? JSON.parse(options.body) : null]);
    return Response.json({ status: "Partial", waypoints: [{ temperatureCelsius: null }] });
  });
  try {
    await client.validateMission({ id: "mission" }, "request");
    await client.approveMission("mission", "Reviewed", "run", "request");
    await client.rejectMission("mission", "Findings", "request");
    await client.getMissionHistory("mission");
    await client.getMissionValidation("mission", "request");
    assert.equal((await client.getMissionWeather("mission", "request")).waypoints[0].temperatureCelsius, null);
    await client.getMissionWeather("mission", "request", true);
    assert.deepEqual(calls, [
      ["/api/missions/validate", "POST", { missionId: "mission", updateRequestId: "request" }],
      ["/api/missions/mission/approve", "POST", { comment: "Reviewed", updateRequestId: "request", validationRunId: "run" }],
      ["/api/missions/mission/reject", "POST", { reason: "Findings", updateRequestId: "request" }],
      ["/api/missions/mission/history", "GET", null],
      ["/api/missions/mission/validation?updateRequestId=request", "GET", null],
      ["/api/missions/mission/weather?updateRequestId=request", "GET", null],
      ["/api/missions/mission/weather/refresh?updateRequestId=request", "POST", null],
    ]);
  } finally { mock.mock.restore(); }
});

test("mission detail preserves pending proposal, altitude datum and unknown altitude", async () => {
  const data = { id: "mission", name: "Current", status: "Approved", altitudeReference: "MSL", pendingUpdate: { id: "request", status: "Pending", proposedPlan: { name: "Proposed" } }, waypoints: [{ sequenceOrder: 1, latitude: 10, longitude: 106, altitude: null }] };
  const mock = test.mock.method(globalThis, "fetch", async () => Response.json(data));
  try {
    const result = await client.getMission("mission");
    assert.equal(result.pendingUpdate.proposedPlan.name, "Proposed");
    assert.equal(result.altitudeReference, "MSL");
    assert.equal(result.waypoints[0].altitude, null);
    assert.equal("latestVersion" in result, false);
  } finally { mock.mock.restore(); }
});
