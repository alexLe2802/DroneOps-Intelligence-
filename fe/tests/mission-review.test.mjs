import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const code = ts.transpileModule(await readFile(new URL("../src/lib/portal/mission-review.ts", import.meta.url), "utf8"), {
  compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext },
}).outputText;
const { validationState, approvalBlockReason, missionActions, reviewHistory } = await import(`data:text/javascript;base64,${Buffer.from(code).toString("base64")}`);
const valid = { id: "run", missionId: "mission", updateRequestId: "request", isValid: true, status: "Pass", outcome: "Pass", message: "Passed" };

test("approval fails closed for failed, incomplete, error and stale evidence even with isValid=true", () => {
  for (const state of ["Fail", "Incomplete", "Error", "Stale"]) {
    const result = { ...valid, status: state };
    assert.equal(validationState(result), state);
    assert.ok(approvalBlockReason(result, "mission", "request"), state);
  }
  for (const patch of [{ error: true }, { incomplete: true }, { stale: true }, { outcome: "Error" }, { outcome: "Fail" }, { issues: ["Blocked"] }, { isValid: false }]) {
    assert.ok(approvalBlockReason({ ...valid, ...patch }, "mission", "request"));
  }
  assert.ok(approvalBlockReason(null, "mission", "request"));
  assert.equal(approvalBlockReason(valid, "mission", "request"), null);
});

test("approval requires an exact mission, proposal and persisted run identity", () => {
  for (const patch of [{ id: undefined }, { missionId: "other" }, { missionId: undefined }, { updateRequestId: "superseded" }, { updateRequestId: null }]) {
    assert.match(approvalBlockReason({ ...valid, ...patch }, "mission", "request"), /identify this review request/);
  }
  assert.ok(approvalBlockReason(valid, "mission", undefined));
  assert.equal(approvalBlockReason({ ...valid, warnings: ["Weather advisory"] }, "mission", "request"), null);
});

test("operator and manager lifecycle controls respect ownership, pending proposals and terminal states", () => {
  const mission = { id: "mission", createdBy: "pilot", status: "Approved" };
  assert.equal(missionActions(mission, "uav_operator", "pilot").start, true);
  assert.equal(missionActions(mission, "operation_manager", "manager").cancel, true);
  assert.equal(missionActions(mission, "operation_manager", "manager").edit, false);
  for (const actor of ["other", undefined]) {
    assert.ok(Object.values(missionActions(mission, "uav_operator", actor)).every(value => !value));
  }
  const pending = { ...mission, pendingUpdate: { id: "request" } };
  assert.equal(missionActions(pending, "uav_operator", "pilot").start, false);
  assert.equal(missionActions(pending, "operation_manager", "manager").review, true);
  for (const status of ["InProgress", "In Progress", "Completed", "Cancelled", "Unknown"]) {
    assert.ok(Object.values(missionActions({ ...mission, status }, "uav_operator", "pilot")).every(value => !value), status);
  }
  assert.equal(missionActions({ ...mission, status: "Draft" }, "uav_operator", "pilot").submit, true);
  assert.equal(missionActions({ ...pending, status: "Draft" }, "uav_operator", "pilot").submit, false);
  assert.equal(missionActions({ ...mission, status: "Pending Review" }, "uav_operator", "pilot").submit, false);
  assert.equal(missionActions({ ...mission, status: "Pending Confirm" }, "uav_operator", "pilot").edit, false);
});

test("history sorts newest requests first without rewriting the server evidence", () => {
  const history = [{ id: "old", createdAt: "2026-11-01T08:00:00+07:00" }, { id: "new", createdAt: "2026-11-02T01:00:00Z" }];
  assert.deepEqual(reviewHistory(history).map(item => item.id), ["new", "old"]);
  assert.equal(history[0].id, "old");
});
