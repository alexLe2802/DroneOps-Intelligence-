import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString("base64")}`;
const authUrl = compile(await readFile(new URL("../src/lib/auth/client.ts", import.meta.url), "utf8"));
const client = await import(compile((await readFile(new URL("../src/lib/portal/client.ts", import.meta.url), "utf8")).replace('"@/lib/auth/client"', JSON.stringify(authUrl))));
const routing = await import(compile(await readFile(new URL("../src/app/portal/notification-route.ts", import.meta.url), "utf8")));
const id = "12345678-1234-1234-1234-123456789abc";

test("notification feed keeps read history and exposes unread count", async () => {
  const response = { unreadCount: 1, items: [{ id, isRead: false, title: "New Pending Mission" }] };
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, "/api/notifications?unreadOnly=false&limit=50");
    assert.equal(options.method, undefined);
    return Response.json(response);
  });
  try { assert.deepEqual(await client.getNotificationFeed(), response); }
  finally { mock.mock.restore(); }
});

test("notification read mutations use persistent read endpoints", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push([url, options.method]);
    return Response.json(url.endsWith("read-all") ? { updatedCount: 2 } : { id, isRead: true });
  });
  try {
    await client.markNotificationRead(id);
    await client.markAllNotificationsRead();
    assert.deepEqual(calls, [[`/api/notifications/${id}/read`, "PATCH"], ["/api/notifications/read-all", "PATCH"]]);
  } finally { mock.mock.restore(); }
});

test("notification links open the exact mission workflow without trusting external URLs", () => {
  assert.equal(routing.notificationDestination({ type: "NewPendingMission", referenceId: id, actionUrl: null }), `/portal/mission-approvals?missionId=${id}`);
  assert.equal(routing.notificationDestination({ type: "MissionApproved", referenceId: id, actionUrl: null }), `/portal/missions/${id}`);
  assert.equal(routing.notificationDestination({ type: "Legacy", referenceId: null, actionUrl: `/validation-approvals?missionId=${id}` }), `/portal/mission-approvals?missionId=${id}`);
  assert.equal(routing.notificationDestination({ type: "Unknown", referenceId: null, actionUrl: "https://example.com/phishing" }), "/portal");
});

test("proxy permits notification feed and read actions", async () => {
  const source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  const declaration = source.split("\n").find(line => line.startsWith("const allowed = "));
  const allowed = Function(`${declaration} return allowed;`)();
  for (const path of ["notifications", "notifications/unread-count", "notifications/read-all", `notifications/${id}/read`, `notifications/${id}/seen`]) assert.equal(allowed.test(path), true, path);
  for (const path of ["notifications/delete-all", "notifications/not-a-guid/read", `notifications/${id}/delete`]) assert.equal(allowed.test(path), false, path);
});
