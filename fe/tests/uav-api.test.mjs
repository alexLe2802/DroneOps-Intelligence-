import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText).toString("base64")}`;
const authUrl = compile(await readFile(new URL("../src/lib/auth/client.ts", import.meta.url), "utf8"));
const client = await import(compile((await readFile(new URL("../src/lib/portal/client.ts", import.meta.url), "utf8")).replace('"@/lib/auth/client"', JSON.stringify(authUrl))));
const id = "12345678-1234-1234-1234-123456789abc";

test("operator edit sends the update DTO and accepts a message-only response", async () => {
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    assert.equal(url, `/api/uavs/${id}`);
    assert.equal(options.method, "PUT");
    assert.deepEqual(JSON.parse(options.body), { code: "UAV-02", name: "Survey", model: "Model B" });
    return Response.json({ message: "UAV updated successfully." });
  });
  try { assert.equal(await client.updateAssignedUav(id, { code: "UAV-02", name: "Survey", model: "Model B" }), undefined); assert.equal(mock.mock.calls.length, 1); }
  finally { mock.mock.restore(); }
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

test("proxy permits exact UAV IDs and admin actions, rejecting invented routes", async () => {
  const source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  const declaration = source.split("\n").find(line => line.startsWith("const allowed = "));
  const allowed = Function(`${declaration} return allowed;`)();
  for (const path of ["uavs", "uavs/myUAV", `uavs/${id}`, "admin/uavs", `admin/uavs/${id}/deactivate`]) assert.equal(allowed.test(path), true, path);
  for (const path of ["uavs/not-an-id", `admin/uavs/${id}/delete`, `uavs/${id}/deactivate`]) assert.equal(allowed.test(path), false, path);
});
