import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const compile = source => `data:text/javascript;base64,${Buffer.from(ts.transpileModule(source, {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext, jsx: ts.JsxEmit.ReactJSX },
}).outputText).toString("base64")}`;

const clientUrl = compile(await readFile(new URL("../src/lib/auth/client.ts", import.meta.url), "utf8"));
const registrationSource = (await readFile(new URL("../src/lib/pilot-registration.ts", import.meta.url), "utf8"))
  .replace('"@/lib/auth/client"', JSON.stringify(clientUrl));
const updateSource = (await readFile(new URL("../src/lib/pilot-update-review.ts", import.meta.url), "utf8"))
  .replace('"@/lib/auth/client"', JSON.stringify(clientUrl));
const registration = await import(compile(registrationSource));
const updates = await import(compile(updateSource));

test("manager registration decisions use exact endpoints and trimmed rejection evidence", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push({ url, options });
    return Response.json({ success: true, message: "Decision stored" });
  });
  try {
    await registration.approvePilotRegistration("registration-id");
    await registration.rejectPilotRegistration("registration-id", "  License image is unreadable.  ");
    assert.equal(calls.length, 2);
    assert.equal(calls[0].url, "/api/pilot-registrations/registration-id/approve");
    assert.equal(calls[0].options.method, "POST");
    assert.deepEqual(JSON.parse(calls[0].options.body), { note: null });
    assert.equal(calls[1].url, "/api/pilot-registrations/registration-id/reject");
    assert.equal(calls[1].options.method, "POST");
    assert.deepEqual(JSON.parse(calls[1].options.body), { reason: "License image is unreadable." });
  } finally {
    mock.mock.restore();
  }
});

test("manager license decisions use canonical routes and submit reject reason once", async () => {
  const calls = [];
  const mock = test.mock.method(globalThis, "fetch", async (url, options) => {
    calls.push({ url, options });
    return Response.json({ status: "Reviewed" });
  });
  try {
    await updates.approvePilotUpdateRequest("request/id");
    await updates.rejectPilotUpdateRequest("request/id", "  Expiration date does not match.  ");
    assert.equal(calls.length, 2);
    assert.equal(calls[0].url, "/api/users/pilot-update-requests/request%2Fid/approve");
    assert.equal(calls[0].options.method, "PUT");
    assert.equal(calls[1].url, "/api/users/pilot-update-requests/request%2Fid/reject");
    assert.equal(calls[1].options.method, "PUT");
    assert.deepEqual(JSON.parse(calls[1].options.body), { rejectReason: "Expiration date does not match." });
  } finally {
    mock.mock.restore();
  }
});

test("authorization and stale decision failures never become successful manager decisions", async () => {
  for (const status of [403, 409]) {
    const mock = test.mock.method(globalThis, "fetch", async () => Response.json({
      message: status === 403 ? "Manager role required" : "Request was already reviewed. Refresh the list.",
    }, { status }));
    try {
      await assert.rejects(updates.approvePilotUpdateRequest("request-id"), error => error.status === status);
      assert.equal(mock.mock.calls.length, 1);
    } finally {
      mock.mock.restore();
    }
  }
});

test("manager review UI requires confirmation, keeps reject reason in the dialog and refreshes stale data", async () => {
  const dialog = await readFile(new URL("../src/app/portal/review-confirmation-dialog.tsx", import.meta.url), "utf8");
  const registrations = await readFile(new URL("../src/app/portal/operators/pilot-registration-requests.tsx", import.meta.url), "utf8");
  const licenses = await readFile(new URL("../src/app/portal/profile/license-changes.tsx", import.meta.url), "utf8");
  const operatorsPage = await readFile(new URL("../src/app/portal/operators/page.tsx", import.meta.url), "utf8");
  const profilePage = await readFile(new URL("../src/app/portal/profile/profile-client.tsx", import.meta.url), "utf8");

  assert.match(dialog, /role="dialog"/);
  assert.match(dialog, /aria-modal="true"/);
  assert.match(dialog, /REJECTION REASON · REQUIRED/);
  assert.match(dialog, /disabled=\{busy \|\| reasonMissing\}/);
  assert.match(dialog, /onClick=\{onCancel\}>Cancel/);
  assert.doesNotMatch(dialog, /\bapi\s*\(/);

  assert.match(registrations, /setPendingAction\("approve"\)/);
  assert.match(registrations, /setPendingAction\("reject"\)/);
  assert.match(registrations, /value\.status === 409/);
  assert.match(registrations, /value\.status === 400/);
  assert.match(registrations, /await loadList\(\)/);
  assert.match(licenses, /setReviewAction\("approve"\)/);
  assert.match(licenses, /setReviewAction\("reject"\)/);
  assert.match(licenses, /e\.status === 409/);
  assert.match(licenses, /await load\(\)/);
  assert.match(operatorsPage, /requirePortalSession\(\["operation_manager"\]\)/);
  assert.match(operatorsPage, /<LicenseChanges manager(?: [^>]*)? \/>/);
  assert.match(profilePage, /account\.role === "uav_operator" && <LicenseChanges onRefresh=/);
});
