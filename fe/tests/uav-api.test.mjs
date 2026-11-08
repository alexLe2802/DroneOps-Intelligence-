import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString("base64")}`;
const authUrl = compile(await readFile(new URL("../src/lib/auth/client.ts", import.meta.url), "utf8"));
const client = await import(compile((await readFile(new URL("../src/lib/portal/client.ts", import.meta.url), "utf8")).replace('"@/lib/auth/client"', JSON.stringify(authUrl))));
const id = "12345678-1234-1234-1234-123456789abc";
const request = {
  code: "UAV-02", name: "Survey", manufacturer: "DJI", model: "Matrice 350 RTK", serialNumber: "SN-02",
  emptyWeightKg: 6.3, maxTakeoffWeightKg: 9.2, maxPayloadKg: 2.7,
  batteryType: "LiPo 6S", batteryCapacityMah: 5880, batteryVoltage: 22.8, batteryCellCount: 6,
  maxFlightTimeMinutes: 45, maxRangeKm: 15, maxSpeedKmh: 82.8, maxWindSpeedMps: 12,
  maxOperatingAltitudeMeters: 5000, purchaseDate: "2026-11-02T00:00:00.000Z",
};

test("operator edit sends the update DTO and accepts a message-only response", async () => {
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `/api/uavs/${id}`);
    assert.equal(options.method, "PUT");
    assert.deepEqual(JSON.parse(options.body), request);
    return Response.json({ message: "UAV updated successfully." });
  });
  try { assert.equal(await client.updateAssignedUav(id, request), undefined); assert.equal(mock.mock.calls.length, 1); }
  finally { mock.mock.restore(); }
});

test("operator UAV list accepts the paged backend response and normalizes specifications", async () => {
  const mock = test.mock.method(globalThis, "fetch", async url => {
    assert.equal(url, "/api/uavs/myUAV?PageNumber=1&PageSize=100");
    return Response.json({ items: [{ id, code: "UAV-02", name: "Survey", status: "Pending", manufacturer: "DJI", emptyWeightKg: 6.3, totalFlights: 4 }], pageNumber: 1, pageSize: 100, totalItems: 1, totalPages: 1 });
  });
  try {
    const result = await client.getAssignedUavs();
    assert.equal(result.length, 1);
    assert.equal(result[0].manufacturer, "DJI");
    assert.equal(result[0].emptyWeightKg, 6.3);
    assert.equal(result[0].totalFlights, 4);
    assert.equal(result[0].batteryType, null);
  } finally { mock.mock.restore(); }
});

test("operator uploads a UAV image as multipart data to the dedicated endpoint", async () => {
  const file = new File([new Uint8Array([137, 80, 78, 71])], "survey.png", { type: "image/png" });
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `/api/uavs/${id}/image`);
    assert.equal(options.method, "PUT");
    assert.ok(options.body instanceof FormData);
    assert.equal(options.body.get("file").name, "survey.png");
    return Response.json({ id, code: "UAV-02", name: "Survey", status: "Pending", imageUrl: `/api/uavs/${id}/image?v=1` });
  });
  try {
    const result = await client.uploadUavImage(id, file);
    assert.equal(result.imageUrl, `/api/uavs/${id}/image?v=1`);
  } finally { mock.mock.restore(); }
});

test("operator delete handles both 200-message and 204 responses", async () => {
  for (const status of [200, 204]) {
    const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
      assert.equal(url, `/api/uavs/${id}`); assert.equal(options.method, "DELETE");
      return status === 204 ? new Response(null, { status }) : Response.json({ message: "Deleted" });
    });
    try { assert.equal(await client.deleteAssignedUav(id), undefined); }
    finally { mock.mock.restore(); }
  }
});

test("delete authorization, ownership and history conflicts never become success", async () => {
  for (const status of [401, 403, 404, 409]) {
    const mock = test.mock.method(globalThis, "fetch", async () => Response.json({ message: "Not allowed" }, { status }));
    try { await assert.rejects(client.deleteAssignedUav(id), error => error.status === status); }
    finally { mock.mock.restore(); }
  }
});

test("manager deactivate uses the admin status endpoint", async () => {
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `/api/admin/uavs/${id}/deactivate`); assert.equal(options.method, "PUT");
    return Response.json({ id, status: "Inactive" });
  });
  try { assert.equal((await client.setManagedUavStatus(id, "deactivate")).status, "Inactive"); }
  finally { mock.mock.restore(); }
});

test("manager approval uses the pending UAV approval endpoint", async () => {
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `/api/admin/uavs/${id}/approve`); assert.equal(options.method, "PUT");
    return Response.json({ id, status: "Active" });
  });
  try { assert.equal((await client.setManagedUavStatus(id, "approve")).status, "Active"); }
  finally { mock.mock.restore(); }
});

test("manager manual-review queue sends the moderation filter and preserves AI evidence", async () => {
  const mock = test.mock.method(globalThis, "fetch", async url => {
    assert.equal(url, "/api/admin/uavs?PageNumber=1&PageSize=20&imageModerationStatus=ManualReview");
    return Response.json({
      items: [{ id, code: "UAV-02", name: "Survey", status: "Pending", imageUrl: "/uav.png", imageModerationStatus: "ManualReview", imageModerationReason: "AI retries exhausted." }],
      pageNumber: 1, pageSize: 20, totalItems: 12, totalPages: 1,
    });
  });
  try {
    const result = await client.getManagedUavs({ imageModerationStatus: "ManualReview" });
    assert.equal(result.totalItems, 12);
    assert.equal(result.items[0].imageModerationStatus, "ManualReview");
    assert.equal(result.items[0].imageModerationReason, "AI retries exhausted.");
    assert.equal(result.items[0].imageUrl, "/uav.png");
  } finally { mock.mock.restore(); }
});

test("proxy permits exact UAV IDs and admin actions, rejecting invented routes", async () => {
  const source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  const declaration = source.split("\n").find(line => line.startsWith("const allowed = "));
  const allowed = Function(`${declaration} return allowed;`)();
  for (const path of ["uavs", "uavs/myUAV", `uavs/${id}`, `uavs/${id}/image`, "admin/uavs", `admin/uavs/${id}/approve`, `admin/uavs/${id}/deactivate`]) assert.equal(allowed.test(path), true, path);
  for (const path of ["uavs/not-an-id", `admin/uavs/${id}/delete`, `uavs/${id}/deactivate`, `uavs/${id}/images`]) assert.equal(allowed.test(path), false, path);
});
