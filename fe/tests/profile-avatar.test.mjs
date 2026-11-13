import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import ts from "typescript";

const helperSource = await readFile(new URL("../src/lib/avatar-file.ts", import.meta.url), "utf8");
const helperCode = ts.transpileModule(helperSource, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const { avatarFileError, MAX_AVATAR_BYTES } = await import(`data:text/javascript;base64,${Buffer.from(helperCode).toString("base64")}`);

test("avatar validation accepts supported images up to 5 MB", () => {
  for (const type of ["image/jpeg", "image/png", "image/webp"]) {
    assert.equal(avatarFileError({ type, size: MAX_AVATAR_BYTES }), null);
  }
});

test("avatar validation rejects unsupported, empty and oversized files", () => {
  assert.match(avatarFileError({ type: "image/gif", size: 1024 }), /JPEG, PNG or WebP/);
  assert.match(avatarFileError({ type: "image/png", size: 0 }), /up to 5 MB/);
  assert.match(avatarFileError({ type: "image/png", size: MAX_AVATAR_BYTES + 1 }), /up to 5 MB/);
});

test("profile clears pending preview on invalid selection and upload failure", async () => {
  const source = await readFile(new URL("../src/app/portal/profile/profile-client.tsx", import.meta.url), "utf8");
  assert.match(source, /if \(validationError\) \{\s*clearAvatarSelection\(\);\s*setAvatarError/);
  assert.match(source, /catch \(e\) \{\s*clearAvatarSelection\(\);/);
  assert.match(source, /Your current avatar was kept\./);
  assert.match(source, /disabled=\{uploading\}/);
});
