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

test("live monitoring clients use scoped mission, telemetry and incident endpoints", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push([url, options.method ?? "GET"]);
    if (url === "/api/live/missions") return Response.json([{ missionId: "mission-id", missionName: "Patrol" }]);
    if (url.includes("telemetry/latest")) return Response.json({ missionId: "mission-id", connectivityStatus: "Online" });
    return Response.json({ items: [], totalCount: 0, pageNumber: 1, pageSize: 8, totalPages: 0 });
  });
  try {
    assert.equal((await client.getLiveMissions())[0].missionId, "mission-id");
    assert.equal((await client.getLatestMissionTelemetry("mission-id")).connectivityStatus, "Online");
    assert.equal((await client.getMissionIncidents("mission-id")).items.length, 0);
    assert.deepEqual(calls, [
      ["/api/live/missions", "GET"],
      ["/api/missions/mission-id/telemetry/latest", "GET"],
      ["/api/incidents?missionId=mission-id&pageNumber=1&pageSize=8", "GET"],
    ]);
  } finally {
    mock.mock.restore();
  }
});

test("live stream URL is same-origin and encodes the mission identifier", () => {
  assert.equal(client.liveMissionStreamUrl("mission/id"), "/api/live/stream?missionId=mission%2Fid");
});

test("incident registry and detail clients preserve the backend filter contract", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async url => {
    calls.push(url);
    if (url.includes("/incidents/")) return Response.json({ id: "incident-id", title: "Link loss", status: "Open" });
    return Response.json({ items: [], totalCount: 0, pageNumber: 2, pageSize: 6, totalPages: 0 });
  });
  try {
    await client.getIncidents({
      pageNumber: 2,
      pageSize: 6,
      search: "link loss",
      type: "CommunicationLoss",
      severity: "Critical",
      status: "Open",
      fromDate: "2026-10-01T00:00:00.000Z",
      toDate: "2026-10-21T23:59:59.999Z",
    });
    assert.equal((await client.getIncident("incident-id")).id, "incident-id");
    assert.deepEqual(calls, [
      "/api/incidents?pageNumber=2&pageSize=6&search=link+loss&type=CommunicationLoss&severity=Critical&status=Open&fromDate=2026-10-01T00%3A00%3A00.000Z&toDate=2026-10-21T23%3A59%3A59.999Z",
      "/api/incidents/incident-id",
    ]);
  } finally {
    mock.mock.restore();
  }
});
