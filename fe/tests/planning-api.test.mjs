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

test("manager mission registry reads mission plans and enriches assigned aircraft", async () => {
  const mission = { id: "mission-id", createdBy: "user-id", name: "Port survey", status: "Pending Review", uavId: "uav-id", startTime: null, endTime: null, createdAt: "2026-10-23T00:00:00Z", waypoints: [] };
  const aircraft = { id: "uav-id", code: "UAV-07", name: "North Survey", model: null, status: "Active", pilotId: "pilot-id", pilotName: "Pilot One", createdAt: "2026-10-01T00:00:00Z" };
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async url => {
    calls.push(String(url));
    if (String(url) === "/api/missions") return Response.json([mission]);
    if (String(url).startsWith("/api/admin/uavs?")) return Response.json({ items: [aircraft], pageNumber: 1, pageSize: 100, totalItems: 1, totalPages: 1 });
    throw new Error(`Unexpected request: ${url}`);
  });
  try {
    const result = await client.getManagerMissions();
    assert.equal(result[0].id, mission.id);
    assert.equal(result[0].uavCode, aircraft.code);
    assert.equal(result[0].uavName, aircraft.name);
    assert.equal(result[0].pilotName, aircraft.pilotName);
    assert.equal(calls.some(url => url.includes("flight-history")), false);
  } finally { mock.mock.restore(); }
});

test("post-flight archive and report detail preserve backend query and mutation contracts", async () => {
  const calls = [];
  const report = { reportId: "report-id", missionId: "mission-id", generationStatus: "Available", revision: 1 };
  const mock = test.mock.method(globalThis, "fetch", async (url, options = {}) => {
    calls.push([String(url), options.method ?? "GET", options.body ? JSON.parse(options.body) : null]);
    if (String(url).includes("/regenerate")) return Response.json({ success: true, message: "Queued", data: { ...report, generationStatus: "Queued", revision: 2 } }, { status: 202 });
    if (String(url).endsWith("/report/revisions")) return Response.json([report]);
    if (String(url).endsWith("/report")) return Response.json(report);
    if (String(url).endsWith("/telemetry")) return Response.json([]);
    if (String(url).endsWith("/incidents")) return Response.json([]);
    if (String(url).startsWith("/api/flight-history?")) return Response.json({ items: [], pageNumber: 2, pageSize: 8, totalItems: 0, totalPages: 0 });
    if (String(url).startsWith("/api/flight-history/analytics?")) return Response.json({ totalFlights: 0 });
    throw new Error(`Unexpected request: ${url}`);
  });
  try {
    await client.getFlightHistory({ pageNumber: 2, pageSize: 8, search: "survey", status: "Completed", fromDate: "2026-10-01", toDate: "2026-10-23" });
    await client.getOperationalAnalytics({ fromDate: "2026-10-01", toDate: "2026-10-23" });
    await client.getPostFlightReport("mission-id");
    await client.getPostFlightReportRevisions("mission-id");
    await client.getFlightTelemetry("mission-id");
    await client.getPostFlightIncidents("mission-id");
    const queued = await client.regeneratePostFlightReport("mission-id", "Late telemetry received");
    assert.equal(queued.data.revision, 2);
    assert.deepEqual(calls, [
      ["/api/flight-history?pageNumber=2&pageSize=8&search=survey&status=Completed&fromDate=2026-10-01T00%3A00%3A00.000Z&toDate=2026-10-23T23%3A59%3A59.999Z", "GET", null],
      ["/api/flight-history/analytics?fromDate=2026-10-01T00%3A00%3A00.000Z&toDate=2026-10-23T23%3A59%3A59.999Z", "GET", null],
      ["/api/flight-history/mission-id/report", "GET", null],
      ["/api/flight-history/mission-id/report/revisions", "GET", null],
      ["/api/flight-history/mission-id/telemetry", "GET", null],
      ["/api/flight-history/mission-id/incidents", "GET", null],
      ["/api/flight-history/mission-id/regenerate", "POST", { reason: "Late telemetry received" }],
    ]);
  } finally { mock.mock.restore(); }
});

test("proxy allows post-flight regeneration and report revision routes", async () => {
  const source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  const declaration = source.split("\n").find(line => line.startsWith("const allowed = "));
  const allowed = Function(`${declaration} return allowed;`)();
  const id = "12345678-1234-1234-1234-123456789abc";
  for (const path of [`flight-history/${id}/regenerate`, `flight-history/${id}/report/revisions`]) {
    assert.equal(allowed.test(path), true, path);
  }
  for (const path of [`flight-history/${id}/regenerate/delete`, "flight-history/not-a-guid/regenerate"]) {
    assert.equal(allowed.test(path), false, path);
  }
});
