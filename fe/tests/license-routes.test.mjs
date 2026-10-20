import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

test("proxy allows canonical license routes and rejects retired or invented actions", async () => {
  const source = await readFile(new URL("../src/app/api/[...path]/route.ts", import.meta.url), "utf8");
  const declaration = source.split("\n").find(line => line.startsWith("const allowed = "));
  const allowed = Function(`${declaration} return allowed;`)();
  const base = "users/pilot-update-requests";
  const id = "12345678-1234-1234-1234-123456789abc";
  for (const path of [base, `${base}/${id}`, `${base}/${id}/approve`, `${base}/${id}/reject`]) {
    assert.equal(allowed.test(path), true, path);
  }
  for (const path of ["license-changes", `license-changes/${id}`, `license-changes/${id}/review`, `${base}/other-user`, `${base}/${id}/review`, `${base}/${id}/delete`]) {
    assert.equal(allowed.test(path), false, path);
  }
});
